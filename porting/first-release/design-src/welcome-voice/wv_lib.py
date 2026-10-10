# Welcome asks about voice typing (OD-0096 owner pick and its consent amendment, OD-0097 Q-V-1 amendment): the shared
# data, CSS and markup of the six places the question can sit on WelcomeView, used by the canvas boards (static
# instants: build_canvas.py) and the interactive page (live: build_page.py + wv-page.js).
#
# The window is the Getting ready page's own (decisions-2026-10-03/getting-ready/src/gr_lib.py): look I "Sheet" with
# words W1 and download D3 (locked 2026-10-04), its sidebar, icon, caption buttons and CSS. WelcomeView is redrawn here
# with the same blocks at the same places, its lower part in flow (button, hint, Activity list, Providers) so a
# question placed between them pushes what follows down smoothly instead of overlapping it.
#
# The question's state is one attribute on its root, data-vq: none | ask | yes | ready | no | slow | slowlive |
# slowacc | gone. Each state is a layer; the layer that is "on" is in flow, the others are stacked on top of it and
# faded out, so a change crossfades while the box eases to the new layer's height (the page measures; a board just
# shows the "on" layer). Text carries data-wk keys so the page can swap word sets; sizes carry data-mb.
import html, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
GR_SRC = os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', 'getting-ready', 'src')
sys.path.insert(0, GR_SRC)
import gr_lib as G  # noqa: E402

E = html.escape
LOOK, GR_WORDS = 'I', 'W1'   # locked (OD-0096 owner pick, 2026-10-04)

# ------------------------------------------------------------------------------------------------ machines
# "pc" is the owner's PC (gr_lib's measured profile). "slow" is a PC without a usable graphics card, 8 cores, 16 GB:
# the candidate is Large v3 Turbo compact (voice SPEC V-10.2, CPU only, >= 8 cores, multilingual), the self-test lands
# between RTF 0.15 and 0.5 (V-10.3), so Q-V-1's question is asked. Its timings are ESTIMATES (labelled on the page).
G.PROFILES['slow'] = dict(name='Slow PC', line='No graphics card, 8 cores, 16 GB (estimated)', steps={
    'gpu': dict(d=0.2, det='Microsoft Basic Render Driver', res='No GPU · software renderer'),
    'knob': dict(d=2.4, det='4 programs', res='4 programs · 2.4 s'),
    'lights': dict(d=0.6, det='Border beam', res='Border beam · 0.6 s'),
    'liquid': dict(d=0.7, det='Glass liquid', res='Glass liquid · 0.7 s'),
    'strike': dict(d=0.6, det='Plume and ink', res='Plume and ink · 0.6 s'),
    'hw': dict(d=0.3, det='CPU, memory, GPU', res='8 cores · 16 GB · CPU → Large v3 Turbo, compact'),
    'dl': dict(d=57.0, det='574 MB', res='574 MB · checksum OK', mb=574),
    'test': dict(d=3.5, det='10 s clip', res='10 s clip in 3.5 s · too slow for live words'),
})
MACH = {
    'pc': dict(name='This PC', line='RTX 4080 (measured): Large v3 Turbo, balanced, 874 MB; live words, never asked',
               mb='874 MB', small='', live=True, dl_real='about 35 s at 25 MB/s', test='0.2 s for a 10 s clip (measured)'),
    'slow': dict(name='Slow PC', line='No graphics card, 8 cores (estimated): Large v3 Turbo, compact, 574 MB; too slow for live words, so it asks',
                 mb='574 MB', small='264 MB', live=False, dl_real='about 57 s at 10 MB/s', test='3.5 s for a 10 s clip (estimated)'),
}
SCREEN = ['gpu', 'knob', 'lights', 'liquid', 'strike', 'hw']   # D3: the download and the speed test leave the screen

PATH = 'Settings > Voice typing > Speech model'   # OD-0096 amendment 2026-10-04; voice SPEC V-16.3.2

# ------------------------------------------------------------------------------------------------ the three word sets
# {mb} the model's size, {small} the smaller live model's size, {path} the Settings path.
WORDS = {
    'V1': dict(name='Plain', line='Says what happens and where, in full sentences.',
               ask=dict(t='Type by speaking?', b='Marrow downloads a speech model ({mb}) and runs it on this PC. Do you want voice typing?', yes='Yes, download', no='No, thanks'),
               yes=dict(t='Downloading the speech model', b='Voice typing will work once the model has downloaded. To see its progress, click the voice orb in the composer. To change the model, go to {path}.'),
               ready=dict(t='Voice typing is ready', b='Click the voice orb in the composer to start. To change the model, go to {path}.'),
               no=dict(t='Voice typing is off for now', b='Nothing was downloaded. To start the download any time, click the voice orb in the composer. The model is chosen in {path}.'),
               slow=dict(t='Live words, or better accuracy?', b='This PC is too slow to show your words live with the accurate model. Choose how voice typing works here.',
                         live_t='Live words', live_d='A smaller model writes as you speak. It handles languages other than English less well. Downloads {small}.',
                         acc_t='After you stop', acc_d='The accurate model writes your text when you stop speaking.',
                         foot='You can change this later in {path}.'),
               slowlive=dict(t='Live words', b='The smaller model is downloading. To change this later, go to {path}.'),
               slowacc=dict(t='After you stop', b='Voice typing uses the accurate model and writes your text when you stop speaking. To change this later, go to {path}.'),
               done='Done'),
    'V2': dict(name='Warm', line='Friendlier, a little longer; names Hindi and Urdu.',
               ask=dict(t='Talk instead of type?', b='nth can turn your voice into text, right here on your PC. It needs a one-time {mb} download.', yes='Yes, set it up', no='Maybe later'),
               yes=dict(t='Setting up voice typing', b='It will be ready as soon as the download finishes. Click the voice orb in the composer any time to see how it’s going. Want a different model? It’s in {path}.'),
               ready=dict(t='Voice typing is ready', b='Click the voice orb in the composer and start talking. Want a different model? It’s in {path}.'),
               no=dict(t='No problem', b='Nothing was downloaded. Whenever you’d like voice typing, click the voice orb in the composer and the download starts. You can pick the model in {path}.'),
               slow=dict(t='How should voice typing work here?', b='Your PC can’t write your words live with the accurate model. Which matters more to you?',
                         live_t='See my words as I speak', live_d='Uses a smaller model ({small} more). It hears languages other than English less well, Hindi and Urdu most.',
                         acc_t='Get every word right', acc_d='Keeps the accurate model; your text appears when you stop speaking.',
                         foot='You can switch any time in {path}.'),
               slowlive=dict(t='Live words it is', b='The smaller model is on its way. You can switch any time in {path}.'),
               slowacc=dict(t='Accuracy it is', b='Your text appears when you stop speaking. You can switch any time in {path}.'),
               done='Got it'),
    'V3': dict(name='Minimal', line='Fewest words; the path still in full.',
               ask=dict(t='Speak to type', b='Download the speech model? {mb}.', yes='Download', no='Skip'),
               yes=dict(t='Downloading', b='Works when done. Progress: the voice orb in the composer. Model: {path}.'),
               ready=dict(t='Voice typing ready', b='Start it with the voice orb in the composer. Model: {path}.'),
               no=dict(t='Skipped', b='To download later, click the voice orb in the composer. Model: {path}.'),
               slow=dict(t='Live words or accuracy?', b='This PC is too slow for both.',
                         live_t='Live words', live_d='Smaller model, {small}. Weaker outside English.',
                         acc_t='Accurate', acc_d='Text after you stop.',
                         foot='Change it in {path}.'),
               slowlive=dict(t='Live words', b='Downloading the smaller model. Change it in {path}.'),
               slowacc=dict(t='Accurate', b='Text after you stop. Change it in {path}.'),
               done='Done'),
}
REC = {'place': 'A', 'words': 'V1'}

