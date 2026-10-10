# Builds the "Voice notes" decision page D-VN (BUILD-PLAN row D-VN; OD-0105 item 4, "Yes, make the page"):
# voice-notes.html (full page, with the skeleton, for local viewing) and share/voice-notes.html (skeleton stripped,
# <title> first, everything inline, Google Fonts only), with the decisions-2026-10-03 kit (kit.css, kit.js).
#
# Three questions, one letter each:
#   Notes      (Q-V-7 A)  the error notes' words: each says what happened plus ONE link to the place that fixes it;
#   Countdown  (Q-V-6 B)  the pill counts down the last 30 s before the 5-minute cap, then finishes the text;
#   CUDA       (Q-V-9 C)  on NVIDIA PCs the hardware card offers the CUDA runtime as a separate download.
#
# Sources (read, never copied into the app): porting/first-release/BUILD-PLAN.md (rows Q-V-6, Q-V-7, Q-V-9, D-VN, VT-17),
# voice-SPEC.md (V-4.4, V-4.5, V-7.9, V-7.12, V-9.2, V-10.1 to V-10.4, V-12.2 to V-12.4, V-12.9, V-12.10, V-14.6, V-16.3,
# V-17.6, section 23), owner_decisions.md (OD-0099, OD-0104, OD-0105), the locked voice composer design
# (E:/Droppy/work/voice/design/voice-composer.html: the note layer, the glass pill and its tokens), the Voice typing page
# design (design-src/customization/voice.js, gen.py: the hardware card), the port's composer notice line
# (src/Marrow.App/Views/Composer/ComposerView.cs:1180-1196) and the CUDA pins (src/Marrow.Services/Port/Voice/Assets/
# voice-pins.json "cuda").
#
# Sources of this page: vn-page.css (the page and the mocks), vn-page.js (the stages), and the words below.
import json, os, re, sys
from html import escape as esc

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402

# ------------------------------------------------------------------------------------------------ where a link goes
# Each note's one link opens one of these. tbc: the plan names no such place ("(destination to confirm)").
DEST = {
    'privacy': dict(t='Windows Settings > Privacy & security > Microphone', src='V-14.6, locked (ms-settings:privacy-microphone)'),
    'sound': dict(t='Windows Settings > System > Sound', src='voice SPEC section 23, Q-V-7: \u201copen Sound settings\u201d'),
    'mic': dict(t='Settings > Voice typing > Microphone', src='Q-V-19 B (OD-0099), BUILD-PLAN VT-13'),
    'model': dict(t='Settings > Voice typing > Speech model', src='V-16.3.2, V-16.3.3; the welcome-voice path (OD-0098)'),
    'engine': dict(t='Settings > Voice typing > Engine', src='V-16.3.1 (On this PC | OpenAI cloud)'),
    'key': dict(t='Settings > Voice typing > OpenAI key', src='V-16.3.1, Q-V-16 (the Providers key field)'),
    'hw': dict(t='Check my hardware, in Settings > Voice typing > Speech model', src='V-10.6, V-16.3.2'),
    'page': dict(t='Settings > Voice typing', src='V-16.3; voice SPEC Q-V-7: \u201copen Voice typing\u201d'),
    'billing': dict(t='OpenAI\u2019s billing page (destination to confirm)', src='not in the plan', tbc=True),
    'network': dict(t='Windows network settings (destination to confirm)', src='not in the plan', tbc=True),
}

