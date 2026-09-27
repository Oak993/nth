"""Plan implementation units: one per Swift file, ordered by the dependency graph.

    py porting/tools/plan_units.py [--out porting/deps/units.json]

ENGINEERING TOOL. A unit is one pinned Swift file and every requirement and UI record whose primary
source is that file (the same rule gen_tasks.mjs uses, so units and packets agree). Large units are
split into parts of at most PART_SIZE records, implemented one after another in the same worktree.

Dependencies come from porting/deps/swift_deps.json with three corrections, each an existing
engineering decision rather than a new one:
- files the port does not carry are dropped (ED-001: the website/tour capture subsystem);
- the five upstream layering violations are dropped as edges, because the recorded resolutions move
  the offending code up a layer (HANDOFF section 9; MCP.swift's MCPProxy and Storage.root use moves
  to Services, WallpaperStore to App, MarkdownView's hydra rendering becomes an App-supplied hook);
  at B3 upstream fixed all five (swift_deps.py reports 0 violations), so the set no longer matches an edge;
- a unit whose records are all IMPLEMENTED or VERIFIED and none flagged "rework" is DONE and is not
  scheduled; its dependents treat it as satisfied.
Cycles (strongly connected components) are marked so the workflow can generate C# skeletons for a
component before its members are implemented.
"""

import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.getcwd()
out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else 'porting/deps/units.json'
J = lambda p: json.load(open(os.path.join(ROOT, p), encoding='utf-8'))

deps = J('porting/deps/swift_deps.json')
matrix = J('porting/parity_matrix.json')
ui = J('porting/ui_contract.json')
queue = J('porting/tasks/queue.json')

EXCLUDED = {  # ED-001
    'DroppyCode/App/Captures/TourCaptures.swift',
    'DroppyCode/App/Captures/TourCaptures+Hydra.swift',
    'DroppyCode/App/Captures/WebsiteCaptures.swift',
}
LAYER_EDGES_DROPPED = {
    ('DroppyCode/Core/Models/MCP.swift', 'DroppyCode/Services/MCP/MCPProxy.swift'),
    ('DroppyCode/Core/Models/MCP.swift', 'DroppyCode/Services/Store/Storage.swift'),
    ('DroppyCode/UI/Markdown/MarkdownView.swift', 'DroppyCode/App/Views/Chat/TimelineRows.swift'),
    ('DroppyCode/UI/Common/Wallpaper.swift', 'DroppyCode/App/Captures/WebsiteCaptures.swift'),
    ('DroppyCode/UI/Common/Wallpaper.swift', 'DroppyCode/Services/Store/Storage.swift'),
}
OUT_OF_SCOPE = {'RETIRED', 'NOT_APPLICABLE_WITH_EVIDENCE'}
DONE = {'IMPLEMENTED', 'VERIFIED'}
PART_SIZE = 24

SWIFT = re.compile(r'DroppyCode/[A-Za-z0-9_+/.-]+\.swift')


def primary(rec):
    for e in rec.get('source_evidence') or []:
        m = SWIFT.search(str(e))
        if m:
            return m.group(0)
    return None


# Orchestrator overrides (porting/deps/overrides.json): a record assigned to another file's unit ("assign", record id ->
# Swift file) and extra edges the Swift import graph cannot see ("extra_deps", Swift file -> [Swift files]), each
# backed by an engineering decision named in the file's "note".
OV = J('porting/deps/overrides.json') if os.path.exists(os.path.join(ROOT, 'porting', 'deps', 'overrides.json')) else {}
OV_ASSIGN = OV.get('assign', {})
OV_DEPS = OV.get('extra_deps', {})

recs = defaultdict(list)
for r in matrix['requirements']:
    if r.get('status') in OUT_OF_SCOPE:
        continue
    recs[OV_ASSIGN.get(r['id']) or primary(r)].append({'id': r['id'], 'status': r.get('status'), 'rework': bool(r.get('rework')), 'kind': 'req'})
for u in ui['records']:
    if u.get('status') in OUT_OF_SCOPE:
        continue
    recs[OV_ASSIGN.get(u['id']) or primary(u)].append({'id': u['id'], 'status': u.get('status', 'SPECIFIED'), 'rework': bool(u.get('rework')), 'kind': 'ui'})

packets = defaultdict(list)
risk = {}
for t in queue['tasks']:
    packets[t['source']].append(t['id'])
    if t.get('risk') == 'HIGH':
        risk[t['source']] = 'HIGH'


def project(f):
    layer = f.split('/')[1]
    return {'Core': 'Marrow.Core', 'Services': 'Marrow.Services', 'UI': 'Marrow.UI', 'App': 'Marrow.App'}[layer]


# B3 (AM-0003): C# files that already hold a new Swift file's code, so the unit maps onto them (B3_PLAN.md section 3).
# N3: the port made MCPCatalogEntry+Resolve's move into MCPResolver.cs before upstream did (architecture V1).
# N4: MCPPaths stays in Models/MCP.cs (ED-177) until the batch-6 relocation.
TARGET_OVERRIDES = {
    'DroppyCode/Services/MCP/MCPCatalogEntry+Resolve.swift': 'src/Marrow.Services/MCP/MCPResolver.cs',
    'DroppyCode/Services/MCP/MCPPaths.swift': 'src/Marrow.Services/Models/MCP.cs',
}


