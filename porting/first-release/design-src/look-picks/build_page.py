# Builds the Look Picks decision page: four open look questions (Q-CZ-3, Q-RIM, Q-TEXT, U-06 of
# work/design/open-questions-2026-10-04.md), each drawn working before the owner picks. The decisions-2026-10-03 kit
# (theme, glass cards, segmented controls, pick buttons) and the owner-questions picks bar. Writes look-picks.html (full
# page, for local viewing) and share/look-picks.html (no <html>/<head>/<body>, <title> first, everything inline, Google
# Fonts the only external file, pictures as data: URIs).
# Inputs, each checked by sha256 where it is a locked source:
#   jewel-knob-locked/strike-engine.js, knob-variants.js, strike-light-locked.html (lines 284-865), liquid-palettes.json
#   decisions-2026-10-03/bubble-rim/src/rim_logic.js (its fragment shader)
#   accent-palette-locked.json; src/avalonia-ref (Avalonia 12.1.2, MIT): ColorView.xaml icons, FluentColorPalette.cs
#   src/text-png (SkiaSharp renders by src/textsmooth)
import base64, hashlib, html, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402

LOCK = 'E:/Droppy/work/design/jewel-knob-locked'
SHA = {
    LOCK + '/strike-engine.js': 'ce2da0566ae9f611141ed9c3b744c702061283007d25b87ccc6eb25b9328a518',
    LOCK + '/knob-variants.js': '42672728ee259efb5d89db291f7ea03f24bc1125d65f2405a1431c7a0173861e',
    LOCK + '/strike-light-locked.html': 'd386ee71fca450fed5338427db62d90c89e3cb0abfa210eed51c3fa9729f7df5',
    LOCK + '/liquid-palettes.json': 'a600baefbcf22aca861392456ff4d9c05088c37627fe30e95cd5142d5c441f15',
    'E:/Droppy/work/design/decisions-2026-10-03/bubble-rim/src/rim_logic.js': 'f8b7f61d5b8759af98b270d2f5145dc8c78a3e44c08c698ec2964241b91ebb99',
}


def read(p):
    b = open(p, 'rb').read()
    if p in SHA:
        assert hashlib.sha256(b).hexdigest() == SHA[p], 'changed: ' + p
    return b.decode('utf-8')


def rep(s, a, b):
    assert s.count(a) == 1, (a[:100], s.count(a))
    return s.replace(a, b)


esc = lambda s: html.escape(s, quote=True)

# ---------------------------------------------------------------- the locked strike, bundled into one classic script
kv = read(LOCK + '/knob-variants.js')
ORB_IDS = ['neural-plasma', 'violet-ember', 'iridescent-opal', 'chromatic-metal', 'refractive-gel']
kv = rep(kv, "import set1 from './knob-orbs-1.js';\nimport set2 from './knob-orbs-2.js';",
         "// LOOK PICKS bundle: the knob orbs are not drawn on this page, so only their ids and names are kept\nconst set1 = %s, set2 = [];" % json.dumps([{'id': i, 'name': i} for i in ORB_IDS]))
kv = kv.replace('export function ', 'function ').replace('export const ', 'const ')
for line in ('export { mixHex };', 'export { ORB };', 'export default VARIANTS;'):
    kv = rep(kv, line, '')
assert 'export ' not in kv and 'import ' not in kv
se = rep(read(LOCK + '/strike-engine.js'), 'export default Component;', '')
assert 'export ' not in se and 'import ' not in se
pg = read(LOCK + '/strike-light-locked.html').split('\n')
assert pg[283].startswith('const FX_NONE') and pg[865].startswith('const wave'), (pg[283][:30], pg[865][:30])
excerpt = '\n'.join(pg[283:865])
STRIKE = ('// ---- the LOCKED strike (jewel-knob-locked): knob-variants.js, strike-engine.js and strike-light-locked.html lines 284-865, unchanged\n'
          '(function () {\nconst KV = (function () {\n' + kv + '\nreturn { liquidOf, strikeTint, mixTint };\n})();\n'
          'const Component = (function () {\n' + se + '\nreturn Component;\n})();\n'
          'const { liquidOf, strikeTint, mixTint } = KV;\n' + excerpt + '\n'
          'window.Strike = { E, FX, VARS, tintOf, SEED, OFF, BUB_SM, liquidOf };\n})();\n')

