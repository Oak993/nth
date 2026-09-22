"""Re-anchor source citations from one baseline to the next using the git diff line map.

    py porting/tools/remap_citations.py <old-sha> <new-sha> <reference-checkout> <spec-dir-in> <spec-dir-out>

For every file that changed between the two commits, `git diff -U0` gives exact hunks. A line
outside every hunk is byte-identical in both commits and moves by a known offset, so a citation
"path:N" or "path:N-M" whose lines all fall outside the hunks is rewritten with the new numbers.
A citation that touches a changed line, or names a deleted file, is NOT touched: the record is
listed in the report for a person or agent to re-cite against the new source. Nothing is guessed.

Citations are recognised as `<path>.swift:<n>[-<m>]` and the bare `:<n>[-<m>]` forms that follow a
named path in the same string ("MCPStore.swift:124 and :193-199"). Files that did not change keep
their numbers. Every string field of every requirement and UI record is scanned.
"""

import json
import os
import re
import subprocess
import sys

old, new, ref, src_dir, out_dir = sys.argv[1:6]
git = lambda *a: subprocess.check_output(['git', '-C', ref, *a], text=True, encoding='utf-8')

status = {}
for line in git('diff', '--name-status', old, new).splitlines():
    s, p = line.split('\t', 1)
    status[p] = s[0]


def hunks(path):
    out = []
    for line in git('diff', '-U0', old, new, '--', path).splitlines():
        m = re.match(r'@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@', line)
        if m:
            a, b = int(m.group(1)), int(m.group(2) or 1)
            c, d = int(m.group(3)), int(m.group(4) or 1)
            out.append((a, b, c, d))
    return out


maps = {}


def mapper(path):
    if path in maps:
        return maps[path]
    hs = hunks(path)

    def f(n):
        delta = 0
        for a, b, c, d in hs:
            if b == 0:  # pure insertion after old line a
                if n > a:
                    delta += d
                continue
            if a <= n < a + b:
                return None  # the cited line itself changed
            if n >= a + b:
                delta += d - b
        return n + delta
    maps[path] = f
    return f


# A citation is a path (full, or a bare file name that is unique in the tree) followed by :N or
# :N-M, or a bare :N / :N-M that follows whitespace, '(' ',' or ';' after such a path in the same
# string. A colon glued to other text ("16:9", "http:") is never treated as a citation.
CITE = re.compile(
    r'(?:(?P<path>(?:(?:DroppyCode|scripts|website|release|docs)/)?[A-Za-z0-9_+/.-]*?[A-Za-z0-9_+-]\.(?:swift|metal|h|m|py|sh|js|mjs|json|md|yml|html|css))'
    r'|(?<=[\s(,;]))'
    r':(?P<a>\d+)(?:-(?P<b>\d+))?(?![\d:])')
tracked = git('ls-tree', '-r', '--name-only', new).splitlines() + [p for p, s in status.items() if s == 'D']
by_base = {}
for p in tracked:
    by_base.setdefault(p.rsplit('/', 1)[-1], set()).add(p)
report = {}
counts = {'rewritten': 0, 'unchanged_file': 0, 'flagged': 0}


def resolve(path):
    if '/' in path and path in status or path in by_base.get(path.rsplit('/', 1)[-1], ()):
        return path
    hits = by_base.get(path.rsplit('/', 1)[-1], set())
    suffix = [h for h in hits if h.endswith('/' + path) or h == path]
    if len(suffix) == 1:
        return suffix[0]
    return None


def remap_text(text, rid, area):
    current = [None]
    flagged = []

    def rep(m):
        raw, a, b = m.group('path'), int(m.group('a')), m.group('b')
        path = None
        if raw:
            path = resolve(raw)
            if path is None:
                current[0] = None
                return m.group(0)  # not a file in this repository, or ambiguous: leave it
            current[0] = path
        p = current[0]
        if not p:
            return m.group(0)
        st = status.get(p)
        if st is None:
            counts['unchanged_file'] += 1
            return m.group(0)
        if st == 'D':
            flagged.append(f'{p}:{a}{"-" + b if b else ""} names a file deleted at the new baseline')
            return m.group(0)
        f = mapper(p)
        na = f(a)
        nb = f(int(b)) if b else None
        inner_changed = False
        if b:
            for n in range(a, int(b) + 1):
                if f(n) is None:
                    inner_changed = True
                    break
        if na is None or (b and (nb is None or inner_changed)):
            flagged.append(f'{p}:{a}{"-" + b if b else ""} touches lines changed at the new baseline')
            return m.group(0)
        if na == a and (not b or nb == int(b)):
            return m.group(0)
        counts['rewritten'] += 1
        prefix = raw if raw else ''
        return f'{prefix}:{na}' + (f'-{nb}' if b else '')

    out = CITE.sub(rep, text)
    if flagged:
        report.setdefault(area, {}).setdefault(rid, []).extend(flagged)
        counts['flagged'] += len(flagged)
    return out


def walk(obj, rid, area):
    if isinstance(obj, str):
        return remap_text(obj, rid, area)
    if isinstance(obj, list):
        return [walk(x, rid, area) for x in obj]
    if isinstance(obj, dict):
        return {k: (v if k == 'id' else walk(v, rid, area)) for k, v in obj.items()}
    return obj


os.makedirs(out_dir, exist_ok=True)
for name in sorted(os.listdir(src_dir)):
    if not name.endswith('.json'):
        continue
    doc = json.load(open(os.path.join(src_dir, name), encoding='utf-8'))
    area = doc.get('area') or name[:-5]
    doc['requirements'] = [walk(r, r.get('id'), area) for r in doc.get('requirements', [])]
    if 'ui' in doc:
        doc['ui'] = [walk(u, u.get('id'), area) for u in doc['ui']]
    with open(os.path.join(out_dir, name), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')

json.dump({'old': old, 'new': new, 'counts': counts, 'flagged': report},
          open(os.path.join(out_dir, '_remap_report.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print(counts, {a: len(v) for a, v in report.items()})