# ------------------------------------------------------------------------------------------------ the six places
VARIANTS = [
    # letter, name, one line, slot, fold (h: the box folds open in flow; drop: hangs from the top; rise: rises in place)
    ('A', 'Section', 'A “Voice typing” section above Activity list, drawn the way Welcome draws its own sections and rows; the answer replaces the row in place.', 'section', 'h'),
    ('B', 'Banner', 'A glass banner above “Add a project”, the first thing read after the tagline; it eases the button down while it is there.', 'banner', 'h'),
    ('C', 'Tiles', 'A second row in the Activity list card, answered with two tiles like Project mark’s Icon and Project name.', 'actrow', 'h'),
    ('D', 'Sheet', 'A small glass sheet hangs from the top, as the Getting ready sheet did, and lifts away on Done.', 'sheet', 'drop'),
    ('E', 'Sidebar', 'A glass card at the foot of the still-empty sidebar; after Yes it carries the download ring.', 'side', 'rise'),
    ('F', 'Line', 'One quiet line under “Or drop a folder…”, with two text buttons; the answer replaces the line.', 'line', 'h'),
]
VMAP = {v[0]: v for v in VARIANTS}
LAYERS = ['ask', 'yes', 'ready', 'no', 'slow', 'slowlive', 'slowacc']


def fill(text, mach):
    m = MACH[mach]
    s = E(text)
    s = s.replace('{mb}', '<span data-mb="main">%s</span>' % m['mb'])
    s = s.replace('{small}', '<span data-mb="small">%s</span>' % (m['small'] or '264 MB'))
    s = s.replace('{path}', '<b class="sp">%s</b>' % E(PATH))
    return s


def words_html(mach):
    """Every word set as {key: html}, the keys the markup's data-wk attributes use."""
    out = {}
    for k, w in WORDS.items():
        d = {}
        for part in ('ask', 'yes', 'ready', 'no', 'slow', 'slowlive', 'slowacc'):
            for f, txt in w[part].items():
                d['%s.%s' % (part, f)] = fill(txt, mach)
        d['done'] = fill(w['done'], mach)
        out[k] = d
    return out


# ------------------------------------------------------------------------------------------------ small pieces
def wk(key, W, mach, tag='span', cls=''):
    part, _, f = key.partition('.')
    txt = WORDS[W][part][f] if f else WORDS[W][key]
    return '<%s%s data-wk="%s">%s</%s>' % (tag, (' class="%s"' % cls) if cls else '', key, fill(txt, mach), tag)


def orb(size=28, cls=''):
    # the Siri Wave orb drawn as a still (the live orb is the exact Liquid Orb export, voice SPEC V-11)
    return '<span class="vorb %s" style="--os:%dpx" aria-hidden="true"></span>' % (cls, size)


def ring(size=20):
    return ('<span class="qring" style="width:%dpx;height:%dpx" aria-hidden="true"><svg viewBox="0 0 20 20"><circle class="t" cx="10" cy="10" r="7.5"/>'
            '<circle class="a" cx="10" cy="10" r="7.5" pathLength="100"/></svg></span>') % (size, size)


def check(size=20):
    return G.CHECK.replace('class="ck"', 'class="ck qck" style="width:%dpx;height:%dpx"' % (size, size))


def mbt(mach, dg):
    mb = int(MACH[mach]['mb'].split()[0])
    return '<span class="mbt" data-mbt="1">%d of %d MB</span>' % (round(mb * dg), mb)


def btn(act, key, W, mach, pri=False, cls='qbtn'):
    return '<button type="button" class="%s%s" data-act="%s">%s</button>' % (cls, ' pri' if pri else '', act, wk(key, W, mach))


def opt(act, W, mach, glyph=None):
    g = G.glyph(glyph, 'gl og') if glyph else ''
    return ('<button type="button" class="opt" data-act="%s">%s<span class="ot">%s%s</span><span class="oc" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="m6 3.5 4.5 4.5L6 12.5"/></svg></span></button>') % (
        act, g, wk('slow.%s_t' % act, W, mach, 'b'), wk('slow.%s_d' % act, W, mach))


