"""Set the status of named requirements and UI records in porting/spec/*.json.

    py porting/tools/set_status.py <STATUS> <id> [<id> ...]

Used by the orchestrated merge step after a unit has passed its reviews and the full solution builds
and tests green: the unit's records move to IMPLEMENTED (never to VERIFIED; that is derived by
porting/tools/req_verify.mjs from a bound test run). Clears the "rework" flag a rebaseline set.
Refuses unknown ids and refuses to move a RETIRED or NOT_APPLICABLE record. Rewrites only the
records it changes; the files keep their exact JSON formatting.
"""

import glob
import json
import sys

ALLOWED = {'SPECIFIED', 'IMPLEMENTED'}
status, ids = sys.argv[1], set(sys.argv[2:])
if status not in ALLOWED:
    sys.exit(f'status must be one of {sorted(ALLOWED)}; VERIFIED is derived by req_verify.mjs')
found = set()
for path in sorted(glob.glob('porting/spec/*.json')):
    doc = json.load(open(path, encoding='utf-8'))
    changed = False
    for key in ('requirements', 'ui'):
        for rec in doc.get(key, []):
            if rec.get('id') in ids:
                found.add(rec['id'])
                if rec.get('status') in ('RETIRED', 'NOT_APPLICABLE_WITH_EVIDENCE'):
                    sys.exit(f'{rec["id"]} is {rec["status"]}; refusing to mark it {status}')
                rec['status'] = status
                rec.pop('rework', None)
                changed = True
    if changed:
        with open(path, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')
missing = ids - found
if missing:
    sys.exit(f'unknown ids: {sorted(missing)[:10]}')
print(f'{len(found)} record(s) -> {status}')
