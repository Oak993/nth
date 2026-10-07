#!/usr/bin/env python3
"""OD-0112: execute the locked Aurora Canvas source; package, never redraw it."""
import argparse
import base64
import hashlib
import importlib.metadata
import json
import os
import platform
from pathlib import Path
import shutil
import struct
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[2]
DESIGN = 'porting/first-release/design-src/app-icon'
GENERATED = DESIGN + '/generated'
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
BROWSER = {'name': 'Chromium', 'version': '151.0.7922.173'}
LOCKED = {
    DESIGN + '/src/iconkit.js': '930efb3d74ba7913e863139b8bda322ccd7d26ed67d40662c389480ab18d0b75',
    DESIGN + '/src/icons-a-j/D-aurora.js': 'a5b97966a4ba80020123536074ded566b3e2818a15fd0a3de023cf06a2feb0b2',
}
FLAGS = ['--disable-gpu', '--disable-dev-shm-usage', '--disable-background-networking',
         '--disable-component-update', '--disable-default-apps', '--disable-sync',
         '--no-first-run', '--no-default-browser-check', '--force-color-profile=srgb',
         '--force-device-scale-factor=1', '--host-resolver-rules=MAP * ~NOTFOUND']


def sha(data):
    return hashlib.sha256(data).hexdigest()


