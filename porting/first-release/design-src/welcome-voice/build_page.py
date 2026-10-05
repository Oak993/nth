# Builds the interactive Welcome voice page (welcome-voice.html) and its sharing copy (share/welcome-voice.html: the
# doctype/html/head/body skeleton stripped, everything inline, Google Fonts only), with the decisions-2026-10-03 kit.
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import wv_lib as W
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402

G = W.G
ROOT = os.path.join(HERE, '..')

PAGE_CSS = r"""
.qrow{display:grid;grid-template-columns:96px minmax(0,1fr) auto;gap:6px 14px;align-items:center;padding:12px 0;box-shadow:0 -1px 0 var(--hair)}
.qrow:first-of-type{box-shadow:none;padding-top:2px}
.qrow .qk{display:flex;flex-direction:column;gap:2px}
.qrow .qk b{font-size:13px;font-weight:600}
.qrow .qk span{font:500 11px/1.2 var(--mono);color:var(--good)}
.qrow .ln{grid-column:2/4;font-size:12.5px;color:var(--sub);min-height:18px}
.qrow .pk{justify-self:end}
@media (max-width:720px){.qrow{grid-template-columns:minmax(0,1fr) auto}.qrow .qk{grid-column:1/3}.qrow .ln{grid-column:1/3}}
.qs{padding:14px 18px}
.stagecard{padding:18px;border-radius:22px;display:flex;flex-direction:column;gap:14px}
.stagecard h2{margin:0;font-size:16px;font-weight:600}
.stagecard .sub{margin:-8px 0 0;font-size:13px;color:var(--sub);max-width:90ch}
.ctl{display:flex;flex-wrap:wrap;align-items:center;gap:10px 14px}
.wallbg{position:absolute;inset:0;background:radial-gradient(900px 600px at 12% 8%,#2d5672 0%,transparent 60%),radial-gradient(800px 700px at 90% 35%,#7a5634 0%,transparent 55%),radial-gradient(1000px 800px at 45% 90%,#155f5f 0%,transparent 60%),#121b25;transition:opacity .45s ease}
.wallbg.l{background:radial-gradient(900px 600px at 12% 8%,#a9cde6 0%,transparent 60%),radial-gradient(800px 700px at 90% 35%,#f1cfa8 0%,transparent 55%),radial-gradient(1000px 800px at 45% 90%,#9fd9d2 0%,transparent 60%),#e8edf2;opacity:0}
.light>.wallbg.l{opacity:1}
#stage,#cmStage{position:relative;width:100%;border-radius:14px;overflow:hidden}
#slots{position:absolute;left:50%;top:calc(24px * var(--sc,1));width:960px;height:680px;transform-origin:50% 0;transform:translateX(-50%) scale(var(--sc,1))}
#slots .slot,#cmSlots .slot{position:absolute;inset:0;transition:opacity .32s ease}
#cmSlots{position:absolute;left:50%;top:calc(12px * var(--cs,1));width:720px;height:430px;transform-origin:50% 0;transform:translateX(-50%) scale(var(--cs,1))}
#strip{display:grid;grid-template-columns:repeat(auto-fit,minmax(132px,1fr));gap:6px}
#strip .sc{appearance:none;border:0;position:relative;overflow:hidden;text-align:left;display:flex;flex-direction:column;gap:1px;padding:7px 9px;border-radius:10px;background:var(--chip);cursor:pointer;min-width:0;
  transition:background-color .2s ease,box-shadow .25s ease}
#strip .sc:hover{background:var(--chipH)}
#strip .sc i{position:absolute;left:0;top:0;bottom:0;width:calc(var(--f,0) * 100%);background:var(--accentSoft)}
#strip .sc.on{box-shadow:inset 0 0 0 1px var(--accentRing)}
#strip .sc .n{position:relative;font-size:12px;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
#strip .sc .d{position:relative;font:500 10.5px/1.3 var(--mono);color:var(--sub);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.rd{display:flex;flex-wrap:wrap;align-items:center;gap:8px 16px;font:500 11.5px/1.3 var(--mono);color:var(--sub)}
.rd b{color:var(--ink);font-weight:500}
.rd input[type=range]{flex:1 1 220px;accent-color:var(--accent)}
.xl{display:grid}.xl>span{grid-area:1/1;opacity:0;transition:opacity .28s ease}.xl>span.on{opacity:1}
.tip{font-size:12px;color:var(--faint)}
"""