# ---------------------------------------------------------------- the v22 liquid shader (from rim_logic.js)
rl = read('E:/Droppy/work/design/decisions-2026-10-03/bubble-rim/src/rim_logic.js')
FS = rl[rl.index('const FS = `') + len('const FS = `'):]
FS = FS[:FS.index('}`;') + 1]
FS = rep(FS, 'uniform int mode; uniform vec3 kk;', 'uniform int mode; uniform vec3 kk;\nuniform sampler2D gradTex2; uniform float gmix; uniform sampler2D bubTex; uniform int bubMode;')
FS = rep(FS, 'vec3 g = saturateBright(textureLod(gradTex, vec2(clamp(p.x/size.x, 0.0, 1.0), 0.5), 0.0).rgb, breathe.x, breathe.y);',
         'vec2 gu = vec2(clamp(p.x/size.x, 0.0, 1.0), 0.5);\n  vec3 g = saturateBright(mix(textureLod(gradTex, gu, 0.0).rgb, textureLod(gradTex2, gu, 0.0).rgb, gmix), breathe.x, breathe.y);')
FS = rep(FS, 'if (withBub) c = bubbles(p, c);',
         'if (withBub) { if (bubMode == 1) c = bubbles(p, c); else if (bubMode == 2) c = over(c, textureLod(bubTex, clamp(p/size, 0.0, 1.0), 0.0)); }')
LIQ = rep(open(os.path.join(HERE, 'lp-liquid.js'), encoding='utf-8').read(), '__FS__', json.dumps(FS))

# ---------------------------------------------------------------- data
P = json.loads(read(LOCK + '/liquid-palettes.json'))
MAKERS = ['Claude', 'OpenAI', 'DeepSeek', 'Gemini', 'Grok']
PAL = {m: {t: {'high': {k: P[m][t]['high'][k] for k in ('deep', 'mid', 'light', 'highlight')}} for t in ('dark', 'light')} for m in MAKERS}
acc = json.load(open('E:/Droppy/work/design/accent-palette-locked.json', encoding='utf-8'))
ACCENTS = [[a['id'], a['name'], a['group'], a['dark']['hex'], a['light']['hex']] for a in acc['accents']]
assert len(ACCENTS) == 22
fcp = open(os.path.join(HERE, 'avalonia-ref', 'FluentColorPalette.cs'), encoding='utf-8').read()
FL = re.findall(r'/\* (#[0-9A-F]{6}) \*/', fcp)
assert len(FL) == 48
FLUENT = [FL[i * 8:(i + 1) * 8] for i in range(6)]
cvx = open(os.path.join(HERE, 'avalonia-ref', 'ColorView.xaml'), encoding='utf-8').read()
ICONS = []
for key in ('ColorViewSpectrumIconGeometry', 'ColorViewPaletteIconGeometry', 'ColorViewComponentsIconGeometry'):
    m = re.search(r'<PathGeometry x:Key="%s">(.*?)</PathGeometry>' % key, cvx, re.S)
    ICONS.append(' '.join(m.group(1).split()))
PICK = open(os.path.join(HERE, 'lp-pickers.js'), encoding='utf-8').read()
PICK = rep(rep(rep(PICK, '__ACCENTS__', json.dumps(ACCENTS)), '__FLUENT__', json.dumps(FLUENT)), '__ICONS__', json.dumps(ICONS))
TD = os.path.join(HERE, 'text-png')
TEXT = {}
for f in sorted(os.listdir(TD)):
    if f.endswith('.png'):
        TEXT[f[:-4]] = 'data:image/png;base64,' + base64.b64encode(open(os.path.join(TD, f), 'rb').read()).decode('ascii')
assert len(TEXT) == 24, len(TEXT)
TLOG = open(os.path.join(TD, 'render-log.txt'), encoding='utf-8').read()

