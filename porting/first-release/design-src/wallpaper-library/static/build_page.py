# Builds the Static Wallpapers library page: static.html (with the skeleton, for local viewing) and share/static.html
# (skeleton stripped, <title> first, everything inline, Google Fonts only), on the decisions-2026-10-03 kit.
# Sources: sw-color.js, sw-core.js and walls-*.js (the wallpapers, compiled to wallpapers.json by make_json.cjs),
# glass-head.txt + glass-src.txt (the glass shader and the MIT notices of the two reference repos, from the wallpapers
# page) + sw-gl.body.js (the engine), sw-page.js, sw-page.css. measured.json (check.cjs) feeds the measured costs.
import json, os, re, sys, subprocess
from html import escape as esc

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.environ.get('SW_ROOT') or os.path.join(HERE, '..')   # SW_ROOT: build somewhere else (dev builds never touch share/)
os.makedirs(os.path.join(ROOT, 'share'), exist_ok=True)
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402

subprocess.run(['node', os.path.join(HERE, 'make_json.cjs')], check=True)
DATA = json.load(open(os.path.join(HERE, 'wallpapers.json'), encoding='utf-8'))


def rd(f):
    return open(os.path.join(HERE, f), encoding='utf-8').read()


def engine():
    body = rd('sw-gl.body.js').replace('/*@@GLASS@@*/', rd('glass-src.txt'))
    head = rd('glass-head.txt').replace(' and the eased dark / light / glass / Reduce Motion mixes.', ' and the eased theme, pack and glass mixes.')
    return head + body


