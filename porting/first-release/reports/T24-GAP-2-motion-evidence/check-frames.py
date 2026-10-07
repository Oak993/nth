"""Check every emitted frame against an independent CSS solve and the unchanged .15 cap.
Returns 1 while motion is NOT READY; this is evidence, not a replacement test.
"""
import csv, json, math, pathlib, sys
root = pathlib.Path(__file__).resolve().parent

def curve(x):
    if x <= 0: return 0.0
    if x >= 1: return 1.0
    def at(a,b,t): return 3*a*t*(1-t)**2 + 3*b*t*t*(1-t) + t**3
    lo, hi = 0.0, 1.0
    for _ in range(80):
        mid = (lo+hi)/2
        if at(.25,.25,mid) < x: lo=mid
        else: hi=mid
    return at(.1,1,(lo+hi)/2)

def read(name):
    groups = {}
    for r in csv.DictReader((root/name).open()):
        for key in ('elapsed_ms','opacity','step_fraction','step_percent'): r[key]=float(r[key])
        r['frame']=int(r['frame'])
        groups.setdefault((r['path'],r['transition']),[]).append(r)
    return groups

before, after, page = read('before.csv'), read('after.csv'), read('after-page.csv')
checks=[]
for stage, groups in [('before',before),('after',after),('after-page',page)]:
    for (path,transition), frames in groups.items():
        assert [r['frame'] for r in frames] == list(range(25)), 'missing sample'
        assert frames[0]['opacity']==1 and frames[-1]['opacity']==0
        duration=.1 if transition=='detail-out' else .15
        for previous,r in zip(frames,frames[1:]):
            assert math.isclose(r['elapsed_ms']-previous['elapsed_ms'],16.6666,abs_tol=1e-9)
            assert r['opacity'] <= previous['opacity']
            assert math.isclose(r['step_fraction'],previous['opacity']-r['opacity'],abs_tol=1e-12)
            seconds=r['elapsed_ms']/1000
            expected=0 if seconds+.001>=duration else 1-curve(seconds/duration)
            assert math.isclose(r['opacity'],expected,abs_tol=1e-12), 'wrong source curve'
        violations=[{'frame':r['frame'],'elapsed_ms':r['elapsed_ms'],'step_fraction':r['step_fraction']} for r in frames if r['step_fraction']>.15+1e-9]
        checks.append({'stage':stage,'path':path,'transition':transition,'all_25_frames_present':True,'source_curve_matches':True,'max_step_fraction':max(r['step_fraction'] for r in frames),'violations':violations})
for key in before:
    assert [(r['elapsed_ms'],r['opacity']) for r in before[key]] == [(r['elapsed_ms'],r['opacity']) for r in after[key]], 'baseline and merged motion differ'
for transition in ('detail-out','placeholder-out'):
    assert [r['opacity'] for r in after[('scalar',transition)]] == [r['opacity'] for r in page[('page',transition)]], 'page differs from isolated scalar'
exact=1-curve(1/6)
result={'verdict':'NOT READY','baseline_equals_after':True,'page_equals_isolated_scalar':True,'threshold':.15,'ideal_detail_frame_1_opacity':exact,'curve_test_frame_1_upper_bound':exact+.020001,'gap_test_frame_1_lower_bound':1-.15-1e-9,'disjoint_frame_1_constraints':exact+.020001<1-.15-1e-9,'checks':checks}
print(json.dumps(result,indent=2))
sys.exit(1 if any(c['violations'] for c in checks) else 0)