# ---------------------------------------------------------------- the questions
CK = '<svg viewBox="0 0 12 12"><path d="M2.5 6.3 5 8.6l4.6-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>'
QS = [
    dict(id='Q-CZ-3', q='Which colour picker opens from the Uiverse light\u2019s Custom colour wells?', rec='B',
         swift='Nothing: the Mac app has no Uiverse light and no colour picker (customization SPEC CZ-12.6 leaves it open).',
         intro='Settings > Customization > Uiverse light > Colours: Custom shows two wells, One side and The other. Tap a well: the picker opens in Marrow\u2019s glass popover with the Mac\u2019s arrow tip (OD-0095), card 13 pt below the well. Pick a colour and the light above follows. Switch pickers with the letters; tap the other well to move the popover to it, or the same well to close it.',
         opts=[('A', 'Avalonia\u2019s own ColorView', 'The 12.1.2 control as its Fluent template draws it: three tabs (a spectrum box or ring with value and alpha sliders; the 48-colour Fluent palette; RGB or HSV fields with a hex box) over a previewer. 350 px wide, in the Fluent look (3 px corners, an accent pipe under the tab), not Marrow\u2019s. One package pin.'),
               ('B', 'Marrow\u2019s glass picker', 'Built for the port in the app\u2019s own look: a saturation and brightness square, hue and opacity strips, a hex field and your recent colours, 260 px wide. New code with its own tests; nothing to pin.'),
               ('C', 'A colour wheel, like the Mac\u2019s', 'Hue around the wheel and saturation outward, as macOS\u2019s colour panel shows it, with a brightness strip, opacity and a hex field. Familiar from the Mac; an exact shade is harder to hit than on a square.'),
               ('D', 'Swatches with a custom area', 'The 22 accent colours (Classic and Signature) as the swatch rows draw them, then a compact custom area: a hue strip, a shade strip and a hex field. Quick, matching picks; fewer free colours.')],
         note='Opacity: the spec stores each colour as #RRGGBB (CZ-12.6). A, B and C offer opacity, so picking one of them also means storing #RRGGBBAA and letting the light draw it; D has none. The open-questions list recommended A; drawn side by side, B is recommended here (see its reason in the notes).'),
    dict(id='Q-RIM', q='The slider\u2019s bubbles show rainbow teardrops near the tube\u2019s rim. Keep them, soften the rim map, or draw the bubbles on top?', rec='B',
         swift='Nothing: the liquid slider is beyond the Mac app (owner-ruled). The teardrops come from the locked glass map (v22).',
         intro='The app\u2019s track, 298 x 34 at High with 9 bubbles, three ways at once. The loupes under the tubes magnify the same spot on all three: move the pointer over any tube (or drag on a phone), or send them to the left rim, the top edge or the right end. Slow it down or pause it to catch a bubble at the rim.',
         opts=[('A', 'Keep, as locked', 'v22 exactly: the bubbles sit inside the liquid the glass map bends (red, green and blue by 36, 32 and 28 px). Near the rim a round bubble folds into a slanted teardrop with a rainbow edge.'),
               ('B', 'Soften the rim map', 'The same glass with a gentler fold: the map\u2019s blur doubled and the bend under half as strong and nearly even (15, 14.5, 14). Bubbles stay round at the rim and keep a softer lens; the liquid barely changes.'),
               ('C', 'Bubbles on top of the bend', 'The liquid bent exactly as now; the bubbles drawn after the bend, where they are. Round everywhere, but they no longer sit in the glass: no lens on them at the rim.')],
         note=''),
    dict(id='Q-TEXT', q='Text smoothing on Windows: greyscale everywhere, subpixel, or mixed?', rec='A',
         swift='The Swift states none; macOS draws all text greyscale. Avalonia 12.1.2 asks Skia for subpixel text by default (GlyphRunImpl.cs: SubpixelAntialias, hinting Full).',
         intro='Real renders, not a browser imitation: SkiaSharp 3.119.4 (the version the port resolves) drew the same small scene in each mode, with Segoe UI Variable at the app\u2019s sizes, at 100 % and 150 % scale, dark and light. Each picture is shown at 1:1 device pixels. Move the pointer over any picture (or drag) and the magnifiers below show that spot on all three, pixel by pixel.',
         opts=[('A', 'Greyscale everywhere', 'One weight on every surface, still or moving, and no colour fringes: the look macOS has everywhere.'),
               ('B', 'Subpixel everywhere', 'The crispest small text on a 100 % RGB-stripe screen, with coloured edges. Skia cannot keep subpixel text in a translucent layer, so on glass this is B\u2019s best case, not a promise.'),
               ('C', 'Mixed', 'Subpixel on opaque surfaces (the chat pane), greyscale on glass (sidebar, composer, popovers) and on moving text (the drifting effort title). Text changes weight between surfaces.')],
         note='A screenshot shows subpixel text correctly only at 100 % zoom on an RGB-stripe monitor, viewed at 1:1. These pictures are drawn for an RGB stripe; on a BGR, OLED or rotated screen, or zoomed, B\u2019s fringes look wrong. The magnifier shows the pixels themselves.'),
    dict(id='U-06', q='The lightning strike\u2019s bubbles: inside the bent liquid under the lightning, or above it, unbent?', rec='B',
         swift='Nothing; OD-0093 locks the strike page and the port is built test-first against it. Strike SPEC 6.4 records the difference for your eye.',
         intro='Fast mode on the app\u2019s track, live, from the locked strike engine with Flash streaks + Ink. Each row runs one timeline twice, so the strikes and every burst are identical; only the bubbles\u2019 layer differs. The top row is a dark liquid (Claude), the bottom a pale one (OpenAI, where the strike is ink). Slow it down to watch a bubble near a bolt.',
         opts=[('A', 'Inside the liquid, under the lightning', 'What the port draws today (ED-248, ED-252): the bubbles and their burst rings are part of the liquid the glass bends, and the lightning lies over them. At the rim they fold like the liquid (the Q-RIM teardrops).'),
               ('B', 'Above the lightning, unbent', 'What the locked page draws (OD-0093): bubbles and rings over the strike, never bent, always round and crisp. The port needs one more layer for it.')],
         note='Every bubble a lit line touches bursts first in both (OD-0078), so a whole bubble under a lit bolt is rare; the difference shows on the bubbles beside the bolt, on the rings, and at the rim.'),
]