# ------------------------------------------------------------------------------------------------ the cases
# id, the short name, when it shows (with the clause that names it)
CASES = [
    ('blocked', 'Microphone blocked', 'Locked already (V-4.5, V-14.6): Windows denies the microphone. Shown for comparison; not part of the pick.'),
    ('nomic', 'No microphone', 'The click finds no input device (V-17.6).'),
    ('unplug', 'Microphone unplugged', 'The device goes away mid-session: the final pass runs on what was heard, then the note shows (V-4.4).'),
    ('busy', 'Microphone busy', 'Another app holds the device for itself (V-4.4, AUDCLNT_E_DEVICE_IN_USE).'),
    ('dl', 'Model still downloading', 'The click comes before the speech model has downloaded (V-17.6).'),
    ('load', 'Model failed to load', 'The model file does not load (V-7.9).'),
    ('avx', 'No AVX2', 'The processor lacks AVX2, which the processor runtime needs (V-3.1). Q-V-12 (ED): not supported on this PC; the cloud engine still works.'),
    ('engine', 'Engine failure', 'A pass fails inside the engine (V-17.6).'),
    ('nokey', 'Cloud: no key', 'OpenAI cloud is the engine and no key is stored (V-17.6).'),
    ('refused', 'Cloud: key refused', 'OpenAI answers 401 (V-17.6, V-T112).'),
    ('quota', 'Cloud: out of quota', 'OpenAI answers 429, out of quota or over its rate limit (V-17.6).'),
    ('nonet', 'No network', 'The request to OpenAI cannot go out (V-17.6). Only the cloud engine needs the network while you speak.'),
]

LOCKED = ('Microphone blocked. Allow nth in ', 'Privacy & security > Microphone', 'privacy')

# ------------------------------------------------------------------------------------------------ the four word sets
# Each note: (text before the link, the link's words, where it goes). The link is always last, as in the locked note.
SETS = [
    ('A', 'Plain', 'The locked note\u2019s shape: what happened, then the place, named the way Windows names Privacy & security > Microphone.', {
        'nomic': ('No microphone found. Set one up in ', 'System > Sound', 'sound'),
        'unplug': ('Microphone disconnected. Choose another in ', 'Voice typing > Microphone', 'mic'),
        'busy': ('Microphone in use by another app. Choose another in ', 'Voice typing > Microphone', 'mic'),
        'dl': ('Speech model still downloading. See it in ', 'Voice typing > Speech model', 'model'),
        'load': ('Speech model didn\u2019t load. Choose a model in ', 'Voice typing > Speech model', 'model'),
        'avx': ('This processor can\u2019t run the speech model. Use OpenAI in ', 'Voice typing > Engine', 'engine'),
        'engine': ('Voice typing stopped on an error. Check it in ', 'Settings > Voice typing', 'page'),
        'nokey': ('No OpenAI key yet. Add one in ', 'Voice typing > OpenAI key', 'key'),
        'refused': ('OpenAI refused the key. Replace it in ', 'Voice typing > OpenAI key', 'key'),
        'quota': ('This key reached its OpenAI limit. Switch to this PC in ', 'Voice typing > Engine', 'engine'),
        'nonet': ('Can\u2019t reach OpenAI: no internet. Switch to this PC in ', 'Voice typing > Engine', 'engine'),
    }),
    ('B', 'Short', 'Fewest words. The link is only the place\u2019s name, so every note stays well inside one line.', {
        'nomic': ('No microphone found. ', 'Sound settings', 'sound'),
        'unplug': ('Microphone disconnected. ', 'Microphone', 'mic'),
        'busy': ('Microphone in use by another app. ', 'Microphone', 'mic'),
        'dl': ('Speech model still downloading. ', 'Speech model', 'model'),
        'load': ('Speech model didn\u2019t load. ', 'Speech model', 'model'),
        'avx': ('This processor can\u2019t run the speech model. ', 'Engine', 'engine'),
        'engine': ('Voice typing stopped on an error. ', 'Voice typing', 'page'),
        'nokey': ('No OpenAI key yet. ', 'OpenAI key', 'key'),
        'refused': ('OpenAI refused the key. ', 'OpenAI key', 'key'),
        'quota': ('This key reached its OpenAI limit. ', 'Engine', 'engine'),
        'nonet': ('Can\u2019t reach OpenAI: no internet. ', 'Engine', 'engine'),
    }),
    ('C', 'Action', 'The link says what to do there. It still only opens the place; nothing changes until you change it.', {
        'nomic': ('No microphone found. ', 'Open Sound settings', 'sound'),
        'unplug': ('The microphone was disconnected; your words so far are kept. ', 'Choose a microphone', 'mic'),
        'busy': ('Another app is using the microphone. ', 'Choose a microphone', 'mic'),
        'dl': ('The speech model is still downloading. ', 'Show the download', 'model'),
        'load': ('The speech model couldn\u2019t be loaded. ', 'Choose a model', 'model'),
        'avx': ('This processor can\u2019t run the speech model. ', 'Set up OpenAI cloud', 'engine'),
        'engine': ('Voice typing stopped on an error. ', 'Check my hardware', 'hw'),
        'nokey': ('Cloud voice typing needs your OpenAI key. ', 'Add your key', 'key'),
        'refused': ('OpenAI didn\u2019t accept the key. ', 'Replace the key', 'key'),
        'quota': ('This key reached its OpenAI limit. ', 'Use this PC instead', 'engine'),
        'nonet': ('Can\u2019t reach OpenAI without internet. ', 'Use this PC instead', 'engine'),
    }),
    ('D', 'Explained', 'Adds what it means for you. Longer: most notes no longer fit one line in the composer and are cut short.', {
        'nomic': ('No microphone found, so nothing was recorded. Set one up in ', 'System > Sound', 'sound'),
        'unplug': ('Microphone disconnected. The words so far are in the composer. Choose another in ', 'Voice typing > Microphone', 'mic'),
        'busy': ('Another app has the microphone, so Marrow can\u2019t listen. Choose another in ', 'Voice typing > Microphone', 'mic'),
        'dl': ('Voice typing is ready as soon as the speech model finishes downloading. See it in ', 'Voice typing > Speech model', 'model'),
        'load': ('The speech model couldn\u2019t be loaded, so nothing was written. Choose a model in ', 'Voice typing > Speech model', 'model'),
        'avx': ('Voice typing on this PC needs a newer processor; OpenAI cloud still works. Set it up in ', 'Voice typing > Engine', 'engine'),
        'engine': ('Voice typing stopped on an error. Try again, or check ', 'Settings > Voice typing', 'page'),
        'nokey': ('Cloud voice typing uses your own OpenAI key, and none is saved yet. Add it in ', 'Voice typing > OpenAI key', 'key'),
        'refused': ('OpenAI refused the key, so nothing was written. Replace it in ', 'Voice typing > OpenAI key', 'key'),
        'quota': ('OpenAI turned this down: the key is out of credit or over its rate limit. See ', 'OpenAI billing', 'billing'),
        'nonet': ('Cloud voice typing needs the internet, and there\u2019s none right now. Check ', 'Network settings', 'network'),
    }),
]
REC = {'Notes': 'A', 'Countdown': 'B', 'CUDA': 'A'}

