"""Build the input for one implementation wave of the impl-units workflow.

    py porting/tools/build_impl_args.py --max-level N [--out E:/Droppy/work/impl/wave.json]

Takes porting/deps/units.json (from plan_units.py), keeps every unit that still has work (status TODO)
at dependency level <= N, attaches the independent-verification findings recorded for units that
already had code (E:/Droppy/work/takeover/rework_findings.json), and refuses to build a wave that
contains a unit with no records, because such a unit cannot be proven by any test.
Units at higher levels are left for later waves; by construction nothing in a wave depends on them.
"""

import json
import os
import sys

ROOT = os.getcwd()
lvl = int(sys.argv[sys.argv.index('--max-level') + 1])
out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else 'E:/Droppy/work/impl/wave.json'
plan = json.load(open(os.path.join(ROOT, 'porting', 'deps', 'units.json'), encoding='utf-8'))
findings_path = 'E:/Droppy/work/takeover/rework_findings.json'
findings = json.load(open(findings_path, encoding='utf-8')) if os.path.exists(findings_path) else {}

units = []
empty = []
for u in plan['units']:
    if u['level'] > lvl or u['status'] != 'TODO':
        continue
    if not u['parts']:
        empty.append(u['id'])
        continue
    u = dict(u)
    if u['id'] in findings:
        u['findings'] = [{k: f.get(k) for k in ('severity', 'kind', 'requirement', 'problem', 'swift', 'csharp', 'repro')}
                         for f in findings[u['id']]]
    units.append(u)
if empty:
    sys.exit(f'units with work but no records (specify them first): {empty}')
# A unit whose records are all NOT_APPLICABLE (dead code, or not ported by a recorded decision) has
# nothing to build; its dependents follow the not_applicable evidence instead of waiting for it.
done_ids = {u['id'] for u in plan['units'] if u['status'] in ('DONE', 'NO_RECORDS')}
wave_ids = {u['id'] for u in units}
for u in units:
    missing = [d for d in u['deps'] + u.get('intra_deps', []) if d not in wave_ids and d not in done_ids]
    if missing:
        sys.exit(f'{u["id"]} depends on {missing}, which are neither done nor in this wave')
os.makedirs(os.path.dirname(out), exist_ok=True)
json.dump({'units': units, 'integration': 'E:/Droppy/wt/integration', 'wtRoot': 'E:/Droppy/wt/u', 'maxRounds': 3},
          open(out, 'w', encoding='utf-8'), indent=1)
print(f'wave <= level {lvl}: {len(units)} units, {sum(len(u["parts"]) for u in units)} parts, '
      f'{sum(len(u["records_todo"]) for u in units)} records, {sum(1 for u in units if u.get("findings"))} with rework findings')