def opt_html(q, o):
    letter, title, detail = o
    rc = '<span class="rc">Recommended</span>' if letter == q['rec'] else ''
    return ('<div class="dk-opt" data-o="%s" aria-current="false"><div class="oh"><span class="ol">%s</span><span class="ot">%s</span>%s</div><span class="od">%s</span>'
            '<button type="button" class="dk-pick pk" data-pick-q="%s" data-pick-v="%s" aria-pressed="false" aria-label="Pick %s for %s">'
            '<span class="ck">%s</span><span class="lb"><span class="a">Pick %s</span><span class="b">Picked</span></span></button></div>'
            % (letter, letter, esc(title), rc, esc(detail), q['id'], letter, letter, q['id'], CK, letter))


def seg(id_, label):
    return '<div class="dk-grp"><span class="dk-glab">%s</span><div id="%s"></div></div>' % (esc(label), id_)


CONTROLS = {
    'Q-CZ-3': seg('czO', 'Picker') + '<div class="czA" id="czA">' + seg('czShape', 'Spectrum') + seg('czTabs', 'Tabs') + '</div>',
    'Q-RIM': seg('rimM', 'Liquid') + seg('rimZ', 'Loupe') + seg('rimW', 'Look at') + seg('rimS', 'Speed'),
    'Q-TEXT': seg('txS', 'Display scale') + seg('txK', 'Show') + seg('txZ', 'Magnifier'),
    'U-06': seg('u6S', 'Speed'),
}
STAGE = {'Q-CZ-3': '<div class="cz-stage" id="cz"></div>', 'Q-RIM': '<div class="rim" id="rim"></div>', 'Q-TEXT': '<div class="tx" id="tx"></div>', 'U-06': '<div class="u6" id="u6"></div>'}

