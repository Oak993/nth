# Builds the app-icon decision page (D-ICON, owner 2026-10-05: twenty premium directions for Marrow's app icon, each
# with its launch and idle animation): app-icon.html (full page, with the skeleton, for local viewing) and
# share/app-icon.html (skeleton stripped, <title> first, everything inline, Google Fonts only), with the
# decisions-2026-10-03 kit (kit.css, kit.js).
#
# Sources of this page: iconkit.js (the helpers and the registry), the icon modules (icons-a-j/*.js by designer 1,
# icons-k-t/*.js by designer 2, contract ICON-MODULE.md), ic-page.css and ic-page.js (the page), and walls/ (two
# captures of the approved Dichroic static wallpaper, dark and light, made by grab_walls.cjs from the wallpaper
# library page, for the desktop preview).
import base64, glob, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402


def read(p):
    return open(p, encoding='utf-8').read()


def modules():
    files = sorted(glob.glob(os.path.join(HERE, 'icons-a-j', '*.js')) + glob.glob(os.path.join(HERE, 'icons-k-t', '*.js')),
                   key=lambda p: os.path.basename(p))
    out, seen = [], set()
    for f in files:
        b = os.path.basename(f)
        m = re.match(r'([A-T])-([a-z0-9-]+)\.js$', b)
        if not m:
            continue
        if m.group(1) in seen:
            raise SystemExit('two modules for letter ' + m.group(1))
        seen.add(m.group(1))
        src = read(f)
        for bad in ('eval(', 'new Function', 'fetch(', 'XMLHttpRequest', 'localStorage', 'Math.random', 'setTimeout', 'requestAnimationFrame', 'http://', 'https://', 'import ', 'fillText'):
            assert bad not in src, (b, bad)
        assert re.search(r"letter:\s*'%s'" % m.group(1), src), (b, 'letter does not match the file name')
        out.append('/* ---- %s ---- */\n%s' % (b, src))
    return out, sorted(seen)


def wall(name):
    data = open(os.path.join(HERE, 'walls', name), 'rb').read()
    return 'url("data:image/jpeg;base64,%s")' % base64.b64encode(data).decode('ascii')


BODY = r"""
<div class="dk-page">
  <header class="dk-hdr">
    <div class="dk-tags"><span class="dk-tag">D-ICON</span><span class="dk-tag">owner, 2026-10-05</span><span class="dk-tag">20 directions</span><span class="dk-tag">launch and idle motion</span><span class="dk-tag">Windows 11 sizes</span><span class="dk-tag">dark and light</span></div>
    <h1>Marrow&#8217;s app icon: twenty directions</h1>
    <p>An icon tells you what the app is like inside. These are twenty original directions, each a different idea, material and colour story, drawn in code at the real Windows sizes. Every card shows the icon large with its launch animation (the basis for the loading animation, made after you pick) and its idle loop, then its 16 to 256 px renders, the 16 and 24 px pixels exactly as Windows draws them, and the icon in the taskbar, Start, Alt-Tab and on the desktop. Pick one; the bar at the bottom writes the line to paste to the lead. This page sends nothing.</p>
    <nav class="links" id="links" aria-label="Directions"><a href="#compare">All</a></nav>
  </header>
  <div class="tb dk-glass" role="toolbar" aria-label="Page settings">
    <div class="dk-grp"><span class="dk-glab">Page theme</span><div id="segT"></div></div>
    <div class="dk-grp"><span class="dk-glab">Motion</span><div id="segM"></div></div>
    <span class="dk-sp"></span>
    <button class="dk-btn" id="clear" type="button">Clear the pick</button>
  </div>
  <section class="cmp dk-q dk-glass" id="compare" aria-labelledby="cmp-h">
    <div class="dk-qh"><span class="dk-qn">All</span><h2 id="cmp-h">Side by side</h2></div>
    <p class="sub">All twenty at 32 px, the size of a Start tile, on Windows&#8217; dark and light surfaces, and at 256 px. Choose one to go to its card; Play every launch runs all twenty launches together.</p>
    <div id="cmp"></div>
  </section>
  <div id="cards" style="display:flex;flex-direction:column;gap:20px"></div>
  <div class="dk-notes">
    <div class="dk-note dk-glass"><h3>How the sizes are drawn</h3><p>Each direction has its own simplified art for the small sizes (16 and 20 px, then 24 and 32 px), as a real icon set would: fewer shapes, no hairlines, edges on whole pixels. The rows marked true pixels and the 8x loupes are drawn one canvas pixel per Windows pixel, as at 100% display scale. The shipped .ico and the app package images would be exported from the picked direction at each size.</p></div>
    <div class="dk-note dk-glass"><h3>The animations</h3><p>Every launch lasts 0.8 to 1.2 s and ends exactly on the still icon, so nothing jumps when it stops. Every idle loop is seamless and starts and ends on the still icon. Under Reduce Motion (Windows: Settings, Accessibility, Visual effects, Animation effects off) the launch is a plain fade and there is no idle loop; the Motion switch above previews both.</p></div>
    <div class="dk-note dk-glass"><h3>What the contexts are</h3><p>The taskbar, Start, Alt-Tab and the desktop are drawn approximations of Windows 11 at 100% scale, not Microsoft&#8217;s artwork. Neutral grey tiles stand in for other apps. The desktop wallpaper is the approved Dichroic static wallpaper from the wallpaper library, in its dark and light looks.</p></div>
  </div>
  <p class="dk-foot">Every icon is original and drawn in code (Canvas 2D), with no images, fonts or marks of any company. Your pick stays in this browser only, if it allows storing it. Every change on this page is transitioned (OD-0076).</p>
</div>
<div class="ibar dk-glass" id="bar" role="region" aria-label="Your pick"><span class="pl">Your pick</span><code id="pline" aria-label="The line to paste to the lead">app-icon: ?</code><span class="nm" id="pname"></span><span class="note">Tell the lead this line.</span><button class="dk-btn" id="copy" type="button">Copy</button></div>
"""


