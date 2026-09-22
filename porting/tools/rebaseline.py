"""Move the port's reading record from one pinned upstream commit to another (owner decision OD-0006).

    py porting/tools/rebaseline.py <new-reference-checkout> <raw-inventory-from-gen_inventory.json>

What it does, and why each step is safe:

1. Archives the current inventory and ledger under porting/baselines/<old-id>/ so the evidence that
   Gate A accepted for the old commit is kept, not overwritten.
2. Takes the raw inventory that gen_inventory.mjs produced for the new commit (every file UNREAD).
3. Carries reading evidence across ONLY for files whose bytes are identical in both commits: same
   sha256 of the working-tree bytes AND same git blob id. A reader who read those bytes has read
   the new file, because it is the same file. Every other file stays UNREAD.
4. Keeps ledger records only for carried-over paths. Records for changed or removed files are
   dropped from the live ledger (they remain in the archive), so ledger_apply.mjs must accept a
   fresh evidence-bearing report before any changed file counts as read again.
5. Writes porting/baseline.json, the single source of truth the verification tools now read.

It never marks a changed file READ. It is the rebaseline counterpart of gen_inventory.mjs, and like
it, it records mechanical facts only.
"""

import json
import os
import shutil
import subprocess
import sys
from datetime import datetime, timezone

ROOT = os.getcwd()
P = lambda *a: os.path.join(ROOT, *a)

new_ref, raw_inv_path = sys.argv[1], sys.argv[2]
git = lambda *a: subprocess.check_output(['git', '-C', new_ref, *a], text=True).strip()

old_inv = json.load(open(P('porting', 'source_inventory.json'), encoding='utf-8'))
raw = json.load(open(raw_inv_path, encoding='utf-8'))
old_base = old_inv['baseline']

new_sha, new_tree = git('rev-parse', 'HEAD'), git('rev-parse', 'HEAD^{tree}')
if raw['baseline']['commit_sha'] != new_sha:
    sys.exit(f'raw inventory is for {raw["baseline"]["commit_sha"]}, checkout is at {new_sha}')
if git('status', '--porcelain'):
    sys.exit('new reference checkout is dirty; refusing')

# 1. Archive.
bl_path = P('porting', 'baseline.json')
if os.path.exists(bl_path):
    baseline = json.load(open(bl_path, encoding='utf-8'))
else:
    baseline = {'current': {'id': 'B1', 'commit_sha': old_base['commit_sha'], 'tree_sha': old_base['tree_sha'],
                            'reference_path': old_base['reference_path'], 'repository': old_base['repository'],
                            'version': '1.6.0', 'build': '19', 'frozen': '2026-09-19',
                            'authority': 'PORT_CONTRACT section 4; porting/baseline.md'},
                'history': []}
old_id = baseline['current']['id']
arch = P('porting', 'baselines', old_id)
os.makedirs(arch, exist_ok=True)
shutil.copy2(P('porting', 'source_inventory.json'), os.path.join(arch, 'source_inventory.json'))
shutil.copy2(P('porting', 'read_ledger.jsonl'), os.path.join(arch, 'read_ledger.jsonl'))

# 2-3. Carry over evidence for byte-identical files only.
old_items = {i['path']: i for i in old_inv['items']}
CARRY = ['read_status', 'ranges_read', 'reader', 'findings_ref', 'asset_inspection', 'exclusion_reason']
carried, reread, added = [], [], []
for item in raw['items']:
    prev = old_items.get(item['path'])
    if prev and prev['sha256'] == item['sha256'] and prev['git_blob'] == item['git_blob']:
        for k in CARRY:
            item[k] = prev.get(k)
        carried.append(item['path'])
    elif prev:
        reread.append(item['path'])
    else:
        added.append(item['path'])
removed = sorted(set(old_items) - {i['path'] for i in raw['items']})

raw['note'] = raw.get('note', '') + (
    f' Rebaselined from {old_base["commit_sha"]} by porting/tools/rebaseline.py: reading evidence carried'
    f' over for {len(carried)} byte-identical files; {len(reread) + len(added)} changed or added files are UNREAD'
    ' until ledger_apply.mjs accepts a fresh reader report.')
json.dump(raw, open(P('porting', 'source_inventory.json'), 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
open(P('porting', 'source_inventory.json'), 'a', encoding='utf-8').write('\n')

# 4. Ledger: keep only carried-over records.
keep = set(carried)
kept = []
for line in open(os.path.join(arch, 'read_ledger.jsonl'), encoding='utf-8'):
    if line.strip() and json.loads(line)['path'] in keep:
        kept.append(line.rstrip('\n'))
open(P('porting', 'read_ledger.jsonl'), 'w', encoding='utf-8', newline='\n').write('\n'.join(kept) + '\n')

# 5. Baseline record.
stamp = datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
prev_current = dict(baseline['current'], superseded=stamp,
                    archive=f'porting/baselines/{old_id}/')
yml = subprocess.check_output(['git', '-C', new_ref, 'show', 'HEAD:project.yml'], text=True)
ver = next((l.split(':', 1)[1].strip().strip('"') for l in yml.splitlines() if 'MARKETING_VERSION' in l), '?')
bld = next((l.split(':', 1)[1].strip().strip('"') for l in yml.splitlines() if 'CURRENT_PROJECT_VERSION' in l), '?')
baseline = {
    'current': {
        'id': 'B%d' % (int(old_id[1:]) + 1),
        'repository': old_base['repository'],
        'commit_sha': new_sha,
        'tree_sha': new_tree,
        'reference_path': new_ref.replace('\\', '/'),
        'version': ver,
        'build': bld,
        'frozen': stamp,
        'authority': 'OD-0006 and AM-0002 (porting/owner_decisions.md, porting/contract_amendments.md)',
    },
    'history': baseline.get('history', []) + [prev_current],
    'rebaseline': {
        'from': old_base['commit_sha'], 'to': new_sha, 'at': stamp,
        'carried_over': len(carried), 'changed_unread': reread, 'added_unread': added, 'removed': removed,
    },
}
json.dump(baseline, open(bl_path, 'w', encoding='utf-8'), indent=2)
open(bl_path, 'a', encoding='utf-8').write('\n')

print(f'{old_id} -> {baseline["current"]["id"]}: {new_sha} (v{ver} build {bld})')
print(f'carried over {len(carried)}, changed {len(reread)}, added {len(added)}, removed {len(removed)}')