# ------------------------------------------------------------------------------------------------ countdown looks
COUNT = [
    ('A', 'Time left, in place of the words', 'From 0:30 the label reads \u201c0:30 left\u201d and counts each second; at the cap it turns to Finishing\u2026. Listening and Transcribing are not shown in those 30 s.'),
    ('B', 'Time after the words', 'The label keeps Listening\u2026 or Transcribing\u2026 and adds the time after an en space in the lighter weight, the way the pill already names the language (V-12.10, no dot). The pill widens once, by the time\u2019s width.'),
    ('C', 'A draining line, no numbers', 'The words stay as they are. A 2 px line inside the pill\u2019s foot, from the cross to the orb, shortens over the 30 s and takes the warning colour for the last 10. No digits.'),
    ('D', '\u201cStops in\u201d, amber at the end', 'The label says what will happen: \u201cStops in 0:30\u201d. The digits turn the warning colour for the last 10 s. Listening and Transcribing are not shown in those 30 s.'),
]
COUNT_WHY = 'It reuses the pattern the pill already has for the language name, so it adds no new kind of thing, and you still see whether Marrow is listening or writing.'

# ------------------------------------------------------------------------------------------------ CUDA offer looks
CUDA = [
    ('A', 'Its own row in the card', 'A row under the recommendation: \u201cNVIDIA CUDA\u201d, a detail with the size and where it comes from, and a Download capsule. Progress, cancel and the end states use the model list\u2019s own state column and 2 px bar (V-16.3.3).'),
    ('B', 'Inside the recommendation box', 'One line under the reason, \u201cYour RTX 4080 can also run it on NVIDIA CUDA, a separate 644 MB download\u201d, and a Download CUDA capsule beside the state line.'),
    ('C', 'On the GPU backend line', 'The probe row that says \u201cVulkan, working\u201d gets a small capsule at its end: \u201cGet CUDA, 644 MB\u201d. The quietest; the size and source are only in its tooltip and the next state.'),
    ('D', 'A sentence under the card\u2019s note', 'A short paragraph at the foot of the card, in the note\u2019s own small type, ending with a Download CUDA link.'),
]
CUDA_WHY = 'It is the page\u2019s own row (title, detail, capsule) and the model list\u2019s progress column, so it needs no new control and has room for the size and both sources (OD-0104).'
NOTES_WHY = 'It has the locked Microphone blocked note\u2019s shape, so all twelve notes read as one family, and every one fits on one line in the composer.'

