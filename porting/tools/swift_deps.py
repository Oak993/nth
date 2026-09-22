"""File-level dependency graph of the pinned Swift source, for ordering implementation waves.

    py porting/tools/swift_deps.py [--out porting/deps/swift_deps.json]

ENGINEERING TOOL, not evidence of comprehension. It answers one question: before a Swift file can
be ported and its tests compiled, which other files' declarations must already exist in C#?

Method (static, conservative):
- Strip comments and string literals (including multi-line and raw strings) so words inside them
  do not count as references.
- A file DECLARES the top-level types (class, struct, enum, protocol, actor, typealias), top-level
  functions and top-level let/var it defines at brace depth 0, plus the members it adds at depth 1
  inside `extension <ForeignType>` blocks when the foreign type is not a project type (String,
  Array, Color, View...). Those members are how Swift files quietly depend on each other
  (`.trimmed`, `.nilIfEmpty`), so they are included when the name is distinctive enough.
- A file DEPENDS on another file when it names a symbol that file declares, or extends a type that
  file declares.
- Cycles are allowed inside one Swift module; they are condensed into strongly connected
  components, and each component is placed on a level after everything it needs.

Over-approximation (a false edge) only delays a unit. Under-approximation (a missed edge) shows up
as a compile error in the unit, which the implementing agent must report instead of inventing the
missing piece.
"""

import json
import os
import re
import subprocess
import sys
from collections import defaultdict

ROOT = os.getcwd()
BASE = json.load(open(os.path.join(ROOT, 'porting', 'baseline.json'), encoding='utf-8'))['current']
REF = BASE['reference_path']
out_path = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else 'porting/deps/swift_deps.json'

files = [p for p in subprocess.check_output(['git', '-C', REF, 'ls-files', 'DroppyCode'], text=True).split('\n')
         if p.endswith('.swift')]


def strip(src):
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith('//', i):
            j = src.find('\n', i)
            i = n if j < 0 else j
            continue
        if src.startswith('/*', i):
            depth, i = 1, i + 2
            while i < n and depth:
                if src.startswith('/*', i):
                    depth, i = depth + 1, i + 2
                elif src.startswith('*/', i):
                    depth, i = depth - 1, i + 2
                else:
                    i += 1
            out.append(' ')
            continue
        m = re.match(r'(#*)("""|")', src[i:])
        if m:
            hashes, quote = m.group(1), m.group(2)
            close = quote + hashes
            j = i + len(m.group(0))
            while j < n:
                if src.startswith(close, j) and (quote == '"""' or True):
                    # an escaped quote in a non-raw single-line string does not close it
                    if not hashes and quote == '"' and src[j - 1] == '\\' and src[j - 2] != '\\':
                        j += 1
                        continue
                    j += len(close)
                    break
                if quote == '"' and src[j] == '\n':
                    break
                j += 1
            out.append('""')
            i = j
            continue
        out.append(c)
        i += 1
    return ''.join(out)


DECL = re.compile(r'^\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|private|fileprivate|internal|open|final|indirect|nonisolated|static)\s+)*'
                  r'(class|struct|enum|protocol|actor|typealias|func|let|var|extension)\s+([A-Za-z_][A-Za-z0-9_]*)')
IDENT = re.compile(r'[A-Za-z_][A-Za-z0-9_]*')
STOP = {'body', 'value', 'title', 'label', 'color', 'count', 'first', 'last', 'isEmpty', 'description', 'string',
        'lines', 'name', 'text', 'image', 'content', 'items', 'index', 'state', 'style', 'shape', 'width', 'height',
        'size', 'frame', 'offset', 'padding', 'action', 'result', 'error', 'model', 'effort', 'kind', 'path', 'data'}

decl_top, ext_members, extends, idents = {}, defaultdict(set), defaultdict(set), {}
for f in files:
    src = strip(open(os.path.join(REF, f), encoding='utf-8', errors='replace').read())
    depth, current_ext = 0, None
    tops = set()
    for line in src.split('\n'):
        m = DECL.match(line)
        if m:
            kind, name = m.group(1), m.group(2)
            if depth == 0:
                if kind == 'extension':
                    extends[f].add(name)
                    current_ext = name
                else:
                    tops.add(name)
                    current_ext = None
            elif depth == 1 and current_ext and kind in ('func', 'var', 'let'):
                ext_members[(f, current_ext)].add(name)
        depth += line.count('{') - line.count('}')
        if depth <= 0:
            depth = 0
            if not (m and m.group(1) == 'extension'):
                pass
    decl_top[f] = tops
    idents[f] = set(IDENT.findall(src))