def target(f):
    if f in TARGET_OVERRIDES:
        return TARGET_OVERRIDES[f]
    m = re.match(r'^DroppyCode/(Core|Services|UI|App)/(.+)\.swift$', f)
    return f'src/{project(f)}/{m.group(2)}.cs'


def uid(f):
    return f[len('DroppyCode/'):-len('.swift')].replace('/', '.').replace('+', '_')


files = [f for f in deps['files'] if f not in EXCLUDED and f.startswith('DroppyCode/')]
comp_members = defaultdict(list)
for f in files:
    comp_members[deps['files'][f]['component']].append(f)

units = []
for f in sorted(files, key=lambda x: (deps['files'][x]['level'], x)):
    rs = sorted(recs.get(f, []), key=lambda x: (x['kind'], x['id']))
    todo = [x for x in rs if x['status'] not in DONE or x['rework']]
    comp = deps['files'][f]['component']
    members = sorted(comp_members[comp])
    d = [x for x in deps['files'][f]['depends_on']
         if x not in EXCLUDED and (f, x) not in LAYER_EDGES_DROPPED and x in deps['files']]
    d += [x for x in OV_DEPS.get(f, []) if x in deps['files'] and x not in d]
    parts = [[x['id'] for x in todo[i:i + PART_SIZE]] for i in range(0, len(todo), PART_SIZE)]
    units.append({
        'id': uid(f),
        'swift': f,
        'target': target(f),
        'project': project(f),
        'test_project': f'tests/{project(f)}.Tests',
        'level': deps['files'][f]['level'],
        'component': comp if len(members) > 1 else None,
        'component_members': [uid(m) for m in members] if len(members) > 1 else [],
        'deps': sorted({uid(x) for x in d} - {uid(m) for m in members}),
        'intra_deps': sorted({uid(x) for x in d} & {uid(m) for m in members} - {uid(f)}),
        'records_total': len(rs),
        'records_todo': [x['id'] for x in todo],
        'parts': parts,
        'packets': packets.get(f, []),
        'risk': risk.get(f, 'STANDARD'),
        'status': 'DONE' if rs and not todo else ('NO_RECORDS' if not rs else 'TODO'),
    })

# Port-only foundation units (porting/deps/foundation_units.json, IMPLEMENTING.md section 4.3). A Swift unit
# depends on one when its B2 source uses the API the foundation unit replaces, and the foundation unit's
# layer is at or below the Swift unit's (Core < Services, UI < App), so no dependency crosses the layering.
fpath = os.path.join(ROOT, 'porting', 'deps', 'foundation_units.json')
if os.path.exists(fpath):
    RANK = {'Marrow.Core': 0, 'Marrow.Services': 1, 'Marrow.UI': 1, 'Marrow.App': 2, 'Marrow.Testing': 9}
    REF = deps.get('reference') or json.load(open(os.path.join(ROOT, 'porting', 'baseline.json'), encoding='utf-8'))['current']['reference_path']
    fdoc = json.load(open(fpath, encoding='utf-8'))
    fdeps = {'F.SettingsStore': ['F.FileOps'], 'F.StyledText': ['F.SwiftFont', 'F.SwiftColor'], 'F.SwiftAnimation': ['F.FrameClock']}
    for u in units:
        src = open(os.path.join(REF, u['swift']), encoding='utf-8', errors='replace').read()
        for f in fdoc['units']:
            if RANK[f['project']] > RANK[u['project']] or RANK[f['project']] == 1 and u['project'] != 'Marrow.App' and f['project'] != u['project']:
                continue
            if any(p in src for p in f['patterns']) or u['id'] in f.get('dependents', []):
                u['deps'] = sorted(set(u['deps']) | {f['id']})
    lv = {u['id']: u['level'] for u in units}
    fdone_path = os.path.join(ROOT, 'porting', 'deps', 'foundation_done.json')
    fdone = set(json.load(open(fdone_path, encoding='utf-8'))['done']) if os.path.exists(fdone_path) else set()
    for f in fdoc['units']:
        exists = os.path.exists(os.path.join(ROOT, f['target'].split(' ')[0]))
        units.insert(0, {
            'id': f['id'], 'kind': 'foundation', 'swift': None, 'target': f['target'], 'project': f['project'],
            'test_project': 'tests/Marrow.Core.Tests' if f['project'] in ('Marrow.Core', 'Marrow.Testing') else f'tests/{f["project"]}.Tests',
            'level': max([-1] + [lv[d] + 1 for d in f.get('deps', []) if d in lv]), 'component': None, 'component_members': [], 'deps': sorted(set(fdeps.get(f['id'], []) + f.get('deps', []))), 'intra_deps': [],
            'noMerge': f.get('noMerge', False), 'base': f.get('base'),
            'records_total': 0, 'records_todo': [], 'parts': [[]], 'packets': [], 'risk': f['risk'],
            'spec': f['spec'], 'extends_existing': exists, 'status': 'DONE' if f['id'] in fdone else 'TODO',
        })

os.makedirs(os.path.dirname(os.path.join(ROOT, out)), exist_ok=True)
json.dump({'baseline': deps['baseline'], 'part_size': PART_SIZE, 'units': units},
          open(os.path.join(ROOT, out), 'w', encoding='utf-8'), indent=1)
from collections import Counter
print(len(units), 'units', dict(Counter(u['status'] for u in units)),
      'parts', sum(len(u['parts']) for u in units), 'records todo', sum(len(u['records_todo']) for u in units))
print('no-record units:', [u['id'] for u in units if u['status'] == 'NO_RECORDS'])