NOTES = {
    'Q-CZ-3': [('What is drawn', '<p><b>A</b> follows Avalonia 12.1.2\u2019s Fluent templates (ColorView.xaml, ColorSpectrum.xaml, ColorSlider.xaml, ColorPreviewer.xaml) and their defaults (ColorView.Properties.cs): HueSaturation box drawn at full value, 16 px selector with the light or dark ring by contrast, 20 px sliders with 3 px thumbs, the six-column Fluent palette, the four component rows and the previewer\u2019s two accents each side (value \u00b10.1, \u00b10.2). Its colours are the Fluent theme\u2019s resources (BaseColorsPalette.xaml); the tab pipe uses Windows\u2019 accent (Avalonia\u2019s fallback #0078D7). Avalonia\u2019s own ColorPicker is a 64 x 32 drop-down button that opens this same view in a flyout above it; here the app\u2019s wells open it in the glass popover instead.</p><p><b>B</b>, <b>C</b> and <b>D</b> are new designs in the page\u2019s glass, made for this question (ENGINEERING DECISION on every size). The recent colours in B are this visit\u2019s picks.</p>'),
               ('Why B', '<p>It is the only one that reads as part of the app: Marrow\u2019s glass, capsules and swatch rows, every part you asked for (square, hue, opacity, hex, recent) in 260 px. A brings Fluent\u2019s tabs, square corners and accent pipe into a Mac-styled popover and is the widest; C is lovely but imprecise; D is quick but limits the colours.</p>'),
               ('Not drawn faithfully', '<p>A\u2019s text fields use the browser\u2019s text box with the template\u2019s sizes and colours, not Avalonia\u2019s TextBox and NumericUpDown; its focus visuals, the touch-sized 48 px selector and the tooltips are left out. The Uiverse light above is the playground\u2019s CSS without its Halftone dots, at the typing brightness.</p>')],
    'Q-RIM': [('What is drawn', '<p>Each tube runs the locked liquid shader (effort-liquid-locked-v22, ported in the bubble-rim board\u2019s rim_logic.js, the shader the port follows) with v22\u2019s never-repeating bubbles; only the last step differs between A, B and C. The loupe reads the tube\u2019s own device pixels unsmoothed and outlines the tube\u2019s edge.</p>'),
              ('Why B', '<p>It removes the teardrops and the rainbow edges at their cause, the fold of the map near the rim, while every bubble stays inside the glass, so the liquid still reads as one material. C is the fallback if B still shows a fringe on the real track.</p>'),
              ('Not drawn faithfully', '<p>The knob is a still stand-in in the maker\u2019s colours (the live Jewel knob is on the strike page). The tube\u2019s glass edge and gloss are CSS over the liquid, as on the bubble-rim board, and the loupe shows the liquid only.</p>')],
    'Q-TEXT': [('What is drawn', '<p>src/textsmooth renders with SkiaSharp\u2019s own rasteriser: SKFontEdging.Antialias (greyscale) or SubpixelAntialias on an RGB-stripe surface, every other setting Avalonia\u2019s default (hinting Full, subpixel positioning, baseline snap). Segoe UI Variable Text at 11, 13 and 15, Display at 34 (render log: %s). The scene: the sidebar (glass over the wallpaper), the chat paragraph (an opaque pane), the composer and its placeholder (glass), the effort title in a glass pill (glass, and it drifts).</p>' % esc(' \u00b7 '.join(TLOG.strip().split('\n')))),
               ('Why A', '<p>One weight on every surface, still or moving. The port draws much of its text on glass and moves it, where subpixel text cannot be kept, so B and C change weight between still and moving text. Greyscale is what macOS draws, so A is also the faithful look.</p>'),
               ('Not drawn faithfully', '<p>B\u2019s glass text is drawn straight onto the final pixels, its best case; in the app, text in a translucent layer may fall back to greyscale or take fringes against the wrong colour. What the port draws today is UNRESOLVED: Avalonia asks for subpixel text, but Skia only gives it on an opaque layer with a known pixel layout. Moving text is shown still.</p>')],
    'U-06': [('What is drawn', '<p>The lightning, the bubbles, their bursts and the ink are the locked code itself (strike-engine.js and the locked page\u2019s engine, sha256-checked and bundled unchanged), on its own clock, with the locked offset and seed. The liquid under both is the v22 shader the port follows, its glass map as locked. In A the engine\u2019s bubble layer is sampled inside the liquid, before the bend (the port, strike SPEC 6.4); in B the same layer lies above the strike, unbent (the page).</p>'),
             ('Why B', '<p>It is what the locked page draws, and OD-0093 locked that page: the port is meant to match it. The bubbles stay round and crisp next to the bolts and at the rim; the cost is one more layer in the port.</p>'),
             ('Not drawn faithfully', '<p>The knob is a still stand-in: the live Jewel knob that reads the strike (OD-0090) is left out here, so the charge and flare on the knob are missing. The locked page\u2019s own liquid is CSS without a glass bend; here both tubes use the port\u2019s bent liquid, so only the bubbles\u2019 layer differs.</p>')],
}