LEAD = {'ask': 'orb', 'yes': 'ring', 'ready': 'check', 'no': 'orbdim', 'slow': 'orb', 'slowlive': 'check', 'slowacc': 'check'}


def lead(kind, size):
    if kind == 'orb':
        return orb(size)
    if kind == 'orbdim':
        return orb(size, 'dim')
    if kind == 'ring':
        return ring(min(size, 22))
    return check(min(size, 20))


# ------------------------------------------------------------------------------------------------ layers, per place
def layer(place, ly, W, mach, dg):
    on_buttons = place in ('D',)

    def tx(part, lead_size=28, extra=''):
        return ('<div class="qr"><span class="ld">%s</span><div class="tx">%s%s</div>%s</div>') % (
            lead(LEAD[ly], lead_size), wk(part + '.t', W, mach, 'b'), wk(part + '.b', W, mach), extra)

    done = ('<div class="dn"><button type="button" class="qbtn pri" data-act="done">%s</button></div>' % wk('done', W, mach)) if on_buttons else ''
    if place == 'C':
        # a row with tiles, like Project mark's (RootView.swift's ActivityListSettings in WelcomeView)
        def tiles(a, b, sel=None):
            def t1(act, glyph, key):
                return ('<button type="button" class="tile qt%s" data-act="%s"><span class="st">%s</span><span class="cp">%s</span></button>') % (
                    ' on' if sel == act else '', act, glyph, wk(key, W, mach))
            return '<div class="tiles">%s%s</div>' % (t1(*a), t1(*b))
        yesg, nog = orb(24), '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.2"/><path d="m4.4 11.6 7.2-7.2"/></svg>'
        liveg, accg = '<svg viewBox="0 0 16 16">%s</svg>' % G.GLYPH['wave'], '<svg viewBox="0 0 16 16"><path d="M3 4.5h10M3 8h10M3 11.5h6"/><path d="m10.4 11.6 1.3 1.3 2.5-2.7"/></svg>'
        if ly in ('ask', 'yes', 'ready', 'no'):
            sel = {'ask': None, 'yes': 'yes', 'ready': 'yes', 'no': 'no'}[ly]
            extra = mbt(mach, dg) if ly == 'yes' else ''
            body = '<div class="tx">%s%s%s</div>' % (wk(ly + '.t', W, mach, 'b'), wk(ly + '.b', W, mach), extra)
            return '<div class="crow">%s%s</div>' % (body, tiles(('yes', yesg, 'ask.yes'), ('no', nog, 'ask.no'), sel))
        if ly == 'slow':
            body = ('<div class="tx">%s%s<span class="odl">%s %s</span><span class="odl">%s %s</span>%s</div>') % (
                wk('slow.t', W, mach, 'b'), wk('slow.b', W, mach), wk('slow.live_t', W, mach, 'b'), wk('slow.live_d', W, mach),
                wk('slow.acc_t', W, mach, 'b'), wk('slow.acc_d', W, mach), wk('slow.foot', W, mach, 'span', 'ft'))
            return '<div class="crow">%s%s</div>' % (body, tiles(('live', liveg, 'slow.live_t'), ('acc', accg, 'slow.acc_t')))
        sel = 'live' if ly == 'slowlive' else 'acc'
        body = '<div class="tx">%s%s</div>' % (wk(ly + '.t', W, mach, 'b'), wk(ly + '.b', W, mach))
        return '<div class="crow">%s%s</div>' % (body, tiles(('live', liveg, 'slow.live_t'), ('acc', accg, 'slow.acc_t'), sel))
    if place == 'F':
        # one quiet centred line
        if ly == 'ask':
            return ('<div class="fl">%s%s</div><div class="fa">%s<span class="sep" aria-hidden="true"></span>%s</div>') % (
                orb(16), wk('ask.b', W, mach), btn('yes', 'ask.yes', W, mach, cls='lnk'), btn('no', 'ask.no', W, mach, cls='lnk'))
        if ly == 'slow':
            return ('<div class="fl"><b data-wk="slow.t">%s</b> %s</div><div class="fo">'
                    '<div><button type="button" class="qbtn" data-act="live">%s</button>%s</div>'
                    '<div><button type="button" class="qbtn" data-act="acc">%s</button>%s</div></div>%s') % (
                fill(WORDS[W]['slow']['t'], mach), wk('slow.b', W, mach), wk('slow.live_t', W, mach), wk('slow.live_d', W, mach, 'span', 'fd'),
                wk('slow.acc_t', W, mach), wk('slow.acc_d', W, mach, 'span', 'fd'), wk('slow.foot', W, mach, 'div', 'ff'))
        icon = {'yes': ring(14), 'ready': check(14), 'no': '', 'slowlive': check(14), 'slowacc': check(14)}[ly]
        extra = ' ' + mbt(mach, dg) if ly == 'yes' else ''
        return '<div class="fl">%s<b data-wk="%s.t">%s</b>%s</div><div class="fb">%s</div>' % (
            icon, ly, fill(WORDS[W][ly]['t'], mach), extra, wk(ly + '.b', W, mach))
    # A, B, D, E: a lead mark, the words, then the actions
    if ly == 'ask':
        return tx('ask') + '<div class="acts">%s%s</div>' % (btn('no', 'ask.no', W, mach), btn('yes', 'ask.yes', W, mach, pri=True))
    if ly == 'yes':
        return tx('yes', extra=mbt(mach, dg)) + done
    if ly in ('ready', 'no', 'slowlive', 'slowacc'):
        return tx(ly) + done
    if ly == 'slow':
        return tx('slow') + '<div class="opts">%s%s</div>%s' % (opt('live', W, mach, 'wave'), opt('acc', W, mach, None), wk('slow.foot', W, mach, 'div', 'qft'))
    raise KeyError(ly)


