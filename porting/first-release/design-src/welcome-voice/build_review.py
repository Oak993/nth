# Builds the review page (review/index.html + review/boards/*.html) from the canvas project, with the
# decisions-2026-10-03 review kit (reviewkit.py), pointed at this folder.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
TOOLS03 = os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools')
sys.path.insert(0, TOOLS03)
import reviewkit as R  # noqa: E402
import wv_lib as W  # noqa: E402
from reviewkit import E  # noqa: E402

# reviewkit finds a page's folder as <TOOLS>/../<name>; aim it at decisions-2026-10-04 (its kit path is already fixed)
R.TOOLS = os.path.join(HERE, '..', '..', '_tools')

rec = W.REC
cover = [
    ('Q1 · where the question sits', '6 places, A to F', ['<b>%s</b> %s' % (v[0], E(v[1])) for v in W.VARIANTS],
     '<strong>Recommended: A Section.</strong> It is Welcome’s own pattern (a titled section of rows, like Activity list and Providers), it has room for the three things each answer must say, and it never covers Add a project. Runner-up: E Sidebar, out of the way and already where the D3 download capsule was drawn.'),
    ('Q2 · the words', '3 sets, V1 to V3', ['<b>%s</b> %s: “%s”' % (k, E(w['name']), E(w['ask']['t'])) for k, w in W.WORDS.items()],
     '<strong>Recommended: V1 Plain.</strong> Matches W1 on Getting ready, says each fact once in a full sentence, names the path in full, and states the language trade-off plainly.'),
]
questions = [
    {'id': 'place', 'label': 'Place', 'title': 'Where Welcome asks about voice typing', 'ops': [(v[0], '<b>%s</b> %s' % (E(v[1]), E(v[2])), v[0] == rec['place']) for v in W.VARIANTS]},
    {'id': 'words', 'label': 'Words', 'title': 'The words of the question and its answers', 'ops': [(k, '<b>%s</b> “%s” %s [%s] [%s]. %s' % (
        E(w['name']), E(w['ask']['t']), W.fill(w['ask']['b'], 'pc'), E(w['ask']['yes']), E(w['ask']['no']), E(w['line'])), k == rec['words']) for k, w in W.WORDS.items()]},
]
cards = [{'board': 'Place' + v[0], 'letter': v[0], 'name': v[1], 'line': E(v[2]) + ' Shown: the question, Yes, No, and the slow-PC question.', 'rec': v[0] == rec['place'], 'picks': [('place', v[0])]} for v in W.VARIANTS]
cards.append({'board': 'Words', 'letter': 'V', 'name': 'Words: three sets', 'line': 'Every line of each set, with the question and the slow-PC question drawn in place A.', 'picks': [('words', k) for k in W.WORDS]})
cards.append({'board': 'Orb', 'letter': 'O', 'name': 'At the orb, later', 'line': 'What the words promise: the progress at the orb, the download a click starts after No, and the slow-PC question when Welcome has gone (a popover with the Mac’s arrow tip).'})
R.build('welcome-voice', page_title='Welcome Voice Boards', icon='mic',
        description='Six places and three word sets for the Welcome screen’s voice-typing question (OD-0096 consent amendment, OD-0097 Q-V-1), each with Yes, No and the slow-PC question, dark and light.',
        heading='Welcome asks about voice typing',
        lede='Getting ready is locked (look I, words W1, download D3 with consent): nothing downloads until Welcome asks. Yes starts the download in the background; No downloads nothing; both name the voice orb in the composer and Settings &gt; Voice typing &gt; Speech model. On a slow PC the speed test then asks once: live words with a smaller model, or the accurate model after you stop. Two choices; answer by letter (“A, V1”).',
        cover=cover, also='', questions=questions, cards=cards,
        live_note='<b>Played live:</b> the interactive page “Welcome Voice Question” plays the whole first run (sheet, hand-over, question, answer, download, speed test, slow-PC question) at any place with any words; its drawn buttons work.',
        notes=[('When the slow-PC question shows', 'My call, as asked: it comes when the speed test ends. If Welcome is still up, it takes the answer’s place (boards A to F, frame 4); if a project is already open, the next click on the orb asks in a popover above it before listening (board O, frame 3).'),
               ('Estimated', 'The slow PC (no graphics card, 8 cores) is estimated: Large v3 Turbo compact 574 MB, Small 264 MB as the live model. This PC’s numbers are measured. Provider statuses are a sample; the orb is a stand-in drawn as a still; the icon is the Marrow icon.')])
