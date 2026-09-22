#!/usr/bin/env node
/**
 * Measured progress counts for the owner's status updates.
 *
 *   node porting/tools/progress.mjs [--json]
 *
 * Every number here is a literal count read from the repository at the moment it runs: reading
 * evidence, requirement statuses, packet statuses, Swift files with a C# counterpart, and the
 * verification record. None is an estimate of remaining effort. Contract section 14 forbids an
 * invented overall completion percentage; a ratio of counted items is not one, and each line says
 * exactly what it counts.
 */

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const J = (p) => JSON.parse(readFileSync(p, 'utf8'));
const pct = (a, b) => (b ? `${((100 * a) / b).toFixed(1)}%` : 'n/a');

const inv = J('porting/source_inventory.json');
const read = inv.items.filter((i) => i.read_status === 'READ').length;

const matrix = J('porting/parity_matrix.json');
const ui = J('porting/ui_contract.json');
const blocked = new Set(J('porting/blocked_requirements.json').entries.filter((e) => e.status === 'OPEN').map((e) => e.id));
const all = [...matrix.requirements, ...(ui.records || [])];
const inScope = all.filter((r) => !['RETIRED', 'NOT_APPLICABLE_WITH_EVIDENCE'].includes(r.status));
const implemented = inScope.filter((r) => ['IMPLEMENTED', 'VERIFIED'].includes(r.status)).length;

let verified = 0, applicable = inScope.length, vrun = 'no verification run yet';
if (existsSync('porting/verification/requirements.json')) {
  const v = J('porting/verification/requirements.json');
  verified = v.verified;
  applicable = v.applicable;
  vrun = `run at ${String(v.run.head).slice(0, 8)}${v.run.bound_to_current_head ? '' : ' (unbound)'}`;
}

const q = J('porting/tasks/queue.json');
const done = q.tasks.filter((t) => t.status === 'DONE').length;

let swift = 0, ported = 0;
if (existsSync('porting/deps/swift_deps.json')) {
  const d = J('porting/deps/swift_deps.json');
  for (const f of Object.keys(d.files)) {
    const m = f.match(/^DroppyCode\/(Core|Services|UI|App)\/(.+)\.swift$/);
    if (!m) continue;
    swift++;
    const proj = { Core: 'Marrow.Core', Services: 'Marrow.Services', UI: 'Marrow.UI', App: 'Marrow.App' }[m[1]];
    const target = join('src', proj, `${m[2].replace(/\+/g, '.')}.cs`);
    if (existsSync(target) && !/MARROW_STUB|NotImplementedException/.test(readFileSync(target, 'utf8'))) ported++;
  }
}

let tests = 0;
const walk = (dir) => {
  for (const n of readdirSync(dir)) {
    if (n === 'bin' || n === 'obj') continue;
    const p = join(dir, n);
    if (statSync(p).isDirectory()) walk(p);
    else if (n.endsWith('.cs')) tests += (readFileSync(p, 'utf8').match(/\[(Fact|Theory|AvaloniaFact|AvaloniaTheory)\b/g) || []).length;
  }
};
if (existsSync('tests')) walk('tests');

const out = {
  source_files_read: `${read}/${inv.items.length} (${pct(read, inv.items.length)})`,
  requirements_verified: `${verified}/${applicable} (${pct(verified, applicable)}) - ${vrun}`,
  requirements_implemented: `${implemented}/${inScope.length} (${pct(implemented, inScope.length)})`,
  packets_done: `${done}/${q.tasks.length} (${pct(done, q.tasks.length)})`,
  swift_files_with_real_csharp: `${swift ? `${ported}/${swift} (${pct(ported, swift)})` : 'n/a'}`,
  test_methods: tests,
  open_quarantine: blocked.size,
};
if (process.argv.includes('--json')) console.log(JSON.stringify(out, null, 2));
else for (const [k, v] of Object.entries(out)) console.log(`${k.padEnd(30)} ${v}`);
