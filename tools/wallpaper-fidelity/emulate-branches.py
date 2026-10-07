#!/usr/bin/env python3
"""FP32 emulation of SwiftShader highp and Skia m119 raster branch arithmetic.

Requires numpy. All ordinary operators round to binary32; mad alone contracts.
Coefficients/order are from SwiftShader Pipeline/ShaderCore.cpp and Skia
chrome/m119 src/opts/SkRasterPipeline_opts.h. Live RGBA32F probes independently
check the model; PNG references and the fidelity metric are never rewritten.
"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

import numpy as np

F = np.float32
PI = F(3.14159265)


def mad(a, b, c):
    # A binary64 product/sum exactly represents these binary32 polynomial inputs
    # well beyond the precision needed for their final binary32 rounding.
    return F(np.float64(a) * np.float64(b) + np.float64(c))


def horner(x, coefficients, fused):
    result = F(coefficients[0])
    for c in coefficients[1:]:
        result = mad(result, x, F(c)) if fused else result * x + F(c)
    return result


def sincos(x, cosine=False, backend='skia'):
    inv = F(1 / F(2 * PI))
    z = x * inv if cosine else (mad(x, -inv, F(.25)) if backend == 'skia' else x*(-inv)+F(.25))
    z = F(.25) - abs(z - np.rint(z))
    # SwiftShader computes these constants in double, then narrows to float.
    a = F((36288 - 20736 * np.sqrt(3.)) / 5)
    b = F(288 * np.sqrt(3.) - 540)
    c = F((47 - 9 * np.sqrt(3.)) / 5)
    return (mad(mad(a, z*z, b), z*z, c) if backend == 'skia' else (a*(z*z)+b)*(z*z)+c) * z


def atan2(y, x, backend):
    if backend == 'skia':
        flip = abs(y) > abs(x)
        arg = np.where(flip, x, y) / np.where(flip, y, x)
        theta = horner(abs(arg), [.14130025741326729, -.34312835980675116,
            -.016172900528248768, 1.0037696976200385, -.00014758242182738969], True)
        theta = np.where(flip, F(PI/2)-theta, theta)
        theta = np.where(arg < 0, -theta, theta)
        theta = np.where((y >= 0) & (x < 0), theta+PI, theta)
        return np.where((y < 0) & (x <= 0), theta-PI, theta).astype('float32')
    lower = y < 0
    x0, y0 = np.where(lower, -x, x), abs(y)
    left = x0 < 0
    theta = np.where(lower, -PI, F(0)) + np.where(left, F(PI/2), F(0))
    x1, y1 = np.where(left, y0, x0), np.where(left, -x0, y0)
    flip = y1 >= x1
    z = np.where(flip, x1, y1) / np.where(flip, y1, x1)
    z2 = z*z
    poly = horner(z2, [.0028662257, -.0161657367, .0429096138, -.0752896400,
        .1065626393, -.1420889944, .1999355085, -.3333314528], False)
    a = z + z * (z2 * poly)
    theta += np.where(flip, F(PI/2)-a, a)
    return np.where(lower & left & flip, -a, theta).astype('float32')


def log2(x, backend):
    bits = np.asarray(x, dtype='float32').view('uint32')
    if backend == 'skia':
        e = F(bits) * F(1/(1 << 23))
        m = ((bits & 0x007fffff) | 0x3f000000).view('float32')
        return e - F(124.225514990) - F(1.498030302)*m - F(1.725879990)/(F(.3520887068)+m)
    # The pinned Chrome binary uses SwiftShader's legacy precision path, with
    # separate mul/add in these rational polynomials (confirmed by live probes).
    exponent = F((bits >> 23).astype('int32') - 127)
    m = ((bits & 0x007fffff) | 0x3f800000).view('float32')
    numerator = horner(m, [.095428179, .47779095, .19782813], False)
    denominator = horner(m, [.016618466, .20350508, .27382900, .040496687], False)
    return exponent + (m-F(1))*(numerator/denominator)


def powf(x, y, backend):
    z = log2(x, backend) * y
    f = z - np.floor(z)
    if backend == 'skia':
        value = z+F(121.274057500) - f*F(1.490129070)
        value += F(27.728023300)/(F(4.84252568)-f)
        bits = np.rint(value*F(1 << 23)).astype('uint32')
    else:
        i = np.rint(z-F(.5)).astype('int32')
        scale = ((i+127).astype('uint32') << 23).view('float32')
        f = z-F(i)
        coefficients = [np.array(c,dtype='uint32').view('float32') for c in
            [0x3AF61905,0x3C134806,0x3D64AA23,0x3E75EAD4,0x3F31727B]] + [F(1)]
        return scale*horner(f,coefficients,False)
    return bits.view('float32')


def mix(x, y, t, backend):
    x, y, t = F(x), F(y), F(t)
    return mad(y-x, t, x) if backend == 'skia' else t*(y-x)+x


def verify_es2_decomposition():
    # ES2 has no bit reinterpretation. Scaling by exact powers of two recovers
    # the identical normal binary32 mantissa/exponent, without log/exp intrinsics.
    bits = np.random.default_rng(20).integers(0x00800000,0x7f800000,262144,dtype='uint32')
    x = bits.view('float32'); m=x.copy(); exponent=np.zeros(x.shape,dtype='float32')
    for n in [64,32,16,8,4,2,1]:
        low=m<F(2.**(-n)); m[low]*=F(2.**n); exponent-=F(low)*F(n)
    low=m<F(1);m[low]*=F(2);exponent-=F(low)
    for n in [64,32,16,8,4,2,1]:
        high=m>=F(2.**n);m[high]*=F(2.**(-n));exponent+=F(high)*F(n)
    assert np.array_equal(m.view('uint32'),(bits&0x7fffff)|0x3f800000)
    assert np.array_equal(exponent,F((bits>>23).astype('int32')-127))
    return len(bits)


def coordinates(tilt, tg):
    y, x = np.mgrid[:100, :160].astype('float32')
    px = ((x+F(.5))/F(160)-F(.5))*F(2)*F(160/100)
    py = ((F(100)-y-F(.5))/F(100)-F(.5))*F(2)
    return px+F(tilt[0])*F(tg), py+F(tilt[1])*F(tg)


def tunnel(px, py, tilt, backend, k=3, reference_power=False, reference_angle=False):
    k = F(k)
    t = k/F(5)
    px, py = px+F(tilt[0])*(F(1)-t), py+F(tilt[1])*(F(1)-t)
    sm = mix(t, t*t*(F(3)-F(2)*t), F(.6), backend)
    cx = mix(F(-.2), F(.62), sm, backend)
    # ANGLE/SkSL fold the constant mix endpoint difference before runtime.
    cy = (F(-.13)*sm+F(.05) if backend == 'swift' else mad(F(-.13),sm,F(.05))) + F(.05)*sincos(t*F(3),backend=backend)
    radius = mix(F(1.32), F(.26), powf(t, F(.85), 'swift' if reference_power else backend), backend)
    angle = F(.25)+F(.5)*t
    c, s = sincos(angle, True, backend), sincos(angle,backend=backend)
    qx = (c*(px-cx) - s*(py-cy))*(F(.78)+F(.1)*t)
    qy = s*(px-cx) + c*(py-cy)
    a = atan2(qy, qx, 'swift' if reference_angle else backend)
    length = F(np.sqrt(qx*qx+qy*qy))
    s2, s3 = sincos(F(2)*a+k*F(1.3),backend=backend), sincos(F(3)*a-k*F(2.1),backend=backend)
    distance = length - radius*(F(1)+F(.055)*s2+F(.03)*s3)
    return {'center': (cx,cy,radius), 'angle': (qx,qy,a), 'terms': (length,s2,s3), 'd': distance}


def stairwell(px, py, backend, option=.5, reference_log=False, reference_angle=False):
    dx, dy = px-F(.62), py-F(.3)
    r = F(np.sqrt(dx*dx+dy*dy))
    th = atan2(dy, dx, 'swift' if reference_angle else backend)
    lr = F(.693147181)*log2(r, 'swift' if reference_log else backend)
    tight = mix(F(.95),F(1.6),F(option),backend)
    s = th/F(2*PI)-lr*tight
    return {'angle': (r,th,lr), 'distance': (s,s-np.floor(s),F(s-np.floor(s)>=F(.06)))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probes', type=Path, default=Path('/tmp/marrow-branch-probes'))
    parser.add_argument('--pixel-scan', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--fixed', action='store_true', help='Validate live Skia probes after the production fix')
    args = parser.parse_args()
    if args.probes.suffix == '.zip':
        archive = zipfile.ZipFile(args.probes)
        read = lambda name: archive.read(name)
    else:
        read = lambda name: (args.probes/name).read_bytes()
    index = json.loads(read('index.json'))
    root = Path(__file__).resolve().parents[2]
    manifest_bytes = (root/'tests/fixtures/wallpapers/reference/manifest.json').read_bytes()
    manifest = json.loads(manifest_bytes)
    report = {'browser': index['browser'], 'manifestSha256': hashlib.sha256(manifest_bytes).hexdigest(),
        'referenceFrames': len(manifest['renders']), 'es2ExactDecompositionChecks':verify_es2_decomposition(),
        'geometryChecks': [], 'failingPixels': []}
    models = {}
    for id in ['tunnel','stairwell']:
      for variant in ['default','perspective']:
        name = id+'-'+variant
        record = next(r for r in index['records'] if r['name']==name+'-coordinates')
        u = record['uniforms']; px,py = coordinates(u['u_tilt'],u['u_tg'])
        models[name] = {}
        for backend in ['swift','skia']:
            live = np.frombuffer(read(name+'-'+('loop' if id=='tunnel' else 'distance')+'-'+backend+'.bin'),dtype='<f4').reshape(100,160,4)
            if id == 'tunnel':
                distances = np.stack([tunnel(px,py,u['u_tilt'],backend,k,
                    reference_power=args.fixed and backend=='skia',reference_angle=args.fixed and backend=='skia')['d'] for k in range(5)])
                branches = np.argmax(np.concatenate([distances>0,np.ones((1,100,160),dtype=bool)]),axis=0)
                actual = live[:,:,0].astype('int32')
                models[name][backend] = (distances,branches)
            else:
                f = stairwell(px,py,backend,reference_log=args.fixed and backend=='skia',
                    reference_angle=args.fixed and backend=='skia')['distance'][1]
                branches,actual = (f>=F(.06)).astype('int32'),live[:,:,2].astype('int32')
                models[name][backend] = (f,branches)
            disagreements = int(np.count_nonzero(branches != actual))
            assert disagreements == 0, (name,backend,disagreements)
            report['geometryChecks'].append({'geometry':name,'backend':backend,'pixels':16000,'branchDisagreementsWithLive':disagreements})
        if id == 'tunnel':
            distances=np.stack([tunnel(px,py,u['u_tilt'],'skia',k,reference_power=True,reference_angle=True)['d'] for k in range(5)])
            branches=np.argmax(np.concatenate([distances>0,np.ones((1,100,160),dtype=bool)]),axis=0)
            models[name]['predicted']=(distances,branches)
        else:
            f=stairwell(px,py,'skia',reference_log=True,reference_angle=True)['distance'][1]
            branches=(f>=F(.06)).astype('int32');models[name]['predicted']=(f,branches)
        disagreements=int(np.count_nonzero(branches != models[name]['swift'][1]))
        assert disagreements == 0, (name,'predicted',disagreements)
        report['geometryChecks'].append({'geometry':name,'backend':'predicted-fix','pixels':16000,'branchDisagreementsWithReference':disagreements})
    # Every affected frame is covered, including passing frames and blurred ones;
    # palette/theme/time/accent/Focus do not change these pre-blur branches.
    frames=[r for r in manifest['renders'] if r['collection']=='static' and r['wallpaper'] in ['tunnel','stairwell']]
    for frame in frames:
        name=frame['wallpaper']+'-'+('perspective' if frame['variant']=='perspective' else 'default')
        u=next(r['uniforms'] for r in index['records'] if r['name']==name+'-coordinates')
        assert frame['uniforms']['tilt']==u['u_tilt'] and frame['uniforms']['tg']==u['u_tg']
        assert frame['options']['option']==u['u_opt']
        assert np.array_equal(models[name]['predicted'][1],models[name]['swift'][1]), frame['file']
    report['affectedFrames']=len(frames)
    report['framePixelBranchChecks']=len(frames)*16000
    if args.pixel_scan:
        scan=[json.loads(line) for line in args.pixel_scan.read_text().splitlines()]
        report['failingFrames']=sum(bool(r['Pixels']) for r in scan)
        for row in scan:
            for pixel in row['Pixels']:
                id = row['Label'].split('/')[1]
                variant = 'perspective' if '/perspective/' in row['Label'] else 'default'
                x,y=pixel['X'],pixel['Y'];model=models[id+'-'+variant]
                before,reference,predicted=[int(model[b][1][y,x]) for b in ['skia','swift','predicted']]
                assert before != reference and predicted == reference, row
                k=min(before,reference) if id=='tunnel' else None
                values={b:float(model[b][0][k,y,x] if id=='tunnel' else model[b][0][y,x]) for b in ['swift','skia','predicted']}
                report['failingPixels'].append({'label':row['Label'],'reference':row['File'],**pixel,
                    'threshold':0 if k is not None else float(F(.06)), 'sheet':k,
                    'values':values,'referenceBranch':reference,'beforeBranch':before,'predictedBranch':predicted})
    if args.report:
        args.report.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ['geometryChecks','failingPixels']},indent=2))
    print('Live/model branch checks: 128000; predicted/reference checks: 64000; failing pixel occurrences:',len(report['failingPixels']))


if __name__ == '__main__':
    main()
