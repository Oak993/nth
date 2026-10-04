# Generates E:/Droppy/work/design/customization/customization.html: Settings > Customization (OD-0085, OD-0089) as an
# interactive review page. The composer and its light are the Composer Lights playground's own: its generator is run
# here (up to the point where it writes its board) for the composer's CSS and markup, the light's registered
# properties and beam.js + logic.js with their tables; cz.js subclasses its Component to draw the Settings window.
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), 'customization.html')
PG = 'E:/Droppy/work/design/composer-lights/playground/src/gen.py'

src = open(PG, encoding='utf-8').read()
cut = src.index("html = '''<!doctype html>")
ns = {'__file__': PG, '__name__': 'playground'}
exec(compile(src[:cut], PG, 'exec'), ns)

css_pg = ns['css']
a, b = css_pg.index('/* the composer, as the port draws it'), css_pg.index('/* control panel */')
composer_css = css_pg[a:b]
reg_css, board_reg_css = ns['reg_css'], ns['board_reg_css']
dark_css, light_css = ns['dark_css'], ns['light_css']
WIN_D, WIN_L = ns['WIN_D'], ns['WIN_L']
BOARD_REG = ns['BOARD_REG']

# the composer markup, with the runtime's attribute names (data-on*, data-ref)
INSTANCE = re.sub(r' on([A-Z][A-Za-z]*)="', lambda m: ' data-on' + m.group(1).lower() + '="', ns['INSTANCE'])
INSTANCE = INSTANCE.replace(' ref="', ' data-ref="')
assert INSTANCE.count(ns['WDOTS']) == 1
# the working dots: the playground's markup (ComposerWorkingDots.swift, 180 animated elements at this width) drawn on
# one canvas per composer instead, with the same geometry, pulse curve, wave delays and fade (cz.js dotsTick)
INSTANCE = INSTANCE.replace(ns['WDOTS'], '<div class="wdots" aria-hidden="true"><canvas class="wdotc" data-ref="{{i.dotRef}}"></canvas></div>')
m = re.search(r'<svg class="clip".*?</svg>', INSTANCE, re.S)
CLIP = m.group(0)
# the voice orb where the attachment was, the cancel cross in its slot, the glass pill and the attachment button,
# as voice-composer.html draws them (OD-0091)
INSTANCE = INSTANCE.replace(CLIP, '<span class="orbslot" data-ref="{{i.slotRef}}"><button class="xbtn" type="button" tabindex="{{i.xTab}}" aria-label="Cancel voice input" title="Cancel (Esc)" data-onclick="{{i.cancel}}"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.2 3.2 8.8 8.8M8.8 3.2 3.2 8.8"></path></svg></button><span class="orbmove" data-ref="{{i.moveRef}}"><sc-for list="{{i.orbs}}" as="ob"><canvas class="{{ob.cls}}" data-ref="{{ob.ref}}"></canvas></sc-for><span class="orbbtn" role="button" tabindex="0" aria-label="{{i.orbAria}}" aria-pressed="{{i.orbPressed}}" title="Voice input" data-ref="{{i.holdRef}}" data-onclick="{{i.orbTap}}" data-onpointerenter="{{i.orbIn}}" data-onpointerleave="{{i.orbOut}}" data-onpointerdown="{{i.orbDown}}" data-onpointerup="{{i.orbUp}}" data-onkeydown="{{i.orbKey}}"></span></span></span>', 1)
assert INSTANCE.count('<div class="ph">') == 1
INSTANCE = INSTANCE.replace('<div class="ph">', '<button class="attach in" type="button" aria-label="Attach files" title="Attach files">' + '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M10.6 4.6 5.7 9.5a1.55 1.55 0 0 0 2.2 2.2l5-5a3 3 0 0 0-4.25-4.25l-5 5a4.4 4.4 0 0 0 6.2 6.2l4.2-4.2"></path></svg>' + '</button><div class="ph" data-ref="{{i.fieldRef}}">', 1)
assert INSTANCE.count('<div class="tools">') == 1
INSTANCE = INSTANCE.replace('<div class="tools">', '<div class="{{i.pillCls}}" data-ref="{{i.pillRef}}"><button class="px" type="button" tabindex="{{i.pxTab}}" aria-label="Cancel voice input" title="Cancel (Esc)" data-onclick="{{i.cancel}}"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.2 3.2 8.8 8.8M8.8 3.2 3.2 8.8"></path></svg></button><span class="plab" aria-live="polite"><sc-for list="{{i.plabs}}" as="pl"><span class="{{pl.cls}}">{{pl.t}}<i class="lg">&#8194;English</i></span></sc-for></span></div>' + '<div class="tools">', 1)
# OD-0097: the composer light's layers in one wrapper (its opacity is the light's level while you speak), and the
# voice glow under the text and controls, as the voice design draws it
# (the light's outer box, its halo outside the composer, takes the same level)
assert INSTANCE.count('<div class="g-aura"><div class="au-in">') == 1 and INSTANCE.count('<div class="cc">') == 1 and INSTANCE.count('<div class="obox">') == 1
INSTANCE = INSTANCE.replace('<div class="g-aura"><div class="au-in">', '<div class="vlgt" data-ref="{{i.lightRef}}"><div class="g-aura"><div class="au-in">', 1)
INSTANCE = INSTANCE.replace('<div class="obox">', '<div class="obox" data-ref="{{i.lightORef}}">', 1)
INSTANCE = INSTANCE.replace('<div class="cc">', '</div><div class="vglowh" data-keep="1" data-ref="{{i.glowRef}}"></div><div class="cc">', 1)
assert INSTANCE.count('<div class="files">') == 1
INSTANCE = INSTANCE.replace('<div class="files">', '<button class="attach out" type="button" aria-label="Attach files" title="Attach files">' + '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M10.6 4.6 5.7 9.5a1.55 1.55 0 0 0 2.2 2.2l5-5a3 3 0 0 0-4.25-4.25l-5 5a4.4 4.4 0 0 0 6.2 6.2l4.2-4.2"></path></svg>' + '</button><div class="files">', 1)

# the light: beam.js + logic.js with the tables, the composer narrowed to the Settings preview (496 px window,
# 12 px inset): every size the engine draws (the fitted perimeter, the automatic duration, the halo scale) follows CW
js = ns['js']
OLD_CW = 'const CW = 820, CH = 52, CR = 22;'
assert js.count(OLD_CW) == 1
CWV = 472
js = js.replace(OLD_CW, 'let CW = %d; const CH = 52, CR = 22;   // the Settings preview\'s composer (playground: 820); 428 with the attachment outside' % CWV)

# the makers: liquid-palettes.json, effort High
PAL = json.load(open('E:/Droppy/work/design/orb/liquid-palettes.json', encoding='utf-8'))
FIRST = ['Claude', 'OpenAI', 'Gemini', 'DeepSeek', 'Grok', 'NVIDIA']
ORDER = FIRST + [k for k in PAL if k not in FIRST and not k.startswith('_')]
NICE = {'OpenCodeGo': 'OpenCode Go', 'CommandCode': 'Command Code', 'NousResearch': 'Nous Research', 'LiquidAI': 'Liquid AI', 'Zai': 'Z.ai', 'MiniMax': 'MiniMax'}
MAKERS = []
for k in ORDER:
    m = PAL[k]['_meta'].get('model') or ''
    if m.startswith('('): m = ''
    hd, hl = PAL[k]['dark']['high'], PAL[k]['light']['high']
    MAKERS.append([k, NICE.get(k, k), m, hd['deep'], hd['mid'], hd['light'], hd['highlight'], hd['title'], hl['title']])

cz = open(os.path.join(HERE, 'cz.js'), encoding='utf-8').read().replace('/*@MAKERS@*/null', json.dumps(MAKERS, separators=(',', ':')))
cz += '\n' + open(os.path.join(HERE, 'voice.js'), encoding='utf-8').read()
cz += '\n' + open(os.path.join(HERE, 'voicecmp.js'), encoding='utf-8').read()
cz += '\n' + open(os.path.join(HERE, 'voiceopts.js'), encoding='utf-8').read()   # OD-0097: words, light while you speak, spoken language
# the voice design's CSS, carried over from its page (E:/Droppy/work/voice/design/voice-composer.html): the attachment
# button, the glass status pill and the cancel cross, and its glass tokens for both themes
VCH = open('E:/Droppy/work/voice/design/voice-composer.html', encoding='utf-8').read()
def vc_slice(a, b):
    i = VCH.index(a); j = VCH.index(b, i); return VCH[i:j]
VC_CSS = (vc_slice('/* attachment: inside on the left', '/* tools on the right') + vc_slice('.vpill{', '.orbslot{') +
          vc_slice('/* the cancel cross: tucked behind', '.chip{'))
VC_TOK = {}
for blk in re.findall(r'--pgBg:[^\n]*', VCH)[:2]:
    VC_TOK.setdefault('light' if 'light' not in VC_TOK else 'dark', blk.strip())
def vc_tok(names, which):
    blk = VCH.split('@media (prefers-color-scheme: dark)')[0 if which == 'light' else 1]
    return ';'.join(m.group(0) for n in names for m in [re.search('--' + n + r':[^;]+', blk)] if m)
VC_LIGHT = VC_TOK['light'].rstrip(';') + ';' + vc_tok(['hoverfill', 'pressfill'], 'light')
VC_DARK = VC_TOK['dark'].rstrip(';') + ';' + vc_tok(['hoverfill', 'pressfill'], 'dark')
assert '--pgLab' in VC_LIGHT and '--hoverfill' in VC_DARK, (VC_LIGHT, VC_DARK)
import shutil
shutil.copy('E:/Droppy/work/voice/design/voice-orbs.js', os.path.join(os.path.dirname(HERE), 'voice-orbs.js'))
shutil.copy('E:/Droppy/work/voice/design/voice-glow.js', os.path.join(os.path.dirname(HERE), 'voice-glow.js'))   # the voice glow (OD-0097: the light while you speak is previewed with it)   # published beside the page
LOCK = json.load(open('E:/Droppy/work/design/accent-palette-locked.json', encoding='utf-8'))
cz = cz.replace('/*@LOCKACC@*/null', json.dumps([[a['id'], a['name'], a['group'], a['dark']['hex'], a['light']['hex']] for a in LOCK['accents']], separators=(',', ':')))
runtime = open(os.path.join(HERE, 'runtime.js'), encoding='utf-8').read()
for part in (js, cz, runtime):
    assert '</script' not in part.lower() and '<!--' not in part