CK = '<svg viewBox="0 0 12 12"><path d="M2.5 6.3 5 8.6l4.6-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>'


def opt_html(qid, letter, title, detail, why):
    rc = '<span class="rc">Recommended</span>' if REC[qid] == letter else ''
    wy = '<span class="why"><b>Why:</b> %s</span>' % esc(why) if REC[qid] == letter else ''
    return ('<div class="dk-opt" data-o="%s" aria-current="false"><div class="oh"><span class="ol">%s</span><span class="ot">%s</span>%s</div><span class="od">%s</span>%s'
            '<button type="button" class="dk-pick pk" data-pick-q="%s" data-pick-v="%s" aria-pressed="false" aria-label="Pick %s for %s">'
            '<span class="ck">%s</span><span class="lb"><span class="a">Pick %s</span><span class="b">Picked</span></span></button></div>'
            % (letter, letter, esc(title), rc, esc(detail), wy, qid, letter, letter, qid, CK, letter))


def note_line(letter, n):
    before, link, dk = n
    d = DEST[dk]
    tbc = ' tbc' if d.get('tbc') else ''
    return ('<li data-set="%s"><span class="ol">%s</span><span class="nt"><span class="nx">%s<u>%s</u></span>'
            '<span class="dst%s">Opens: %s</span></span></li>') % (letter, letter, esc(before), esc(link), tbc, esc(d['t']))


def word_list():
    out = []
    for cid, name, when in CASES:
        if cid == 'blocked':
            lines = '<li data-set="L"><span class="ol lk">Locked</span><span class="nt"><span class="nx">%s<u>%s</u></span><span class="dst">Opens: %s</span></span></li>' % (
                esc(LOCKED[0]), esc(LOCKED[1]), esc(DEST['privacy']['t']))
        else:
            lines = ''.join(note_line(L, s[cid]) for L, _, _, s in SETS)
        out.append('<div class="wcase" data-case="%s"><div class="wh"><b>%s</b><span>%s</span></div><ul>%s</ul></div>' % (cid, esc(name), esc(when), lines))
    return '<div class="wlist" id="wlist">%s</div>' % ''.join(out)


def dest_table():
    rows = []
    used = {}
    for L, _, _, s in SETS:
        for cid, n in s.items():
            used.setdefault(n[2], set()).add(L)
    used.setdefault('privacy', set()).add('locked')
    for k, d in DEST.items():
        sets = ', '.join(sorted(used.get(k, [])))
        rows.append('<tr class="%s"><td>%s</td><td>%s</td><td>%s</td></tr>' % ('tbc' if d.get('tbc') else '', esc(d['t']), esc(d['src']), esc(sets or '-')))
    return '<div class="dtab"><table><thead><tr><th>Where the link goes</th><th>Named in</th><th>Sets</th></tr></thead><tbody>%s</tbody></table></div>' % ''.join(rows)


def seg(id_, label):
    return '<div class="dk-grp"><span class="dk-glab">%s</span><div id="%s"></div></div>' % (esc(label), id_)


PLAY = '<svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.5 2.2 9.8 6 3.5 9.8Z" fill="currentColor"/></svg>'