def main():
    mods, letters = modules()
    css = read(os.path.join(HERE, 'ic-page.css'))
    css += '\n:root{--wallD:%s;--wallL:%s}\n' % (wall('dichroic-dark.jpg'), wall('dichroic-light.jpg'))
    js = read(os.path.join(HERE, 'iconkit.js')) + '\n' + '\n'.join(mods) + '\n' + read(os.path.join(HERE, 'ic-page.js'))
    html = page(title='App Icon Picks',
                description='Twenty premium directions for Marrow\'s app icon, each drawn in code with its launch and idle animation, at the Windows sizes and in the taskbar, Start, Alt-Tab and desktop, dark and light.',
                css=css, body=BODY, js=js)
    assert html.startswith('<title>App Icon Picks</title>')
    for bad in (r'<!doctype', r'<html[\s>]', r'<body[\s>]', r'<head[\s>]'):
        assert not re.search(bad, html, re.I), bad
    for url in re.findall(r'(?:src|href)="(https?://[^"]+)"', html):
        assert url.startswith('https://fonts.googleapis.com/'), url
    for url in re.findall(r'https?://[^\s"\')]+', html):
        assert url.startswith('https://fonts.googleapis.com/') or url.startswith('http://www.w3.org/'), url
    # the owner's scope change: the page never mentions the old mark or its colours
    for bad in (r'(?i)\bthree colou?rs\b', r'(?i)\bfour colou?rs\b', r'(?i)\bold (mark|icon)\b', r'(?i)\bcurrent (mark|icon)\b', r'(?i)\barcs?\b', r'(?i)bright core'):
        m = re.search(bad, re.sub(r'<script>.*?</script>', '', html, flags=re.S) + '\n'.join(re.findall(r"(?:meaning|story|note|name):\s*'([^']*)'", js)))
        assert not m, (bad, m.group(0))
    for bad in (r'(?i)[a-z]:[\\/]+users', r'(?i)[\\/]users[\\/]', r'(?i)omera', r'(?i)e:[\\/]+droppy', r'[\w.+-]+@[\w-]+\.[a-z]{2,}\b', r'(?i)\b(?:sk|ghp|gho|xox[bp])[-_][A-Za-z0-9_-]{16,}'):
        m = re.search(bad, html)
        assert not m, (bad, html[max(0, m.start() - 40):m.end() + 40])
    out = ascii_only(html)
    write_both(ROOT, 'app-icon', out)
    print('letters', ''.join(letters), 'share', round(len(out) / 1024), 'KB')


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
