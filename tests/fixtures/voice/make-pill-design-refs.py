# VT-12, OD-0125: renders the approved design's voice pill for every V-T133 identity the port's pill has specks in, as locked test references.
#
# What it makes: for each identity, a Chrome render of voice-composer.html's `.vpill` (its own CSS, verbatim, plus V-12.12's radial mask from voice-options.html) drawn over the PORT's own
# frame with the pill hidden (the same orb, glow, composer and backdrop), at the same device scale, theme, pill size, state (pill opacity, label opacity / position / sweep, cross
# opacity and scale) as the port's pill had in that frame; and an ink-only render (the label's glyphs and the cross, black on white) to exclude the design's own glyphs the way V-T133 excludes
# the port's. Both are cropped to the pill and a 12 DIP margin (+4 px for the 7x7 median) and written, with manifest.json (every file's SHA-256, the sources' hashes, Chrome's version), to
# tests/fixtures/voice/ref-pill-design/. ComposerVoiceCleanPillTests (V-T133) runs its own detector on these and verifies the hashes. NEVER regenerate to make a failure pass (OD-0125).
#
# How to run (two steps):
#   1. dump the port's frames and pill states:  VT12_DESIGN_DUMP=<dump folder> through E:/Droppy/work/claude-verify/one-names-long.sh <worktree> Marrow.App.Tests
#        "(FullyQualifiedName~ComposerVoiceCleanPillTests.The_pill_and_a_margin_round_it_have_no_specks)&FullyQualifiedName!~Native"   (writes pillless-<identity>.png and state-<identity>.json
#        for each identity whose pill has specks under V-T133's unchanged detector)
#   2. python make-pill-design-refs.py <dump folder> [<design html> <options html>]
# Needs Chrome (C:/Program Files/Google/Chrome/Application/chrome.exe), numpy and Pillow. It starts only its own Chrome processes (each exits after its screenshot).
import sys, os, re, json, math, hashlib, subprocess, tempfile, shutil, base64
from concurrent.futures import ThreadPoolExecutor
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get('PILL_REF_OUT') or os.path.join(HERE, 'ref-pill-design')
CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'
# T33 lead decision 1 (OD-0123/0125): exact upright 600 face embedded in Avalonia.Fonts.Inter 12.1.2.
FONT = os.path.join(HERE, 'Inter-SemiBold.ttf')
FONT_DATA = base64.b64encode(open(FONT, 'rb').read()).decode('ascii')
DESIGN = sys.argv[2] if len(sys.argv) > 2 else 'E:/Droppy/work/voice/design/voice-composer.html'
OPTIONS = sys.argv[3] if len(sys.argv) > 3 else 'E:/Droppy/work/design/decisions-2026-10-03/voice-options/voice-options.html'
PAGE = open(DESIGN, encoding='utf-8').read().split('\n')
OPT = open(OPTIONS, encoding='utf-8').read().split('\n')


def toks(line):  # the pg* tokens of one theme line
    return ' '.join(re.findall(r'--pg[A-Za-z]+:[^;]+;', line))


LIGHT = toks(PAGE[22])
DARK = toks(PAGE[48])
RULES = '\n'.join(PAGE[160:192])  # .vpill ... @keyframes pill-sweep (lines 161-192), verbatim
MASK = '\n'.join(OPT[376:378])    # V-12.12 radial mask (lines 377-378), verbatim
LINE_HEIGHT = '\n'.join(OPT[379:380])  # `.vpill .pl{line-height:1.5}` (voice-options line 380)


def sha(path):
    return hashlib.sha256(open(path, 'rb').read()).hexdigest()