BODY = r"""
<div class="dk-page">
  <header class="dk-hdr">
    <div class="dk-tags"><span class="dk-tag">D-VN</span><span class="dk-tag">OD-0105: make the page</span><span class="dk-tag">OD-0099: Q-V-7 A, Q-V-6 B, Q-V-9 C</span><span class="dk-tag">OD-0104</span><span class="dk-tag">dark and light</span></div>
    <h1>Voice notes: the error words, the countdown and the CUDA offer</h1>
    <p>Three things voice typing still needs from you, drawn in the composer&#8217;s own note and pill and in the Voice typing page&#8217;s hardware card, each in dark and light. Pick one letter per question, or take the recommended ones. The bar at the bottom writes one line; paste it to the lead. This page sends nothing.</p>
    <nav class="links" aria-label="Questions"><a href="#Notes">Notes <b>?</b></a><a href="#Countdown">Countdown <b>?</b></a><a href="#CUDA">CUDA <b>?</b></a></nav>
  </header>
  <div class="tb dk-glass" role="toolbar" aria-label="Actions">
    <button class="dk-btn pri" id="all" type="button"><svg viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 6.3 5 8.6l4.6-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>Take all recommended</button>
    <button class="dk-btn" id="clear" type="button">Clear picks</button>
    <span class="dk-sp"></span>
    <div class="dk-grp"><span class="dk-glab">Page theme</span><div id="segT"></div></div>
  </div>

  <section class="dk-q dk-glass" id="Notes" data-q="Notes" data-rec="A" aria-labelledby="Notes-h">
    <div class="dk-qh"><span class="dk-qn">Q-V-7 A</span><h2 id="Notes-h">The error notes: which words?</h2></div>
    <p class="swift"><span class="sl">Already picked</span><span class="st">Each note says what happened, plus one link to the place that fixes it (OD-0099). Where and how a note shows is the locked one&#8217;s (V-14.6): with an empty draft it replaces the placeholder, with its warning mark; with text in the draft it shows in the composer&#8217;s notice line above your text, so your words are never hidden.</span></p>
    <p class="dk-qd">Four word sets for the eleven notes. Choose a set, a note and the draft below to see it in the composer; the full list of every note in every set is under the cards. The links are underlined and each one says where it goes.</p>
    <div class="ctl">%(segN)s</div>
    <div class="stage" id="stN"></div>
    <div class="dk-opts" role="group" aria-label="Options for Notes">%(optsN)s</div>
    <h3 class="lh">Every note in every set</h3>
    %(wlist)s
    <div class="dk-notes">
      <div class="dk-note"><h3>How long a note stays</h3><p>The locked note stays 5.2 s, then the placeholder (or your text) comes back (V-14.6). The others have no time yet; the lead proposes the same 5.2 s (<b>ED</b>). The spoken version of each note (V-14.5 has one for the blocked note) is written in VT-17 from the picked words.</p></div>
      <div class="dk-note"><h3>To confirm: the download note</h3><p>An orb click during the first download already shows the download in the pill (&#8220;Getting voice ready&#8221;, the welcome-voice pick, VT-15). So &#8220;Model still downloading&#8221; is for when that pill is not shown. Whether both show is for the lead to settle in VT-17 (<b>UN</b>).</p></div>
      <div class="dk-note"><h3>Drawn faithfully, and not</h3><p>The composer, its field note and the warning mark are the locked voice design&#8217;s (voice-composer.html); the notice line is the port&#8217;s (ComposerView.cs: subheadline, the warning colour, one line, cut at the end). Giving the notice line a link is new (<b>ED</b>). The composer light and the voice glow are left out; the orb is a still.</p></div>
    </div>
  </section>

  <section class="dk-q dk-glass" id="Countdown" data-q="Countdown" data-rec="B" aria-labelledby="Countdown-h">
    <div class="dk-qh"><span class="dk-qn">Q-V-6 B</span><h2 id="Countdown-h">The countdown in the pill: which look?</h2></div>
    <p class="swift"><span class="sl">Already picked</span><span class="st">The pill counts down the last 30 s before the 5-minute cap, then finishes the text as if you had clicked the orb (OD-0099, V-7.12). The countdown starts at 4:30 of listening.</span></p>
    <p class="dk-qd">The pill is the locked glass pill in the composer (OD-0091, V-12.2 to V-12.4): the cross at its far end, the words with their sweep, the orb at its right. Play runs the last 33 seconds; the frames below are the same look at 0:30, 0:10, 0:00 and finishing.</p>
    <div class="ctl"><button class="dk-btn pri" id="playC" type="button">%(play)s<span id="playCL">Play</span></button>%(segC)s</div>
    <div class="stage" id="stC"></div>
    <div class="frames" id="frC"></div>
    <div class="dk-opts" role="group" aria-label="Options for Countdown">%(optsC)s</div>
    <div class="dk-notes">
      <div class="dk-note"><h3>What is fixed in all four</h3><p>The pill&#8217;s width is the label&#8217;s measured width plus 88 px (V-12.2) and eases over 0.42 s when the words change; the figures are tabular, so the pill keeps still while they count. At 0:00 the label crossfades to Finishing&#8230; as at a second click (V-15). Under Reduce Motion the figures change without moving and the line steps once a second.</p></div>
      <div class="dk-note"><h3>With the language in the pill</h3><p>When the language is named in the pill (language default B, V-12.10), B reads &#8220;Transcribing&#8230; English 0:30&#8221;: the name, then the time, each after an en space in the lighter weight. A and D replace the words, so the name goes for those 30 s; C keeps it.</p></div>
      <div class="dk-note"><h3>Not on this page</h3><p>Whether a screen reader hears the countdown (for example once at 0:30 and once at 0:10) is not designed; the lead proposes one polite announcement at each of those two moments (<b>ED</b>). The cross-beside-the-orb style has no pill, so it has no countdown in any look.</p></div>
    </div>
  </section>

  <section class="dk-q dk-glass" id="CUDA" data-q="CUDA" data-rec="A" aria-labelledby="CUDA-h">
    <div class="dk-qh"><span class="dk-qn">Q-V-9 C</span><h2 id="CUDA-h">The CUDA offer in the hardware card: which look?</h2></div>
    <p class="swift"><span class="sl">Already picked</span><span class="st">The installer ships the Vulkan and processor runtimes only. On NVIDIA PCs the hardware check offers the CUDA runtime as a separate download (OD-0099). It is fetched only when you ask: the Whisper.net CUDA 12 pack from nuget.org and NVIDIA&#8217;s own libraries from NVIDIA&#8217;s servers, never hosted by Marrow, checked by hash, and a half-installed pack is never loaded (OD-0104).</span></p>
    <p class="dk-qd">The card is Settings &gt; Voice typing &gt; Speech model as drawn (V-16.3.2) on your desktop&#8217;s probe. Choose a state, or press Play to download it. Not NVIDIA shows a PC where nothing is offered.</p>
    <div class="ctl"><button class="dk-btn pri" id="playU" type="button">%(play)s<span>Play</span></button>%(segU)s</div>
    <div class="stage" id="stU"></div>
    <div class="dk-opts" role="group" aria-label="Options for CUDA">%(optsU)s</div>
    <div class="dk-notes">
      <div class="dk-note"><h3>The size: 644 MB</h3><p>The sum of the three pinned files in voice-pins.json: the Whisper.net CUDA 12 pack 1.9.1 (250,058,371 bytes, nuget.org), NVIDIA&#8217;s cuBLAS 12.4.2.65 (391,101,865 bytes) and CUDA runtime 12.4.99 (2,474,121 bytes), shown the way the model list shows sizes (V-9.2). The space it takes on disk is not measured yet (the pack&#8217;s own five files are 566 MB).</p></div>
      <div class="dk-note"><h3>To confirm: when it starts working</h3><p>Whisper.net loads its native runtime once per process (VT-6 item 5), and the hardware check itself loads one. So after the download CUDA may only be used from Marrow&#8217;s next start; the Installed state here says &#8220;Restart nth to use it&#8221; on that reading (<b>UN</b>, checked on the real PC, BUILD-PLAN section 4 item D). After that the GPU backend row reads &#8220;CUDA, working&#8221; (V-10.1).</p></div>
      <div class="dk-note"><h3>No speed claim</h3><p>Vulkan has not been timed against CUDA on your RTX 4080 yet (section 4 item D decides how strongly the offer is worded), so no look promises it is faster. The reason line and the model are the design&#8217;s examples (V-10.2, V-10.4). Shown only on an NVIDIA graphics card with a working probe (VT-17).</p></div>
    </div>
  </section>

  <section class="dk-glass stagecard" aria-label="Where each link goes">
    <h2>Where each link goes</h2>
    <p class="sub">Every place a note&#8217;s link opens, and where the plan names it. Two places in set D are not in the plan; they are marked <span class="tbcw">(destination to confirm)</span>.</p>
    %(dtab)s
  </section>
  <p class="dk-foot">Your picks stay in this browser only, if it allows storing them. Sources: BUILD-PLAN (rows Q-V-6, Q-V-7, Q-V-9, D-VN, VT-17), the voice SPEC, OD-0099, OD-0104 and OD-0105, the locked voice composer design, the Voice typing page design, the port&#8217;s composer and the CUDA pins. Every change on this page is transitioned (OD-0076). The orb is drawn as a still; the live one is the Liquid Orb export (V-11).</p>
</div>
"""
BAR = ('<div class="dk-picks dk-glass" id="bar" role="region" aria-label="Your picks"><span class="cnt" id="cnt" aria-live="polite"></span>'
       '<code class="pline" id="line" aria-label="The line to paste to the lead"></code><button class="dk-btn" id="copy" type="button">Copy</button></div>')