# ---------------- CSS ----------------
D3_DARK = '--search:rgba(255,255,255,.07);--selrow:rgba(255,255,255,.1);--head:rgba(255,255,255,.84);--tilesel:rgba(255,255,255,.1);--wedge:rgba(255,255,255,.14);--tx:#ECECEC;--tx2:rgba(236,236,236,.7);--cardfill:rgba(255,255,255,.03);--div:rgba(255,255,255,.12);--cap:rgba(0,0,0,.2);--caph:rgba(0,0,0,.3);--pill:rgba(255,255,255,.16);--tile:rgba(255,255,255,.07);--sel:rgba(255,255,255,.24);--trk:rgba(255,255,255,.1);--trkfill:rgba(255,255,255,.24);--swoff:rgba(255,255,255,.2);--popbg:#1E1E1E;--popedge:rgba(255,255,255,.2);--hov:rgba(255,255,255,.08);--prs:rgba(255,255,255,.14);--ring:rgba(255,255,255,.92);--rim:rgba(255,255,255,.22)'
D3_LIGHT = '--search:rgba(0,0,0,.06);--selrow:rgba(0,0,0,.11);--head:rgba(0,0,0,.8);--tilesel:rgba(0,0,0,.07);--wedge:rgba(0,0,0,.12);--tx:#1C1C1E;--tx2:rgba(28,28,30,.68);--cardfill:rgba(0,0,0,.03);--div:rgba(0,0,0,.1);--cap:rgba(255,255,255,.55);--caph:rgba(255,255,255,.82);--pill:#FFFFFF;--tile:rgba(0,0,0,.05);--sel:rgba(0,0,0,.72);--trk:rgba(0,0,0,.1);--trkfill:rgba(0,0,0,.72);--swoff:rgba(0,0,0,.16);--popbg:#FFFFFF;--popedge:rgba(0,0,0,.08);--hov:rgba(0,0,0,.05);--prs:rgba(0,0,0,.1);--ring:rgba(0,0,0,.78);--rim:rgba(0,0,0,.22)'
D3_NAMES = [x.split(':')[0] for x in D3_DARK.split(';')]
REG_NAMES = [n for n, i in BOARD_REG]
EXTRA_REG = [n for n in D3_NAMES if n not in REG_NAMES]
extra_reg_css = '\n'.join("@property %s{syntax:'<color>';inherits:true;initial-value:transparent}" % n for n in EXTRA_REG)
win_trans = ','.join('%s .32s ease' % n for n in REG_NAMES + EXTRA_REG + ['--wl'])
WIN_BG_D = 'linear-gradient(176deg,#484646 0%,#4b4644 48%,#51453d 82%,#4a3e36 100%)'
WIN_BG_L = 'linear-gradient(176deg,#d6d5d5 0%,#d8d3d0 48%,#ded2ca 82%,#d6c9c1 100%)'
PVW, PVH, PVHS = CWV + 24, 112, 212

