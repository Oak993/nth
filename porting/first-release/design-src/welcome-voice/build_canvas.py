# Builds the Welcome voice canvas (project/): a cover, six lettered place boards (A to F) each drawing the question,
# Yes, No and the slow-PC question in the real window, the Words board (V1 to V3) and the Orb board (what a click on
# the voice orb shows later), every board with a Light twin, and canvas.json. House format: an <x-dc> template whose
# root is class="board {{themeClass}}", a Component giving themeClass from props.dark, a Light twin that dc-imports
# its dark board with dark=false (as decisions-2026-10-03/getting-ready/src/build_canvas.py).
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wv_lib as W

G = W.G
E = W.E
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'project')
os.makedirs(OUT, exist_ok=True)

BOARD_CSS = r"""
body{margin:0}
.board{position:relative;overflow:hidden;font-family:Geist,system-ui,sans-serif;color:#f4f6fb;--sub:rgba(244,246,251,.64);--faint:rgba(244,246,251,.44);--chip:rgba(255,255,255,.08);--edge:rgba(255,255,255,.14);--acc:#60CDFF;--good:#6CCB5F}
.board.light{color:#14161c;--sub:rgba(20,22,28,.64);--faint:rgba(20,22,28,.46);--chip:rgba(20,30,45,.07);--edge:rgba(255,255,255,.85);--acc:#005FB8;--good:#0F7B3F}
.wall{position:absolute;inset:0;background:radial-gradient(900px 600px at 12% 8%,#2d5672 0%,transparent 60%),radial-gradient(800px 700px at 90% 35%,#7a5634 0%,transparent 55%),radial-gradient(1000px 800px at 45% 90%,#155f5f 0%,transparent 60%),#121b25}
.board.light .wall{background:radial-gradient(900px 600px at 12% 8%,#a9cde6 0%,transparent 60%),radial-gradient(800px 700px at 90% 35%,#f1cfa8 0%,transparent 55%),radial-gradient(1000px 800px at 45% 90%,#9fd9d2 0%,transparent 60%),#e8edf2}
.hd{position:absolute;left:40px;top:30px;right:40px;display:flex;align-items:center;gap:16px}
.lt{min-width:52px;height:52px;padding:0 10px;border-radius:14px;display:grid;place-items:center;font-size:26px;font-weight:600;background:var(--chip);box-shadow:inset 0 0 0 1px var(--edge);flex:none}
.hd .nm{display:flex;flex-direction:column;gap:3px;min-width:0}
.hd .nm b{font-size:24px;font-weight:600;letter-spacing:-.01em;line-height:30px}
.hd .nm span{font-size:14px;color:var(--sub);line-height:19px}
.hd .rc{margin-left:auto;font-size:12px;font-weight:600;color:var(--good);padding:5px 11px;border-radius:999px;background:var(--chip);white-space:nowrap}
.bcap{font-family:"Geist Mono",ui-monospace,monospace;font-size:11px;line-height:14px;color:var(--faint)}
.bcap b{color:var(--sub);font-weight:500}
.quad{position:absolute;left:40px;top:118px;display:grid;grid-template-columns:662px 662px;gap:26px 36px}
.cell{display:flex;flex-direction:column;gap:8px}
.fr{position:relative;width:662px;height:469px}
.fr>.grw{position:absolute;left:0;top:0;transform:scale(.69);transform-origin:0 0}
.foot{position:absolute;left:40px;right:40px;bottom:26px;display:flex;flex-direction:column;gap:5px;font-size:12.5px;line-height:18px;color:var(--sub)}
.foot b{color:inherit;font-weight:600}
"""

HEAD = '''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>%s</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600&amp;family=Geist+Mono:wght@400;500&amp;display=swap" rel="stylesheet">
<style>
%s
</style>
</helmet>
'''

TAIL = '''</x-dc>
<script type="text/x-dc" data-dc-script data-props='{"dark":{"editor":"boolean","default":true},"$preview":{"width":%d,"height":%d}}'>
class Component extends DCLogic {
  renderVals() {
    const dark = this.props.dark ?? true;
    return { themeClass: dark ? 'dark' : 'light' };
  }
}
</script>
</body>
</html>
'''


def light_twin(name, title, w, h):
    return '''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>%s, light</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<style>
body{margin:0}
</style>
</helmet>
<div style="width: %dpx; height: %dpx">
<dc-import name="%s" dark="{{no}}" hint-size="%dpx,%dpx"></dc-import>
</div>
</x-dc>
<script type="text/x-dc" data-dc-script data-props='{"$preview":{"width":%d,"height":%d}}'>
class Component extends DCLogic {
  renderVals() {
    return { no: false };
  }
}
</script>
</body>
</html>
''' % (title, w, h, name, w, h, w, h)