def vq(place, W='V1', mach='pc', state='ask', dg=0.36, press=''):
    """The question block for one place, every layer present, `state`'s layer on."""
    v = VMAP[place]
    lys = ''.join('<div class="ly%s" data-ly="%s">%s</div>' % (' on' if (ly == state or (state in ('none', 'gone') and ly == 'ask')) else '', ly, layer(place, ly, W, mach, dg)) for ly in LAYERS)
    head = '<div class="sh">Voice typing</div>' if place == 'A' else ''
    frame_open = {'A': head + '<div class="card qcard">', 'B': '<div class="qglass">', 'C': '', 'D': '<div class="qsheet">', 'E': '<div class="qglass">', 'F': ''}[place]
    frame_close = '</div>' if frame_open else ''
    return ('<div class="vq vq%s" data-place="%s" data-fold="%s" data-vq="%s" data-press="%s" style="--dg:%.3f"><div class="fold"><div class="fin">%s<div class="stk2">%s</div>%s</div></div></div>') % (
        place, place, v[4], state, press, dg, frame_open, lys, frame_close)


# ------------------------------------------------------------------------------------------------ WelcomeView, in flow
def welcome(state, place, qhtml):
    rows = []
    for i, (name, status, action) in enumerate(G.PROVIDERS):
        trail = G.CHECK if action == 'ok' else '<span class="gbtn">%s</span>' % E(action)
        rows.append('<div class="prow" data-pv="%d" data-st="%s"><span class="pic">%s</span><span class="tx"><b>%s</b><span class="ps">'
                    '<span class="a">Checking…</span><span class="b">%s</span></span></span><span class="pt">%s<span class="pr">%s</span></span></div>' % (
                        i, 'done' if state['welcome_resolved'] else 'run', E(name[0]), E(name), E(status), G.spinner(), trail))
    tiles = ('<div class="tiles"><span class="tile on"><span class="st"><svg viewBox="0 0 16 16"><path d="M3 4.5h10M3 8h10M3 11.5h10"/></svg></span><span class="cp">Icon</span></span>'
             '<span class="tile"><span class="st"><svg viewBox="0 0 16 16"><path d="M3 4.5h10M3 8h7M3 11.5h9"/></svg></span><span class="cp">Project name</span></span></div>')
    slot = lambda s: qhtml if place and VMAP[place][3] == s else ''
    return ('<div class="wel">'
            '<div class="w1 wt1">Marrow</div>'
            '<div class="w1 wt2">A calm, native home for your coding agents.</div>'
            '<div class="wcol">'
            + slot('banner') +
            '<div class="w2 wbtn2"><span class="gbig"><svg viewBox="0 0 16 16"><path d="M1.8 4.2c0-.8.6-1.4 1.4-1.4h3l1.4 1.6h5.2c.8 0 1.4.6 1.4 1.4v5.8c0 .8-.6 1.4-1.4 1.4H3.2c-.8 0-1.4-.6-1.4-1.4Z"/><path d="M8 7v3.6M6.2 8.8h3.6"/></svg>Add a project</span></div>'
            '<div class="w2 whint2">Or drop a folder anywhere in this window.</div>'
            + slot('line') +
            '<div class="wgap"></div>'
            + slot('section') +
            '<div class="w3 wsec2 wact2"><div class="sh">Activity list</div><div class="card"><div class="arow"><span class="tx"><b>Project mark</b><span>Each project’s emoji or symbol before the thread’s name, one line per thread</span></span>' + tiles + '</div>' + slot('actrow') + '</div></div>'
            '<div class="w4 wsec2 wprov2"><div class="sh">Providers</div><div class="card">' + ''.join(rows) + '</div></div>'
            '</div></div>')


def sidebar(qhtml_side):
    return ('<div class="side"><div class="srch"><svg viewBox="0 0 16 16"><circle cx="7" cy="7" r="4.6"/><path d="m10.4 10.4 3.4 3.4"/></svg><span>Search</span></div>'
            '<div class="sbody" data-sb="1"></div>%s</div>') % qhtml_side


def window(place, W='V1', mach='pc', vqstate='ask', dg=0.36, gr_state=None, press='', extra_cls=''):
    """The whole window after (or during) Getting ready, look I, with the question at `place`."""
    st = gr_state or G.make_state(h=1.0, words=GR_WORDS, prof=mach, dl='D3')
    q = vq(place, W, mach, vqstate, dg, press) if place else ''
    side = q if place and VMAP[place][3] == 'side' else ''
    sheet = q if place and VMAP[place][3] == 'sheet' else ''
    return ('<div class="grw vI m-pane wv %s" data-v="I" data-dl="D3" data-place="%s" style="--p:%.3f;--h:%.3f">'
            '<div class="gbg"></div>%s<div class="pane">%s</div><div class="vl-wrap">%s</div>'
            '<div class="gic">%s</div>%s%s</div>') % (
        extra_cls, place or '', st['p'], st['h'], sidebar(side), welcome(st, place, q), G.v_layer(LOOK, st), G.icon_svg(),
        ('<div class="qsheet-wrap">%s</div>' % sheet) if sheet else '', G.caption())


# ------------------------------------------------------------------------------------------------ the composer (at the orb)
# What a click on the voice orb shows later: the download's progress (after Yes, or the download a click starts after
# No), and the slow-PC question when the test finishes after Welcome has gone: a popover with the Mac's arrow tip
# (OD-0095) above the orb, before listening starts. The pill is OD-0091's variant B (glass capsule inside the composer,
# the cancel cross at its far end, the orb at its right), with OD-0097's L1 words.
CM_STATES = [('dl', 'After Yes: a click on the orb'), ('nodl', 'After No: a click starts the download'), ('ask', 'Slow PC: the next click asks'), ('listen', 'After the pick: listening')]


