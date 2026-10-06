# Builds the interactive "Skills Downloads" decision page, round 5 (all picks made, Custom C), from round 2 (OD-0099 pick "Page D, Offer A, Words S1" and the
# owner's changes: separate Plugins and Skills sections, Download all with a size disclaimer, skills listed and invoked
# the way Claude does it; OD-0070): skills-downloads.html
# (full page, with the skeleton, for local viewing) and share/skills-downloads.html (skeleton stripped, <title> first,
# everything inline, Google Fonts only, the Marrow icon as a data: image), with the decisions-2026-10-03 kit
# (kit.css, kit.js) and the window tokens of the sibling update-everything page.
#
# Sources: sd-body.html (the page), sd-page.css (the page and the window mocks), sd-page.js (the model, nine Settings
# windows: three section layouts times three Download all placements, and three chat windows for invoking a skill), and the Marrow
# icon read from the port's own asset (src/Marrow.App/Assets/icon.svg, OD-0015), never Droppy's.
import base64, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', 'decisions-2026-10-03', '_tools'))
from pagekit import page, write_both  # noqa: E402

ICON = r'E:\Droppy\wt\cl\terminal-verify\src\Marrow.App\Assets\icon.svg'


def icon_uri():
    svg = open(ICON, encoding='utf-8').read()
    svg = re.sub(r'<!--.*?-->\s*', '', svg, flags=re.S).strip()
    assert 'Droppy' not in svg and '<svg' in svg
    return 'data:image/svg+xml;base64,' + base64.b64encode(svg.encode('utf-8')).decode('ascii')


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


def main():
    css = open(os.path.join(HERE, 'sd-page.css'), encoding='utf-8').read()
    js = open(os.path.join(HERE, 'sd-page.js'), encoding='utf-8').read()
    body = open(os.path.join(HERE, 'sd-body.html'), encoding='utf-8').read()
    body += '\n<script type="application/json" id="sddata">%s</script>\n' % json.dumps({'icon': icon_uri()}).replace('</', '<\\/')
    html = page(title='Skills Downloads',
                description="Marrow's skills pages (OD-0099), round 5, all picks made: separate Plugins, Skills and Tools pages with Download all, the hidden-download pill and popover, the manual fallback, the Add button with a chevron in each title row (Custom C) and skills the model calls by itself, played live in dark and light.",
                css=css, body=body, js=js)
    assert html.startswith('<title>Skills Downloads</title>')
    for bad in (r'<!doctype', r'<html[\s>]', r'<body[\s>]', r'<head[\s>]'):
        assert not re.search(bad, html, re.I), bad
    for url in re.findall(r'(?:src|href)="(https?://[^"]+)"', html):
        assert url.startswith('https://fonts.googleapis.com/'), url
    for bad in ('eval(', 'new Function', 'fetch(', 'XMLHttpRequest', 'droppy'):
        assert bad not in js.lower() if bad == 'droppy' else bad not in js, bad
    # nothing private in the share copy: no local paths, user or account names, e-mail addresses or tokens
    for bad in (r'(?i)[a-z]:[\\/]+users', r'(?i)[\\/]users[\\/]', r'(?i)omera', r'(?i)e:[\\/]+droppy', r'[\w.+-]+@[\w-]+\.[\w.-]+', r'(?i)\b(?:sk|ghp|gho|xox[bp])[-_][A-Za-z0-9_-]{16,}', r'(?i)api[_-]?key\s*[:=]'):
        m = re.search(bad, html)
        assert not m, (bad, html[max(0, m.start() - 40):m.end() + 40])
    write_both(ROOT, 'skills-downloads', ascii_only(html))


main()