owner = {}
for f, names in decl_top.items():
    for n in names:
        owner.setdefault(n, f)
project_types = set(owner)

member_owner = defaultdict(set)
for (f, ext), names in ext_members.items():
    if ext in project_types:
        continue
    for n in names:
        if len(n) >= 5 and n not in STOP:
            member_owner[n].add(f)

deps = defaultdict(set)
for f in files:
    for n in idents[f]:
        o = owner.get(n)
        if o and o != f:
            deps[f].add(o)
        for o2 in member_owner.get(n, ()):
            if o2 != f:
                deps[f].add(o2)
    for t in extends[f]:
        o = owner.get(t)
        if o and o != f:
            deps[f].add(o)

# Tarjan SCC
sys.setrecursionlimit(10000)
index, low, stack, on, comps, counter = {}, {}, [], set(), [], [0]


def strong(v):
    index[v] = low[v] = counter[0]
    counter[0] += 1
    stack.append(v)
    on.add(v)
    for w in deps[v]:
        if w not in index:
            strong(w)
            low[v] = min(low[v], low[w])
        elif w in on:
            low[v] = min(low[v], index[w])
    if low[v] == index[v]:
        comp = []
        while True:
            w = stack.pop()
            on.discard(w)
            comp.append(w)
            if w == v:
                break
        comps.append(sorted(comp))


for f in files:
    if f not in index:
        strong(f)
comp_of = {f: i for i, c in enumerate(comps) for f in c}
level = {}
for i, c in enumerate(comps):  # Tarjan emits components in reverse topological order
    need = {comp_of[d] for f in c for d in deps[f]} - {i}
    level[i] = 1 + max((level[j] for j in need), default=-1)

layer = lambda p: p.split('/')[1] if p.startswith('DroppyCode/') else '?'
RANK = {'Core': 0, 'Services': 1, 'UI': 1, 'App': 2}
violations = []
for f in files:
    for d in deps[f]:
        a, b = layer(f), layer(d)
        if a in RANK and b in RANK and (RANK[b] > RANK[a] or (a in ('Services', 'UI') and b in ('Services', 'UI') and a != b)):
            violations.append({'from': f, 'to': d, 'from_layer': a, 'to_layer': b})

doc = {
    'baseline': BASE['commit_sha'],
    'note': 'Engineering aid for ordering work; see the docstring of porting/tools/swift_deps.py for its limits.',
    'files': {f: {'declares': sorted(decl_top[f]), 'depends_on': sorted(deps[f]), 'component': comp_of[f],
                  'level': level[comp_of[f]]} for f in sorted(files)},
    'components': [{'id': i, 'files': c, 'level': level[i]} for i, c in enumerate(comps) if len(c) > 1],
    'layer_violations': violations,
}
os.makedirs(os.path.dirname(os.path.join(ROOT, out_path)), exist_ok=True)
json.dump(doc, open(os.path.join(ROOT, out_path), 'w', encoding='utf-8'), indent=1)
lv = defaultdict(int)
for f in files:
    lv[level[comp_of[f]]] += 1
print(f'{len(files)} Swift files, {sum(len(v) for v in deps.values())} edges, {len(comps)} components '
      f'({sum(1 for c in comps if len(c) > 1)} cycles), max level {max(level.values())}')
print('files per level:', dict(sorted(lv.items())))
for c in doc['components']:
    print(f'  cycle L{c["level"]}: {len(c["files"])} files: {", ".join(x.split("/")[-1] for x in c["files"][:12])}{" ..." if len(c["files"]) > 12 else ""}')
print(f'layer violations: {len(violations)}')
for v in violations[:40]:
    print(f'  {v["from_layer"]} -> {v["to_layer"]}: {v["from"].split("/")[-1]} -> {v["to"].split("/")[-1]}')