boards = {}


def write(name, title, w, h, body, extra_css=''):
    src = HEAD % (E(title), BOARD_CSS + W.css() + extra_css) + body + TAIL % (w, h)
    open(os.path.join(OUT, name + '.dc.html'), 'w', encoding='utf-8', newline='\n').write(src)
    open(os.path.join(OUT, name + 'Light.dc.html'), 'w', encoding='utf-8', newline='\n').write(light_twin(name, title, w, h))
    boards[name] = (w, h, title)
    boards[name + 'Light'] = (w, h, title + ', light')


def hdr(letter, name, line, rec=False):
    return ('<div class="hd"><span class="lt">%s</span><span class="nm"><b>%s</b><span>%s</span></span>%s</div>' % (
        E(letter), E(name), E(line), '<span class="rc">Recommended</span>' if rec else ''))


# ---------------------------------------------------------------------------------------------------- place boards
for letter, name, line, slot, fold in W.VARIANTS:
    cells = [
        ('1', 'The question, as Welcome settles (this PC)', W.window(letter, 'V1', 'pc', 'ask')),
        ('2', 'Yes: the model downloads in the background (315 of 874 MB)', W.window(letter, 'V1', 'pc', 'yes', dg=0.36)),
        ('3', 'No: nothing downloads', W.window(letter, 'V1', 'pc', 'no')),
        ('4', 'Slow PC: the speed test ends while Welcome is up, and asks once', W.window(letter, 'V1', 'slow', 'slow')),
    ]
    body = ('<div class="board {{themeClass}}" style="width: 1440px; height: 1220px"><div class="wall"></div>%s<div class="quad">%s</div>'
            '<div class="foot"><span><b>Shown:</b> look I after its hand-over, words V1 Plain. 1 to 3 on this PC (Large v3 Turbo, balanced, 874 MB); 4 on a PC without a graphics card (Large v3 Turbo, compact, 574 MB; the live model, Small, 264 MB), where the speed test finds it too slow for live words.</span>'
            '<span><b>Played live</b> on the interactive page: the question folds in, the answer crossfades in place while the box eases to its new height, and nothing below it jumps (OD-0076).</span></div></div>') % (
        hdr(letter, '%s  %s' % (letter, name), line, letter == W.REC['place']),
        ''.join('<div class="cell"><span class="bcap"><b>%s</b> %s</span><div class="fr">%s</div></div>' % (n, E(c), win) for n, c, win in cells))
    write('Place' + letter, 'Welcome voice %s: %s' % (letter, name), 1440, 1220, body)

# ---------------------------------------------------------------------------------------------------- words board
WCSS = r"""
.wgrid{position:absolute;left:40px;top:118px;width:1360px;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:24px 32px}
.wc{display:flex;flex-direction:column;gap:8px;min-width:0}
.wc .bcap em{font-style:normal;color:var(--good);font-weight:600;margin-left:6px}
.wf{position:relative;width:432px;height:306px}
.wf>.grw{position:absolute;left:0;top:0;transform:scale(.45);transform-origin:0 0}
.wl{font-size:12px;line-height:17px;color:var(--sub);margin:0}
.wl dt{color:var(--good);font:500 10.5px/1.3 "Geist Mono",ui-monospace,monospace;margin-top:8px}
.wl dd{margin:2px 0 0}
.wl dd b{color:inherit}
.wl .sp{white-space:nowrap}
"""
cells = []
for k, w in W.WORDS.items():
    rec = ' <em>Recommended</em>' if k == W.REC['words'] else ''
    items = [('Question', '<b>%s</b> %s  [%s] [%s]' % (W.fill(w['ask']['t'], 'pc'), W.fill(w['ask']['b'], 'pc'), E(w['ask']['yes']), E(w['ask']['no'])))]
    items += [('After Yes', '<b>%s</b> %s' % (W.fill(w['yes']['t'], 'pc'), W.fill(w['yes']['b'], 'pc'))),
              ('Once downloaded (fast PC)', '<b>%s</b> %s' % (W.fill(w['ready']['t'], 'pc'), W.fill(w['ready']['b'], 'pc'))),
              ('After No', '<b>%s</b> %s' % (W.fill(w['no']['t'], 'pc'), W.fill(w['no']['b'], 'pc'))),
              ('Slow PC asks', '<b>%s</b> %s<br>[<b>%s</b>] %s<br>[<b>%s</b>] %s<br>%s' % (
                  W.fill(w['slow']['t'], 'slow'), W.fill(w['slow']['b'], 'slow'), W.fill(w['slow']['live_t'], 'slow'), W.fill(w['slow']['live_d'], 'slow'),
                  W.fill(w['slow']['acc_t'], 'slow'), W.fill(w['slow']['acc_d'], 'slow'), W.fill(w['slow']['foot'], 'slow'))),
              ('After the pick', '<b>%s</b> %s<br><b>%s</b> %s' % (W.fill(w['slowlive']['t'], 'slow'), W.fill(w['slowlive']['b'], 'slow'), W.fill(w['slowacc']['t'], 'slow'), W.fill(w['slowacc']['b'], 'slow')))]
    dl = ''.join('<dt>%s</dt><dd>%s</dd>' % (E(a), b) for a, b in items)
    cells.append('<div class="wc"><div class="bcap"><b>%s</b> %s%s · %s</div><div class="wf">%s</div><div class="wf">%s</div><dl class="wl">%s</dl></div>' % (
        k, E(w['name']), rec, E(w['line']), W.window('A', k, 'pc', 'ask'), W.window('A', k, 'slow', 'slow'), dl))