def composer(state='dl', W='V1', mach='slow', dg=0.36, extra_cls=''):
    mb = int(MACH[mach]['mb'].split()[0])
    got = 0.04 if state == 'nodl' else dg
    X = '<span class="cx" aria-hidden="true"><svg viewBox="0 0 12 12"><path d="M3.2 3.2 8.8 8.8M8.8 3.2 3.2 8.8"/></svg></span>'
    pl_dl = ('<span class="pl%s" data-pl="dl">%s%s<b>Getting voice ready</b><i class="mbt" data-mbt="1">%d of %d MB</i></span>') % (
        ' on' if state in ('dl', 'nodl') else '', X, ring(16), round(mb * got), mb)
    pl_li = '<span class="pl%s" data-pl="listen">%s<b>Listening…</b></span>' % (' on' if state == 'listen' else '', X)
    pl_no = '<span class="pl%s" data-pl="none"></span>' % (' on' if state == 'ask' else '')
    pop = ('<div class="cm-pop%s" role="dialog" aria-label="Live words or better accuracy"><div class="pq">%s</div><div class="pb">%s</div><div class="opts">%s%s</div>%s</div>') % (
        ' open' if state == 'ask' else '', wk('slow.t', W, mach, 'b'), wk('slow.b', W, mach), opt('live', W, mach, 'wave'), opt('acc', W, mach, None), wk('slow.foot', W, mach, 'div', 'qft'))
    return ('<div class="grw cm %s" data-cm="%s" style="--dg:%.3f"><div class="gbg"></div>'
            '<div class="cm-msg"><span>Add a shortcut for the theme switch</span></div>'
            '<div class="cm-reply"><b>Done.</b> The theme switch now answers to Ctrl+Shift+L, in Settings &gt; Keyboard too.</div>'
            '<div class="cm-box"><span class="cm-att" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="M10.8 4.6 5.6 9.8a1.6 1.6 0 0 0 2.3 2.3l5.6-5.6a3 3 0 0 0-4.3-4.3L3.6 7.8a4.4 4.4 0 0 0 6.2 6.2l4-4"/></svg></span>'
            '<span class="cm-ph">Ask Claude for anything</span>'
            '<span class="cm-right"><span class="cm-pill%s"><span class="pls">%s%s%s</span></span>%s%s</span></div>'
            '</div>') % (extra_cls, state, got, ' open' if state != 'ask' else '', pl_no, pl_dl, pl_li, orb(32, 'cm-orb'), pop)


# ------------------------------------------------------------------------------------------------ CSS
def css():
    return G.css() + CSS