css = r'''
/* Layout: the Settings window at its real 780 x 580, the review notes beside it; the page's own chrome is quiet. */
:root{--page:#ECEEF1;--ink:#16181D;--ez:cubic-bezier(.32,.72,0,1);--ez2:cubic-bezier(.25,.1,.25,1);--ui:"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,sans-serif;--sans:Geist,system-ui,-apple-system,"Segoe UI",sans-serif}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--page:#17181B;--ink:#ECECEC;color-scheme:dark}}
:root[data-theme="dark"]{--page:#17181B;--ink:#ECECEC;color-scheme:dark}
body{margin:0;background:var(--page);color:var(--ink);overflow-x:hidden}
#cz-root{max-width:100%;overflow-x:hidden}
@property --ga{syntax:'<angle>';inherits:false;initial-value:0deg}
@property --vt{syntax:'<length>';inherits:false;initial-value:0px}
@property --wdc{syntax:'<color>';inherits:true;initial-value:#4B9FFC}
@property --cl{syntax:'<length>';inherits:true;initial-value:12px}
@property --cw{syntax:'<length>';inherits:true;initial-value:472px}
@property --cwk{syntax:'<number>';inherits:true;initial-value:4.72}
''' + reg_css + '\n' + board_reg_css + '\n' + extra_reg_css + r'''

/* ---------- the board ---------- */
.cz{position:relative;min-height:100vh;box-sizing:border-box;font-family:var(--sans);color:var(--tx);-webkit-font-smoothing:antialiased;
 --btx:#ECECEC;--bsub:rgba(236,236,236,.62);--bcard:rgba(255,255,255,.05);--bedge:rgba(255,255,255,.11);--tx:#ECECEC;--tx2:rgba(236,236,236,.66);--cap:rgba(0,0,0,.24);--caph:rgba(0,0,0,.32);--pill:rgba(255,255,255,.16);--rim:rgba(255,255,255,.2);--pac:#4CC2FF;--wl:0;
 transition:--tx .32s ease,--tx2 .32s ease,--cap .32s ease,--caph .32s ease,--pill .32s ease,--rim .32s ease,--wl .32s ease}
.cz.light{--btx:#16181D;--bsub:rgba(22,24,29,.62);--bcard:rgba(255,255,255,.55);--bedge:rgba(0,0,0,.09);--tx:#16181D;--tx2:rgba(22,24,29,.64);--cap:rgba(0,0,0,.06);--caph:rgba(0,0,0,.1);--pill:#FFFFFF;--rim:rgba(0,0,0,.1);--pac:#0067C0;--wl:1}
.czwall,.czwall2{position:absolute;inset:0;pointer-events:none}
.czwall{background:radial-gradient(900px 640px at 10% 0%,#3a3430 0%,transparent 62%),radial-gradient(900px 700px at 92% 40%,#2c3540 0%,transparent 58%),radial-gradient(1000px 800px at 40% 100%,#33291f 0%,transparent 60%),#17181b}
.czwall2{opacity:var(--wl);background:radial-gradient(900px 640px at 10% 0%,#f3ebe4 0%,transparent 62%),radial-gradient(900px 700px at 92% 40%,#e2e8ef 0%,transparent 58%),radial-gradient(1000px 800px at 40% 100%,#efe4d8 0%,transparent 60%),#eceef1}
.czin{position:relative;box-sizing:border-box;max-width:1290px;margin:0 auto;padding:40px 32px 56px;display:flex;flex-direction:column;gap:22px}
.czhdr{display:flex;flex-wrap:wrap;align-items:flex-end;gap:8px 24px}
.czhdr div{min-width:0;flex:1 1 420px}
.czhdr h1{margin:0;font-size:30px;line-height:38px;font-weight:600;letter-spacing:-.01em;color:var(--btx);text-wrap:balance}
.czhdr p{margin:6px 0 0;font-size:14px;line-height:21px;color:var(--bsub);max-width:760px}
.czod{font-family:"Geist Mono",ui-monospace,monospace;font-size:11px;line-height:16px;color:var(--bsub);letter-spacing:.01em}
.cztools{display:flex;flex-wrap:wrap;align-items:center;gap:10px 22px}
.cztl{display:flex;align-items:center;gap:9px;font-size:12px;color:var(--bsub);min-width:0;max-width:100%}
.cztl .czseg{font-family:var(--sans)}
.czmain{display:flex;flex-wrap:wrap;gap:28px;align-items:flex-start}
.czwrap{flex:0 0 auto;width:786px;max-width:100%;box-sizing:border-box;overflow-x:auto;overflow-y:hidden;padding:2px 2px 30px;scrollbar-width:thin}
.cznotes{flex:1 1 300px;min-width:0;max-width:460px;display:flex;flex-direction:column;gap:12px}
.czn{padding:14px 16px;border-radius:14px;background:var(--bcard);box-shadow:inset 0 0 0 1px var(--bedge);font-size:12.5px;line-height:18px;color:var(--bsub)}
.czn h3{margin:0 0 6px;font-size:13px;line-height:18px;font-weight:600;color:var(--btx);display:flex;align-items:baseline;gap:8px}
.czn h3 em{font-style:normal;font-family:"Geist Mono",ui-monospace,monospace;font-size:11px;font-weight:400;padding:0 6px;border-radius:5px;box-shadow:inset 0 0 0 1px var(--bedge)}
.czn p{margin:0 0 6px}.czn p:last-child{margin-bottom:0}
.czn b{font-weight:500;color:var(--btx)}
.czn ul{margin:0;padding-left:16px}.czn li{margin:0 0 4px}
.czvns{display:grid}
.czvn{grid-area:1/1;opacity:0;visibility:hidden;transform:translateY(4px);transition:opacity .18s ease,transform .3s var(--ez),visibility 0s linear .3s}
.czvn.on{opacity:1;visibility:visible;transform:none;transition:opacity .3s ease .1s,transform .45s var(--ez) .1s,visibility 0s}

/* ---------- the Settings window (accent-d3-locked.dc.html, SettingsView.swift: 780 x 580, sidebar 212) ---------- */
.czwin{position:relative;width:782px;height:581px;border-radius:8px;overflow:hidden;isolation:isolate;background:''' + WIN_BG_D + r''';box-shadow:0 0 0 1px var(--wedge),0 18px 48px rgba(0,0,0,.28);font-family:var(--ui);color:var(--tx);-webkit-font-smoothing:antialiased;--pac:#4CC2FF;
 transition:''' + win_trans + r'''}
.czwin.wd{''' + dark_css + ';' + D3_DARK + ';' + VC_DARK + r'''}
.czwin.wl{''' + light_css + ';' + D3_LIGHT + ';' + VC_LIGHT + r'''}
.wbgl{position:absolute;inset:0;background:''' + WIN_BG_L + r''';opacity:var(--wl);pointer-events:none}
.wside{position:absolute;left:0;top:0;bottom:0;width:213px;background:linear-gradient(180deg,#626b75 0%,#67645f 58%,#7a6c5e 100%)}
.wside::before{content:"";position:absolute;inset:0;background:linear-gradient(180deg,#afb8c2 0%,#beb7b0 58%,#c8b5aa 100%);opacity:var(--wl)}
.wsearch{position:absolute;left:11px;top:44px;width:192px;height:30px;box-sizing:border-box;border-radius:15px;background:var(--search);display:flex;align-items:center;gap:8px;padding-left:11px;font-size:14px;color:var(--tx2)}
.wsearch svg path{fill:none;stroke:currentColor;stroke-width:1.4;stroke-linecap:round}
.witems{position:absolute;left:11px;top:86px;width:192px;display:flex;flex-direction:column;gap:1px}
.witem{height:28px;border-radius:7px;display:flex;align-items:center;gap:10px;padding-left:12px;font-size:14px;color:var(--tx)}
.witem.sel{background:var(--selrow)}
.witem svg{flex-shrink:0;opacity:.9}
.witem svg path{fill:none;stroke:currentColor;stroke-width:1.25;stroke-linecap:round;stroke-linejoin:round}
.caps{position:absolute;left:0;top:22px;pointer-events:none;z-index:8}
.caps path,.caps rect{fill:none;stroke:var(--tx);stroke-width:1}
.czdetail{position:absolute;left:213px;top:0;right:0;bottom:0}
.czpage{position:absolute;inset:0;pointer-events:none;opacity:0;visibility:hidden;transform:translateY(6px);transition:opacity .2s ease,transform .3s var(--ez),visibility 0s linear .3s}
.czpage.on{pointer-events:auto;opacity:1;visibility:visible;transform:none;transition:opacity .3s ease .1s,transform .45s var(--ez) .1s,visibility 0s}
.czscroll{position:absolute;inset:0;overflow-y:auto;overflow-x:hidden;scrollbar-width:none;box-sizing:border-box;padding:59px 24px 28px;overscroll-behavior:contain;-webkit-mask-image:linear-gradient(to bottom,transparent 0,transparent calc(var(--vt) * .68),#000 var(--vt));mask-image:linear-gradient(to bottom,transparent 0,transparent calc(var(--vt) * .68),#000 var(--vt));transition:--vt .25s ease}
.czscroll::-webkit-scrollbar{display:none}
.czscroll.sc{--vt:62px}
.czscroll.pa{padding-top:0}
.czchrome{position:absolute;left:14px;top:12px;right:142px;height:28px;display:flex;align-items:center;gap:10px;pointer-events:none;z-index:7}
.cztitle{font-size:14px;line-height:18px;font-weight:600;color:var(--head);white-space:nowrap;opacity:0;transform:translateY(3px);transition:opacity .2s ease,transform .3s var(--ez)}
.cztitle.on{opacity:1;transform:none}
.czacc{margin-left:auto;opacity:0;visibility:hidden;transform:scale(.97);transition:opacity .18s ease,transform .3s var(--ez),visibility 0s linear .3s}
.czacc.on{opacity:1;visibility:visible;transform:none;pointer-events:auto;transition:opacity .3s ease .1s,transform .4s var(--ez) .1s,visibility 0s}

/* the pinned block (A): the window's own backdrop behind it, so the page scrolls under it */
.czpin{position:sticky;top:0;z-index:4;margin:0 -24px 2px;padding:59px 24px 18px;isolation:isolate;background:''' + WIN_BG_D + r''' -213px 0/782px 581px no-repeat}
.czpin::before{content:"";position:absolute;inset:0;z-index:-1;background:''' + WIN_BG_L + r''' -213px 0/782px 581px no-repeat;opacity:var(--wl)}
.czpin::after{content:"";position:absolute;left:24px;right:24px;bottom:0;height:1px;background:var(--div);opacity:0;transition:opacity .25s ease}
.czpin.sc::after{opacity:1}
.czhead{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:10px;min-height:26px}
.czhead .sh{margin:0}

/* sections, cards and rows (ChromeSection, ChromeCard, ChromeRow) */
.czsec{padding-bottom:20px}
.sh{font-size:14px;line-height:18px;font-weight:600;color:var(--head);margin-bottom:10px}
.card{border-radius:12px;background:var(--cardfill)}
.czf{display:grid;grid-template-rows:1fr;opacity:1;transition:grid-template-rows .34s var(--ez2),opacity .24s ease .08s}
.czf.off{grid-template-rows:0fr;opacity:0;pointer-events:none;transition:grid-template-rows .34s var(--ez2),opacity .14s ease}
.czf>.in{min-height:0;overflow:hidden;visibility:inherit;transition:visibility 0s}
.czf.off>.in{visibility:hidden;transition:visibility 0s linear .34s}
.czf.open>.in{overflow:visible}
.row{display:flex;align-items:center;gap:20px;padding:9px 11px 9px 16px;min-height:36px;transition:opacity .3s ease}
.row.dim{opacity:.5}
.row.tall{display:block;padding:12px 12px 0}
.rt{flex:1;min-width:0;display:flex;flex-direction:column}
.rt b{font-weight:400;font-size:14px;line-height:20px}
.dt{font-size:12px;line-height:16px;color:var(--tx2)}
.dv{height:1px;margin-left:16px;background:var(--div);transition:opacity .3s ease}
.dv.none{opacity:0}
.row.stack{flex-direction:column;align-items:stretch;gap:8px;padding-bottom:11px}
.row.stack .rt{flex:none}
.row.stack .czsegs{justify-items:start}
.row.top{align-items:flex-start;padding-top:11px;padding-bottom:12px}
.czopts{display:flex;gap:12px;flex-shrink:0}
.czopt{appearance:none;border:0;background:none;padding:0;font:inherit;display:flex;flex-direction:column;align-items:center;gap:6px;font-size:12px;line-height:16px;color:var(--tx2);cursor:pointer;border-radius:9px;outline:2px solid transparent;outline-offset:2px;transition:color .2s ease,outline-color .12s ease}
.czopt i{display:flex;align-items:center;justify-content:center;width:96px;height:56px;border-radius:8px;background:var(--tile);box-shadow:inset 0 0 0 0 var(--sel);transition:background-color .2s ease,box-shadow .2s ease,transform .12s ease}
.czopt:hover i{background:var(--tilesel)}
.czopt:active i{transform:scale(.97)}
.czopt.on{color:var(--tx)}
.czopt.on i{background:var(--tilesel);box-shadow:inset 0 0 0 2px var(--sel)}
.czopt:focus-visible{outline-color:var(--pac)}
.czoc{display:block;width:52px;height:52px}
.czbadge{display:inline-block;vertical-align:1px;margin-left:8px;font-family:"Geist Mono",ui-monospace,monospace;font-size:10.5px;line-height:15px;font-weight:400;padding:0 6px;border-radius:5px;background:rgba(255,178,62,.16);box-shadow:inset 0 0 0 1px rgba(255,178,62,.45);color:#FFC46B}
.czwin.wl .czbadge{background:rgba(196,110,0,.1);box-shadow:inset 0 0 0 1px rgba(196,110,0,.4);color:#8A4B00}
.czcap{font-size:12px;line-height:16px;font-weight:600;color:var(--tx2);padding:12px 16px 3px}
.cznote{font-size:12.5px;line-height:18px;color:var(--tx2);padding:12px 16px}

/* crossfading text: the outgoing label fades fast, the incoming one a moment later */
.xf{display:inline-grid;min-width:0}
.xf>span{grid-area:1/1;justify-self:start;opacity:0;white-space:nowrap;transition:opacity .1s ease}
.xf>span.on{opacity:1;transition:opacity .2s ease .08s}
.dt.xf{display:grid}
.dt.xf>span{white-space:normal}

/* segmented control (the D3 Theme control); the pill is measured from the picked button */
.czseg{position:relative;display:inline-flex;padding:2px;border-radius:14px;background:var(--cap);height:28px;box-sizing:border-box;flex-shrink:0;max-width:100%}
.czpill{position:absolute;top:2px;bottom:2px;left:2px;width:0;border-radius:12px;background:var(--pill);box-shadow:0 1px 2px rgba(0,0,0,.18),inset 0 0 0 .5px var(--rim);opacity:0;transition:left .3s var(--ez),width .3s var(--ez),opacity .2s ease}
.czseg button{appearance:none;border:0;background:none;font:inherit;position:relative;height:24px;padding:0 11px;border-radius:12px;font-size:12px;font-weight:500;color:var(--tx2);cursor:pointer;white-space:nowrap;outline:2px solid transparent;outline-offset:1px;transition:color .15s ease,transform .12s ease,outline-color .12s ease}
.czseg button:hover{color:var(--tx)}
.czseg button:active{transform:scale(.96)}
.czseg button.on{color:var(--tx)}
.czseg button:focus-visible{outline-color:var(--pac)}
.czseg.sm{height:26px;border-radius:13px}
.czseg.sm .czpill{border-radius:11px}
.czseg.sm button{height:22px;padding:0 9px;font-size:11.5px;border-radius:11px}
.czsegs{display:grid;justify-items:end;flex-shrink:0}
.czsgw{grid-area:1/1;opacity:0;visibility:hidden;transform:translateY(3px);transition:opacity .16s ease,transform .3s var(--ez),visibility 0s linear .3s}
.czsgw.on{opacity:1;visibility:inherit;transform:none;transition:opacity .28s ease .08s,transform .4s var(--ez) .08s,visibility 0s}

/* slider (the Transparency slider), with the value and an Auto capsule */
.czslw{display:flex;align-items:center;gap:6px;flex-shrink:0}
.czsl{position:relative;width:150px;height:22px;box-sizing:border-box;padding:0 9px;cursor:pointer;touch-action:none;outline:none;border-radius:11px}
.czsl .tk{position:relative;display:block;height:22px}
.czsl .tr{position:absolute;left:0;right:0;top:9px;height:4px;border-radius:2px;background:var(--trk);overflow:hidden}
.czsl .fl{position:absolute;left:0;top:0;bottom:0;border-radius:2px;background:var(--trkfill);transition:width .32s var(--ez)}
.czsl .kn{position:absolute;top:4px;width:18px;height:14px;margin-left:-9px;border-radius:7px;background:#FFFFFF;box-shadow:0 1px 3px rgba(0,0,0,.3),0 0 0 .5px rgba(0,0,0,.12);transition:left .32s var(--ez),transform .15s ease,box-shadow .15s ease}
.czsl:hover .kn{transform:scale(1.06)}
.czsl:focus-visible .kn{box-shadow:0 0 0 2px var(--pac),0 1px 3px rgba(0,0,0,.3)}
.czsl.drag .fl{transition:none}
.czsl.drag .kn{transition:transform .15s ease,box-shadow .15s ease;transform:scale(1.1)}
.row.dim .czsl{cursor:default}
.czval{width:48px;text-align:right;font-size:11.5px;line-height:16px;font-variant-numeric:tabular-nums;color:var(--tx2);white-space:nowrap}
.czau{appearance:none;border:0;font:inherit;width:40px;height:20px;padding:0;border-radius:10px;font-size:11px;font-weight:500;background:transparent;color:var(--tx2);box-shadow:inset 0 0 0 1px var(--div);cursor:pointer;flex-shrink:0;outline:2px solid transparent;outline-offset:1px;transition:background-color .2s ease,color .2s ease,box-shadow .2s ease,transform .12s ease,outline-color .12s ease}
.czau:hover{color:var(--tx)}
.czau:active{transform:scale(.95)}
.czau.on{background:var(--pill);color:var(--tx);box-shadow:inset 0 0 0 .5px var(--rim),0 1px 2px rgba(0,0,0,.15)}
.czau.none{visibility:hidden}
.czau:focus-visible{outline-color:var(--pac)}

/* switch (SettingsSwitch, small) */
.tog{appearance:none;border:0;padding:0;position:relative;display:block;width:28px;height:16px;border-radius:8px;background:var(--swoff);cursor:pointer;flex-shrink:0;outline:2px solid transparent;outline-offset:2px;transition:background-color .2s ease,filter .1s ease,outline-color .12s ease}
.tog i{position:absolute;left:2px;top:2px;width:12px;height:12px;border-radius:6px;background:#FFFFFF;box-shadow:0 1px 2px rgba(0,0,0,.3);transition:transform .22s var(--ez),width .12s ease}
.tog.on{background:var(--pac)}
.tog.on i{transform:translateX(12px)}
.tog:hover{filter:brightness(1.08)}
.tog:active i{width:14px}
.tog.on:active i{transform:translateX(10px)}
.tog:focus-visible{outline-color:var(--pac)}
.tog.still{cursor:default}

/* palette swatches (the accent swatches' ring) */
.czsws{position:relative;display:flex;gap:2px;flex-shrink:0}
.czsw{appearance:none;border:0;background:none;padding:0;position:relative;width:24px;height:24px;border-radius:50%;display:flex;align-items:center;justify-content:center;cursor:pointer;flex-shrink:0;outline:2px solid transparent;outline-offset:1px;transition:outline-color .12s ease}
.czsw i{display:block;width:16px;height:16px;border-radius:50%;box-shadow:inset 0 0 0 .5px var(--rim);transition:transform .12s ease}
.czsw::before{content:"";position:absolute;inset:0;border-radius:50%;box-shadow:inset 0 0 0 1.5px var(--ring);opacity:0;transform:scale(.8);transition:opacity .18s ease,transform .24s var(--ez)}
.czsw.sel::before{opacity:1;transform:none}
.czsw:hover i{transform:scale(1.12)}
.czsw:active i{transform:scale(.86);transition-duration:.08s}
.czsw:focus-visible{outline-color:var(--pac)}
.czwells{display:flex;gap:14px;flex-shrink:0}
.czwell{position:relative;display:flex;align-items:center;gap:7px;font-size:12px;color:var(--tx2);cursor:pointer;border-radius:12px;outline:2px solid transparent;outline-offset:2px;transition:outline-color .12s ease}
.czwell:focus-within{outline-color:var(--pac)}
.czwell input{position:absolute;left:0;top:0;width:22px;height:22px;margin:0;padding:0;border:0;opacity:0;cursor:pointer}
.czwell i{display:block;width:20px;height:20px;border-radius:50%;box-shadow:inset 0 0 0 .5px var(--rim),0 0 0 1px var(--div);transition:background-color .3s ease}

/* capsule buttons: Advanced and Reset */
.czmore,.czbtn{appearance:none;border:0;font:inherit;display:flex;align-items:center;gap:5px;height:26px;box-sizing:border-box;border-radius:13px;background:var(--cap);color:var(--tx);font-size:12px;font-weight:500;cursor:pointer;flex-shrink:0;white-space:nowrap;outline:2px solid transparent;outline-offset:2px;transition:background-color .12s ease,transform .12s ease,opacity .25s ease,outline-color .12s ease}
.czmore{padding:0 9px 0 12px}
.czbtn{padding:0 12px}
.czmore:hover,.czbtn:hover{background:var(--caph)}
.czmore:active,.czbtn:active{transform:scale(.96)}
.czmore:focus-visible,.czbtn:focus-visible{outline-color:var(--pac)}
.czmore .xf{width:30px;justify-items:center}
.czmore .xf>span{justify-self:center}
.czmore svg{opacity:.75;transition:transform .3s var(--ez)}
.czmore.open svg{transform:rotate(180deg)}
.czbtn.idle{opacity:.42;pointer-events:none}

/* the preview */
.pv{position:relative;z-index:5;border-radius:12px;background:var(--cardfill);padding:12px 12px 0}
.row.tall .pv{background:none;padding:0}
.pvw{position:relative;height:''' + str(PVH) + r'''px;border-radius:10px;overflow:hidden;isolation:isolate;box-shadow:inset 0 0 0 1px var(--div);--pvh:''' + str(PVH) + r'''px}
.pvw.stage{height:''' + str(PVHS) + r'''px;--pvh:''' + str(PVHS) + r'''px}
.pvfoot{display:flex;align-items:center;justify-content:space-between;gap:12px;min-height:50px}
.pvcap{font-size:12px;color:var(--tx2);min-width:0;overflow:hidden}
.pvchat{position:absolute;left:24px;right:24px;top:18px;z-index:2;font-size:13px;line-height:19px;color:var(--wink)}
.pvchat h4{margin:0 0 4px;font-size:14px;line-height:20px;font-weight:600}
.pvchat p{margin:0;color:var(--wink)}
.pvchat code{font-family:"Cascadia Mono",Consolas,ui-monospace,monospace;font-size:12px}
''' + composer_css + r'''
.pvw.wbg::before,.pvw.wbg::after,.pvw .wbg::before,.pvw .wbg::after{background-size:''' + str(PVW) + r'''px var(--pvh)}
.pvw .plate.wbg::before,.pvw .plate.wbg::after{background-position:calc(-1 * var(--cl)) calc(64px - var(--pvh))}
.pvw .au-dots.wbg::before,.pvw .au-dots.wbg::after{background-position:calc(var(--au-hs) + var(--au-hb) * 2.2 - var(--cl)) calc(var(--au-hs) + var(--au-hb) * 2.2 + 64px - var(--pvh))}
.pvw .cmp,.pvw .obox{left:var(--cl);width:var(--cw)}
.pvw .ring{left:auto;right:46px}
.pvw .send{left:auto;right:8px}
.pvw .ph{overflow:hidden;left:48px;transition:left .34s var(--ez),right .42s var(--ez)}
/* the voice orb, where the attachment was (voice-composer.html): the tools as that page spaces them; the ball is
   drawn in a 72 px canvas centred on its slot, so its glow and its pop have room */
.pvw .tools{right:72px;gap:14px;z-index:2}
.pvw .cc{--accent:var(--pac)}
.orbslot{position:relative;display:block;flex-shrink:0;width:32px;height:32px}
.orbmove{position:absolute;inset:0;will-change:transform}
.orbcv{position:absolute;left:50%;top:50%;width:72px;height:72px;margin:-36px 0 0 -36px;pointer-events:none;z-index:2;opacity:0;transition:opacity .45s ease}
.orbcv.on{opacity:1}
.orbbtn{position:absolute;inset:-4px;border-radius:50%;cursor:pointer;z-index:3;outline:none}
.orbbtn:focus-visible{box-shadow:0 0 0 2px var(--pac)}
/* the attachment outside sits in the preview's margin, centred on the composer's line */
.pvw .attach.out{left:10px;bottom:20px}
.pvw .attach{z-index:4}
.pvw .vpill{z-index:1}
.v-pill .xbtn{display:none}
.pvw .files{transition:opacity .25s ease .05s}
.pvw.fpo .files{opacity:0;transition:opacity .16s ease}
/* carried over from voice-composer.html */
''' + VC_CSS + r'''
.pvw .ph .hint{right:0;overflow:hidden;text-overflow:ellipsis}
.pvw .ph .typed{display:flex;justify-content:flex-end;max-width:100%;overflow:hidden;white-space:nowrap}
.pvw .au-in .au-sq{transform:scale(var(--cwk),.52) rotate(calc(var(--au-r, 10) * 1deg)) scale(var(--au-s, 1))}

/* the preview's model switch: steppers, a capsule naming the maker, and a menu of every maker */
.czms{position:relative;display:flex;align-items:center;gap:2px;min-width:0}
.czst{appearance:none;border:0;background:none;padding:0;width:22px;height:22px;border-radius:11px;color:var(--tx2);display:flex;align-items:center;justify-content:center;cursor:pointer;flex-shrink:0;outline:2px solid transparent;outline-offset:1px;transition:background-color .1s ease,color .1s ease,transform .12s ease,outline-color .12s ease}
.czst:hover{background:var(--hov);color:var(--tx)}
.czst:active{transform:scale(.9)}
.czst:focus-visible{outline-color:var(--pac)}
.czst svg path{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.czmc{appearance:none;border:0;font:inherit;display:flex;align-items:center;gap:7px;height:28px;box-sizing:border-box;padding:0 10px 0 10px;border-radius:14px;background:var(--cap);color:var(--tx);font-size:13px;cursor:pointer;flex-shrink:0;outline:2px solid transparent;outline-offset:2px;transition:background-color .1s ease,transform .12s ease,outline-color .12s ease}
.czmc:hover,.czms.open .czmc{background:var(--caph)}
.czmc:active{transform:scale(.97)}
.czmc:focus-visible{outline-color:var(--pac)}
.czmc .xf,.czbtn .xf{overflow:hidden;transition:width .3s var(--ez)}
.czmc svg{opacity:.7;flex-shrink:0;transition:transform .3s var(--ez)}
.czms.open .czmc svg{transform:rotate(180deg)}
.czdot{display:block;width:10px;height:10px;border-radius:50%;flex-shrink:0;background-image:linear-gradient(135deg,rgba(255,255,255,.3),rgba(255,255,255,0) 62%);box-shadow:inset 0 0 0 .5px var(--rim);transition:background-color .35s ease}
.czpop{position:absolute;left:24px;top:calc(100% + 6px);width:270px;box-sizing:border-box;padding:6px;border-radius:12px;background:var(--popbg);box-shadow:0 0 0 1px var(--popedge),0 16px 40px rgba(0,0,0,.32),0 2px 8px rgba(0,0,0,.16);transform-origin:40px 0;opacity:0;transform:translateY(-4px) scale(.96);visibility:hidden;pointer-events:none;z-index:30;color:var(--tx);transition:opacity .15s ease,transform .15s ease,visibility 0s linear .15s}
.czpop.open{opacity:1;transform:none;visibility:inherit;pointer-events:auto;transition:opacity .2s ease,transform .22s cubic-bezier(.2,.9,.25,1),visibility 0s}
.czplist{position:relative;max-height:236px;overflow-y:auto;scrollbar-width:thin;scrollbar-color:var(--trk) transparent;overscroll-behavior:contain}
.czph{font-size:11px;line-height:14px;font-weight:600;color:var(--tx2);padding:6px 8px 4px}
.pitem{appearance:none;border:0;background:transparent;font:inherit;color:var(--tx);width:100%;display:flex;align-items:center;gap:8px;height:28px;box-sizing:border-box;padding:0 8px;border-radius:7px;cursor:pointer;text-align:left;outline:2px solid transparent;outline-offset:-2px;transition:background-color .1s ease,outline-color .12s ease}
.pitem:hover{background:var(--hov)}
.pitem:active{background:var(--prs)}
.pitem:focus-visible{outline-color:var(--pac)}
.pcheck{width:12px;flex-shrink:0;opacity:0;transform:scale(.6);transition:opacity .16s ease,transform .24s var(--ez)}
.pcheck.on{opacity:1;transform:none}
.pcheck path{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.pname{display:flex;align-items:baseline;gap:7px;min-width:0;font-size:13px;line-height:16px;white-space:nowrap}
.pname b{font-weight:400;flex-shrink:0}
.pname i{font-style:normal;font-size:11px;color:var(--tx2);overflow:hidden;text-overflow:ellipsis;min-width:0}

/* the style cards (B) */
.czscs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}
.czsc{appearance:none;border:0;font:inherit;text-align:left;display:flex;flex-direction:column;gap:3px;padding:8px 8px 11px;border-radius:12px;background:var(--cardfill);color:var(--tx);cursor:pointer;box-shadow:inset 0 0 0 1px transparent;outline:2px solid transparent;outline-offset:2px;transition:box-shadow .2s ease,background-color .2s ease,transform .12s ease,outline-color .12s ease}
.czsc:hover{background:var(--tile)}
.czsc:active{transform:scale(.98)}
.czsc.on{background:var(--tilesel);box-shadow:inset 0 0 0 2px var(--sel)}
.czsc:focus-visible{outline-color:var(--pac)}
.czsc b{font-size:13px;line-height:18px;font-weight:600;padding:5px 4px 0}
.czsc .d{font-size:11.5px;line-height:15px;color:var(--tx2);padding:0 4px}
.czgl{position:relative;height:56px;border-radius:8px;background:var(--tile);display:flex;align-items:center;justify-content:center;overflow:hidden}
.czgl i{position:relative;display:block;width:104px;height:24px;border-radius:12px;isolation:isolate;background:color-mix(in srgb,var(--tx) 5%,transparent);box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--tx) 16%,transparent)}
.gl-beam i::before,.gl-beam i::after{content:"";position:absolute;inset:-1px;border-radius:13px;background:conic-gradient(from var(--ga),transparent 0 48%,#4F8BFF 60%,#B46BFF 72%,#FF6F7D 84%,#FFB45C 92%,transparent 98%);animation:czga 3.4s linear infinite}
.gl-beam i::before{padding:1.5px;-webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);-webkit-mask-composite:xor;mask:linear-gradient(#000 0 0) content-box exclude,linear-gradient(#000 0 0)}
.gl-beam i::after{inset:-3px;border-radius:15px;filter:blur(6px);opacity:.55;z-index:-1}
.gl-aura i::before{content:"";position:absolute;inset:-6px;border-radius:18px;z-index:-1;background:conic-gradient(from var(--ga),#FF5500 0 16%,transparent 32% 68%,#0055FF 84% 100%);filter:blur(8px);opacity:.55;animation:czga 9s linear infinite}
@keyframes czga{to{--ga:360deg}}


/* ---------- Voice typing ---------- */
.witem.nav{cursor:pointer;transition:background-color .2s ease;outline:2px solid transparent;outline-offset:-2px}
.witem.nav:hover{background:var(--hov)}
.witem.nav.sel{background:var(--selrow)}
.witem.nav:focus-visible{outline-color:var(--pac)}
.cztitle .xf>span{white-space:nowrap}
.czbtn.pri{background:var(--pac);color:#FFFFFF;background-image:linear-gradient(180deg,rgba(255,255,255,.18),rgba(255,255,255,0) 60%);box-shadow:inset 0 1px 0 rgba(255,255,255,.25),0 1px 2px rgba(0,0,0,.2)}
.czbtn.pri:hover{background-color:var(--pac);filter:brightness(1.08)}
.czwin.wd .czbtn.pri{color:#06121C}
.czbtn .xf{transition:width .3s var(--ez)}
.czspin{display:block;width:11px;height:11px;border-radius:50%;box-sizing:border-box;border:1.6px solid color-mix(in srgb,currentColor 25%,transparent);border-top-color:currentColor;animation:czspin .8s linear infinite;flex-shrink:0}
@keyframes czspin{to{transform:rotate(360deg)}}
.hwgo .czspin{width:0;margin-right:-5px;opacity:0;transition:width .25s var(--ez),margin-right .25s var(--ez),opacity .2s ease}
.hwgo.busy{pointer-events:none}
.hwgo.busy .czspin{width:11px;margin-right:0;opacity:.85}
.hwlist{display:flex;flex-direction:column;gap:2px;padding:0 16px 10px}
.hwi{display:grid;grid-template-columns:16px 104px minmax(0,1fr);align-items:center;column-gap:8px;min-height:24px;font-size:12.5px;line-height:16px;opacity:.35;transform:translateY(2px);transition:opacity .3s ease,transform .4s var(--ez)}
.hwi.on{opacity:1;transform:none}
.hwk{color:var(--tx2)}
.hwv{color:var(--tx);min-width:0}
.hwv>span{white-space:normal}
.hwic{position:relative;width:12px;height:12px}
.hwic .czspin,.hwic .ck{position:absolute;inset:0;opacity:0;transform:scale(.6);transition:opacity .2s ease,transform .3s var(--ez)}
.hwic .ck{color:var(--tx2)}
.hwi.busy .hwic .czspin{opacity:.8;transform:none}
.hwi.ok .hwic .ck{opacity:1;transform:none}
.hwrec{margin:0 12px 12px;padding:10px 12px 12px;border-radius:10px;background:var(--tile);display:flex;flex-direction:column;gap:10px}
.hwrt{display:flex;flex-direction:column;gap:2px;min-width:0}
.hwrt b{font-weight:600;font-size:13px;line-height:18px}
.hwrt .dt>span{white-space:normal}
.hwbtns{display:flex;gap:8px;flex-wrap:wrap}
.hwstate{display:flex;align-items:center;gap:6px;font-size:12px;color:var(--tx2)}
.hwnote{padding:0 16px 12px;font-size:11.5px;line-height:16px}
.mlist{margin-top:10px;padding-bottom:4px}
.mcap{display:flex;align-items:baseline;gap:8px}
.mcap span{font-weight:400;color:var(--tx2);opacity:.8}
.row.mr{gap:14px;padding-top:8px;padding-bottom:8px}
.row.mr .rt b{display:flex;align-items:center;gap:8px}
.vbadge{font-size:10.5px;line-height:15px;font-weight:500;padding:0 6px;border-radius:5px;color:var(--pac);background:color-mix(in srgb,var(--pac) 14%,transparent);opacity:0;transform:scale(.85);transition:opacity .25s ease,transform .3s var(--ez)}
.vbadge.on{opacity:1;transform:none}
.mver{appearance:none;border:0;background:none;padding:0;font:inherit;align-self:flex-start;display:flex;align-items:center;gap:4px;font-size:12px;line-height:16px;color:var(--tx2);cursor:pointer;border-radius:4px;outline:2px solid transparent;outline-offset:1px;transition:color .12s ease,outline-color .12s ease}
.mver:hover{color:var(--tx)}
.mver:focus-visible{outline-color:var(--pac)}
.mver svg{transition:transform .3s var(--ez)}
.mver.open svg{transform:rotate(180deg)}
.meters{display:flex;flex-direction:column;gap:3px;flex-shrink:0;width:112px}
.mtr{display:flex;align-items:center;justify-content:space-between;gap:8px;font-size:11px;line-height:13px;color:var(--tx2)}
.mtr em{font-style:normal}
.mdots{display:flex;gap:3px}
.mdots i{display:block;width:5px;height:5px;border-radius:50%;background:var(--trk)}
.mdots i.on{background:var(--tx2)}
.mst{display:grid;justify-items:end;align-items:center;width:96px;flex-shrink:0}
.mso{grid-area:1/1;opacity:0;visibility:hidden;transform:scale(.94);transition:opacity .15s ease,transform .25s var(--ez),visibility 0s linear .25s}
.mso.on{opacity:1;visibility:inherit;transform:none;transition:opacity .25s ease .08s,transform .35s var(--ez) .08s,visibility 0s}
.minuse{display:flex;align-items:center;gap:5px;font-size:12px;font-weight:500;color:var(--tx)}
.mprog{display:flex;align-items:center;gap:6px}
.mpct{font-size:11.5px;font-variant-numeric:tabular-nums;color:var(--tx2);min-width:30px;text-align:right}
.mx{appearance:none;border:0;padding:0;width:20px;height:20px;border-radius:10px;background:var(--cap);color:var(--tx2);display:flex;align-items:center;justify-content:center;cursor:pointer;outline:2px solid transparent;outline-offset:1px;transition:background-color .12s ease,color .12s ease,outline-color .12s ease}
.mx:hover{background:var(--caph);color:var(--tx)}
.mx:focus-visible{outline-color:var(--pac)}
.mbar{height:2px;margin:0 16px;border-radius:1px;background:var(--trk);overflow:hidden;opacity:0;transition:opacity .3s ease}
.mbar.on{opacity:1}
.mbar i{display:block;height:100%;border-radius:1px;background:var(--pac);transition:width .18s linear}
.mvers{display:flex;align-items:center;flex-wrap:wrap;gap:8px 14px;padding:2px 16px 4px}
.men{display:flex;align-items:center;gap:8px;font-size:12px;color:var(--tx2)}
.mnote{padding:2px 16px 10px;font-size:11.5px;line-height:16px;color:var(--tx2)}
.mfoot{padding:8px 16px 8px;font-size:11.5px;color:var(--tx2);border-top:1px solid var(--div);margin-top:4px}
.czlang .czpop{left:auto;right:0;width:220px;transform-origin:calc(100% - 30px) 0}
/* the preview's model menu opens above its chip, as the composer's model popover does */
.czms:not(.czlang) .czpop{top:auto;bottom:calc(100% + 6px);transform-origin:40px 100%;transform:translateY(4px) scale(.96)}
.czms:not(.czlang) .czpop.open{transform:none}
/* OD-0097: the voice options. Menu rows (the Voice typing page's Language menu as a shared control): the menu opens
   below its button, right-aligned under an inline row, left-aligned under a stacked one */
.czms.czvom .czpop{top:calc(100% + 6px);bottom:auto;left:auto;right:0;transform-origin:calc(100% - 30px) 0;transform:translateY(-4px) scale(.96)}
.czms.czvom.left{align-self:flex-start}
.czms.czvom.left .czpop{left:0;right:auto;transform-origin:30px 0}
.czms.czvom .czpop.open{transform:none}
.czvom .czplist{max-height:none}
.row.dim .czvom,.row.dim .czseg{pointer-events:none}
/* the light while you speak: one wrapper over the composer light's layers, its opacity the level (JS) */
.pvw .vlgt{position:absolute;inset:0;pointer-events:none}
/* the voice glow (voice-glow 0.2.1, as the voice design draws it): under the text and controls; the selector outranks
   the package's own [data-voice-beam] rule, which would make it static */
.cmp>.vglowh{position:absolute;inset:0;border-radius:22px;z-index:3;pointer-events:none}
.vglowh>.vglow{position:absolute;inset:0;border-radius:22px;pointer-events:none}
/* the language name in the pill (B): an en space and the name, regular weight, no separator dot (OD-0097 amendment);
   out of the layout until shown, since the label's text sweep (background-clip: text) paints every glyph inside it */
.vpill .pl .lg{font-style:normal;font-weight:400;display:none}
.vpill.lang .pl .lg{display:inline}
.vpill.relabel .pl.on{opacity:0!important;transition:opacity .14s ease!important}
/* nothing beside the orb (OD-0097 amendment): the pill's rims, fringe and end band fade out around the orb */
.vpill{--pR:20px;--oR:16px;-webkit-mask:radial-gradient(circle at calc(100% - var(--pR)) 50%,transparent calc(var(--oR) - 1px),#000 calc(var(--pR) + 1.5px));mask:radial-gradient(circle at calc(100% - var(--pR)) 50%,transparent calc(var(--oR) - 1px),#000 calc(var(--pR) + 1.5px))}
.vpill.float{--pR:22px;-webkit-mask:radial-gradient(circle at var(--pR) 50%,transparent calc(var(--oR) - 1px),#000 calc(var(--pR) + 1.5px));mask:radial-gradient(circle at var(--pR) 50%,transparent calc(var(--oR) - 1px),#000 calc(var(--pR) + 1.5px))}
/* the label's box holds its descenders (the sweep paints only inside it; at 1.1 it cut the foot of g) */
.vpill .pl{line-height:1.5}
/* the language question (D): the decision page's popover, Marrow glass with the Mac's arrow tip (OD-0095), anchored
   on the orb, outside the stage's clip; below the orb (tip up) when the window has no room above */
.vask{position:absolute;left:0;top:0;width:268px;box-sizing:border-box;padding:12px 12px 10px;border-radius:14px;z-index:40;
  background:var(--pgBg);-webkit-backdrop-filter:blur(14px) saturate(1.7);backdrop-filter:blur(14px) saturate(1.7);
  box-shadow:inset 0 0 0 .5px var(--pgRim),inset 0 0 0 1px var(--pgEdge),0 14px 30px -10px rgba(0,0,0,.45);
  opacity:0;visibility:hidden;transform:translateY(6px) scale(.96);transform-origin:var(--tip,230px) 100%;pointer-events:none;
  transition:opacity .18s ease,transform .24s var(--ez),visibility 0s linear .24s}
.vask[data-below]{transform:translateY(-6px) scale(.96);transform-origin:var(--tip,230px) 0}
.vask.open{opacity:1;visibility:visible;transform:none;pointer-events:auto;transition:opacity .18s ease,transform .24s var(--ez),visibility 0s}
.vask::after{content:"";position:absolute;left:calc(var(--tip,230px) - 7px);bottom:-6px;width:14px;height:14px;transform:rotate(45deg);border-radius:2px;
  background:inherit;box-shadow:inset -1px -1px 0 0 var(--pgEdge);clip-path:polygon(100% 0,100% 100%,0 100%)}
.vask[data-below]::after{bottom:auto;top:-6px;box-shadow:inset 1px 1px 0 0 var(--pgEdge);clip-path:polygon(0 0,100% 0,0 100%)}
.vask .aq{font-size:13px;line-height:17px;font-weight:600;color:var(--pgLabHi);margin-bottom:9px}
.vask .ach{display:flex;flex-wrap:wrap;gap:6px}
.vask .ach button{appearance:none;border:0;height:26px;padding:0 11px;border-radius:13px;font:inherit;font-size:12.5px;cursor:pointer;color:var(--pgLabHi);background:var(--pgXBg);box-shadow:inset 0 0 0 1px var(--pgXRim);outline:2px solid transparent;outline-offset:1px;transition:background-color .15s ease,color .15s ease,filter .15s ease,outline-color .12s ease}
.vask .ach button:hover{filter:brightness(1.1)}
.vask .ach button:focus-visible{outline-color:var(--pac)}
.vask .ach button.on{background:var(--pac);color:#fff;box-shadow:none}
.vask .af{font-size:11px;line-height:14px;color:var(--pgLab);margin-top:8px}
/* the working dots: the colour picked here (accent by default), eased */
.pvw{transition:--wdc .4s ease,--cl .42s var(--ez),--cw .42s var(--ez),--cwk .42s var(--ez)}
.pvw .wdotc{position:absolute;left:0;bottom:0;width:100%;height:100%;display:block}
.row.accsw{gap:6px;padding-top:2px}
.czacl{display:flex;align-items:center;gap:10px;min-width:0}
.czacap{width:62px;flex-shrink:0;font-size:11.5px;color:var(--tx2)}
/* B: the stage pinned while the composer's settings scroll under it; it compacts with the first 100 px of scroll
   (the margin gives the room back below, so nothing moves faster than the scroll) and lets go at Speech */
.czscroll.pb{padding-top:0;scroll-padding-top:290px}
.czscroll.pb.sc{--vt:44px}
.czpin.pinb{--col:0px;--colk:0;margin-bottom:2px;padding-bottom:14px}
.czpin.pinb .pvw.stage{--pvh:calc(212px - var(--col));height:var(--pvh)}
.czpin.pinb .pvchat{opacity:clamp(0,calc(1 - var(--colk) * 1.8),1);transform:translateY(calc(var(--colk) * -14px))}
.czpin.pinb .pvfoot{min-height:46px}
.czstk{position:relative}
.czkey{flex:1;min-width:0;height:28px;box-sizing:border-box;border:0;border-radius:8px;padding:0 10px;font:inherit;font-size:13px;color:var(--tx);background:var(--cap);box-shadow:inset 0 0 0 1px var(--div);outline:none;transition:box-shadow .15s ease}
.czkey:focus{box-shadow:inset 0 0 0 1.5px var(--pac)}
.czpriv{padding:4px 4px 0;font-size:12px;line-height:17px;color:var(--tx2)}
.czprivx{display:grid}
.czprivx>span{white-space:normal}
@media (max-width:640px){.czin{padding:24px 16px 40px}.czhdr h1{font-size:24px;line-height:30px}}
@media (prefers-reduced-motion:reduce){.cz *,.cz *::before,.cz *::after{transition:none!important;animation:none!important}}
'''

