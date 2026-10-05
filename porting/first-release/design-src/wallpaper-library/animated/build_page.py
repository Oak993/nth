# Builds the animated half of the wallpaper library: animated.html (with the skeleton, for local viewing) and
# share/animated.html (skeleton stripped, <title> first, everything inline, Google Fonts only), on the decision kit of
# 2026-10-03 (kit.css, kit.js) like the voice-notes and wallpapers pages. This page is a library to browse, not a pick
# sheet: no picks bar.
#
# Sources: catalog.py (names, tiers, periods, packs, options; writes wallpapers.json and lints every shader against the
# SkSL subset), common.glsl and walls/*.glsl (the shaders), lib-gl.js (the engine, with the glass of the 2026-10-05
# wallpapers page and the two MIT repos' notices), lib-page.js and lib-page.css (the page).
import json, os, re, subprocess, sys
from html import escape as esc

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402
sys.path.insert(0, HERE)
from gl_build import built_gl  # noqa: E402

subprocess.check_call([sys.executable, os.path.join(HERE, 'catalog.py')])
DATA = json.load(open(os.path.join(HERE, 'wallpapers.json'), encoding='utf-8'))
W = DATA['wallpapers']
NPACK = sum(len(w['packs']) + 1 for w in W)   # five named packs and Match accent
NW = len(W)
NT = {t: sum(1 for w in W if w['tier'] == t) for t in ('vibrant', 'balanced', 'minimal')}
WORDS = {24: 'Twenty-four', 48: 'Forty-eight', 72: 'Seventy-two'}
NWORD = WORDS.get(NW, str(NW))

ICON = {
    'prev': '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M10 3.5 5.5 8l4.5 4.5"/></svg>',
    'next': '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3.5 10.5 8 6 12.5"/></svg>',
    'close': '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 4l8 8M12 4l-8 8"/></svg>',
}


def slider(id_, label_id, label, lo, hi, minv, maxv, stepv):
    return ('<div class="vg"><div class="vl"><label for="%s" id="%s">%s</label><output id="%sv" for="%s"></output></div>'
            '<input class="rg" type="range" id="%s" min="%s" max="%s" step="%s"><div class="ends"><span id="%slo">%s</span><span id="%shi">%s</span></div></div>'
            % (id_, label_id, esc(label), id_, id_, id_, minv, maxv, stepv, id_, esc(lo), id_, esc(hi)))