body = ('<div class="board {{themeClass}}" style="width: 1440px; height: 1300px"><div class="wall"></div>%s<div class="wgrid">%s</div></div>') % (
    hdr('V', 'Words: three sets', 'The question, its two answers, the line once ready, the slow-PC question and its two outcomes; each names Settings > Voice typing > Speech model. Drawn in place A.'),
    ''.join(cells))
write('Words', 'Welcome voice: the words', 1440, 1300, body, WCSS)

# ---------------------------------------------------------------------------------------------------- orb board
OCSS = r"""
.ogrid{position:absolute;left:40px;top:118px;display:grid;grid-template-columns:648px 648px;gap:24px 64px}
.oc{display:flex;flex-direction:column;gap:8px}
.of{position:relative;width:648px;height:387px}
.of>.grw{position:absolute;left:0;top:0;transform:scale(.9);transform-origin:0 0}
"""
cells = []
for (st, cap), n in zip(W.CM_STATES, '1234'):
    cells.append('<div class="oc"><span class="bcap"><b>%s</b> %s</span><div class="of">%s</div></div>' % (n, E(cap), W.composer(st, 'V1', 'pc' if st in ('dl', 'nodl') else 'slow')))
body = ('<div class="board {{themeClass}}" style="width: 1440px; height: 1100px"><div class="wall"></div>%s<div class="ogrid">%s</div>'
        '<div class="foot"><span><b>1, 2</b> are what Welcome’s words promise: the progress shows at the orb (the D3 capsule words, “Getting voice ready”), and after No the first click starts the same download, its cross cancelling it.</span>'
        '<span><b>3</b> is the slow-PC question when the speed test ends after Welcome has gone: it waits for the next click on the orb and asks there, in a popover with the Mac’s arrow tip (OD-0095), before listening starts. <b>4</b> after the pick, the pill opens on L1’s “Listening…” (OD-0097).</span></div></div>') % (
    hdr('O', 'At the orb', 'What a click on the voice orb shows later, and where the slow-PC question goes when Welcome is no longer on screen.'), ''.join(cells))
write('Orb', 'Welcome voice: at the orb', 1440, 1100, body, OCSS)