# ---------------- template ----------------
CHEVR = '<svg width="5" height="8" viewBox="0 0 5 8" aria-hidden="true" style="margin-left: 2px; opacity: .7"><path d="M1.2 1 4 4 1.2 7" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"></path></svg>'
CHEV = '<svg width="7" height="5" viewBox="0 0 7 5" aria-hidden="true"><path d="M1 1.2 3.5 3.7 6 1.2" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"></path></svg>'

def XF(v, cls='xf'):
    return '<span class="%s"><span class="{{%s.a.cls}}">{{%s.a.t}}</span><span class="{{%s.b.cls}}">{{%s.b.t}}</span></span>' % (cls, v, v, v, v)

def SEG(g):
    return ('<div class="{{%s.cls}}" role="radiogroup" aria-label="{{%s.label}}" data-onkeydown="{{%s.key}}"><span class="czpill"></span>'
            '<sc-for list="{{%s.opts}}" as="o"><button type="button" class="{{o.cls}}" role="radio" aria-checked="{{o.on}}" tabindex="{{o.tab}}" data-onclick="{{o.pick}}">{{o.t}}</button></sc-for></div>') % (g, g, g, g)

def MS(m):
    return ('<div class="{{%(m)s.cls}}" data-onkeydown="{{%(m)s.key}}">'
            '<button type="button" class="czst" aria-label="Previous model" data-onclick="{{%(m)s.prev}}"><svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M6.2 2 3.2 5l3 3"></path></svg></button>'
            '<button type="button" class="czmc" aria-haspopup="menu" aria-expanded="{{%(m)s.open}}" aria-label="{{%(m)s.aria}}" data-onclick="{{%(m)s.toggle}}"><span class="czdot" style="background-color: {{%(m)s.dot}}"></span>'
            '<span class="xf" style="width: {{%(m)s.w}}px"><span class="{{%(m)s.lab.a.cls}}">{{%(m)s.lab.a.t}}</span><span class="{{%(m)s.lab.b.cls}}">{{%(m)s.lab.b.t}}</span></span>' + CHEV + '</button>'
            '<button type="button" class="czst" aria-label="Next model" data-onclick="{{%(m)s.next}}"><svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M3.8 2 6.8 5l-3 3"></path></svg></button>'
            '<div class="{{%(m)s.popc}}" role="menu" aria-label="Preview model"><div class="czph">Preview with</div><div class="czplist">'
            '<sc-for list="{{%(m)s.items}}" as="it"><button type="button" class="pitem" role="menuitemradio" aria-checked="{{it.on}}" tabindex="-1" data-onclick="{{it.pick}}">'
            '<svg class="{{it.chk}}" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.7 9.6 3.6"></path></svg>'
            '<span class="czdot" style="background-color: {{it.dot}}"></span><span class="pname"><b>{{it.name}}</b><i>{{it.model}}</i></span></button></sc-for></div></div></div>') % {'m': m}