BODY = r"""
<div class="dk-page">
  <header class="lib-hdr">
    <div class="dk-tags"><span class="dk-tag">Wallpaper library</span><span class="dk-tag">animated half</span><span class="dk-tag">OD-0109</span><span class="dk-tag">behind the liquid glass</span><span class="dk-tag">dark and light</span></div>
    <h1>Animated wallpapers</h1>
    <p>@@NWORD@@ wallpapers that move, in three tiers for three tastes: vibrant, balanced and minimal. Every tile below is drawn live, and only the tiles on screen are drawn. Open one to see it large, behind the Marrow window&#8217;s liquid glass, and to set its colour pack (or match your accent colour), its speed, how lively it moves, one more option of its own, and four premium options: Dynamic (follow the time of day), Perspective, and Focus. Every animation is a seamless loop, proved on this page, and every shader is plain maths that ports to Skia&#8217;s SkSL, with its packs and options as data. Your choices stay in this browser, if it allows storing them; this page sends nothing.</p>
    <div class="stats">
      <div class="stat dk-glass"><span>Wallpapers</span><b>@@NW@@</b><small>@@NV@@ vibrant, @@NB@@ balanced, @@NM@@ minimal</small></div>
      <div class="stat dk-glass"><span>Colour packs</span><b id="sPacks">@@NPACK@@</b><small>five named packs and Match accent, each with a dark and a light palette, crossfaded in OKLab</small></div>
      <div class="stat dk-glass"><span>Loop proof</span><b id="sProof">proving</b><small id="sProofs">frame 0 against frame T</small></div>
      <div class="stat dk-glass"><span>Frame cost</span><b id="sCost">measuring</b><small id="sCosts">at 1280 x 800</small></div>
    </div>
  </header>
  <div class="tb dk-glass" role="toolbar" aria-label="Library controls">
    <div class="dk-grp"><span class="dk-glab">Show</span><div id="segTier"></div></div>
    <div class="dk-grp"><span class="dk-glab">Theme</span><div id="segT"></div></div>
    <button class="sw" id="play" type="button" role="switch" aria-checked="true"><span class="tr"></span>Motion</button>
    <span class="dk-sp"></span>
    <span class="note">Click a wallpaper for the large view</span>
  </div>
  <div id="tiers" style="display:flex;flex-direction:column;gap:18px"></div>
  <div class="dk-notes">
    <div class="dk-note dk-glass"><h3>How a wallpaper ports</h3><p>Each wallpaper is one function, <code>vec3 wall(vec2 p, float t)</code>, plus the shared maths (OKLab, gradient noise, the loop helpers). It reads these uniforms: <code>u_pal[6]</code> (the colour pack in OKLab), <code>u_dark</code>, <code>u_opt</code> (motion and the second option), <code>u_px</code>, and for the premium options <code>u_tilt</code>, <code>u_sun</code> and <code>u_focus</code>. It is written in the subset of GLSL ES 3 that SkSL runtime effects accept: maths only, no textures, no derivatives, no preprocessor, constant loop bounds. The build checks every file against that list. All of it, with the packs and options, is in the JSON block <code>wallpapers-json</code> in this page.</p></div>
    <div class="dk-note dk-glass"><h3>Seamless loops, proved here</h3><p>Every motion is built from <code>a = TAU * t / T</code>: whole multiples of it, and closed loops through noise space. The build rejects any other use of the clock. The page renders each wallpaper at <code>t = 0</code> and at <code>t = T</code>, for every pack and Match accent in dark and light, at both ends of the options, at the four times of day, and at full tilt and full focus, and reports the largest difference in 8-bit pixel values. It also renders <code>t = T/2</code>, to show that the frame really moves. Speed scales a phase that is accumulated and wrapped, so changing it never jumps the picture.</p></div>
    <div class="dk-note dk-glass"><h3>Packs, options and the theme</h3><p>A pack is six colours in OKLCH on the wallpaper&#8217;s own roles, with a dark and a light palette brought into gamut by lowering chroma only. Each wallpaper has a rich pack, a quiet pack and three in between; the first is its default. On a pack or theme change the shader&#8217;s palette eases from the old colours to the new through OKLab, so nothing cuts. Every wallpaper has Motion (calm to lively) and one option suited to it. Speed runs from 0.25x to 2.5x.</p></div>
    <div class="dk-note dk-glass"><h3>The four premium options</h3><p><b>Match accent</b> is a sixth pack built at run time from the accent colour (the page starts at Windows 11&#8217;s #005FB8 and offers a picker): the accent&#8217;s hue goes to the wallpaper&#8217;s hero colour and harmonious companions follow in OKLCH, on the wallpaper&#8217;s own lightness and chroma roles. <b>Dynamic</b> eases the palette and the light through four looks keyed to the clock, dawn, day, dusk and night, with a scrubber to simulate the time; dark mode leans night and light mode leans day. <b>Perspective</b> shifts a wallpaper&#8217;s layers a few pixels with the pointer (the app: with the window&#8217;s place on the screen); it is on for wallpapers with depth, off for flat ones, and always off under Reduce Motion. <b>Focus</b> blurs the picture from sharp to dreamy. All four are uniforms or palette data, so they cost nothing extra; every change eases.</p></div>
    <div class="dk-note dk-glass"><h3>What this page cannot show</h3><p>The frame costs are browser timings on this machine&#8217;s GPU: one wallpaper at a time at 1280 x 800, frames drawn back to back. An integrated GPU is several times slower, and the glass pass costs extra on top. The app should measure natively, cap the drawing size, and stop drawing when the window is hidden. The mock window&#8217;s words are placeholders.</p></div>
  </div>
  <p class="lic"><b>Licences.</b> The glass in the large view reuses techniques and code of two MIT-licensed projects: <b>liquid-glass</b> by Riz Roze (Copyright (c) 2026 Riz Roze) and <b>liquid-glass-js</b> by Armagan Amcalar (Copyright (c) 2025 Armagan Amcalar). Their MIT notices are kept in full in this page&#8217;s script. The OKLab matrices are Bj&#246;rn Ottosson&#8217;s (public domain). The wallpapers, the noise and the caustic method are written for this page. Every change on this page is transitioned (OD-0076).</p>
</div>
<div class="vw" id="vw" role="dialog" aria-modal="true" aria-labelledby="vname" aria-hidden="true">
  <div class="vbk"></div>
  <div class="vp">
    <div class="vstage"><canvas aria-hidden="true"></canvas><div class="mock" aria-hidden="true"></div></div>
    <aside class="vside dk-glass" aria-label="Wallpaper options">
      <div class="vhead"><div><span class="vtier" id="vtier"></span><h2 id="vname"></h2></div>
        <div class="vnav"><button class="ib" id="vprev" type="button" aria-label="Previous wallpaper">@@PREV@@</button><button class="ib" id="vnext" type="button" aria-label="Next wallpaper">@@NEXT@@</button><button class="ib" id="vclose" type="button" aria-label="Close the large view">@@CLOSE@@</button></div></div>
      <p class="vdesc" id="vdesc"></p>
      <div class="vg"><div class="vl"><span>Colour pack</span><b id="vpackname"></b></div><div class="packs" id="vpacks" role="radiogroup" aria-label="Colour pack"></div></div>
      <div class="vg"><div class="vl"><span>Accent</span><b>for Match accent</b></div><div class="accs" id="vacc" role="radiogroup" aria-label="Accent colour for the Match accent pack"><label class="ac accust" role="radio" aria-checked="false" title="Any accent colour"><input type="color" id="vacccustom" value="#005fb8" aria-label="Custom accent colour"></label></div></div>
      @@SPEED@@
      @@MOTION@@
      @@SECOND@@
      @@FOCUS@@
      <div class="vg" id="vdyn"><div class="vl"><button class="sw" id="vdynsw" type="button" role="switch" aria-checked="false"><span class="tr"></span>Dynamic</button><output id="vdynv" for="vclock"></output></div>
        <div class="fold"><div class="foldin"><input class="rg" type="range" id="vclock" min="0" max="24" step="0.05" aria-label="Time of day"><div class="ends"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div><div class="keys" id="vkeys" role="group" aria-label="Key looks"></div>
        <p class="vnote">The colours and the light ease through dawn, day, dusk and night with the clock. Dark mode leans night, light mode leans day.</p></div></div></div>
      <div class="vg"><button class="sw" id="vpersp" type="button" role="switch" aria-checked="false"><span class="tr"></span>Perspective</button><p class="vnote" id="vperspnote"></p></div>
      <div class="vg"><button class="sw" id="vglass" type="button" role="switch" aria-checked="true"><span class="tr"></span>Behind the glass</button></div>
      <div class="vst">
        <div><span>Loop</span><b id="vloop"></b><small id="vloops"></small></div>
        <div><span>Loop proof</span><b id="vproof"></b><small id="vproofs"></small></div>
        <div class="w"><span>Frame cost</span><b id="vcost"></b><small id="vcosts"></small><small id="vpacksall"></small></div>
      </div>
      <div class="vacts"><button class="dk-btn" id="vreset" type="button">Reset to defaults</button><button class="dk-btn" id="vprove" type="button">Prove and measure these settings</button></div>
      <details class="code"><summary>Shader <i>wall(p, t), SkSL subset</i></summary><pre><code id="vcode"></code></pre><button class="dk-btn" id="vcopy" type="button">Copy the shader</button></details>
    </aside>
  </div>
</div>
"""