CSS = r"""
/* ================= Welcome voice question (wv_lib.py) ================= */
.grw{--wez:cubic-bezier(.32,.72,0,1);--wio:cubic-bezier(.42,0,.2,1)}
/* WelcomeView's lower part in flow: the same places as RootView.cs draws them (button 271, hint 321, Activity list 367,
   Providers 521) when there is no question */
.grw .wcol{position:absolute;top:271px;left:115px;width:480px;text-align:center}
.grw .wbtn2{display:flex;justify-content:center;height:40px;margin-bottom:10px}
.grw .whint2{font-size:12px;line-height:16px;color:var(--tx2)}
.grw .wgap{height:30px}
.grw .wsec2{text-align:left}
.grw .wact2{margin-bottom:30px}
.grw .wact2 .card{overflow:hidden}
/* shared */
.grw .vq{position:relative;text-align:left;font-size:13px}
.grw .vq .fold{transition:height .45s var(--wio),opacity .35s ease}
.grw .vq[data-fold="h"] .fold{overflow:clip;overflow-clip-margin:28px}
.grw .vq[data-vq="none"] .fold,.grw .vq[data-vq="gone"] .fold{opacity:0}
.grw .vq .stk2{position:relative;transition:height .36s var(--wio)}
.grw .vq .ly{position:absolute;left:0;right:0;top:0;opacity:0;visibility:hidden;transition:opacity .2s ease,visibility 0s linear .32s}
.grw .vq .ly.on{position:relative;opacity:1;visibility:visible;transition:opacity .3s ease .1s,visibility 0s}
.grw .vq .sp{font-weight:600;color:var(--tx);white-space:nowrap}
.grw .qbtn{appearance:none;border:0;margin:0;font:inherit;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;height:26px;padding:0 13px;border-radius:13px;
  font-size:12px;font-weight:500;color:var(--tx);white-space:nowrap;background:var(--glass);box-shadow:inset 0 0 0 1px var(--glassE),inset 0 1px 0 var(--glassHi);
  transition:transform .15s ease,filter .15s ease,background-color .2s ease}
.grw .qbtn:hover{filter:brightness(1.1)}
.grw .qbtn.pri{color:#fff;background:linear-gradient(180deg,color-mix(in srgb,var(--acc) 82%,#fff) 0%,var(--acc) 100%);
  box-shadow:inset 0 1px 0 rgba(255,255,255,.35),inset 0 0 0 1px rgba(255,255,255,.18),0 4px 12px color-mix(in srgb,var(--acc) 30%,transparent)}
.grw .vq[data-press="yes"] [data-act="yes"],.grw .vq[data-press="no"] [data-act="no"],.grw .vq[data-press="live"] [data-act="live"],
.grw .vq[data-press="acc"] [data-act="acc"],.grw .vq[data-press="done"] [data-act="done"]{transform:scale(.95);filter:brightness(1.18)}
.grw .vorb{position:relative;flex:none;display:block;width:var(--os,28px);height:var(--os,28px);border-radius:50%;overflow:hidden;
  background:radial-gradient(circle at 50% 50%,#1a1240 0%,#140f33 46%,#3a2386 78%,#a688ff 93%,#e2d6ff 100%);box-shadow:0 0 calc(var(--os,28px) * .34) rgba(140,110,255,.5),inset 0 0 0 .5px rgba(255,255,255,.25);transition:filter .4s ease,opacity .4s ease}
.grw .vorb::before{content:"";position:absolute;left:4%;right:4%;top:28%;height:44%;border-radius:50%;
  background:radial-gradient(50% 50% at 50% 50%,#fff 0%,rgba(140,225,255,.95) 26%,rgba(255,95,190,.75) 52%,rgba(150,110,255,.35) 72%,transparent 82%);filter:blur(.6px);animation:wvorb 4.6s ease-in-out infinite}
.grw .vorb::after{content:"";position:absolute;left:20%;right:20%;top:44%;height:12%;border-radius:50%;background:#fff;filter:blur(.4px);opacity:.95;animation:wvorb 4.6s ease-in-out infinite}
@keyframes wvorb{50%{transform:scaleX(.82) translateY(4%)}}
.grw .vorb.dim{filter:saturate(.35);opacity:.7}
.grw .qring{display:block;flex:none}
.grw .qring svg{width:100%;height:100%;transform:rotate(-90deg)}
.grw .qring circle{fill:none;stroke-width:2.2}
.grw .qring .t{stroke:var(--ov10)}
.grw .qring .a{stroke:var(--acc);stroke-linecap:round;stroke-dasharray:calc(var(--dg,0) * 100) 100;transition:stroke-dasharray .3s ease}
.grw .qck{display:block}
.grw .mbt{font-size:11px;color:var(--tx3);font-variant-numeric:tabular-nums;white-space:nowrap;font-style:normal}
/* lead + words rows (A, B, D, E) */
.grw .vq .qr{display:grid;grid-template-columns:28px minmax(0,1fr) auto;column-gap:12px;align-items:start}
.grw .vq .qr .ld{width:28px;height:28px;display:grid;place-items:center}
.grw .vq .qr .tx{display:flex;flex-direction:column;gap:3px;min-width:0;padding-top:1px}
.grw .vq .qr .tx b{font-weight:600;font-size:13px;line-height:17px}
.grw .vq .qr .tx>span{font-size:11.5px;line-height:15.5px;color:var(--tx2)}
.grw .vq .qr .mbt{padding-top:2px}
.grw .vq .acts{display:flex;justify-content:flex-end;gap:8px;margin-top:10px}
.grw .vq .dn{display:flex;justify-content:flex-end;margin-top:12px}
.grw .vq .opts{display:flex;flex-direction:column;margin:10px 0 0 40px;border-radius:12px;background:var(--ov3);box-shadow:inset 0 0 0 1px var(--div)}
.grw .opt{appearance:none;border:0;margin:0;font:inherit;color:inherit;cursor:pointer;text-align:left;display:flex;align-items:center;gap:10px;padding:9px 10px 9px 12px;background:transparent;
  box-shadow:0 -1px 0 var(--div);transition:background-color .15s ease,transform .15s ease,filter .15s ease}
.grw .opt:first-child{box-shadow:none;border-radius:12px 12px 0 0}.grw .opt:last-child{border-radius:0 0 12px 12px}
.grw .opt:hover{background:var(--ov6)}
.grw .opt .og{width:15px;height:15px;color:var(--tx2)}
.grw .opt .ot{display:flex;flex-direction:column;gap:2px;flex:1;min-width:0}
.grw .opt .ot b{font-size:12.5px;font-weight:600;line-height:16px}
.grw .opt .ot span{font-size:11px;line-height:14.5px;color:var(--tx2)}
.grw .opt .oc{width:14px;height:14px;flex:none;color:var(--tx3)}
.grw .opt .oc svg{width:100%;height:100%;fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.grw .vq .qft{margin:8px 0 0 40px;font-size:11px;line-height:14.5px;color:var(--tx2)}
.grw .vq[data-press="live"] .opt[data-act="live"],.grw .vq[data-press="acc"] .opt[data-act="acc"]{background:var(--accSoft);transform:none}

/* A Section: a ChromeSection between Activity list and Providers */
.grw .vqA .fin{padding-bottom:30px}
.grw .vqA .qcard{padding:12px 12px 12px 14px}

/* B Banner: glass, above Add a project */
.grw .vqB .fin{padding-bottom:16px}
.grw .vqB .qglass,.grw .vqE .qglass{border-radius:16px;padding:12px 12px 12px 12px;background:var(--glass);
  box-shadow:inset 0 0 0 1px var(--glassE),inset 0 1px 0 var(--glassHi),var(--gshadow);-webkit-backdrop-filter:blur(26px) saturate(170%);backdrop-filter:blur(26px) saturate(170%)}
.grw .vqB .fold{transform:translateY(calc(var(--bq,0) * 1px))}

/* C Tiles: a second row in the Activity list card */
.grw .vqC .fin{box-shadow:0 -1px 0 var(--div)}
.grw .vqC .crow{display:flex;align-items:center;gap:16px;padding:11px 10px 11px 16px}
.grw .vqC .crow .tx{display:flex;flex-direction:column;gap:2px;min-width:0;flex:1}
.grw .vqC .crow .tx b{font-weight:400;font-size:13px}
.grw .vqC .crow .tx>span{font-size:11px;line-height:14px;color:var(--tx2)}
.grw .vqC .crow .tx .odl{display:block;margin-top:2px}
.grw .vqC .crow .tx .odl b{font-weight:600;font-size:11px;color:var(--tx);margin-right:3px}
.grw .vqC .crow .tx .ft{margin-top:3px}
.grw .vqC .crow .mbt{margin-top:2px}
.grw .vqC .tiles{display:flex;gap:10px;flex:none}
.grw .vqC .tile{appearance:none;border:0;margin:0;padding:0;font:inherit;color:inherit;cursor:pointer;background:none;width:82px;display:flex;flex-direction:column;align-items:center;gap:6px;transition:transform .15s ease}
.grw .vqC .tile .st{width:82px;height:54px;padding:3px;border-radius:9px;box-shadow:inset 0 0 0 2px transparent;display:grid;transition:box-shadow .3s ease}
.grw .vqC .tile .st>*{grid-area:1/1}
.grw .vqC .tile .st::before{content:"";grid-area:1/1;border-radius:6px;background:var(--ov6);transition:background-color .3s ease}
.grw .vqC .tile .st svg{place-self:center;width:18px;height:18px;padding:0;background:none;border-radius:0;fill:none;stroke:var(--tx2);stroke-width:1.4;stroke-linecap:round;stroke-linejoin:round;position:relative;transition:stroke .3s ease}
.grw .vqC .tile .st .vorb{place-self:center}
.grw .vqC .tile.on .st{box-shadow:inset 0 0 0 2px var(--acc)}
.grw .vqC .tile.on .st::before{background:var(--ov10)}
.grw .vqC .tile.on .st svg{stroke:var(--acc)}
.grw .vqC .tile .cp{font-size:11px;line-height:13px;color:var(--tx2);text-align:center;max-width:90px}
.grw .vqC .tile.on .cp{color:var(--tx)}
.grw .vqC .tile:hover .st::before{background:var(--ov10)}
.grw .vq[data-press] .tile[data-act]{transform:none}
.grw .vqC[data-press="yes"] .tile[data-act="yes"] .st,.grw .vqC[data-press="no"] .tile[data-act="no"] .st,
.grw .vqC[data-press="live"] .tile[data-act="live"] .st,.grw .vqC[data-press="acc"] .tile[data-act="acc"] .st{box-shadow:inset 0 0 0 2px var(--acc)}

/* D Sheet: hangs from the top of the pane, as look I's Getting ready sheet did */
.grw .qsheet-wrap{position:absolute;left:250px;right:0;top:0;bottom:0;z-index:7;pointer-events:none}
.grw .vqD{position:absolute;left:125px;top:0;width:460px;pointer-events:auto}
.grw .vqD .fold{transform:translateY(0);transition:transform .6s var(--wio),opacity .4s ease}
.grw .vqD[data-vq="none"] .fold,.grw .vqD[data-vq="gone"] .fold{transform:translateY(calc(-100% - 30px));opacity:.4}
.grw .vqD .qsheet{padding:40px 24px 18px;border-radius:0 0 18px 18px;background:var(--glass);
  box-shadow:inset 0 0 0 1px var(--glassE),inset 0 1px 0 var(--glassHi),var(--gshadow);-webkit-backdrop-filter:blur(30px) saturate(170%);backdrop-filter:blur(30px) saturate(170%)}
.grw .vqD .qr{grid-template-columns:32px minmax(0,1fr) auto;column-gap:14px}
.grw .vqD .qr .ld{width:32px;height:32px}
.grw .vqD .qr .tx b{font-family:"Segoe UI Variable Display","Segoe UI",system-ui,sans-serif;font-size:17px;line-height:22px}
.grw .vqD .qr .tx>span{font-size:12.5px;line-height:17px}
.grw .vqD .acts{margin-top:16px}
.grw .vqD .opts,.grw .vqD .qft{margin-left:46px}
.grw .vqD .dn{margin-top:14px}

/* E Sidebar: a glass card at the foot of the sidebar */
.grw .side .vqE{position:absolute;left:12px;right:12px;bottom:12px}
.grw .vqE .fold{transform:translateY(0);transition:transform .5s var(--wio),opacity .4s ease}
.grw .vqE[data-vq="none"] .fold,.grw .vqE[data-vq="gone"] .fold{transform:translateY(16px)}
.grw .vqE .qglass{padding:12px}
.grw .vqE .qr{grid-template-columns:24px minmax(0,1fr) auto;column-gap:10px}
.grw .vqE .qr .ld{width:24px;height:24px}
.grw .vqE .qr .tx b{font-size:12.5px;line-height:16px}
.grw .vqE .qr .tx>span{font-size:11px;line-height:15px}
.grw .vqE .qr .mbt{display:none}
.grw .vqE .acts{justify-content:stretch;gap:6px}
.grw .vqE .acts .qbtn{flex:1;padding:0 8px}
.grw .vqE .opts{margin:10px 0 0}
.grw .vqE .opt{padding:8px 8px 8px 10px}
.grw .vqE .opt .og{display:none}
.grw .vqE .qft{margin:8px 0 0}
.grw .vqE .sp{white-space:normal}

/* F Line: one quiet centred line under the hint */
.grw .vqF .fin{padding-top:10px;text-align:center}
.grw .vqF .fl{display:flex;flex-wrap:wrap;justify-content:center;align-items:center;gap:4px 7px;font-size:12px;line-height:16px;color:var(--tx2);padding:0 20px}
.grw .vqF .fl b{color:var(--tx);font-weight:600}
.grw .vqF .fl .vorb,.grw .vqF .fl .qring,.grw .vqF .fl .qck{flex:none}
.grw .vqF .fa{display:flex;justify-content:center;align-items:center;gap:8px;margin-top:4px}
.grw .vqF .fa .sep{width:3px;height:3px;border-radius:50%;background:var(--tx3)}
.grw .lnk{appearance:none;border:0;margin:0;padding:2px 4px;font:inherit;font-size:12px;font-weight:600;color:var(--acc);background:none;cursor:pointer;border-radius:6px;transition:background-color .15s ease,transform .15s ease}
.grw .lnk:hover{background:var(--accSoft)}
.grw .vqF .lnk[data-act="no"]{color:var(--tx2);font-weight:500}
.grw .vqF .fb{font-size:11.5px;line-height:16px;color:var(--tx2);margin-top:3px;padding:0 24px}
.grw .vqF .fo{display:flex;justify-content:center;gap:14px;margin-top:8px}
.grw .vqF .fo>div{display:flex;flex-direction:column;align-items:center;gap:4px;width:200px}
.grw .vqF .fd{font-size:11px;line-height:14px;color:var(--tx2)}
.grw .vqF .ff{font-size:11px;line-height:14px;color:var(--tx2);margin-top:6px}

/* ================= the composer, at the orb ================= */
.grw.cm{width:720px;height:430px}
.grw.cm .cm-msg{position:absolute;right:28px;top:26px;display:flex;justify-content:flex-end}
.grw.cm .cm-msg span{max-width:300px;padding:8px 12px;border-radius:14px;background:var(--ov10);font-size:12.5px}
.grw.cm .cm-reply{position:absolute;left:28px;right:150px;top:74px;max-width:330px;font-size:13px;line-height:19px;color:var(--tx)}
.grw.cm .cm-reply b{font-weight:600}
.grw.cm .cm-box{position:absolute;left:24px;right:24px;bottom:22px;height:96px;border-radius:24px;background:var(--glass);
  box-shadow:inset 0 0 0 1px var(--glassE),inset 0 1px 0 var(--glassHi),var(--gshadow)}
.grw.cm .cm-att{position:absolute;left:14px;bottom:14px;width:32px;height:32px;border-radius:16px;display:grid;place-items:center;color:var(--tx2)}
.grw.cm .cm-att svg{width:16px;height:16px;fill:none;stroke:currentColor;stroke-width:1.3;stroke-linecap:round;stroke-linejoin:round}
.grw.cm .cm-ph{position:absolute;left:18px;top:15px;font-size:13px;color:var(--tx3)}
.grw.cm .cm-right{position:absolute;right:12px;bottom:12px;display:flex;align-items:center}
.grw.cm .cm-orb{position:relative;z-index:2;transition:transform .35s var(--wez)}
.grw.cm[data-cm="dl"] .cm-orb,.grw.cm[data-cm="nodl"] .cm-orb,.grw.cm[data-cm="listen"] .cm-orb{transform:scale(1.12)}
.grw.cm .cm-pill{position:absolute;right:-4px;top:50%;height:40px;margin-top:-20px;border-radius:20px;overflow:hidden;
  background:rgba(18,18,22,.5);-webkit-backdrop-filter:blur(6px) saturate(1.7);backdrop-filter:blur(6px) saturate(1.7);
  box-shadow:inset 0 0 0 .5px rgba(255,255,255,.16),inset 0 0 0 1px rgba(0,0,0,.35),inset .7px 0 .8px -.1px rgba(255,96,150,.38),inset -.7px 0 .8px -.1px rgba(96,176,255,.4),0 10px 24px -10px rgba(0,0,0,.6);
  transition:width .42s var(--wez),opacity .3s ease}
.light .grw.cm .cm-pill{background:rgba(250,249,251,.56);box-shadow:inset 0 0 0 .5px rgba(255,255,255,.75),inset 0 0 0 1px rgba(40,30,60,.14),inset .7px 0 .8px -.1px rgba(255,96,150,.38),inset -.7px 0 .8px -.1px rgba(96,176,255,.4),0 8px 22px -10px rgba(70,50,35,.32)}
.grw.cm .cm-pill:not(.open){opacity:0}
.grw.cm .pls{position:relative;display:block;height:40px}
.grw.cm .pl{position:absolute;left:0;top:0;height:40px;width:max-content;display:flex;align-items:center;gap:8px;padding:0 48px 0 8px;white-space:nowrap;opacity:0;transition:opacity .2s ease}
.grw.cm .pl.on{position:relative;opacity:1;transition:opacity .3s ease .12s}
.grw.cm .pl b{font-size:13px;font-weight:600;color:#F7F7F8}
.light .grw.cm .pl b{color:#16161A}
.grw.cm .pl .mbt{color:rgba(247,247,248,.6)}
.light .grw.cm .pl .mbt{color:rgba(22,22,26,.55)}
.grw.cm .cx{width:24px;height:24px;border-radius:12px;display:grid;place-items:center;background:rgba(255,255,255,.09);box-shadow:inset 0 0 0 1px rgba(255,255,255,.16);color:#B4B5BA;flex:none}
.light .grw.cm .cx{background:rgba(255,255,255,.55);box-shadow:inset 0 0 0 1px rgba(40,30,60,.12);color:#4A4B50}
.grw.cm .cx svg{width:11px;height:11px}.grw.cm .cx path{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round}
/* the popover: Marrow glass with the Mac's arrow tip (OD-0095: card 13 pt from the anchor, tip 12 pt high, 24 pt wide) */
.grw.cm .cm-pop{position:absolute;right:-14px;bottom:calc(100% + 13px);width:330px;padding:14px 14px 12px;border-radius:14px;z-index:3;text-align:left;
  background:var(--glass);-webkit-backdrop-filter:blur(26px) saturate(170%);backdrop-filter:blur(26px) saturate(170%);
  box-shadow:inset 0 0 0 1px var(--glassE),inset 0 1px 0 var(--glassHi),var(--gshadow);
  opacity:0;transform:translateY(6px) scale(.96);transform-origin:calc(100% - 30px) 100%;pointer-events:none;transition:opacity .18s ease,transform .24s var(--wez)}
.grw.cm .cm-pop.open{opacity:1;transform:none;pointer-events:auto}
.grw.cm .cm-pop::after{content:"";position:absolute;right:23px;bottom:-7px;width:14px;height:14px;transform:rotate(45deg);border-radius:0 0 3px 0;
  background:inherit;-webkit-backdrop-filter:inherit;backdrop-filter:inherit;box-shadow:inset -1px -1px 0 0 var(--glassE);clip-path:polygon(100% 0,100% 100%,0 100%)}
.grw.cm .cm-pop .pq b{font-size:13px;font-weight:600;line-height:17px}
.grw.cm .cm-pop .pb{font-size:11.5px;line-height:15.5px;color:var(--tx2);margin-top:3px}
.grw.cm .cm-pop .opts{display:flex;flex-direction:column;margin:10px 0 0;border-radius:12px;background:var(--ov3);box-shadow:inset 0 0 0 1px var(--div)}
.grw.cm .cm-pop .qft{margin:8px 0 0;font-size:11px;line-height:14.5px;color:var(--tx2)}
.grw.cm .sp{font-weight:600;color:var(--tx);white-space:nowrap}
"""