def PV(p, extra=''):
    return '<div class="{{%s.cls}}" style="{{pvVars}}">%s<sc-for list="{{%s.inst}}" as="i">%s</sc-for></div>' % (p, extra, p, INSTANCE)

def ROW_BODY(v, nest=True):
    t = ''
    t += '<sc-if value="{{%(v)s.isCap}}"><div class="czcap">{{%(v)s.t}}</div></sc-if>'
    t += '<sc-if value="{{%(v)s.isSeg}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><div class="czsegs"><sc-for list="{{%(v)s.segs}}" as="sg"><div class="{{sg.cls}}">' + SEG('sg.g') + '</div></sc-for></div></div></sc-if>'
    t += ('<sc-if value="{{%(v)s.isSlider}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><div class="czslw">'
          '<div class="{{%(v)s.slc}}" role="slider" tabindex="0" aria-label="{{%(v)s.t}}" aria-valuemin="{{%(v)s.min}}" aria-valuemax="{{%(v)s.max}}" aria-valuenow="{{%(v)s.v}}" aria-valuetext="{{%(v)s.vt}}" '
          'data-onpointerdown="{{%(v)s.down}}" data-onpointermove="{{%(v)s.move}}" data-onpointerup="{{%(v)s.up}}" data-onpointercancel="{{%(v)s.up}}" data-onkeydown="{{%(v)s.key}}">'
          '<span class="tk"><span class="tr"><span class="fl" style="width: {{%(v)s.pct}}%%"></span></span><span class="kn" style="left: {{%(v)s.pct}}%%"></span></span></div>'
          '<span class="czval">{{%(v)s.vt}}</span><button type="button" class="{{%(v)s.auc}}" aria-pressed="{{%(v)s.auOn}}" aria-label="Automatic {{%(v)s.t}}" tabindex="{{%(v)s.auTab}}" data-onclick="{{%(v)s.auTog}}">Auto</button></div></div></sc-if>')
    t += '<sc-if value="{{%(v)s.isToggle}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><button type="button" class="{{%(v)s.tgc}}" role="switch" aria-checked="{{%(v)s.on}}" aria-label="{{%(v)s.t}}" data-onclick="{{%(v)s.tog}}"><i></i></button></div></sc-if>'
    t += ('<sc-if value="{{%(v)s.isSwatches}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><div class="czsws" role="radiogroup" aria-label="{{%(v)s.t}}">'
          '<sc-for list="{{%(v)s.sw}}" as="s"><button type="button" class="{{s.cls}}" role="radio" aria-checked="{{s.on}}" aria-label="{{s.name}}" title="{{s.name}}" data-onclick="{{s.pick}}"><i style="background: {{s.bg}}"></i></button></sc-for></div></div></sc-if>')
    t += ('<sc-if value="{{%(v)s.isColors}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><div class="czwells">'
          '<label class="czwell"><input type="color" value="{{%(v)s.c1}}" aria-label="First colour" data-oninput="{{%(v)s.on1}}"><i style="background-color: {{%(v)s.c1}}"></i>One side</label>'
          '<label class="czwell"><input type="color" value="{{%(v)s.c2}}" aria-label="Second colour" data-oninput="{{%(v)s.on2}}"><i style="background-color: {{%(v)s.c2}}"></i>The other</label></div></div></sc-if>')
    t += '<sc-if value="{{%(v)s.isAdv}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><button type="button" class="{{%(v)s.advc}}" aria-expanded="{{%(v)s.open}}" data-onclick="{{%(v)s.tog}}">' + XF(v + '.lab') + CHEV + '</button></div></sc-if>'
    t += '<sc-if value="{{%(v)s.isReset}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><button type="button" class="{{%(v)s.rc}}" aria-disabled="{{%(v)s.dis}}" tabindex="{{%(v)s.tab}}" data-onclick="{{%(v)s.go}}">Reset to defaults</button></div></sc-if>'
    t += ('<sc-if value="{{%(v)s.isAccSw}}"><div class="row stack accsw"><div class="czacl"><span class="czacap">Classic</span><div class="czsws" role="radiogroup" aria-label="Classic dot colours"><sc-for list="{{%(v)s.classic}}" as="s"><button type="button" class="{{s.cls}}" role="radio" aria-checked="{{s.on}}" aria-label="{{s.name}}" title="{{s.name}}" data-onclick="{{s.pick}}"><i style="background: {{s.bg}}"></i></button></sc-for></div></div>'
          '<div class="czacl"><span class="czacap">Signature</span><div class="czsws" role="radiogroup" aria-label="Signature dot colours"><sc-for list="{{%(v)s.sig}}" as="s"><button type="button" class="{{s.cls}}" role="radio" aria-checked="{{s.on}}" aria-label="{{s.name}}" title="{{s.name}}" data-onclick="{{s.pick}}"><i style="background: {{s.bg}}"></i></button></sc-for></div></div></div></sc-if>')
    t += ('<sc-if value="{{%(v)s.isMenu}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div>'
          '<div class="{{%(v)s.mcls}}" data-onkeydown="{{%(v)s.key}}"><button type="button" class="czmc" aria-haspopup="menu" aria-expanded="{{%(v)s.open}}" aria-label="{{%(v)s.t}}" tabindex="{{%(v)s.tab}}" data-onclick="{{%(v)s.toggle}}">'
          '<span class="xf" style="width: {{%(v)s.w}}px"><span class="{{%(v)s.lab.a.cls}}">{{%(v)s.lab.a.t}}</span><span class="{{%(v)s.lab.b.cls}}">{{%(v)s.lab.b.t}}</span></span>' + CHEV + '</button>'
          '<div class="{{%(v)s.popc}}" role="menu" aria-label="{{%(v)s.t}}" style="width: {{%(v)s.popw}}px"><div class="czplist"><sc-for list="{{%(v)s.items}}" as="it"><button type="button" class="pitem" role="menuitemradio" aria-checked="{{it.on}}" tabindex="-1" data-onclick="{{it.pick}}">'
          '<svg class="{{it.chk}}" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.7 9.6 3.6"></path></svg><span class="pname"><b>{{it.t}}</b></span></button></sc-for></div></div></div></div></sc-if>')
    t += '<sc-if value="{{%(v)s.isNote}}"><div class="cznote">{{%(v)s.t}}</div></sc-if>'
    t += '<sc-if value="{{%(v)s.isLink}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><button type="button" class="czbtn" data-onclick="{{%(v)s.go}}">{{%(v)s.bt}}' + CHEVR + '</button></div></sc-if>'
    t += ('<sc-if value="{{%(v)s.isOrbs}}"><div class="{{%(v)s.rowc}}"><div class="rt"><b>{{%(v)s.t}}</b>' + XF(v + '.d', 'dt xf') + '</div><div class="czopts" role="radiogroup" aria-label="{{%(v)s.t}}" data-onkeydown="{{%(v)s.key}}">'
          '<sc-for list="{{%(v)s.opts}}" as="o"><button type="button" class="{{o.cls}}" role="radio" aria-checked="{{o.on}}" tabindex="{{o.tab}}" data-onclick="{{o.pick}}"><i><canvas class="czoc" data-ref="{{o.ref}}"></canvas></i><span>{{o.t}}</span></button></sc-for></div></div></sc-if>')
    if nest:
        t += '<sc-if value="{{%(v)s.isPreview}}"><div class="row tall">' + PV(v + '.pv') + '<div class="pvfoot">' + MS(v + '.ms') + SEG(v + '.state') + '</div></div></sc-if>'
        t += '<sc-if value="{{%(v)s.isNest}}"><sc-for list="{{%(v)s.nest}}" as="q">' + ROW('q', False) + '</sc-for></sc-if>'
    return t.replace('%(v)s', v).replace('%%', '%')

