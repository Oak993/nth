"""Send already-implemented records back for rework when independent verification found defects.

    py porting/tools/flag_rework.py <findings.json>

<findings.json> maps unit ids (plan_units.py naming) to verification findings. Every record whose
primary source is that unit's Swift file and whose status is IMPLEMENTED or VERIFIED is set back to
SPECIFIED with "rework": true, so the implementation workflow takes the unit again with the findings
attached. Contract section 13: "Update the parity matrix honestly" - code with known defects against
its records is not IMPLEMENTED.
"""

import glob
import json
import re
import sys

findings = json.load(open(sys.argv[1], encoding='utf-8'))
SWIFT = re.compile(r'DroppyCode/[A-Za-z0-9_+/.-]+\.swift')
to_file = lambda uid: 'DroppyCode/' + uid.replace('_', '+').replace('.', '/') + '.swift'
files = {to_file(u) for u in findings}
moved = 0
for path in sorted(glob.glob('porting/spec/*.json')):
    doc = json.load(open(path, encoding='utf-8'))
    changed = False
    for key in ('requirements', 'ui'):
        for rec in doc.get(key, []):
            prim = next((m.group(0) for e in rec.get('source_evidence') or [] for m in [SWIFT.search(str(e))] if m), None)
            if prim in files and rec.get('status') in ('IMPLEMENTED', 'VERIFIED'):
                rec['status'] = 'SPECIFIED'
                rec['rework'] = True
                moved += 1
                changed = True
    if changed:
        with open(path, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')
print(f'{moved} records sent back for rework across {len(files)} files')