def main():
    css = open(os.path.join(HERE, 'vn-page.css'), encoding='utf-8').read()
    js = open(os.path.join(HERE, 'vn-page.js'), encoding='utf-8').read()
    body = BODY % dict(
        segN=seg('segNS', 'Word set') + seg('segNC', 'Note') + seg('segND', 'Draft'),
        optsN=''.join(opt_html('Notes', L, n + ' words', line, NOTES_WHY) for L, n, line, _ in SETS),
        wlist=word_list(),
        segC=seg('segCL', 'Look') + seg('segCS', 'Speed'),
        optsC=''.join(opt_html('Countdown', L, t, d, COUNT_WHY) for L, t, d in COUNT),
        segU=seg('segUL', 'Look') + seg('segUS', 'State'),
        optsU=''.join(opt_html('CUDA', L, t, d, CUDA_WHY) for L, t, d in CUDA),
        dtab=dest_table(), play=PLAY)
    data = {'dest': DEST, 'cases': [dict(id=c, t=n) for c, n, _ in CASES], 'locked': LOCKED,
            'sets': {L: dict(name=n, notes=s) for L, n, _, s in SETS},
            'count': [dict(v=L, t=t) for L, t, _ in COUNT], 'cuda': [dict(v=L, t=t) for L, t, _ in CUDA], 'rec': REC}
    body += BAR + '\n<script type="application/json" id="vndata">%s</script>\n' % json.dumps(data).replace('</', '<\\/')
    html = page(title='Voice Notes Picks',
                description='Voice typing\'s last design picks (D-VN): the error notes\' words with one link each, the 30-second countdown in the pill, and the CUDA download offer in the hardware card, drawn in dark and light.',
                css=css, body=body, js=js)
    assert html.startswith('<title>Voice Notes Picks</title>')
    for bad in (r'<!doctype', r'<html[\s>]', r'<body[\s>]', r'<head[\s>]'):
        assert not re.search(bad, html, re.I), bad
    for url in re.findall(r'(?:src|href)="(https?://[^"]+)"', html):
        assert url.startswith('https://fonts.googleapis.com/'), url
    for bad in ('eval(', 'new Function', 'fetch(', 'XMLHttpRequest'):
        assert bad not in js, bad
    # nothing private in the share copy: no local paths, user or account names, e-mail addresses or tokens
    for bad in (r'(?i)[a-z]:[\\/]+users', r'(?i)[\\/]users[\\/]', r'(?i)omera', r'(?i)e:[\\/]+droppy', r'[\w.+-]+@[\w-]+\.[\w.-]+', r'(?i)\b(?:sk|ghp|gho|xox[bp])[-_][A-Za-z0-9_-]{16,}'):
        m = re.search(bad, html)
        assert not m, (bad, html[max(0, m.start() - 40):m.end() + 40])
    write_both(ROOT, 'voice-notes', ascii_only(html))


def ascii_only(html):
    # the share copy has no charset meta (the publisher adds the skeleton), so every non-ASCII character is escaped:
    # inside scripts as a \u escape (they only occur in strings and comments), elsewhere as a numeric reference
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