def ROW(v, nest=True):
    return '<div class="{{%s.fc}}"><div class="in"><div class="{{%s.dvc}}"></div>' % (v, v) + ROW_BODY(v, nest) + '</div></div>'

BADGE = '<span class="czbadge" title="The real WebGL orbs are being ported; these stand in for them">Placeholder orbs</span>'
def SECS(lst):
    return ('<sc-for list="{{%s}}" as="c"><div class="{{c.fc}}"><div class="in"><div class="czsec"><sc-if value="{{c.hasT}}"><div class="sh">{{c.t}}<sc-if value="{{c.badge}}">' % lst + BADGE + '</sc-if></div></sc-if>'
            '<div class="{{c.cardc}}"><sc-for list="{{c.rows}}" as="r">' + ROW('r') + '</sc-for></div></div></div></div></sc-for>')

STAGE_CHAT = ('<div class="pvchat"><h4>Dark mode toggle</h4><p>I added a <b>Theme</b> switch to the settings page. It reuses the existing <code>Switch</code> row, '
              'so it picks up the same spacing and focus ring as the other options.</p></div>')

PAGE_A = ('<div class="{{pageA}}"><div class="{{scrollA}} pa" data-onscroll="{{onScrollA}}"><sc-if value="{{showA}}">'
          '<div class="{{pinCls}}"><div class="czhead"><div class="sh">Composer light</div>' + SEG('styleSegA') + '</div>'
          '<div class="pv">' + PV('pvA') + '<div class="pvfoot">' + MS('msA') + SEG('stateSegA') + '</div></div></div>'
          '<div class="czbody">' + SECS('secsA') + '</div></sc-if></div></div>')

