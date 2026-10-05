# Generates E:/Droppy/work/design/variants/project/ComposerLights.dc.html
import json, re, os

SRC = 'E:/Droppy/work/design/composer-lights/border-beam-options.json'
OUT = 'E:/Droppy/work/design/variants/project/ComposerLights.dc.html'
HERE = os.path.dirname(os.path.abspath(__file__))
D = json.load(open(SRC, encoding='utf-8'))
PAL = D['palettes']
PNAMES = ['colorful', 'mono', 'ocean', 'sunset', 'forest', 'candy', 'ice', 'gold']

def col(s):
    """'rgb(1, 2, 3)' / 'rgba(1, 2, 3,, 0.5)' -> ([r,g,b,a], bad)"""
    bad = ',,' in s
    nums = [float(x) for x in re.findall(r'[\d.]+', s)]
    if len(nums) == 3: nums.append(1.0)
    return [int(nums[0]), int(nums[1]), int(nums[2]), round(nums[3], 3)], bad

def cs(c):
    r, g, b, a = c
    return 'rgba(%d, %d, %d, %s)' % (r, g, b, ('%g' % a))

# ---------- data for the script ----------
# border-beam 1.4.1's tables (G, Se, Ve, Ue, Qe, pe, Fe, Je, eo, Ye, oo), read out of its published dist/index.es.js
# by beam-fix/ref/dump.mjs; beam.js ports the generator that turns them into CSS.
TABLES = json.load(open('E:/Droppy/work/design/composer-lights/border-beam-tables.json', encoding='utf-8'))
BBT = {k: TABLES[k] for k in ('pe', 'G', 'Se', 'Ve', 'Ue', 'Qe', 'Fe', 'Je', 'eo', 'Ye', 'oo')}

# ---------- registered properties ----------
REG = []  # (name, syntax, initial, transition seconds)
def reg(n, syn, init, t=0.6): REG.append((n, syn, init, t))
for c in ['c1', 'c2', 'k1', 'k2', 'n1', 'n2']: reg('--au-' + c, '<color>', 'transparent')
for n, init in [('i', '0'), ('ro', '1'), ('do', '0'), ('tm', '1'), ('sx', '1'), ('sy', '1'), ('rsx', '1'), ('rsy', '1')]: reg('--au-' + n, '<number>', init, 0.9 if n == 'i' else 0.6)
for n, init in [('hs', '2px'), ('hb', '30px'), ('rw', '1.5px'), ('dd', '3px')]: reg('--au-' + n, '<length>', init)
for n, init in [('so', '0'), ('io', '0'), ('bo', '0'), ('br', '1.3'), ('sat', '1.2'), ('hb', '0'), ('hr', '30'), ('blur', '8'), ('cb', '3'), ('bb', '22.5'), ('sx', '1'), ('sy', '1'), ('boost', '1'), ('spk', '1')]:
    reg('--bb-' + n, '<number>', init)
reg('--bb-r', '<length>', '22px')
# plate glass
reg('--pg', '<number>', '0')
# Marrow designs: generic per-state properties (written by a design's vars(), eased here)
for n, t in [('di', 0.9), ('dg', 1.1), ('do', 0.9), ('dbd', 0.9), ('dbs', 0.9), ('mi', 0.9)]: reg('--' + n, '<number>', '0', t)
for n in ('dc1', 'dc2', 'dc3'): reg('--' + n, '<color>', 'transparent', 1.2)

reg_css = '\n'.join("@property %s{syntax:'%s';inherits:true;initial-value:%s}" % (n, s, i) for n, s, i, t in REG)
DELAY = {'--do': 0.45}   # the orbit rises after the send pulse has run
trans = ','.join('%s %gs var(--ez2)%s' % (n, t, (' %gs' % DELAY[n]) if n in DELAY else '') for n, s, i, t in REG)

BOARD_REG = [('--tx', '#ECECEC'), ('--tx2', 'rgba(236,236,236,.66)'), ('--tx3', 'rgba(236,236,236,.42)'), ('--card', 'rgba(255,255,255,.055)'), ('--edge', 'rgba(255,255,255,.12)'),
             ('--cap', 'rgba(0,0,0,.22)'), ('--pill', 'rgba(255,255,255,.16)'), ('--pillrim', 'rgba(255,255,255,.2)'), ('--trk', 'rgba(255,255,255,.12)'), ('--fill', 'rgba(255,255,255,.62)'),
             ('--knob', '#F2F2F2'), ('--swoff', 'rgba(255,255,255,.2)'), ('--swon', '#3A8DFF'), ('--acc', '#489CF9'),
             ('--wink', '#ECECEC'), ('--wink2', 'rgba(236,236,236,.45)'), ('--wink3', 'rgba(236,236,236,.62)'), ('--turn', 'rgba(255,255,255,.05)'), ('--codebg', 'rgba(255,255,255,.075)'),
             ('--disc', 'rgba(255,255,255,.13)'), ('--discarrow', '#FFFFFF'), ('--stopbg', 'rgb(105,105,105)'), ('--winedge', 'rgba(255,255,255,.1)'), ('--ringtrk', 'rgba(236,236,236,.16)'), ('--ringarc', 'rgba(236,236,236,.62)'),
             ('--dotc', 'rgba(236,236,236,.26)'), ('--plusg', '#3BC45A'), ('--minr', '#F0524D'), ('--codetx', 'rgba(236,236,236,.88)'), ('--codekw', '#E6A0E8'), ('--codest', '#9ED69A')]
board_reg_css = '\n'.join("@property %s{syntax:'<color>';inherits:true;initial-value:%s}" % (n, i) for n, i in BOARD_REG)
board_reg_css += "\n@property --wl{syntax:'<number>';inherits:true;initial-value:0}"
board_reg_css += "\n@property --bb-act{syntax:'<number>';inherits:true;initial-value:0}"
board_trans = ','.join('%s .45s ease' % n for n, i in BOARD_REG) + ',--wl .45s ease'
LIGHT_VALS = {'--tx': '#16181D', '--tx2': 'rgba(22,24,29,.64)', '--tx3': 'rgba(22,24,29,.42)', '--card': 'rgba(255,255,255,.5)', '--edge': 'rgba(0,0,0,.09)', '--cap': 'rgba(0,0,0,.06)',
              '--pill': '#FFFFFF', '--pillrim': 'rgba(0,0,0,.1)', '--trk': 'rgba(0,0,0,.1)', '--fill': 'rgba(0,0,0,.6)', '--knob': '#FFFFFF', '--swoff': 'rgba(0,0,0,.16)', '--swon': '#0067C0', '--acc': '#0067C0',
              '--wink': '#1C1C1E', '--wink2': 'rgba(28,28,30,.4)', '--wink3': 'rgba(28,28,30,.6)', '--turn': 'rgba(0,0,0,.035)', '--codebg': 'rgba(0,0,0,.075)',
              '--disc': 'rgba(0,0,0,.13)', '--discarrow': '#FFFFFF', '--stopbg': 'rgb(44,44,46)', '--winedge': 'rgba(0,0,0,.12)', '--ringtrk': 'rgba(28,28,30,.14)', '--ringarc': 'rgba(28,28,30,.55)',
              '--dotc': 'rgba(28,28,30,.28)', '--plusg': '#1E9E3E', '--minr': '#E0383E', '--codetx': 'rgba(28,28,30,.86)', '--codekw': '#9B3AA0', '--codest': '#2E7D32'}