def q_html(q):
    notes = ''.join('<div class="dk-note"><h3>%s</h3>%s</div>' % (esc(h), b) for h, b in NOTES[q['id']])
    note = '<p class="qnote">%s</p>' % esc(q['note']) if q['note'] else ''
    return ('<section class="dk-q dk-glass" id="%s" data-q="%s" data-rec="%s" aria-labelledby="%s-h">'
            '<div class="dk-qh"><span class="dk-qn">%s</span><h2 id="%s-h">%s</h2></div>'
            '<p class="swift"><span class="sl">What the Mac app says</span><span class="st">%s</span></p>'
            '<p class="dk-qd">%s</p><div class="ctl">%s</div>%s%s'
            '<div class="dk-opts" role="group" aria-label="Options for %s">%s</div><div class="dk-notes">%s</div></section>'
            % (q['id'], q['id'], q['rec'], q['id'], q['id'], q['id'], esc(q['q']), esc(q['swift']), esc(q['intro']), CONTROLS[q['id']], STAGE[q['id']], note,
               q['id'], ''.join(opt_html(q, o) for o in q['opts']), notes))


# ---------------------------------------------------------------- tokens: light on :root, dark twice (system and chosen)
TOK = [
    ('stage', 'radial-gradient(420px 260px at 12% 0%,#cfe0f0 0%,transparent 70%),radial-gradient(420px 300px at 100% 100%,#efdcc8 0%,transparent 70%),#e4e8ee',
     'radial-gradient(420px 260px at 12% 0%,#24435a 0%,transparent 70%),radial-gradient(420px 300px at 100% 100%,#4a3524 0%,transparent 70%),#141a22'),
    ('pane', 'rgba(255,255,255,.55)', 'rgba(40,40,44,.55)'),
    ('pop', 'rgba(250,250,252,.74)', 'rgba(44,44,48,.72)'),
    ('popE', 'rgba(0,0,0,.10)', 'rgba(255,255,255,.16)'),
    ('popHi', 'rgba(255,255,255,.8)', 'rgba(255,255,255,.08)'),
    ('popSh', '0 16px 40px rgba(30,40,60,.20),0 2px 8px rgba(30,40,60,.10)', '0 16px 40px rgba(0,0,0,.40),0 2px 8px rgba(0,0,0,.24)'),
    ('tx1', '#1C1C1E', '#F4F4F6'), ('tx2', 'rgba(28,28,30,.62)', 'rgba(244,244,246,.62)'),
    ('cap', 'rgba(255,255,255,.55)', 'rgba(0,0,0,.2)'), ('caph', 'rgba(255,255,255,.82)', 'rgba(0,0,0,.3)'),
    ('pill', '#FFFFFF', 'rgba(255,255,255,.16)'), ('rim', 'rgba(0,0,0,.22)', 'rgba(255,255,255,.22)'),
    ('ring', 'rgba(0,0,0,.78)', 'rgba(255,255,255,.92)'), ('tile', 'rgba(0,0,0,.05)', 'rgba(255,255,255,.07)'),
    ('chk', 'rgba(128,128,128,.22)', 'rgba(128,128,128,.32)'),
    ('plate', 'rgba(255,255,255,.34)', 'rgba(255,255,255,.045)'),
    ('plateSh', 'inset 0 1px 0 rgba(255,255,255,.7),inset 0 0 0 1px rgba(0,0,0,.06),0 6px 20px rgba(80,50,30,.08)', 'inset 0 1px 0 rgba(255,255,255,.1),inset 0 0 0 1px rgba(255,255,255,.08),0 6px 20px rgba(0,0,0,.18)'),
    ('auTm', '1.2', '1'),
    # Avalonia Fluent (BaseColorsPalette.xaml, Default and Dark)
    ('avBaseHigh', '#000000', '#FFFFFF'), ('avBaseMed', 'rgba(0,0,0,.6)', 'rgba(255,255,255,.6)'), ('avBaseLow', 'rgba(0,0,0,.2)', 'rgba(255,255,255,.2)'),
    ('avListLow', 'rgba(0,0,0,.098)', 'rgba(255,255,255,.098)'), ('avAltML', 'rgba(255,255,255,.4)', 'rgba(0,0,0,.4)'), ('avDis', '#7A7A7A', '#858585'),
    ('avAccent', '#0078D7', '#0078D7'), ('avChk', 'rgba(128,128,128,.098)', 'rgba(128,128,128,.098)'),
    ('tubeBg', 'rgba(24,34,48,.1)', 'rgba(0,0,0,.25)'),
    ('tubeSh', '0 0 0 1px rgba(255,255,255,.55),0 6px 16px rgba(20,32,50,.16),0 1px 0 rgba(255,255,255,.6)', '0 0 0 1px rgba(255,255,255,.18),0 4px 12px rgba(0,0,0,.25),0 1px 0 rgba(255,255,255,.25)'),
    ('lpbg', '#d9dde3', '#0b0f14'), ('lpframe', 'rgba(0,0,0,.55)', 'rgba(255,255,255,.7)'),
]