PAGE_B = ('<div class="{{pageB}}"><div class="{{scrollB}}" data-onscroll="{{onScrollB}}" data-ref="{{scrollRefB}}"><sc-if value="{{showB}}">'
          '<div class="czstk"><div class="{{pinB}}"><div class="sh">Composer light</div><div class="pv">' + PV('pvB', STAGE_CHAT) + '<div class="pvfoot">' + MS('msB') + XF('cap', 'xf pvcap') + '</div></div></div>'
          '<div class="czsec"><div class="sh">Style</div><div class="czscs" role="radiogroup" aria-label="Light">'
          '<sc-for list="{{cards}}" as="s"><button type="button" class="{{s.cls}}" role="radio" aria-checked="{{s.on}}" data-onclick="{{s.pick}}"><span class="{{s.g}}"><i></i></span><b>{{s.t}}</b><span class="d">{{s.d}}</span></button></sc-for>'
          '</div></div>' + SECS('secsB') + '</div>' + SECS('secsB2') + '</sc-if></div></div>')

PAGE_C = ('<div class="{{pageC}}"><div class="{{scrollC}}" data-onscroll="{{onScrollC}}"><sc-if value="{{showC}}">'
          '<div class="czsec"><div class="sh">Composer light</div><div class="card"><sc-for list="{{rowsC}}" as="r">' + ROW('r') + '</sc-for></div></div>'
          '<div class="czsec"><div class="sh">Voice orb<sc-if value="{{voiceBadge}}">' + BADGE + '</sc-if></div><div class="card"><sc-for list="{{voiceC}}" as="r">' + ROW('r') + '</sc-for></div></div>'
          '</sc-if></div></div>')


SPIN = '<span class="czspin" aria-hidden="true"></span>'
CHECK = '<svg class="ck" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.7 9.6 3.6" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"></path></svg>'
DOTS = lambda lst: '<span class="mdots"><sc-for list="{{%s}}" as="d"><i class="{{d}}"></i></sc-for></span>' % lst
PAGE_V = ('<div class="{{pageV}}"><div class="{{scrollV}}" data-onscroll="{{onScrollV}}"><sc-if value="{{showV}}">'
  '<div class="czsec"><div class="sh">Voice typing</div><div class="card">'
  '<div class="row"><div class="rt"><b>Engine</b>' + XF('v.engD', 'dt xf') + '</div>' + SEG('v.engSeg') + '</div>'
  '<div class="{{v.cloudFc}}"><div class="in"><div class="dv"></div><div class="row"><div class="rt"><b>OpenAI key</b><span class="dt">Cloud voice typing uses your own key, kept in Windows\u2019 credential store</span></div><button type="button" class="czbtn" data-onclick="{{v.keyTog}}">' + XF('v.keyLab') + '</button></div>'
  '<div class="{{v.keyFc}}"><div class="in"><div class="row"><input class="czkey" type="password" autocomplete="off" spellcheck="false" placeholder="Paste your OpenAI key" aria-label="OpenAI key"></div></div></div></div></div>'
  '<div class="dv"></div><div class="row"><div class="rt"><b>Language</b>' + XF('v.langD', 'dt xf') + '</div>'
  '<div class="{{v.langCls}}" data-onkeydown="{{v.langKey}}"><button type="button" class="czmc" aria-haspopup="menu" aria-expanded="{{v.langOpen}}" aria-label="Language" data-onclick="{{v.langTog}}">'
  '<span class="xf" style="width: {{v.langW}}px"><span class="{{v.langLab.a.cls}}">{{v.langLab.a.t}}</span><span class="{{v.langLab.b.cls}}">{{v.langLab.b.t}}</span></span>' + CHEV + '</button>'
  '<div class="{{v.langPop}}" role="menu" aria-label="Language"><div class="czplist"><sc-for list="{{v.langs}}" as="it"><button type="button" class="pitem" role="menuitemradio" aria-checked="{{it.on}}" tabindex="-1" data-onclick="{{it.pick}}">'
  '<svg class="{{it.chk}}" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.7 9.6 3.6"></path></svg><span class="pname"><b>{{it.t}}</b></span></button></sc-for></div></div></div></div>'
  '<div class="dv"></div><div class="row"><div class="rt"><b>Orb and after voice input</b><span class="dt">How the orb looks, and whether the words are sent</span></div><button type="button" class="czbtn" data-onclick="{{v.toCz}}">Customization' + CHEVR + '</button></div>'
  '</div></div>'
  '<div class="{{v.localFc}}"><div class="in"><div class="czsec"><div class="sh">Speech model</div>'
  '<div class="card hwcard"><div class="row"><div class="rt"><b>Recommended for this PC</b>' + XF('v.hw.d', 'dt xf') + '</div>'
  '<button type="button" class="{{v.hw.btnc}}" aria-disabled="{{v.hw.dis}}" data-onclick="{{v.hw.run}}">' + SPIN + XF('v.hw.lab') + '</button></div>'
  '<div class="{{v.hw.listFc}}"><div class="in"><div class="hwlist"><sc-for list="{{v.hw.items}}" as="x"><div class="{{x.cls}}"><span class="hwic">' + SPIN + CHECK + '</span><span class="hwk">{{x.k}}</span>' + XF('x.v', 'xf hwv') + '</div></sc-for></div></div></div>'
  '<div class="{{v.hw.recFc}}"><div class="in"><div class="hwrec"><div class="hwrt"><b>' + XF('v.hw.recName') + '</b>' + XF('v.hw.why', 'dt xf') + '</div>'
  '<div class="{{v.hw.useFc}}"><div class="in"><div class="hwbtns"><button type="button" class="czbtn pri" data-onclick="{{v.hw.use}}">Use recommended</button><button type="button" class="czbtn" data-onclick="{{v.hw.keep}}">{{v.hw.keepT}}</button></div></div></div>'
  '<div class="{{v.hw.stateFc}}"><div class="in"><div class="hwstate">' + CHECK + XF('v.hw.stateT') + '</div></div></div>'
  '</div></div></div>'
  '<div class="cznote hwnote">The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it again any time.</div></div>'
  '<div class="card mlist"><sc-for list="{{v.groups}}" as="gr"><div class="czcap mcap">{{gr.t}}<span>{{gr.sub}}</span></div>'
  '<sc-for list="{{gr.rows}}" as="m"><div class="mrow"><div class="{{m.dvc}}"></div>'
  '<div class="{{m.rowc}}"><div class="rt"><b>{{m.name}}<span class="{{m.recc}}">Recommended</span></b>'
  '<button type="button" class="{{m.verc}}" aria-expanded="{{m.open}}" data-onclick="{{m.togV}}">' + XF('m.meta') + CHEV + '</button></div>'
  '<div class="meters" role="img" aria-label="{{m.aria}}"><span class="mtr"><em>Speed</em>' + DOTS('m.sp') + '</span><span class="mtr"><em>Accuracy</em>' + DOTS('m.ac') + '</span></div>'
  '<div class="mst"><span class="{{m.s.in}}"><span class="minuse">' + CHECK + 'In use</span></span>'
  '<span class="{{m.s.prog}}"><span class="mprog"><span class="mpct">{{m.pctT}}</span><button type="button" class="mx" aria-label="Cancel the download" data-onclick="{{m.cancel}}"><svg width="8" height="8" viewBox="0 0 8 8" aria-hidden="true"><path d="M1.5 1.5l5 5M6.5 1.5l-5 5" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"></path></svg></button></span></span>'
  '<span class="{{m.s.use}}"><button type="button" class="czbtn" data-onclick="{{m.sel}}">Use</button></span>'
  '<span class="{{m.s.dl}}"><button type="button" class="czbtn" data-onclick="{{m.dl}}">Download</button></span></div></div>'
  '<div class="{{m.barc}}"><i style="width: {{m.pct}}%"></i></div>'
  '<div class="{{m.verFc}}"><div class="in"><div class="mvers">' + SEG('m.verSeg') + '<sc-if value="{{m.hasEn}}"><span class="men"><button type="button" class="{{m.enc}}" role="switch" aria-checked="{{m.enOn}}" aria-label="English only" data-onclick="{{m.enTog}}"><i></i></button>English only</span></sc-if></div>'
  '<div class="mnote">{{m.note}}</div></div></div>'
  '</div></sc-for></sc-for><div class="mfoot">' + XF('v.total') + '</div></div>'
  '</div></div></div>'
  '<div class="czpriv">' + XF('v.priv', 'xf czprivx') + '</div>'
  '</sc-if></div></div>')