def render(tag, dump, work, ink):
    state = json.load(open(f'{dump}/state-{tag}.json'))
    style, D, dark, st, sc = tag.split('-')
    s = float(state['scale']); px, py, pw, ph = state['pill']
    float_ = style == 'Float'
    ox = math.floor((px - 40) / 4) * 4; oy = math.floor((py - 40) / 4) * 4
    W = int(math.ceil((pw + 80) / 4) * 4); H = int(math.ceil((ph + 80) / 4) * 4)
    back = Image.open(f'{dump}/pillless-{tag}.png').convert('RGB')
    X, Y = int(round(ox * s)), int(round(oy * s))
    crop = f'{work}/bd-{tag}.png'
    back.crop((X, Y, X + int(round(W * s)), Y + int(round(H * s)))).save(crop)
    nominal = 52 if float_ else 40  # .plab's left in the page
    labels = ''.join(
        f'<span class="pl on" style="opacity:{l["opacity"] if not ink else 1};transform:translate({l["x"] - nominal}px,-50%);background-position:{l["sweep"] * 100}% 0">{l["words"].replace("…", "&#8230;")}</span>'
        for l in state['labels'] if l['opacity'] > 0.001)
    crossCss = f'opacity:{state["cross"]["opacity"] if not ink else 1}!important;transform:scale({state["cross"]["scale"]})!important'
    inkcss = '''.vpill,.vpill.float{background:none!important;box-shadow:none!important;-webkit-backdrop-filter:none!important;backdrop-filter:none!important;-webkit-mask:none!important;mask:none!important}
.vpill::before{display:none!important}.vpill .px{background:none!important;box-shadow:none!important;color:#000!important}
.vpill .pl{background-image:none!important;color:#000!important}''' if ink else ''
    fs = state['labels'][0]['fontSize']
    html = f'''<!doctype html><html><head><meta charset="utf-8"><style>
@font-face{{font-family:"Pill Inter";src:url(data:font/ttf;base64,{FONT_DATA}) format("truetype");font-style:normal;font-weight:600}}
:root{{--app:"Pill Inter";--ez:cubic-bezier(.2,.8,.2,1);{DARK if dark == 'True' else LIGHT}}}
html,body{{margin:0;padding:0;overflow:hidden;background:{'#fff' if ink else '#000'}}}
*{{transition:none!important;animation:none!important}}
{RULES}
{MASK}
{LINE_HEIGHT}
.vpill{{--oR:{state["orbSlotDiameter"] / 2}px}}
#bd{{position:absolute;left:0;top:0;width:{W}px;height:{H}px;{'display:none' if ink else ''}}}
.vpill{{left:{px - ox}px!important;top:{py - oy}px!important;width:{pw}px!important;height:{ph}px!important;opacity:{state["pillOpacity"] if not ink else 1}!important}}
.vpill .px{{{crossCss}}}
{inkcss}
</style></head><body><img id="bd" src="file:///{crop}">
<div class="vpill open{' float' if float_ else ''}"><button class="px" type="button"><svg viewBox="0 0 12 12"><path d="M3.2 3.2 8.8 8.8M8.8 3.2 3.2 8.8"></path></svg></button><span class="plab" style="--fs:{fs}px">{labels}</span></div>
</body></html>'''
    name = f'{tag}-{"ink" if ink else "render"}'
    hp = f'{work}/{name}.html'
    open(hp, 'w', encoding='utf-8').write(html)
    shot = f'{work}/{name}.png'
    prof = tempfile.mkdtemp(prefix='pillref-', dir=work)
    try:
        subprocess.run([CHROME, '--headless=new', '--hide-scrollbars', '--disable-lcd-text', '--allow-file-access-from-files', f'--force-device-scale-factor={s}',
                        f'--window-size={W},{H}', f'--user-data-dir={prof}', f'--screenshot={shot}', 'file:///' + hp], capture_output=True, timeout=90)
    finally:
        shutil.rmtree(prof, ignore_errors=True)
    # the detector's area and its 4 px margin, in window pixels
    ax, ay, aw, ah = state['area']
    x0 = max(math.floor(ax) - 4, X); y0 = max(math.floor(ay) - 4, Y)
    x1 = min(math.ceil(ax + aw) + 4, X + int(round(W * s))); y1 = min(math.ceil(ay + ah) + 4, Y + int(round(H * s)))
    im = Image.open(shot).convert('RGB').crop((x0 - X, y0 - Y, x1 - X, y1 - Y))
    return im, [x0, y0, x1 - x0, y1 - y0]


def one(args):
    tag, dump, work = args
    r, crop = render(tag, dump, work, ink=False)
    k, _ = render(tag, dump, work, ink=True)
    ink = k.convert('L')
    r.save(f'{OUT}/render-{tag}.png', optimize=True)
    ink.save(f'{OUT}/ink-{tag}.png', optimize=True)
    return tag, crop


if __name__ == '__main__':
    dump = sys.argv[1]
    tags = sorted(f[len('state-'):-len('.json')] for f in os.listdir(dump) if f.startswith('state-') and f.endswith('.json'))
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    work = tempfile.mkdtemp(prefix='pillref-work-')
    try:
        with ThreadPoolExecutor(max_workers=2) as pool:
            done = dict(pool.map(one, [(t, dump, work) for t in tags]))
    finally:
        shutil.rmtree(work, ignore_errors=True)
    files = {}
    for t in tags:
        for kind in ('render', 'ink'):
            n = f'{kind}-{t}.png'
            files[n] = sha(f'{OUT}/{n}')
    # (chrome.exe --version opens a window on Windows: read the version from the install's own folder instead)
    app = os.path.dirname(CHROME)
    version = 'Chrome ' + ','.join(d for d in os.listdir(app) if re.fullmatch(r'\d+(\.\d+){3}', d))
    manifest = {
        'decision': 'OD-0125', 'generator': 'tests/fixtures/voice/make-pill-design-refs.py', 'chrome': version,
        'sources': {'voice-composer.html': sha(DESIGN), 'voice-options.html': sha(OPTIONS), 'Inter-SemiBold.ttf': sha(FONT)},
        'identities': {t: {'render': f'render-{t}.png', 'ink': f'ink-{t}.png', 'crop': done[t]} for t in tags},
        'files': files,
    }
    json.dump(manifest, open(f'{OUT}/manifest.json', 'w'), indent=1)
    print(len(tags), 'identities,', sum(os.path.getsize(f'{OUT}/{n}') for n in files) // 1024, 'KiB')