def main():
    css = open(os.path.join(HERE, 'lib-page.css'), encoding='utf-8').read()
    boot = ("(function(){var d=JSON.parse(document.getElementById('wallpapers-json').textContent);d.byId={};"
            "d.wallpapers.forEach(function(w){d.byId[w.id]=w;});window.WL_DATA=d;})();\n")
    js = boot + built_gl() + '\n' + open(os.path.join(HERE, 'lib-page.js'), encoding='utf-8').read()
    body = (BODY.replace('@@NPACK@@', str(NPACK)).replace('@@PREV@@', ICON['prev']).replace('@@NEXT@@', ICON['next']).replace('@@CLOSE@@', ICON['close'])
            .replace('@@SPEED@@', slider('vspeed', 'vspeedl', 'Speed', '0.25x', '2.5x', '0.25', '2.5', '0.05'))
            .replace('@@MOTION@@', slider('vmotion', 'vmotionl', 'Motion', 'Calm', 'Lively', '0', '1', '0.01'))
            .replace('@@SECOND@@', slider('vsecond', 'vsecondl', 'Option', '', '', '0', '1', '0.01'))
            .replace('@@FOCUS@@', slider('vfocus', 'vfocusl', 'Focus', 'Sharp', 'Dreamy', '0', '1', '0.01'))
            .replace('@@NWORD@@', NWORD).replace('@@NW@@', str(NW)).replace('@@NV@@', str(NT['vibrant'])).replace('@@NB@@', str(NT['balanced'])).replace('@@NM@@', str(NT['minimal'])))
    blob = json.dumps(DATA, ensure_ascii=True, separators=(',', ':')).replace('</', '<\\/')
    body += '\n<script type="application/json" id="wallpapers-json">%s</script>\n' % blob
    html = page(title='Animated Wallpapers',
                description=NWORD + ' animated wallpapers for the Marrow Windows port in three tiers (vibrant, balanced, minimal), each a seamless loop with colour packs, Match accent, speed, options, Dynamic time of day, Perspective and Focus, drawn live and behind the liquid glass, with the loop proof and frame cost measured in the page.',
                css=css, body=body, js=js)
    assert html.startswith('<title>Animated Wallpapers</title>\n<style>')
    for bad in (r'<!doctype', r'<html[\s>]', r'<body[\s>]', r'<head[\s>]'):
        assert not re.search(bad, html, re.I), bad
    for url in re.findall(r'(?:src|href)="(https?://[^"]+)"', html):
        assert url.startswith('https://fonts.googleapis.com/'), url
    assert not re.search(r'url\(\s*["\']?https?:', html)
    for bad in ('eval(', 'new Function', 'fetch(', 'XMLHttpRequest', 'sendBeacon', 'WebSocket', 'import('):
        assert bad not in js, bad
    for bad in (r'(?i)[a-z]:[\\/]+users', r'(?i)[\\/]users[\\/]', r'(?i)omera', r'(?i)e:[\\/]+droppy', r'[\w.+-]+@[\w-]+\.[\w.-]+', r'(?i)\b(?:sk|ghp|gho|xox[bp])[-_][A-Za-z0-9_-]{16,}'):
        m = re.search(bad, html)
        assert not m, (bad, html[max(0, m.start() - 40):m.end() + 40])
    out = ascii_only(html)
    assert len(out.encode('utf-8')) < 16 * 1024 * 1024
    # WL_OUT redirects the page to a scratch folder for test builds; the share copy is written to ROOT only by the final build
    write_both(os.environ.get('WL_OUT') or ROOT, 'animated', out)


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