# ---------------------------------------------------------------------------------------------------- cover
COVER_CSS = r"""
body{margin:0}
.cv{position:relative;width:1440px;height:640px;box-sizing:border-box;padding:56px 64px;display:flex;flex-direction:column;gap:22px;
  background:radial-gradient(900px 500px at 12% 10%,#2d5672 0%,transparent 60%),radial-gradient(800px 600px at 90% 40%,#7a5634 0%,transparent 55%),#121b25;color:#f4f6fb;font-family:Geist,system-ui,sans-serif}
.cv h1{margin:0;font-size:40px;font-weight:600;letter-spacing:-.01em}
.cv .lede{margin:0;font-size:15px;line-height:22px;color:rgba(244,246,251,.66);max-width:1150px}
.qs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
.q{display:flex;flex-direction:column;gap:10px;padding:18px 20px;border-radius:16px;background:rgba(255,255,255,.07);box-shadow:inset 0 0 0 1px rgba(255,255,255,.14)}
.q .n{font-family:"Geist Mono",monospace;font-size:12px;color:#60CDFF}
.q b{font-size:18px;font-weight:600}
.q ul{margin:0;padding-left:18px;font-size:13px;line-height:20px;color:rgba(244,246,251,.72)}
.q .rec{margin-top:auto;font-size:13px;line-height:19px;color:#9BE08F}
.q .rec strong{color:#C7F3BF}
"""
places = ''.join('<li><b style="font-size:13px">%s</b> %s</li>' % (v[0], E(v[1])) for v in W.VARIANTS)
words = ''.join('<li><b style="font-size:13px">%s</b> %s: “%s”</li>' % (k, E(w['name']), E(w['ask']['t'])) for k, w in W.WORDS.items())
cover = ('<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>Welcome voice cover</title>\n<script src="./support.js"></script>\n</head>\n<body>\n<x-dc>\n<helmet>\n'
         '<link rel="preconnect" href="https://fonts.googleapis.com">\n<link href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600&amp;family=Geist+Mono:wght@400&amp;display=swap" rel="stylesheet">\n'
         '<style>%s</style>\n</helmet>\n'
         '<div class="cv"><h1>Welcome asks about voice typing</h1>'
         '<p class="lede">Getting ready is locked (look I, words W1, download D3 with consent): nothing downloads until the Welcome screen asks. Yes starts the download in the background; No downloads nothing. Either answer names the voice orb in the composer and Settings &gt; Voice typing &gt; Speech model. On a slow PC the speed test then asks once: live words with a smaller model, or the accurate model after you stop. Two choices; answer by letter (“A, V1”).</p>'
         '<div class="qs">'
         '<div class="q"><span class="n">Q1 · where the question sits</span><b>6 places, A to F</b><ul style="columns:2;column-gap:18px">%s</ul>'
         '<div class="rec"><strong>Recommended: A Section.</strong> It is Welcome’s own pattern (a titled section of rows), it has room for the three things the answer must say, and it never covers or pushes Add a project.</div></div>'
         '<div class="q"><span class="n">Q2 · the words</span><b>3 sets, V1 to V3</b><ul>%s</ul>'
         '<div class="rec"><strong>Recommended: V1 Plain.</strong> Matches W1 on Getting ready, says each fact once, and states the language trade-off plainly.</div></div>'
         '</div></div>\n</x-dc>\n'
         '<script type="text/x-dc" data-dc-script data-props=\'{"$preview":{"width":1440,"height":640}}\'>\nclass Component extends DCLogic {\n  renderVals() {\n    return {};\n  }\n}\n</script>\n</body>\n</html>\n') % (COVER_CSS, places, words)
open(os.path.join(OUT, 'Main.dc.html'), 'w', encoding='utf-8', newline='\n').write(cover)

# ---------------------------------------------------------------------------------------------------- canvas.json
B = {'Main.dc.html': dict(x=0, y=0, w=1440, h=640, title='Cover')}
ORDER = ['Main.dc.html']
notes = {}
y = 1000
notes['place'] = dict(x=0, y=y - 260, text='Q1 Where the question sits: A to F (dark, then light)', kind='title1', maxW=8960)
for i, v in enumerate(W.VARIANTS):
    B['Place%s.dc.html' % v[0]] = dict(x=i * 1520, y=y, w=1440, h=1220, title='%s %s' % (v[0], v[1]))
    B['Place%sLight.dc.html' % v[0]] = dict(x=i * 1520, y=y + 1340, w=1440, h=1220, title='%s %s, light' % (v[0], v[1]))
    ORDER += ['Place%s.dc.html' % v[0], 'Place%sLight.dc.html' % v[0]]
y2 = y + 2 * 1340 + 360
notes['words'] = dict(x=0, y=y2 - 260, text='Q2 The words, and what the orb shows later', kind='title1', maxW=5920)
B['Words.dc.html'] = dict(x=0, y=y2, w=1440, h=1300, title='V Words, dark')
B['WordsLight.dc.html'] = dict(x=1520, y=y2, w=1440, h=1300, title='V Words, light')
B['Orb.dc.html'] = dict(x=3040, y=y2, w=1440, h=1100, title='O At the orb, dark')
B['OrbLight.dc.html'] = dict(x=4560, y=y2, w=1440, h=1100, title='O At the orb, light')
ORDER += ['Words.dc.html', 'WordsLight.dc.html', 'Orb.dc.html', 'OrbLight.dc.html']
canvas = {'v': 3, 'createdOnFiles': {'v': 1, 'at': '2026-10-04T12:00:00Z'}, 'title': 'Marrow Welcome voice question (OD-0096, OD-0097)', 'launch': {'view': 'canvas'},
          'pages': [], 'boards': B, 'order': ORDER, 'notes': notes, 'designSystems': []}
json.dump(canvas, open(os.path.join(OUT, 'canvas.json'), 'w', encoding='utf-8', newline='\n'), indent=2, ensure_ascii=False)
print('boards', len(ORDER))