light_css = ';'.join('%s:%s' % (k, v) for k, v in LIGHT_VALS.items()) + ';--wl:1'
dark_css = ';'.join('%s:%s' % (n, i) for n, i in BOARD_REG) + ';--wl:0'

# window background (measured from the port's renders, main-thread-1-dark/-light.png)
WIN_D = 'linear-gradient(100deg,#50463F 0%,#523F36 52%,#41362F 100%)'
WIN_L = 'linear-gradient(100deg,#DFD4CC 0%,#E1CEC4 52%,#D0C4BC 100%)'

css = r'''
body{margin:0}
@property --ez2{syntax:'*';inherits:true}
''' + reg_css + '\n' + board_reg_css + r'''
.board{position:relative;overflow:clip;font-family:Geist,system-ui,sans-serif;color:var(--tx);--ez:cubic-bezier(.32,.72,0,1);--ez2:cubic-bezier(.25,.1,.25,1);--ui:"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;--mono:"Geist Mono",ui-monospace,monospace;
''' + dark_css + ''';transition:''' + board_trans + r'''}
.board.light{''' + light_css + r'''}
.wall,.wall2{position:absolute;inset:0;pointer-events:none}
.wall{background:radial-gradient(900px 640px at 10% 0%,#3a3430 0%,transparent 62%),radial-gradient(900px 700px at 92% 40%,#2c3540 0%,transparent 58%),radial-gradient(1000px 800px at 40% 100%,#33291f 0%,transparent 60%),#17181b}
.wall2{opacity:var(--wl);background:radial-gradient(900px 640px at 10% 0%,#f3ebe4 0%,transparent 62%),radial-gradient(900px 700px at 92% 40%,#e2e8ef 0%,transparent 58%),radial-gradient(1000px 800px at 40% 100%,#efe4d8 0%,transparent 60%),#eceef1}
.inner{position:relative;box-sizing:border-box;padding:38px 48px 0;height:100%}
.hdr{display:flex;align-items:flex-end;gap:24px;height:84px}
.hdr h1{margin:0;font-size:30px;line-height:38px;font-weight:600;letter-spacing:-.01em}
.hdr p{margin:6px 0 0;font-size:14px;line-height:21px;color:var(--tx2);max-width:900px}
.hdr .od{margin-left:auto;font-family:var(--mono);font-size:11px;line-height:16px;color:var(--tx3);text-align:right;white-space:nowrap}
.cols{display:flex;gap:24px;margin-top:22px}
.left{width:940px;flex-shrink:0;display:flex;flex-direction:column;gap:12px}
.toolbar{display:flex;align-items:center;gap:10px;height:30px}
.tcap{margin-left:auto;font-size:12px;color:var(--tx2);white-space:nowrap;display:flex;gap:6px;align-items:center}
.tcap b{font-weight:500;color:var(--tx)}

/* crossfading text */
.xf{display:inline-grid;min-width:0}
.xf>span{grid-area:1/1;opacity:0;white-space:nowrap;transition:opacity .22s ease}
.xf>span.on{opacity:1;transition:opacity .22s ease .08s}

/* segmented control */
.seg{position:relative;display:flex;padding:2px;border-radius:15px;background:var(--cap);height:30px;box-sizing:border-box;flex-shrink:0}
.seg .spill{position:absolute;top:2px;bottom:2px;border-radius:13px;background:var(--pill);box-shadow:0 1px 2px rgba(0,0,0,.18),inset 0 0 0 .5px var(--pillrim);transition:left .32s var(--ez),width .32s var(--ez)}
.seg button{appearance:none;border:0;background:none;font:inherit;position:relative;height:26px;padding:0;border-radius:13px;font-size:12px;font-weight:500;color:var(--tx2);cursor:pointer;white-space:nowrap;outline:2px solid transparent;outline-offset:1px;transition:color .15s ease,transform .12s ease,outline-color .12s ease}
.seg button:hover{color:var(--tx)}
.seg button:active{transform:scale(.96)}
.seg button.on{color:var(--tx)}
.seg button:focus-visible{outline-color:var(--acc)}
.seg.sm{height:26px;border-radius:13px}.seg.sm .spill{border-radius:11px}.seg.sm button{height:22px;font-size:11.5px;border-radius:11px}

.tbtn{appearance:none;border:0;font:inherit;display:flex;align-items:center;gap:7px;height:30px;padding:0 12px 0 10px;border-radius:15px;background:var(--cap);color:var(--tx);font-size:12px;font-weight:500;cursor:pointer;outline:2px solid transparent;outline-offset:1px;transition:background-color .15s ease,transform .12s ease,outline-color .12s ease}
.tbtn:hover{background:var(--pill)}
.tbtn:active{transform:scale(.97)}
.tbtn:focus-visible{outline-color:var(--acc)}
.tbtn .ic{position:relative;width:12px;height:12px}
.tbtn .ic svg{position:absolute;inset:0;transition:opacity .2s ease,transform .25s var(--ez)}
.tbtn .ic svg.off{opacity:0;transform:scale(.6)}
.tbtn .mini{position:relative;width:24px;height:14px;border-radius:7px;background:var(--swoff);transition:background-color .2s ease}
.tbtn .mini i{position:absolute;left:2px;top:2px;width:10px;height:10px;border-radius:50%;background:#fff;box-shadow:0 1px 2px rgba(0,0,0,.3);transition:transform .25s var(--ez)}
.tbtn.on .mini{background:var(--swon)}
.tbtn.on .mini i{transform:translateX(10px)}

/* stage */
.stagebox{position:relative;width:940px;height:600px}
.single,.grid{position:absolute;inset:0}
.single{transition:opacity .45s ease,transform .5s var(--ez),visibility 0s}
.single.off{opacity:0;transform:scale(.985);visibility:hidden;transition:opacity .3s ease,transform .5s var(--ez),visibility 0s linear .5s}
.grid{display:flex;flex-direction:column;gap:10px;visibility:hidden;transition:visibility 0s linear .5s}
.grid.on{visibility:visible;transition:visibility 0s}
.sw{display:flex;flex-direction:column;gap:5px;opacity:0;transform:translateY(10px);transition:opacity .25s ease,transform .4s var(--ez)}
.grid.on .sw{opacity:1;transform:none;transition:opacity .45s ease calc(var(--n) * 70ms + 120ms),transform .55s var(--ez) calc(var(--n) * 70ms + 120ms)}
.swcap{display:flex;align-items:center;gap:8px;height:17px;font-size:12px;color:var(--tx2);white-space:nowrap}
.swcap b{font-weight:600;color:var(--tx)}
.swcap .n{font-family:var(--mono);font-size:10.5px;padding:0 6px;border-radius:5px;background:var(--card);box-shadow:inset 0 0 0 1px var(--edge);color:var(--tx2)}

.win{position:relative;overflow:hidden;will-change:transform;border-radius:9px;box-shadow:0 0 0 1px var(--winedge),0 18px 48px rgba(0,0,0,.26);font-family:var(--ui);color:var(--wink);-webkit-font-smoothing:antialiased;isolation:isolate}
.win.big{width:940px;height:600px}
.win.strip{width:940px;height:120px;-webkit-mask:linear-gradient(to bottom,transparent 0,#000 26px);mask:linear-gradient(to bottom,transparent 0,#000 26px)}
.wbg{position:relative}
.wbg::before,.wbg::after{content:"";position:absolute;inset:0;background-size:940px 600px;pointer-events:none;border-radius:inherit}
.wbg::before{background-image:''' + WIN_D + r''';opacity:calc(1 - var(--wl))}
.wbg::after{background-image:''' + WIN_L + r''';opacity:var(--wl)}
.win.strip.wbg::before,.win.strip.wbg::after{background-position:0 -480px}

/* chat (the lower half of a real thread, as main-thread-1 draws it) */
.chat{position:absolute;left:0;right:0;top:0;height:510px;overflow:hidden;z-index:2}
.turn{position:absolute;left:60px;width:820px;top:-14px;bottom:-40px;border-radius:14px;background:var(--turn)}
.turn .tl{position:absolute;left:14px;display:flex;align-items:center;gap:10px;font-size:13px;line-height:18px;color:var(--wink3)}
.turn .tl svg{flex-shrink:0;opacity:.8}
.turn .tl svg path,.turn .tl svg rect,.turn .tl svg circle{fill:none;stroke:currentColor;stroke-width:1.2;stroke-linecap:round;stroke-linejoin:round}
.turn .tl .g{color:var(--plusg)}.turn .tl .r{color:var(--minr)}
.ans{position:absolute;left:14px;right:14px;top:182px;font-size:14px;line-height:21px;color:var(--wink)}
.ans h3{margin:0 0 8px;font-size:17px;line-height:24px;font-weight:600}
.ans h4{margin:10px 0 6px;font-size:15px;line-height:21px;font-weight:600}
.ans p{margin:0}
.ans ul{margin:0;padding-left:18px}
.ans li{margin:0 0 4px}
.ans code{font-family:"Cascadia Mono",Consolas,monospace;font-size:12.5px}
.codeb{position:absolute;left:14px;right:14px;top:398px;height:200px;border-radius:10px;background:var(--codebg);padding:10px 12px;box-sizing:border-box;font-family:"Cascadia Mono",Consolas,monospace;font-size:12.5px;line-height:15px;color:var(--codetx);white-space:pre}
.codeb .lang{font-family:var(--ui);font-size:11px;color:var(--wink3);display:block;margin-bottom:10px}
.codeb .k{color:var(--codekw)}.codeb .s{color:var(--codest)}

/* the composer, as the port draws it (gallery-r3 main-thread-*, composer-effort-*) */
.lclock,.lopts{position:absolute;inset:0}
.lopts{transition:''' + trans + r'''}
.files{position:absolute;left:0;right:0;bottom:76px;height:20px;display:flex;align-items:center;justify-content:center;gap:7px;font-size:13px;line-height:20px;color:var(--wink);z-index:2}
.files svg{opacity:.85}
.files svg path{fill:none;stroke:currentColor;stroke-width:1.2;stroke-linecap:round}
.files .g{color:var(--plusg)}.files .r{color:var(--minr)}
.cmp{position:absolute;left:60px;bottom:12px;width:820px;height:52px;border-radius:22px;z-index:3}
.plate{position:absolute;inset:0;border-radius:22px;overflow:hidden}
.plate.wbg{position:absolute}
.plate.wbg::before,.plate.wbg::after{background-position:-60px -536px}
.plate .glass{position:absolute;inset:0;border-radius:inherit;opacity:var(--pg);background:rgba(255,255,255,.045);box-shadow:inset 0 1px 0 rgba(255,255,255,.1),inset 0 0 0 1px rgba(255,255,255,.08),0 6px 20px rgba(0,0,0,.18)}
.board.light .plate .glass{background:rgba(255,255,255,.34);box-shadow:inset 0 1px 0 rgba(255,255,255,.7),inset 0 0 0 1px rgba(0,0,0,.06),0 6px 20px rgba(80,50,30,.08)}
.lopts.P-glass{--pg:1}
.cc{position:absolute;inset:0;z-index:4;font-size:15px;line-height:20px}
.ph{position:absolute;left:14px;top:16px;right:240px;height:20px;white-space:nowrap}
.ph .hint{position:absolute;left:0;top:0;color:var(--wink2);transition:opacity .25s ease}
.ph .typed{position:absolute;left:0;top:0;color:var(--wink);opacity:0;transition:opacity .25s ease}
.cmp.typing .ph .hint{opacity:0;transition:opacity .15s ease}
.cmp.typing .ph .typed{opacity:1;transition:opacity .12s ease}
.caret{display:inline-block;width:1.5px;height:17px;margin-left:1px;vertical-align:-3px;background:var(--wink);border-radius:1px;animation:blink 1.06s ease-in-out infinite}
@keyframes blink{0%,45%{opacity:1}55%,95%{opacity:0}100%{opacity:1}}
.tools{position:absolute;right:71.85px;top:16px;height:20px;display:flex;align-items:center;gap:19px}
.clip{flex-shrink:0;width:16px;height:16px;color:var(--wink3)}
.clip path{fill:none;stroke:currentColor;stroke-width:1.35;stroke-linecap:round;stroke-linejoin:round}
.chip{appearance:none;border:0;background:none;padding:0;margin:0;font:inherit;height:20px;display:flex;align-items:center;gap:6px;font-size:14px;color:var(--wink);cursor:pointer;border-radius:6px;outline:2px solid transparent;outline-offset:3px;transition:outline-color .12s ease}
.chip:focus-visible{outline-color:var(--acc)}
.chip .mkw{position:relative;width:15px;height:15px;flex-shrink:0}
.chip .mk,.chip .mdot{position:absolute;transition:opacity .35s ease,transform .45s var(--ez),background-color .6s ease}
.chip .mk{left:0;top:0;width:15px;height:15px}
.chip .mdot{left:3px;top:3px;width:9px;height:9px;border-radius:50%;box-shadow:inset 0 0 0 .5px rgba(0,0,0,.25)}
.chip .off{opacity:0;transform:scale(.5)}
.chip .mn{display:block;flex-shrink:0;white-space:nowrap;transition:width .32s var(--ez)}
.chip .mk path{stroke:#D97757;stroke-width:1.7;stroke-linecap:round}
.chip .ef{color:var(--wink3)}
.chip .cv{width:8px;height:6px;margin-left:2px;color:var(--wink3)}
.chip .cv path{fill:none;stroke:currentColor;stroke-width:1.3;stroke-linecap:round;stroke-linejoin:round}
.ring{position:absolute;left:756px;top:17px;width:18px;height:18px}
.ring svg{display:block;transform:rotate(calc(var(--ring-a, 0) * 1deg))}
.ring .t{fill:none;stroke:var(--ringtrk);stroke-width:2}
.ring .a{fill:none;stroke:var(--ringarc);stroke-width:2;stroke-linecap:round;transition:stroke-dasharray .6s var(--ez2)}
.send{position:absolute;left:784px;top:12px;width:28px;height:28px;border-radius:50%}
.send .d1,.send .d2{position:absolute;inset:0;border-radius:50%;display:flex;align-items:center;justify-content:center;transition:opacity .28s ease,transform .32s var(--ez),background-color .25s ease}
.send .d1{background:var(--disc)}
.send .d1 path{fill:none;stroke:var(--discarrow);stroke-width:1.7;stroke-linecap:round;stroke-linejoin:round}
.send .d2{background:var(--stopbg);opacity:0;transform:scale(.7)}
.send .d2 i{width:10px;height:10px;border-radius:2px;background:#fff}
.cmp.running .send .d1{opacity:0;transform:scale(.7)}
.cmp.running .send .d2{opacity:1;transform:none}
.cmp.typing .send .d1{background:color-mix(in srgb,var(--disc),var(--wink) 12%)}
/* The working dots, as ComposerWorkingDots.swift: 2 pt dots on an 8 pt pitch in the accent colour, filling the bottom
   40 % of the pill (ComposerView.swift 661-675) in up to 3 rows, each row higher a third fainter (instanceAlphaOffset),
   clipped to the pill's 22 pt corners; one opacity pulse per dot (.08 -> .75 at 35 %, ease-out, -> .08, ease-in, 2.2 s)
   spread into a wave by 0.045 s per column and 0.09 s per row; faded in and out over 0.4 s with the run. */
.wdots{position:absolute;left:0;right:0;bottom:0;height:40%;opacity:0;pointer-events:none;transition:opacity .4s ease-in-out;clip-path:inset(-31.2px 0 0 0 round 22px)}
.cmp.running .wdots{opacity:1}
.wdots>b{position:absolute;left:0;right:0;bottom:0;height:100%}
.wdots>b>i{position:absolute;width:2px;height:2px;border-radius:50%;background:var(--acc);opacity:.08;animation:wdot 2.2s infinite}
@keyframes wdot{0%{opacity:.08;animation-timing-function:cubic-bezier(0,0,.58,1)}35%{opacity:.75;animation-timing-function:cubic-bezier(.42,0,1,1)}100%{opacity:.08}}
@media (prefers-reduced-motion:reduce){.wdots>b>i{animation:none;opacity:.28}}

/* light groups: mounted always, faded in and out */
.g-aura,.g-beam,.dg{position:absolute;inset:0;opacity:0;visibility:hidden;pointer-events:none;transition:opacity .5s ease,visibility 0s linear .5s}
.E-aura .g-aura,.E-beam .g-beam,.dg.on{opacity:1;visibility:visible;transition:opacity .6s ease,visibility 0s}
.lout{position:absolute;inset:0;z-index:1;pointer-events:none;-webkit-mask:linear-gradient(to top,transparent 0,#000 13px);mask:linear-gradient(to top,transparent 0,#000 13px)}
.obox{position:absolute;left:60px;bottom:12px;width:820px;height:52px}

/* ---- Uiverse aura (after SelfMadeSystem's button, MIT) ---- */
.au-halo{position:absolute;inset:calc(-1 * var(--au-hs) - var(--dbs) * var(--brth, 0) * 1px);border-radius:calc(22px + var(--au-hs) + var(--dbs) * var(--brth, 0) * 1px);filter:blur(var(--au-hb));opacity:calc(var(--au-i) * var(--au-tm) * (1 - var(--dbd) * (1 - var(--brth, 1))))}
.au-clip{position:absolute;inset:0;border-radius:inherit;overflow:hidden}
.au-sq{position:absolute;left:50%;top:50%;width:400px;height:400px;margin:-200px 0 0 -200px;will-change:transform;transform:scale(var(--au-sx),var(--au-sy)) rotate(calc(var(--au-r, 10) * 1deg)) scale(var(--au-s, 1))}
.au-halo .au-sq{background:linear-gradient(90deg,var(--au-c1) 30%,transparent 50%,var(--au-c2) 70%)}
.au-rim{position:absolute;inset:calc(-1 * var(--au-rw));border-radius:calc(22px + var(--au-rw));filter:blur(calc(var(--au-rw) * 2));opacity:calc(var(--au-i) * var(--au-ro) * var(--au-tm) * (1 - var(--dbd) * .5 * (1 - var(--brth, 1))))}
.au-rim .au-sq{transform:scale(var(--au-rsx),var(--au-rsy)) rotate(calc(var(--au-r, 10) * 1deg)) scale(var(--au-s, 1));background:linear-gradient(90deg,var(--au-k1) 20%,transparent 45% 55%,var(--au-k2) 80%)}
.au-dots{position:absolute;inset:calc(-1 * var(--au-hs) - var(--au-hb) * 2.2);opacity:var(--au-do);
 -webkit-mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 20%,#000 50%) 0 0/var(--au-dd) var(--au-dd),radial-gradient(closest-side,#000 62%,transparent 100%);-webkit-mask-composite:source-in;
 mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 20%,#000 50%) 0 0/var(--au-dd) var(--au-dd) intersect,radial-gradient(closest-side,#000 62%,transparent 100%)}
.au-dots.wbg{position:absolute}
.au-dots.wbg::before,.au-dots.wbg::after{background-position:calc(var(--au-hs) + var(--au-hb) * 2.2 - 60px) calc(var(--au-hs) + var(--au-hb) * 2.2 - 536px)}
.au-in{position:absolute;inset:0;border-radius:22px;overflow:hidden;padding:1px;filter:blur(.6px);opacity:calc(var(--au-i) * var(--au-ro) * var(--au-tm) * .75);-webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);-webkit-mask-composite:xor;mask:linear-gradient(#000 0 0) content-box exclude,linear-gradient(#000 0 0)}
.au-in .au-sq{transform:scale(8.2,.52) rotate(calc(var(--au-r, 10) * 1deg)) scale(var(--au-s, 1));background:linear-gradient(90deg,var(--au-n1) 30%,transparent 45% 55%,var(--au-n2) 70%)}

/* ---- Border beam (border-beam 1.4.1 by Jakub Antalik, MIT): its generated sheets arrive in the board's <style>,
   one per structural key (.bbk-*); .b1 = its ::before, .b2 = ::after, .b3 = [data-beam-bloom], .b4 .b5 = Pulse Outside's
   haloes behind the composer. Three slots per composer cross-fade a structural change. ---- */
.bbh{position:absolute;inset:0;opacity:var(--bb-act);isolation:isolate}
.bbo{position:absolute;inset:0;opacity:var(--bb-act)}
.g-beam{--bb-act:0;transition:opacity .5s ease,visibility 0s linear .5s,--bb-act .5s ease}
.A-on .g-beam{--bb-act:1}
.E-beam .g-beam{transition:opacity .6s ease,visibility 0s,--bb-act .5s ease}
.E-beam.A-on .g-beam{transition:opacity .6s ease,visibility 0s,--bb-act .6s ease}
.bbs{position:absolute;inset:0;border-radius:var(--bb-r);overflow:hidden;opacity:0;transition:opacity .6s ease}
.bbs.on{opacity:1}
.bbs i{position:absolute;inset:0;display:block;box-sizing:content-box;pointer-events:none}
.bbs .b1>i{display:none}

/* ---- Marrow designs: each design brings its own CSS, scoped by [data-design] ---- */
.dg>i{position:absolute;inset:0;border-radius:22px}

/* control panel */
.panel{width:380px;height:902px;flex-shrink:0;display:flex;flex-direction:column;border-radius:18px;background:var(--card);box-shadow:inset 0 0 0 1px var(--edge),0 18px 48px rgba(0,0,0,.18);overflow:clip}
.phead{display:flex;flex-direction:column;gap:12px;padding:16px 16px 14px;border-bottom:1px solid var(--edge)}
.phead .pt{display:flex;align-items:baseline;gap:8px;font-size:13px;font-weight:600}
.phead .pt span{font-weight:400;font-size:12px;color:var(--tx2)}
.pbody{position:relative;flex:1;min-height:0;overflow:clip}
.pane{position:absolute;inset:0;overflow-y:auto;overflow-x:hidden;padding:6px 16px 22px;box-sizing:border-box;opacity:0;visibility:hidden;transform:translateY(8px);transition:opacity .22s ease,transform .3s var(--ez),visibility 0s linear .3s;scrollbar-width:thin;scrollbar-color:var(--trk) transparent;overscroll-behavior:contain}
.pane.on{opacity:1;visibility:visible;transform:none;transition:opacity .32s ease .1s,transform .45s var(--ez) .1s,visibility 0s}
.row{display:grid;grid-template-rows:1fr;transition:grid-template-rows .35s var(--ez),opacity .3s ease}
.row.fold{grid-template-rows:0fr;opacity:0}
.row.na{opacity:.4}
.rin{min-height:0;overflow:hidden}
.gh{display:flex;align-items:baseline;gap:8px;padding:16px 0 6px;font-size:11px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:var(--tx2)}
.gh span{text-transform:none;letter-spacing:0;font-weight:400;color:var(--tx3);font-size:11px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.rl{display:flex;align-items:center;gap:10px;min-height:34px}
.rl .lb{width:112px;flex-shrink:0;font-size:12.5px;line-height:16px;color:var(--tx)}
.rl .lb em{display:block;font-style:normal;font-size:10.5px;color:var(--tx3);line-height:13px}
.rl .seg{flex-shrink:0}
.rl.wide{min-height:30px;padding-bottom:2px}
.segs{display:grid}
.segs>.sgw{grid-area:1/1;opacity:0;visibility:hidden;transform:translateY(3px);transition:opacity .2s ease,transform .3s var(--ez),visibility 0s linear .3s}
.segs>.sgw.on{opacity:1;visibility:visible;transform:none;transition:opacity .3s ease .08s,transform .4s var(--ez) .08s,visibility 0s}
.sl{position:relative;flex:1;height:22px;cursor:pointer;touch-action:none;outline:none;min-width:0}
.sl .tr{position:absolute;left:0;right:0;top:9px;height:4px;border-radius:2px;background:var(--trk);overflow:hidden}
.sl .fi{position:absolute;left:0;top:0;bottom:0;border-radius:2px;background:var(--fill);transition:width .32s var(--ez)}
.sl .kn{position:absolute;top:4px;width:14px;height:14px;margin-left:-7px;border-radius:50%;background:var(--knob);box-shadow:0 1px 3px rgba(0,0,0,.35),0 0 0 .5px rgba(0,0,0,.12);transition:left .32s var(--ez),transform .15s ease,box-shadow .15s ease}
.sl:hover .kn{transform:scale(1.08)}
.sl:focus-visible .kn{box-shadow:0 0 0 2px var(--acc),0 1px 3px rgba(0,0,0,.35)}
.sl.drag .fi,.sl.drag .kn{transition:transform .15s ease}
.sl.drag .kn{transform:scale(1.12)}
.sl .tick{position:absolute;top:15px;width:1px;height:5px;margin-left:-.5px;background:var(--tx3);opacity:.8}
.vl{width:50px;flex-shrink:0;text-align:right;font-family:var(--mono);font-size:11px;color:var(--tx2);white-space:nowrap}
.au{appearance:none;border:0;font:inherit;flex-shrink:0;height:20px;padding:0 7px;border-radius:10px;font-size:10.5px;font-weight:500;background:transparent;color:var(--tx3);box-shadow:inset 0 0 0 1px var(--edge);cursor:pointer;transition:background-color .2s ease,color .2s ease,opacity .2s ease}
.au.on{background:var(--pill);color:var(--tx);box-shadow:inset 0 0 0 .5px var(--pillrim),0 1px 2px rgba(0,0,0,.15)}
.au.none{opacity:0;pointer-events:none}
.tg{appearance:none;border:0;padding:0;position:relative;width:34px;height:20px;border-radius:10px;background:var(--swoff);cursor:pointer;flex-shrink:0;margin-left:auto;outline:2px solid transparent;outline-offset:2px;transition:background-color .22s ease,outline-color .12s ease}
.tg i{position:absolute;left:2px;top:2px;width:16px;height:16px;border-radius:50%;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.3);transition:transform .28s var(--ez)}
.tg.on{background:var(--swon)}
.tg.on i{transform:translateX(14px)}
.tg:focus-visible{outline-color:var(--acc)}
.rl .tx{flex:1;font-size:11.5px;line-height:15px;color:var(--tx2)}
.cin{display:flex;gap:10px;align-items:center;flex:1}
.cin label{display:flex;align-items:center;gap:6px;font-size:11.5px;color:var(--tx2)}
.cin input{appearance:none;-webkit-appearance:none;width:26px;height:26px;padding:0;border:0;border-radius:50%;background:none;cursor:pointer;box-shadow:inset 0 0 0 1px var(--edge)}
.cin input::-webkit-color-swatch-wrapper{padding:2px}
.cin input::-webkit-color-swatch{border:0;border-radius:50%}
.sws{display:grid;grid-template-columns:repeat(8,1fr);gap:6px;flex:1}
.swb{appearance:none;border:0;padding:0;height:26px;border-radius:8px;cursor:pointer;position:relative;box-shadow:inset 0 0 0 1px var(--edge);outline:2px solid transparent;outline-offset:2px;transition:outline-color .2s ease,transform .12s ease}
.swb:active{transform:scale(.94)}
.swb.on{outline-color:var(--tx)}
.row.sw11 .rl{flex-wrap:wrap;row-gap:4px;padding:2px 0 6px}
.row.sw11 .rl .lb{width:100%}
.row.sw11 .sws{grid-template-columns:repeat(11,1fr);flex-basis:100%}
.row.sw11 .lb em{display:inline-block;margin-left:8px}
.swb:focus-visible{outline-color:var(--acc)}
.tin{flex:1;min-width:0;height:26px;box-sizing:border-box;border:0;border-radius:8px;padding:0 9px;font-family:var(--mono);font-size:11px;color:var(--tx);background:var(--cap);box-shadow:inset 0 0 0 1px var(--edge);outline:none}
.tin:focus{box-shadow:inset 0 0 0 1.5px var(--acc)}
.code{margin-top:6px;padding:9px 10px;border-radius:9px;background:var(--cap);font-family:var(--mono);font-size:10.5px;line-height:15px;color:var(--tx2);white-space:normal;word-break:break-word}
.code .xf>span{white-space:normal}
.btns{display:flex;gap:8px;padding:10px 0 2px}
.pbtn{appearance:none;border:0;font:inherit;height:26px;padding:0 11px;border-radius:13px;font-size:11.5px;font-weight:500;color:var(--tx);background:var(--cap);box-shadow:inset 0 0 0 1px var(--edge);cursor:pointer;transition:background-color .15s ease,transform .12s ease}
.pbtn:hover{background:var(--pill)}
.pbtn:active{transform:scale(.96)}
.note{font-size:11.5px;line-height:16px;color:var(--tx3);padding:8px 0 2px}
.note b{font-weight:500;color:var(--tx2)}
.drow{appearance:none;border:0;font:inherit;text-align:left;display:flex;flex-direction:column;gap:2px;width:100%;padding:9px 11px;margin-top:6px;border-radius:11px;background:transparent;box-shadow:inset 0 0 0 1px var(--edge);color:var(--tx);cursor:pointer;transition:background-color .2s ease,box-shadow .2s ease}
.drow b{font-size:12.5px;font-weight:600}
.drow b em{font-style:normal;font-family:var(--mono);font-size:10.5px;font-weight:400;color:var(--tx2);margin-right:7px}
.drow span{font-size:11.5px;color:var(--tx2);line-height:15px}
.drow.on{background:var(--pill);box-shadow:inset 0 0 0 1px var(--pillrim),0 1px 3px rgba(0,0,0,.12)}
.empty{margin-top:8px;padding:22px 16px;border-radius:12px;box-shadow:inset 0 0 0 1px var(--edge);text-align:center;font-size:12px;line-height:17px;color:var(--tx2)}
.empty b{display:block;font-size:13px;color:var(--tx);margin-bottom:4px;font-weight:600}

/* notes */
.notes{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}
.nc{position:relative;padding:12px 14px 12px;border-radius:14px;background:var(--card);box-shadow:inset 0 0 0 1px var(--edge);transition:box-shadow .35s ease}
.nc.on{box-shadow:inset 0 0 0 1px var(--edge),inset 0 0 0 1.5px var(--acc)}
.nc h5{margin:0 0 6px;display:flex;align-items:baseline;gap:8px;font-size:12.5px;font-weight:600}
.nc h5 span{margin-left:auto;font-family:var(--mono);font-size:10.5px;font-weight:400;color:var(--tx2);white-space:nowrap}
.nc p{margin:0 0 5px;font-size:11.2px;line-height:15.2px;color:var(--tx2)}
.nc p b{font-weight:500;color:var(--tx)}
.nc p .xf{display:grid}
.nc p .xf>span{white-space:normal}

@media (prefers-reduced-motion:reduce){.board *,.board *::before,.board *::after{transition:none!important;animation:none!important}}
'''