BODY = r"""
<div class="dk-page">
  <header class="dk-hdr">
    <div class="dk-tags"><span class="dk-tag">OD-0096 consent amendment</span><span class="dk-tag">OD-0097 Q-V-1</span><span class="dk-tag">6 places · 3 word sets</span></div>
    <h1>Welcome asks about voice typing, played live</h1>
    <p>Getting ready is locked (look I “Sheet”, words W1, download D3 with consent), so nothing downloads until Welcome asks. Pick where the question sits and its words; Play runs the whole first run: the sheet, the hand-over, the question, the answer, the download behind Welcome and, on a slow PC, the one-time question. The buttons drawn in the window work.</p>
  </header>
  <section class="dk-glass qs" aria-label="The two questions">
    <div class="qrow"><div class="qk"><b>Q1 Place</b><span>rec. A</span></div><div id="segV"></div><div class="pk" id="pickV"></div><div class="ln xl" id="lineV"><span class="on"></span><span></span></div></div>
    <div class="qrow"><div class="qk"><b>Q2 Words</b><span>rec. V1</span></div><div id="segW"></div><div class="pk" id="pickW"></div><div class="ln xl" id="lineW"><span class="on"></span><span></span></div></div>
  </section>
  <section class="dk-glass stagecard" aria-label="The window">
    <div class="ctl">
      <button class="dk-btn pri" id="play" type="button"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.5 2.2 9.8 6 3.5 9.8Z" fill="currentColor"/></svg><span id="playLab">Play</span></button>
      <button class="dk-btn" id="jumpQ" type="button">From the question</button>
      <div class="dk-grp"><span class="dk-glab">Answer</span><div id="segA"></div></div>
      <div class="dk-grp"><span class="dk-glab">Machine</span><div id="segM"></div></div>
      <div class="dk-grp"><span class="dk-glab">Slow-PC pick</span><div id="segP"></div></div>
      <div class="dk-grp"><span class="dk-glab">Speed</span><div id="segS"></div></div>
      <div class="dk-grp"><span class="dk-glab">Theme</span><div id="segT"></div></div>
    </div>
    <div id="stage"><div class="wallbg"></div><div class="wallbg l"></div><div id="slots"></div></div>
    <div class="rd"><span>t <b id="clock">0.00 s</b></span><span><b id="ph">getting ready</b></span><input id="scrub" type="range" min="0" max="10" step="0.01" value="0" aria-label="Scrub the sequence"><span id="mach"></span></div>
    <div id="strip" aria-label="The phases; click one to play from it"></div>
  </section>
  <section class="dk-glass stagecard" aria-label="At the orb">
    <h2>At the orb, later</h2>
    <p class="sub">What Welcome’s words promise, in the composer: a click on the orb shows the download’s progress; after No, the first click starts it (the cross cancels). If the speed test ends after Welcome has gone, the slow-PC question waits for the next click and asks there, with the Mac’s arrow tip (OD-0095), before listening starts.</p>
    <div class="ctl"><div id="segC"></div></div>
    <div id="cmStage"><div class="wallbg"></div><div class="wallbg l"></div><div id="cmSlots"></div></div>
  </section>
  <section class="dk-notes">
    <div class="dk-note dk-glass"><h3>When the slow-PC question shows (my call)</h3><p>Q-V-1’s amendment says the app asks once, after the speed test. The test runs when the download ends. If Welcome is still up then, the question takes the answer’s place in the chosen spot (drawn for every place). If a project is already open, it waits for the next click on the voice orb and asks in a popover above it, before listening starts. Until it is answered, voice typing uses the accurate model with the text after you stop (Q-V-1’s first recommendation).</p></div>
    <div class="dk-note dk-glass"><h3>What is real, what is estimated</h3><p>This PC’s Getting ready and test times are measured (ED-252, BENCH); its model is Large v3 Turbo, balanced, 874 MB, live, never asked. The slow PC (no graphics card, 8 cores) is estimated: Large v3 Turbo, compact, 574 MB (voice SPEC V-10.2), a self-test between RTF 0.15 and 0.5 (V-10.3); its live alternative is Small, balanced, 264 MB. Downloads play in 6 s here; the real times are on the strip.</p></div>
    <div class="dk-note dk-glass"><h3>Added beyond the words asked for</h3><p>The ring beside the confirmation is the real download’s progress (Welcome may show it; the orb shows it too). “Voice typing is ready”, once a fast PC’s download and test finish while Welcome is up, keeps the confirmation true. Place D’s Done button closes its sheet. Each is a design proposal, not source behaviour; drop any of them with the pick.</p></div>
  </section>
  <p class="dk-foot">Every change is transitioned (OD-0076): the question folds open in flow and what is below eases down; an answer crossfades while the box eases to its new height; the sheet hangs and lifts; the pill eases its width and the popover grows from its tip; the theme crossfades as one picture. Reduce motion stills the hand-over to a crossfade.</p>
</div>
"""


def main():
    templates = []
    for letter, *_ in W.VARIANTS:
        st = G.make_state(p=0, h=0, upto='gpu', run_sp=0.0, words=W.GR_WORDS, prof='slow', dl='D3')
        st['all'] = True
        for s in G.SID:
            st['st'][s] = ('wait', 0.0)
        templates.append('<template id="tpl-%s">%s</template>' % (letter, W.window(letter, 'V1', 'slow', 'none', dg=0.0, gr_state=st)))
    templates.append('<template id="tpl-cm">%s</template>' % W.composer('dl', 'V1', 'slow'))
    data = dict(steps=G.SID, screen=W.SCREEN, profiles={k: G.PROFILES[k] for k in ('pc', 'slow')}, mach=W.MACH,
                variants=[list(v) for v in W.VARIANTS], words={k: dict(name=w['name'], line=w['line'], **W.words_html('slow')[k]) for k, w in W.WORDS.items()},
                cmStates=[list(x) for x in W.CM_STATES], rec=W.REC)
    js = open(os.path.join(HERE, 'wv-page.js'), encoding='utf-8').read()
    body = BODY + '\n'.join(templates) + '\n<script type="application/json" id="wvdata">%s</script>\n' % json.dumps(data, ensure_ascii=False).replace('</', '<\\/')
    html = page(title='Welcome Voice Question', description='Where the Welcome screen asks about voice typing (OD-0096, OD-0097): six places and three word sets, played live with the slow-PC question.',
                css=W.css() + PAGE_CSS, body=body, js=js)
    write_both(ROOT, 'welcome-voice', html)


main()