def measured_sentence():
    ws = [w for w in DATA['wallpapers'] if w.get('cost')]
    if len(ws) < len(DATA['wallpapers']):
        return 'Not measured at build time yet.'
    v = sorted(w['cost']['ms'] for w in ws)
    m = DATA['measured'] or {}
    mm = re.search(r'(NVIDIA GeForce [^(,]+|AMD Radeon[^(,]+|Intel\(R\)[^(,]+)', m.get('renderer', ''))
    gpu = (mm.group(1).strip() if mm else m.get('renderer', 'this machine'))
    heavy = max(ws, key=lambda w: w['cost']['ms'])
    f = lambda x: ('%.2f' % x) if x < 10 else ('%.1f' % x)
    return ('<b>Measured when this page was built</b> in headless Chrome %s on %s (Direct3D 11): one render at 2560 x 1600 costs <b>%s to %s ms</b> '
            '(median %s ms; the heaviest is %s), against the budget of 30 ms. The app draws it once at the window size and keeps it, so after that it costs nothing per frame; '
            'it draws again only when the window is resized, the theme, pack, accent, clock or focus changes, or while the pointer moves the perspective.' % (
                esc(m.get('chrome', '')), esc(gpu), f(v[0]), f(v[-1]), f(v[len(v) // 2]), esc(heavy['name'])))


BODY = r"""
<div class="dk-page">
  <header class="dk-hdr">
    <div class="dk-tags"><span class="dk-tag">Wallpaper library</span><span class="dk-tag">Static</span><span class="dk-tag">@@N@@ wallpapers</span><span class="dk-tag">3 tiers</span><span class="dk-tag">@@NPACKS@@ colour packs</span><span class="dk-tag">dark and light</span><span class="dk-tag">Dynamic</span><span class="dk-tag">Match accent</span><span class="dk-tag">Perspective</span><span class="dk-tag">Focus</span></div>
    <h1>Static wallpapers</h1>
    <p>@@NW@@ still wallpapers for the Marrow window, in three tiers from vibrant to minimal, so there is one for every taste: bold colour, colour with restraint, and calm colour that never pulls the eye. Each one is drawn by maths, not a picture, so it is sharp at any size and on any screen. Each has three to five colour packs and a sixth that matches your accent colour, each with a dark and a light palette, one option of its own and four premium options: follow the time of day, perspective, focus and grain. Pick one to see it large, behind the glass of the Marrow window or bare. The animated half of the library is on its own page.</p>
  </header>
  <div class="tb dk-glass" role="toolbar" aria-label="Library controls">
    <div class="dk-grp"><span class="dk-glab">Show</span><div id="segTier"></div></div>
    <span class="cnt" id="cnt" aria-live="polite"></span>
    <span class="dk-sp"></span>
    <div class="dk-grp"><span class="dk-glab">Theme</span><div id="segT"></div></div>
  </div>

  <section class="view dk-glass" id="view" aria-label="Large view">
    <div class="vst" id="vst" data-glass="1"></div>
    <div class="panel" role="group" aria-label="Wallpaper options">
      <div id="pbody" style="display:flex;flex-direction:column;gap:14px;transition:opacity .2s ease;min-width:0">
        <div class="ph">
          <h2 id="vname"></h2>
          <div class="meta"><span class="tag" id="vtier"></span><span class="tag" id="vpacks"></span></div>
          <p id="vdesc"></p>
        </div>
        <div class="grp"><div class="gl"><span>Colour pack</span><b id="packn"></b></div><div class="packs" id="packs" role="group" aria-label="Colour packs"></div>
          <div class="fold" id="accFold"><div><div class="accp">
            <div class="gl"><span>System accent</span><b id="accn"></b></div>
            <div class="accrow"><div class="accs" id="accs" role="group" aria-label="Accent colour"></div><label class="cu" title="Pick any colour"><input type="color" id="accc" value="#005fb8" aria-label="Custom accent colour"><span></span></label></div>
          </div></div></div>
        </div>
        <div class="grp"><div class="gl"><span id="optl"></span></div><div class="sl"><input class="rg" id="opt" type="range" min="0" max="1" step="0.01" aria-labelledby="optl"><output for="opt"></output></div></div>
        <div class="grp"><div class="gl"><span id="grl">Grain</span></div><div class="sl"><input class="rg" id="grain" type="range" min="0" max="1" step="0.01" aria-labelledby="grl"><output for="grain"></output></div></div>
        <div class="grp"><div class="gl"><span id="fol">Focus</span><span class="ends" aria-hidden="true"><em>Sharp</em><em>Dreamy</em></span></div><div class="sl"><input class="rg" id="focus" type="range" min="0" max="1" step="0.01" aria-labelledby="fol"><output for="focus"></output></div></div>
        <div class="grp opts">
          <button class="swt" id="dyn" type="button" role="switch" aria-checked="false"><span class="sl2">Dynamic<small>Follows the time of day</small></span><span class="tr"></span></button>
          <div class="fold" id="dynFold"><div><div class="dynp">
            <div class="gl"><span>Clock</span><b id="clk"></b></div>
            <div class="sl"><input class="rg tod" id="clock" type="range" min="0" max="24" step="0.05" aria-label="Time of day"><button class="dk-btn" id="now" type="button">Now</button></div>
            <div class="looks" id="looks" role="group" aria-label="The four key looks"><button class="lk" type="button" data-k="0" aria-pressed="false"><i></i><span>Dawn</span><em>06:00</em></button><button class="lk" type="button" data-k="1" aria-pressed="false"><i></i><span>Day</span><em>12:00</em></button><button class="lk" type="button" data-k="2" aria-pressed="false"><i></i><span>Dusk</span><em>18:00</em></button><button class="lk" type="button" data-k="3" aria-pressed="false"><i></i><span>Night</span><em>00:00</em></button></div>
            <p class="hint">Dark mode leans night and light mode leans day: each shows its own palette at its own hour and moves from there.</p>
          </div></div></div>
          <button class="swt" id="persp" type="button" role="switch" aria-checked="false"><span class="sl2">Perspective<small id="perspn"></small></span><span class="tr"></span></button>
        </div>
      </div>
      <button class="swt" id="glass" type="button" role="switch" aria-checked="true">Behind the glass<span class="tr"></span></button>
      <div class="cost"><span class="cl" id="costl"></span><span class="bar" aria-hidden="true"><i id="costbar"></i></span></div>
      <div class="acts"><button class="dk-btn" id="codeb" type="button" aria-expanded="false" aria-controls="code">Show shader</button><button class="dk-btn" id="copy" type="button">Copy shader</button><button class="dk-btn" id="reset" type="button">Reset</button></div>
    </div>
    <div class="code" id="code"><div><pre><code id="src"></code></pre></div></div>
  </section>

  <div id="tiers" style="display:flex;flex-direction:column;gap:18px"></div>

  <section class="costcard dk-glass" id="Cost" aria-label="Render cost">
    <h2>What a still wallpaper costs</h2>
    <p class="sumline">@@MEASURED@@</p>
    <p class="sumline" id="mlive">Press Measure to time all @@N@@ on this device.</p>
    <div class="acts" style="align-items:center"><button class="dk-btn pri" id="meas" type="button">Measure all @@N@@</button><span id="mstat" style="font:500 11.5px/1.3 var(--mono);color:var(--faint)"></span></div>
    <div class="ctab" id="ctab"></div>
  </section>

  <div class="dk-notes">
    <div class="dk-note dk-glass"><h3>How a wallpaper is made</h3><p>Each wallpaper is one small function in the GLSL shading language that returns the colour of a point, in the perceptual OKLab space. It is built from light and material, not flat blobs: crests that catch the light and shadow what lies behind, haze that pales the far ridges, glass that bends what it covers, paper and linen at their real thread size. A fixed film grain and a one-step dither keep every gradient free of bands.</p></div>
    <div class="dk-note dk-glass"><h3>Porting it</h3><p>The block <b>wallpapers</b> in this page (also <b>src/wallpapers.json</b>) holds everything the app needs as data: the shared header, library and main function; each wallpaper&#8217;s source, option and grain; and each pack as six sRGB colours for dark and six for light. The sources use maths only (no textures, no derivatives, fixed loops), so they port to Skia SkSL as written. Render once at the window size and cache it; ease the palette for half a second when the theme or the pack changes. The four premium options are data too (<b>options</b> in the same block): the four key looks of Dynamic, the perspective depth, the light direction and the focus hook, plus how Match accent is built from the accent colour.</p></div>
    <div class="dk-note dk-glass"><h3>What this page cannot show</h3><p>The page draws in a browser; the app draws with its own renderer, so the final timings and the glass strength are settled in the app against the Mac videos. Without a wallpaper the window keeps upstream&#8217;s clear glass over the desktop (OD-0014, OD-0005). The words in the mock window are placeholders.</p></div>
  </div>
  <p class="lic"><b>Licences.</b> The glass shader reuses techniques and code of two MIT-licensed projects: <b>liquid-glass</b> by Riz Roze (Copyright (c) 2026 Riz Roze, github.com/rizzytoday/liquid-glass) and <b>liquid-glass-js</b> by Armagan Amcalar (Copyright (c) 2025 Armagan Amcalar, github.com/dashersw/liquid-glass-js). Their MIT permission and warranty notices are kept in full in this page&#8217;s script. Your choices stay in this browser only, if it allows storing them. Every change on this page is transitioned (OD-0076).</p>
</div>
"""


def main():
    css = rd('sw-page.css')
    js = rd('sw-color.js') + '\n' + engine() + '\n' + rd('sw-page.js') + '\n'
    NW = {30: 'Thirty', 60: 'Sixty', 90: 'Ninety'}.get(len(DATA['wallpapers']), str(len(DATA['wallpapers'])))
    body = BODY.replace('@@MEASURED@@', measured_sentence()).replace('@@NPACKS@@', str(sum(len(w['packs']) + 1 for w in DATA['wallpapers']))).replace('@@NW@@', NW).replace('@@N@@', str(len(DATA['wallpapers'])))
    body += '\n<script type="application/json" id="wallpapers">%s</script>\n' % json.dumps(DATA, separators=(',', ':')).replace('</', '<\\/')
    html = page(title='Static Wallpapers',
                description='A library of procedural still wallpapers for the Marrow window in three tiers (vibrant, balanced, minimal), each with colour packs and a Match accent pack for dark and light, follow the time of day, perspective and focus, shown large behind the liquid glass, with the measured render cost and the shader data to port.',
                css=css, body=body, js=js)
    assert html.startswith('<title>Static Wallpapers</title>\n<style>')
    for bad in (r'<!doctype', r'<html[\s>]', r'<body[\s>]', r'<head[\s>]'):
        assert not re.search(bad, html, re.I), bad
    for url in re.findall(r'(?:src|href)="(https?://[^"]+)"', html):
        assert url.startswith('https://fonts.googleapis.com/'), url
    for bad in ('eval(', 'new Function', 'fetch(', 'XMLHttpRequest', 'sendBeacon', 'WebSocket'):
        assert bad not in js, bad
    for bad in (r'(?i)[a-z]:[\\/]+users', r'(?i)[\\/]users[\\/]', r'(?i)omera', r'(?i)e:[\\/]+droppy', r'[\w.+-]+@[\w-]+\.[a-z]{2,}\b', r'(?i)\b(?:sk|ghp|gho|xox[bp])[-_][A-Za-z0-9_-]{16,}'):
        m = re.search(bad, html)
        assert not m, (bad, html[max(0, m.start() - 40):m.end() + 40])
    write_both(ROOT, 'static', ascii_only(html))


def ascii_only(html):
    out, pos = [], 0
    for m in re.finditer(r'(<script[^>]*>)(.*?)(</script>)', html, re.S):
        out.append(_ent(html[pos:m.start()]))
        out.append(m.group(1) + ''.join(c if ord(c) < 128 else r'\u%04x' % ord(c) for c in m.group(2)) + m.group(3))
        pos = m.end()
    out.append(_ent(html[pos:]))
    res = ''.join(out)
    assert all(ord(c) < 128 for c in res)
    return res


def _ent(t):
    return ''.join(c if ord(c) < 128 else '&#x%x;' % ord(c) for c in t)


main()