# ---------- template ----------
def wdots(width=820, rows=3):
    cols = -(-width // 8) + 1
    out = ['<div class="wdots" aria-hidden="true">']
    for r in range(rows):
        out.append('<b style="opacity:%.4f">' % (1 - r / rows))
        for c in range(cols):
            d = c * 0.045 + r * 0.09 - 44.0   # 44 s = 20 periods: the same phase as a positive delay, already running
            out.append('<i style="left:%dpx;bottom:%dpx;animation-delay:%.3fs"></i>' % (3 + c * 8, 3 + r * 8, d))
        out.append('</b>')
    out.append('</div>')
    return ''.join(out)
WDOTS = wdots()


def seg(g, cls='seg'):
    return ('<div class="%s" role="radiogroup" aria-label="{{%s.label}}"><span class="spill" style="left: {{%s.pl}}px; width: {{%s.pw}}px"></span>'
            '<sc-for list="{{%s.opts}}" as="o" hint-placeholder-count="3"><button class="{{o.cls}}" role="radio" aria-checked="{{o.on}}" style="width: {{o.w}}px" onClick="{{o.pick}}">{{o.t}}</button></sc-for></div>') % (cls, g, g, g, g)

def xf(v):
    return '<span class="xf"><span class="{{%s.a.cls}}">{{%s.a.t}}</span><span class="{{%s.b.cls}}">{{%s.b.t}}</span></span>' % (v, v, v, v)

ICON_FILES = '<svg width="11" height="12" viewBox="0 0 11 12" aria-hidden="true"><path d="M5.5 1.5 V7 M2.7 4.2 H8.3 M2.2 10.3 H8.8"></path></svg>'
CLAUDE = '<svg class="mk" viewBox="0 0 16 16" aria-hidden="true">' + ''.join('<path d="M%.2f %.2f L%.2f %.2f"></path>' % (8 + 2.0 * __import__('math').cos(a), 8 + 2.0 * __import__('math').sin(a), 8 + 6.6 * __import__('math').cos(a), 8 + 6.6 * __import__('math').sin(a)) for a in [i * 3.14159265 / 6 + 0.13 for i in range(12)]) + '</svg>'

INSTANCE = '''<div class="lclock" ref="{{i.clockRef}}">
<div class="{{i.cls}}" style="{{i.vars}}" data-beam="{{i.beamId}}">
<div class="lout">
<div class="obox">
<div class="g-aura"><div class="au-halo"><div class="au-clip"><div class="au-sq"></div></div></div><div class="au-rim"><div class="au-clip"><div class="au-sq"></div></div></div><div class="au-dots wbg"></div></div>
<div class="g-beam"><div class="bbo"><sc-for list="{{i.slots}}" as="s" hint-placeholder-count="3"><div class="{{s.cls}}"><i class="b4"></i><i class="b5"></i></div></sc-for></div></div>
<sc-for list="{{i.dgs}}" as="d" hint-placeholder-count="0"><div class="{{d.oc}}" data-design="{{d.id}}"><sc-for list="{{d.outer}}" as="l" hint-placeholder-count="1"><i class="{{l}}"></i></sc-for></div></sc-for>
</div>
</div>
<div class="files">''' + ICON_FILES + '''<span>2 files</span><span class="g">+14</span><span class="r">&#8722;0</span></div>
<div class="{{i.cmpCls}}">
<div class="plate wbg"><div class="glass"></div></div>
<div class="g-aura"><div class="au-in"><div class="au-sq"></div></div></div>
<div class="g-beam"><div class="bbh"><sc-for list="{{i.slots}}" as="s" hint-placeholder-count="3"><div class="{{s.cls}}"><i class="b1"><i></i></i><i class="b2"></i><i class="b3"></i></div></sc-for>
</div></div>
<sc-for list="{{i.dgs}}" as="d" hint-placeholder-count="0"><div class="{{d.ic}}" data-design="{{d.id}}"><sc-for list="{{d.inner}}" as="l" hint-placeholder-count="1"><i class="{{l}}"></i></sc-for></div></sc-for>
<div class="cc">
<div class="ph"><span class="hint">Ask Claude for anything; big jobs go out to a team of heads</span><span class="typed">{{typed}}<i class="caret"></i></span></div>
''' + WDOTS + '''
<div class="tools"><svg class="clip" viewBox="0 0 16 16" aria-hidden="true"><path d="M10.6 4.6 5.7 9.5a1.55 1.55 0 0 0 2.2 2.2l5-5a3 3 0 0 0-4.25-4.25l-5 5a4.4 4.4 0 0 0 6.2 6.2l4.2-4.2"></path></svg>
<button class="chip" type="button" aria-label="{{mdl.aria}}" onClick="{{chipTap}}"><span class="mkw">''' + CLAUDE.replace('class="mk"', 'class="{{mdl.claudeCls}}"') + '''<i class="{{mdl.dotCls}}" style="background: {{mdl.dot}}"></i></span><span class="mn" style="width: {{mdl.w}}px">''' + xf('mdl.name') + '''</span><span class="ef">High</span><svg class="cv" viewBox="0 0 8 6" aria-hidden="true"><path d="M1.2 1.5 4 4.3 6.8 1.5"></path></svg></button></div>
<div class="ring"><svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true"><circle class="t" cx="9" cy="9" r="7"></circle><circle class="a" cx="9" cy="9" r="7" style="stroke-dasharray: {{i.arc}}" transform="rotate(-90 9 9)"></circle></svg></div>
<div class="send"><span class="d1"><svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M6 10.2V1.9M2.6 5.3 6 1.9l3.4 3.4"></path></svg></span><span class="d2"><i></i></span></div>
</div>
</div>
</div>
</div>'''

CHAT = '''<div class="chat"><div class="turn">
<div class="tl" style="top: 30px"><svg width="14" height="14" viewBox="0 0 14 14"><path d="M2.5 11.5 L3 9 L9.5 2.5 L11.5 4.5 L5 11 Z"></path></svg>Edited files, read files, ran commands, searched<svg width="8" height="6" viewBox="0 0 8 6"><path d="M1.2 1.5 4 4.3 6.8 1.5"></path></svg></div>
<div class="tl" style="top: 65px"><svg width="14" height="14" viewBox="0 0 14 14"><path d="M3.5 1.5 H8 L10.5 4 V12.5 H3.5 Z M5.5 7 H8.5 M5.5 9.5 H8.5"></path></svg>Read src/settings/SettingsPage.tsx</div>
<div class="tl" style="top: 100px"><svg width="14" height="14" viewBox="0 0 14 14"><circle cx="6" cy="6" r="3.8"></circle><path d="M9 9 L12 12"></path></svg>Searched "useLocalStorage"<svg width="6" height="8" viewBox="0 0 6 8"><path d="M1.5 1.2 4.3 4 1.5 6.8"></path></svg></div>
<div class="tl" style="top: 135px"><svg width="14" height="14" viewBox="0 0 14 14"><path d="M2.5 11.5 L3 9 L9.5 2.5 L11.5 4.5 L5 11 Z"></path></svg>Edited src/settings/ThemeToggle.tsx <span class="g">+14</span><span class="r">&#8722;0</span></div>
<div class="ans">
<h3>Dark mode toggle</h3>
<p>I added a <b>Theme</b> switch to the settings page. It reuses the existing <code>Switch</code> row, so it picks up the same spacing and focus ring as the other options.</p>
<h4>What changed</h4>
<ul><li><code>ThemeToggle.tsx</code> renders the row and reads the saved value through <code>useLocalStorage</code>.</li><li><code>settingsSchema.ts</code> lists the new <code>theme</code> entry between <i>Notifications</i> and <i>Language</i>.</li><li><code>settings.test.tsx</code> covers the toggle, including a reload that keeps the choice.</li></ul>
</div>
<div class="codeb"><span class="lang">tsx</span><span class="k">export function</span> ThemeToggle() {
  <span class="k">const</span> [theme, setTheme] = useLocalStorage&lt;<span class="s">'light'</span> | <span class="s">'dark'</span>&gt;(<span class="s">'theme'</span>, <span class="s">'light'</span>)
  <span class="k">return</span> (
    &lt;Switch
      label=<span class="s">"Dark mode"</span></div>
</div></div>'''

PANE_ROWS = '''<sc-for list="{{p.rows}}" as="r" hint-placeholder-count="6"><div class="{{r.cls}}"><div class="rin">
<sc-if value="{{r.isHead}}" hint-placeholder-val="{{false}}"><div class="gh">{{r.t}}<span>{{r.sub}}</span></div></sc-if>
<sc-if value="{{r.isSeg}}" hint-placeholder-val="{{true}}"><div class="rl"><span class="lb">{{r.t}}<em>{{r.sub}}</em></span><div class="segs"><sc-for list="{{r.segs}}" as="g" hint-placeholder-count="1"><div class="{{g.cls}}">''' + seg('g', 'seg sm') + '''</div></sc-for></div></div></sc-if>
<sc-if value="{{r.isSlider}}" hint-placeholder-val="{{false}}"><div class="rl"><span class="lb">{{r.t}}<em>{{r.sub}}</em></span><div class="{{r.slCls}}" role="slider" tabindex="0" aria-label="{{r.t}}" aria-valuemin="{{r.min}}" aria-valuemax="{{r.max}}" aria-valuenow="{{r.v}}" onPointerDown="{{r.down}}" onPointerMove="{{r.move}}" onPointerUp="{{r.up}}" onPointerCancel="{{r.up}}" onKeyDown="{{r.key}}"><span class="tr"><span class="fi" style="width: {{r.pct}}%"></span></span><sc-for list="{{r.ticks}}" as="k" hint-placeholder-count="0"><span class="tick" style="left: {{k}}%"></span></sc-for><span class="kn" style="left: {{r.pct}}%"></span></div><span class="vl">{{r.vt}}</span><button class="{{r.auCls}}" aria-pressed="{{r.auOn}}" onClick="{{r.auTog}}">{{r.auT}}</button></div></sc-if>
<sc-if value="{{r.isToggle}}" hint-placeholder-val="{{false}}"><div class="rl"><span class="lb">{{r.t}}<em>{{r.sub}}</em></span><span class="tx">{{r.tx}}</span><button class="{{r.tgCls}}" role="switch" aria-checked="{{r.on}}" aria-label="{{r.t}}" onClick="{{r.tog}}"><i></i></button></div></sc-if>
<sc-if value="{{r.isColors}}" hint-placeholder-val="{{false}}"><div class="rl"><span class="lb">{{r.t}}<em>{{r.sub}}</em></span><div class="cin"><label><input type="color" value="{{r.c1}}" aria-label="First colour" onInput="{{r.on1}}" onChange="{{r.on1}}">one side</label><label><input type="color" value="{{r.c2}}" aria-label="Second colour" onInput="{{r.on2}}" onChange="{{r.on2}}">the other</label></div></div></sc-if>
<sc-if value="{{r.isSwatches}}" hint-placeholder-val="{{false}}"><div class="rl"><span class="lb">{{r.t}}<em>''' + xf('r.sub') + '''</em></span><div class="sws" role="radiogroup" aria-label="{{r.t}}"><sc-for list="{{r.sw}}" as="s" hint-placeholder-count="8"><button class="{{s.cls}}" role="radio" aria-checked="{{s.on}}" aria-label="{{s.name}}" title="{{s.name}}" style="background: {{s.bg}}" onClick="{{s.pick}}"></button></sc-for></div></div></sc-if>
<sc-if value="{{r.isText}}" hint-placeholder-val="{{false}}"><div class="rl"><span class="lb">{{r.t}}<em>{{r.sub}}</em></span><input class="tin" type="text" spellcheck="false" value="{{r.v}}" placeholder="{{r.ph}}" aria-label="{{r.t}}" onInput="{{r.on}}" onChange="{{r.on}}"></div></sc-if>
<sc-if value="{{r.isCode}}" hint-placeholder-val="{{false}}"><div class="code">''' + xf('r.code') + '''</div></sc-if>
<sc-if value="{{r.isNote}}" hint-placeholder-val="{{false}}"><div class="note">{{r.t}}</div></sc-if>
<sc-if value="{{r.isBtns}}" hint-placeholder-val="{{false}}"><div class="btns"><sc-for list="{{r.btns}}" as="b" hint-placeholder-count="2"><button class="pbtn" onClick="{{b.go}}">{{b.t}}</button></sc-for></div></sc-if>
<sc-if value="{{r.isDesign}}" hint-placeholder-val="{{false}}"><button class="{{r.dCls}}" role="radio" aria-checked="{{r.on}}" onClick="{{r.pick}}"><b><em>{{r.tag}}</em>{{r.t}}</b><span>{{r.sub}}</span></button></sc-if>
<sc-if value="{{r.isWide}}" hint-placeholder-val="{{false}}"><div class="rl wide">''' + seg('r.seg', 'seg sm') + '''</div></sc-if>
<sc-if value="{{r.isEmpty}}" hint-placeholder-val="{{false}}"><div class="empty"><b>{{r.t}}</b>{{r.sub}}</div></sc-if>
</div></div></sc-for>'''

TEMPLATE = '''<div class="{{boardCls}}" style="width: 1440px; height: 1100px">
<div class="wall"></div><div class="wall2"></div>
<style>{{designCss}}</style>
<div class="inner">
<div class="hdr"><div><h1>Composer lights</h1><p>{{intro}}</p></div><div class="od">OD-0083, OD-0076, OD-0069<br>border-beam 1.4.1, Uiverse.io (MIT)</div></div>
<div class="cols">
<div class="left">
<div class="toolbar">
''' + seg('stateSeg') + '''
''' + seg('themeSeg') + '''
<button class="tbtn" aria-pressed="{{paused}}" onClick="{{pauseTog}}"><span class="ic"><svg class="{{pauseIc}}" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><rect x="2.5" y="2" width="2.4" height="8" rx=".8" fill="currentColor"></rect><rect x="7.1" y="2" width="2.4" height="8" rx=".8" fill="currentColor"></rect></svg><svg class="{{playIc}}" width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M3.5 2.2 L9.8 6 L3.5 9.8 Z" fill="currentColor"></path></svg></span>''' + xf('pauseLab') + '''</button>
<button class="{{cmpBtnCls}}" aria-pressed="{{compare}}" onClick="{{compareTog}}"><span class="mini"><i></i></span>Compare</button>
<div class="tcap"><span>Light</span><b>''' + xf('capLight') + '''</b><span>&#183;</span>''' + xf('capState') + '''</div>
</div>
<div class="stagebox">
<div class="{{singleCls}}"><div class="win big wbg">''' + CHAT + '''
<sc-for list="{{single}}" as="i" hint-placeholder-count="1">''' + INSTANCE + '''</sc-for>
</div></div>
<div class="{{gridCls}}">
<sc-for list="{{strips}}" as="i" hint-placeholder-count="4"><div class="sw" style="--n: {{i.n}}"><div class="swcap"><span class="n">{{i.num}}</span><b>{{i.title}}</b>''' + xf('i.sub') + '''</div><div class="win strip wbg">''' + INSTANCE + '''</div></div></sc-for>
</div>
</div>
<div class="notes"><sc-for list="{{notes}}" as="c" hint-placeholder-count="3"><div class="{{c.cls}}"><h5>{{c.t}}<span>''' + xf('c.cost') + '''</span></h5><sc-for list="{{c.ps}}" as="x" hint-placeholder-count="2"><p>''' + xf('x') + '''</p></sc-for></div></sc-for></div>
</div>
<aside class="panel">
<div class="phead"><div class="pt">Light<span>''' + xf('lightSub') + '''</span></div>''' + seg('lightSeg') + '''<div class="rl" style="min-height: 26px"><span class="lb">Composer<em>under every light</em></span>''' + seg('plateSeg', 'seg sm') + '''</div></div>
<div class="pbody"><sc-for list="{{panes}}" as="p" hint-placeholder-count="4"><div class="{{p.cls}}">''' + PANE_ROWS + '''</div></sc-for></div>
</aside>
</div>
</div>
</div>'''

js = open(os.path.join(HERE, 'beam.js'), encoding='utf-8').read() + '\n' + open(os.path.join(HERE, 'logic.js'), encoding='utf-8').read()
js = js.replace('/*@BBTABLES@*/null', json.dumps(BBT, separators=(',', ':')))
ACC = json.load(open('E:/Droppy/work/design/accent/palette.json', encoding='utf-8'))['accents']
js = js.replace('/*@ACCENTS@*/null', json.dumps([[a['id'], a['name'], a['dark']['hex'], a['light']['hex']] for a in ACC], separators=(',', ':')))

html = '''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Composer lights playground</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600&amp;family=Geist+Mono:wght@400&amp;display=swap" rel="stylesheet">
<style>''' + css + '''</style>
</helmet>
''' + TEMPLATE + '''
</x-dc>
<script type="text/x-dc" data-dc-script data-props='{"dark":{"editor":"boolean","default":true},"$preview":{"width":1440,"height":1100}}'>
''' + js + '''
</script>
</body>
</html>
'''
open(OUT, 'w', encoding='utf-8', newline='\n').write(html)
print('wrote', OUT, len(html.encode('utf-8')), 'bytes;', len(REG), 'registered instance properties')