def ico(pngs):
    # PNG-compressed 32-bit entries, supported by Windows 11. Keeping each canvas
    # export verbatim also makes frame provenance independently byte-verifiable.
    offset = 6 + 16 * len(SIZES)
    entries = []
    for size in SIZES:
        png = pngs[size]
        assert png[:8] == b'\x89PNG\r\n\x1a\n'
        assert struct.unpack('>II', png[16:24]) == (size, size)
        assert png[24:26] == bytes((8, 6))  # 8-bit RGBA
        entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0,
                                   1, 32, len(png), offset))
        offset += len(png)
    return struct.pack('<HHH', 0, 1, len(SIZES)) + b''.join(entries) + b''.join(pngs[s] for s in SIZES)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--browser', default=os.environ.get('AURORA_BROWSER'))
    parser.add_argument('--check', action='store_true', help='regenerate and byte-compare; write nothing')
    args = parser.parse_args()
    browser = args.browser or next((shutil.which(x) for x in ('chromium', 'google-chrome', 'msedge') if shutil.which(x)), None)
    if not browser:
        parser.error('set AURORA_BROWSER or --browser to a local Chromium/Chrome/Edge executable')
    executable_name = Path(browser).name.lower()
    browser_name = ('Chromium' if 'chromium' in executable_name else
                    'Microsoft Edge' if 'msedge' in executable_name else
                    'Google Chrome' if 'chrome' in executable_name else 'Unknown')
    if browser_name != BROWSER['name']:
        raise RuntimeError(f"locked renderer requires {BROWSER['name']}, got {browser_name}")
    for path, digest in LOCKED.items():
        if sha((ROOT / path).read_bytes()) != digest:
            raise RuntimeError('locked OD-0112 source changed: ' + path)
    if importlib.metadata.version('playwright') != '1.62.0':
        raise RuntimeError('install the documented playwright==1.62.0 dependency')
    flags = FLAGS
    scripts = '\n'.join((ROOT / p).read_text(encoding='utf-8') for p in LOCKED)
    # Each page has its own IconKit caches and scratch pool, just like a fresh
    # still on the decision page. No resource loads or screenshots are involved.
    jobs = [(str(s), s, False) for s in (*SIZES, 512)] + [('tile-' + str(s), s, True) for s in (256, 512)]
    draws = {}
    for name, size, cropped in jobs:
        draw = ("const c=document.createElement('canvas'); c.width=c.height=" + str(size) + ";"
                "const m=MarrowIcons.get('D'); const t={launch:1,idle:0,still:true};")
        if cropped:
            # ED-276's full-bleed attribution slot: crop only the source's safe
            # box (6% at these sizes), preserving design size and its large band.
            draw += (f"const b=IconKit.box({size}), k={size}/b.w; const x=c.getContext('2d');"
                     "x.setTransform(k,0,0,k,-b.x*k,-b.y*k);"
                     f"m.draw(x,{size},'dark',t);")
        else:
            draw += f"MarrowIcons.render(c,m,{size},'dark',t,1);"
        draws[name] = '() => {' + draw + "return c.toDataURL('image/png');}"
    encoded = {}
    with sync_playwright() as p:
        # One disposable browser, debug pipe only. Playwright 1.62.0 supplies its
        # pinned default flags and temporary unsigned-in profile. No open port.
        instance = p.chromium.launch(executable_path=browser, headless=True, args=flags)
        version = instance.version
        try:
            if version != BROWSER['version']:
                raise RuntimeError(f"locked renderer requires {BROWSER['version']}, got {version}")
            context = instance.new_context(device_scale_factor=1, viewport={'width': 1024, 'height': 1024})
            context.route('**/*', lambda route: route.abort())
            for name, _, _ in jobs:
                page = context.new_page()
                try:
                    page.set_content('<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; script-src \'unsafe-inline\'">'
                                     + '<script>' + scripts + '</script>')
                    encoded[name] = page.evaluate(draws[name])
                finally:
                    page.close()
        finally:
            instance.close()
    pngs = {name: base64.b64decode(encoded[name].split(',', 1)[1]) for name, _, _ in jobs}
    oracle_hashes = {}
    for size in (16, 24, 32, 48, 64, 256, 512):
        reference = f'tests/fixtures/aurora/chrome/png/{size}-dark-still-0.png'
        approved = (ROOT / reference).read_bytes()
        if pngs[str(size)] != approved:
            raise RuntimeError('locked browser still differs: ' + reference)
        oracle_hashes[reference] = sha(approved)
    outputs = {GENERATED + '/icon-' + str(s) + '.png': pngs[str(s)] for s in SIZES}
    outputs['src/Marrow.App/Assets/icon.ico'] = ico({s: pngs[str(s)] for s in SIZES})
    outputs['src/Marrow.App/Assets/icon.png'] = pngs['512']
    outputs['src/Marrow.UI/Assets/Images/marrow-icon.png'] = pngs['tile-256']
    outputs['src/Marrow.UI/Assets/Images/marrow-icon@2x.png'] = pngs['tile-512']
    # Also hash the whole locked decision page and source contract for provenance.
    provenance = dict(LOCKED)
    for path in (DESIGN + '/app-icon.html', DESIGN + '/src/ICON-MODULE.md',
                 'porting/tools/generate_app_icon_d.py'):
        provenance[path] = sha((ROOT / path).read_bytes())
    manifest = {
        'decision': 'OD-0112', 'browser': version, 'flags': flags,
        'browserName': browser_name, 'lockedBrowser': BROWSER,
        'platform': {'system': platform.system(), 'machine': platform.machine()},
        'playwright': importlib.metadata.version('playwright'),
        'launch': {'headless': True, 'chromiumSandbox': False, 'defaultArgs': 'Playwright 1.62.0 Chromium defaults'},
        'sources': provenance, 'theme': 'dark', 'dpr': 1,
        'acceptedStillOracles': oracle_hashes,
        'time': {'launch': 1, 'idle': 0, 'still': True},
        'ico': {'sizes': SIZES, 'encoding': 'verbatim Canvas PNG, 32-bit RGBA'},
        'appPngSize': 512, 'attributionPngSizes': [256, 512],
        'attributionCrop': 'IconKit.box(size), transformed to fill output; no post-render resampling',
        'outputs': {p: {'sha256': sha(data), 'bytes': len(data)} for p, data in sorted(outputs.items())},
    }
    outputs[GENERATED + '/sha256.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    if args.check:
        different = [p for p, data in outputs.items() if not (ROOT / p).exists() or (ROOT / p).read_bytes() != data]
        if different:
            raise RuntimeError('regeneration differs: ' + ', '.join(different))
        print(f'PASS: {len(outputs)} files byte-identical; locked OD-0112 source verified; {version}')
    else:
        for path, data in outputs.items():
            target = ROOT / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        print(f'Generated {len(outputs)} files from locked OD-0112 source; {version}')


if __name__ == '__main__':
    main()