NOTES = '''<aside class="cznotes">
<div class="czn"><h3>Layout B, locked</h3><div class="czvns"><sc-for list="{{notes}}" as="n"><div class="{{n.cls}}"><h3><em>{{n.id}}</em>{{n.t}}</h3><sc-for list="{{n.ps}}" as="p"><p>{{p}}</p></sc-for></div></sc-for><div class="{{vnote}}"><h3><em>V</em>Voice typing page</h3><p>One layout for now: how speech becomes text (engine, the speech model with its hardware check, language) and a privacy line. Customization comes back with the sidebar or the Customization button on this page.</p></div></div></div>
<div class="czn"><h3>Voice settings: where they live</h3><p><b>Customization</b> keeps the voice orb and <b>After voice input</b>, as ruled: how the orb looks and what happens to the words. <b>Voice typing</b>, a page of its own right below it in the sidebar (a waveform icon), holds what turns speech into text: Engine, the Whisper models with Check my hardware, and Language. A model list with downloads and a hardware check would swamp a look-and-feel page; each page links to the other.</p><p>Everything here is simulated: the hardware values, the check and the downloads (about 90 MB a second). <b>Simulated PC</b> above switches between the owner's desktop and a laptop with no working GPU, which changes the recommendation.</p><p>The two orbs are <b>placeholders</b> (marked in the window) until the WebGL ports of Siri Wave and Voice Membrane land; they already show the states: slow at rest, faster with a glow on hover, popped out and pulsing with the voice while listening, fastest while the words are worked out.</p></div>
<div class="czn"><h3>Where it sits</h3><p><b>Customization</b> comes right after <b>Appearance</b> in the sidebar: both are about how Marrow looks, and Appearance already holds the theme and accent the light sits beside. The icon is a pair of sparkles (SF Symbols <b>sparkles</b>, drawn here in the sidebar's stroke), for an effect laid over the app. The other pages keep the source's order.</p><p>Proposed search words for the sidebar's search: light, glow, beam, composer, effects, customization.</p></div>
<div class="czn"><h3>What a user sees</h3><p><b>Border beam, up front:</b> Motion, Shape, Colours, Speed, Brightness, Intensity, Follow the composer. <b>Under Advanced:</b> Colour shift, Saturation, Colour drift, Hold colours still, Glow size, the three layer strengths (edge line, inner glow, outer glow), Corner, Tuned for; Outside glow sizes appear only for Pulse outside, Flare only for Line.</p><p><b>Uiverse light, up front:</b> Colours (Original, Model, Accent, Custom), Speed, Breathing, brightness at rest, typing and while a turn runs, Rim light, Halftone. <b>Under Advanced:</b> halo size and softness, rim width, dot spacing and strength.</p><p>Each style has its own Reset to defaults, which dims when nothing has changed. Switching style keeps the other style's settings.</p><p><b>Working dots</b> (the dots along the composer's bottom while a turn runs, ComposerWorkingDots.swift): Accent by default (the user's accent, the Windows accent here), Model colour, or Custom from the locked 22-colour accent palette. Touching it switches the preview to Running a turn.</p><p><b>Voice orb</b> (voice-composer.html, OD-0091): Orb (Siri Wave or Voice Membrane), Listening style (Pill in the composer, the default; Cross beside the orb; Floating pill), Orb size (32 px or 26 px), Attachment button (inside, or outside in the margin, where the composer gives it 44 px), After voice input. Picking a listening style makes the preview listen at once. In this narrow composer the floating pill would cross the files line, so the line fades while it is open.</p><p><b>Speech</b> closes the page: model, language and engine at a glance, each opening Voice typing. It is the first section not about the composer, so the stage lets go there.</p></div>
<div class="czn"><h3>Left out on purpose</h3><ul><li><b>Geometry:</b> always fitted to the composer.</li><li><b>Active:</b> turning the beam inactive fades it out, which is what Off does; Off keeps every setting.</li><li>The playground's Extra CSS field, the 1.4.1 defect switch, the code readout and the two preset buttons; Reset covers the defaults.</li></ul></div>
<div class="czn"><h3>Preview model</h3><p>The arrows and the menu change only the preview: the composer's model chip and any light whose colours follow the model (the Uiverse light on <b>Model</b> colours). The chip also steps on when clicked. Colours are each maker's own from the liquid palettes at High effort, eased toward the maker's title colour where they would vanish on the window (OpenAI's white in Light, Grok's black in Dark).</p></div>
<div class="czn"><p>Border beam: border-beam 1.4.1 by Jakub Antalik (MIT). Uiverse light: after SelfMadeSystem's button on Uiverse.io (MIT). Voice glow: voice-glow 0.2.1 by Jakub Antalik (MIT), as the voice design carries it. Both lights are drawn by the Composer Lights playground's own code.</p></div>
</aside>'''

WINDOW = ('<div class="czwrap"><div class="{{winCls}}" style="--pac: {{pac}}"><div class="wbgl"></div>'
          '<div class="wside"><div class="wsearch"><svg width="13" height="13" viewBox="0 0 13 13" aria-hidden="true"><path d="M5.6 1.5a4.1 4.1 0 1 1 0 8.2a4.1 4.1 0 1 1 0-8.2ZM8.6 8.6l3 3"></path></svg><span>Search</span></div>'
          '<div class="witems"><sc-for list="{{side}}" as="s"><div class="{{s.cls}}" role="{{s.role}}" tabindex="{{s.tab}}" data-onclick="{{s.go}}" data-onkeydown="{{s.key}}"><svg width="15" height="15" viewBox="0 0 14 14" aria-hidden="true"><path d="{{s.d}}"></path></svg><span>{{s.name}}</span></div></sc-for></div></div>'
          '<svg class="caps" width="782" height="12" viewBox="0 0 782 12" aria-hidden="true"><path d="M652 6.5 H662"></path><rect x="699.5" y="1.5" width="9" height="9" rx="1.5"></rect><path d="M747.5 1.5 L756.5 10.5 M756.5 1.5 L747.5 10.5"></path></svg>'
          '<div class="czdetail">' + PAGE_B + PAGE_V +
          '<div class="czchrome"><span class="{{titleCls}}">' + XF('ctitle') + '</span><div class="{{accCls}}">' + SEG('stateSegB') + '</div></div>'
          '</div>' + '<div class="{{vask.cls}}" role="dialog" aria-label="Voice language" data-ref="{{vaskRef}}" data-onkeydown="{{vask.key}}"><div class="aq">Which language will you speak?</div><div class="ach"><sc-for list="{{vask.items}}" as="a"><button type="button" class="{{a.cls}}" data-onclick="{{a.pick}}">{{a.t}}</button></sc-for></div><div class="af">Kept in Settings &gt; Voice typing</div></div>' + '</div></div>')

TEMPLATE = ('<div class="{{boardCls}}"><style>{{designCss}}</style><div class="czwall"></div><div class="czwall2"></div><div class="czin">'
            '<header class="czhdr"><div><h1>Customization</h1><p>The new Settings pages for the composer light and voice typing, drawn in the Settings window at its real size. Customization is layout B, Stage (locked), with Voice typing in the sidebar below it. Every control works, and the preview follows each change as it is made.</p></div>'
            '<div class="czod">OD-0085, OD-0089, OD-0091, OD-0092, OD-0097, OD-0076</div></header>'
            '<div class="cztools"><div class="cztl"><span>App theme</span>' + SEG('themeSeg') + '</div><div class="cztl"><span>Simulated PC</span>' + SEG('pcSeg') + '</div></div>'
            '<div class="czmain">' + WINDOW + NOTES + '</div></div></div>')

html = ('<title>Customization Page</title>\n'
        '<meta name="description" content="Settings &gt; Customization for Marrow: the composer light with a live preview, in three layouts.">\n'
        '<link rel="preconnect" href="https://fonts.googleapis.com">\n'
        '<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>\n'
        '<link href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600&amp;family=Geist+Mono:wght@400&amp;display=swap" rel="stylesheet">\n'
        '<style>' + css + '</style>\n'
        '<div id="cz-root"></div>\n'
        '<template id="cz-tpl">' + TEMPLATE + '</template>\n'
        '<script>\n' + runtime + '\n</script>\n'
        '<script>\n' + js + '\n' + cz + '\n</script>\n')
# the exact voice orbs (the site's Copy code exports in WebGL2: work/voice/design/voice-orbs.js, published beside the
# page) replace the placeholders as soon as the runtime is up
html += ('<script type="module">import { orbs } from \'./voice-orbs.js\'; '
         'window.__czOrbs = orbs; if (window.__cz && window.__cz.useOrbs) window.__cz.useOrbs(orbs);</script>\n')
html += ('<script type="module">import { mountVoiceGlow } from \'./voice-glow.js\'; '
         'window.__czGlow = mountVoiceGlow; if (window.__cz && window.__cz.useGlow) window.__cz.useGlow(mountVoiceGlow);</script>\n')
open(OUT, 'w', encoding='utf-8', newline='\n').write(html)
print('wrote', OUT, len(html.encode('utf-8')), 'bytes')