def tokens():
    L = ';'.join('--%s:%s' % (k, l) for k, l, d in TOK)
    D = ';'.join('--%s:%s' % (k, d) for k, l, d in TOK)
    return (':root{%s}\n@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){%s}}\n:root[data-theme="dark"]{%s}\n' % (L, D, D))


PAGE_CSS = tokens() + open(os.path.join(HERE, 'lp-page.css'), encoding='utf-8').read()

BODY = r"""
<div class="dk-page">
  <header class="dk-hdr">
    <div class="dk-tags"><span class="dk-tag">4 look questions</span><span class="dk-tag">2026-10-04</span><span class="dk-tag">all live</span></div>
    <h1>Look picks</h1>
    <p>You asked to see each option drawn before choosing. Every option below is drawn working: the colour pickers pick, the liquid and the strike run live, and the text is real SkiaSharp output. Tap Pick on the one you want, or Take all recommended and change the ones you disagree with. The bar at the bottom writes one line; paste it to the lead. This page sends nothing.</p>
    <nav class="links" aria-label="Questions">%(links)s</nav>
  </header>
  <div class="tb dk-glass" role="toolbar" aria-label="Actions">
    <button class="dk-btn pri" id="all" type="button"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.6l4.6-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>Take all recommended</button>
    <button class="dk-btn" id="clear" type="button">Clear picks</button>
    <span class="dk-sp"></span>
    <div class="dk-grp"><span class="dk-glab">Theme</span><div id="segT"></div></div>
  </div>
  %(qs)s
  <p class="dk-foot">Your picks stay in this browser only, if it allows storing them. Sources: work/design/open-questions-2026-10-04.md, the customization and strike specs, the locked strike and liquid code (jewel-knob-locked, bubble-rim), Avalonia 12.1.2\u2019s ColorPicker sources (MIT) and the SkiaSharp renders in src/textsmooth. Every change on this page is transitioned (OD-0076).</p>
</div>
"""
BAR = ('<div class="dk-picks dk-glass" id="bar" role="region" aria-label="Your picks"><span class="cnt" id="cnt" aria-live="polite"></span>'
       '<code class="pline" id="line" aria-label="The line to paste to the lead"></code><button class="dk-btn" id="copy" type="button">Copy</button></div>')


def main():
    links = ''.join('<a href="#%s">%s <b>?</b></a>' % (q['id'], q['id']) for q in QS)
    body = (BODY % dict(links=links, qs=''.join(q_html(q) for q in QS))) + BAR + '\n'
    js = ('window.LP_PAL = %s;\nwindow.LP_TEXT = %s;\n' % (json.dumps(PAL), json.dumps(TEXT)) + STRIKE + LIQ + PICK
          + ''.join(open(os.path.join(HERE, f), encoding='utf-8').read() for f in ('lp-rim.js', 'lp-strike.js', 'lp-text.js', 'lp-page.js')))
    out = page(title='Look Picks', description='Four open look questions of the Marrow Windows port, each drawn working before the owner picks.',
               css=PAGE_CSS, body=body, js=js)
    k = out.index('<script>')
    out = out[:k].encode('ascii', 'xmlcharrefreplace').decode('ascii') + re.sub(r'[^\x00-\x7f]', lambda m: '\\u%04x' % ord(m.group(0)), out[k:])
    write_both(ROOT, 'look-picks', out)


main()
