#!/usr/bin/env node
/**
 * ORCHESTRATOR ONLY. Records the outcome of a reviewed task packet.
 *
 *   node porting/tools/mark.mjs <T-id> <status> [--by <model>] [--note "<text>"]
 *
 * <status> is one of: ASSIGNED, TESTS_REVIEW, IMPLEMENTING, REVIEW, DONE, REJECTED, TODO
 *
 * On DONE, every requirement the packet covers moves to IMPLEMENTED in porting/spec/*.json (never
 * to VERIFIED: that needs Gate D evidence per requirement, which a single packet review does not
 * give), then porting/parity_matrix.json is regenerated. Quarantined requirements are never moved.
 * On REJECTED the requirements are left exactly as they were.
 *
 * PORT_CONTRACT section 13, step 6: "Update the parity matrix honestly and commit the coherent change
 * locally." This tool is the honest part. The commit is yours.
 */

import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { packetBuildFloorPasses } from './packet_acceptance.mjs';

const [id, status, ...rest] = process.argv.slice(2);
const VALID = ['TODO', 'ASSIGNED', 'TESTS_REVIEW', 'IMPLEMENTING', 'REVIEW', 'DONE', 'REJECTED'];
if (!id || !VALID.includes(status)) {
  console.error(`usage: mark.mjs <T-id> <${VALID.join('|')}> [--by <model>] [--note "<text>"]`);
  process.exit(2);
}
const opt = (flag) => { const i = rest.indexOf(flag); return i >= 0 ? rest[i + 1] : undefined; };

const qPath = 'porting/tasks/queue.json';
const queue = JSON.parse(readFileSync(qPath, 'utf8'));
const task = queue.tasks.find((t) => t.id === id);
if (!task) { console.error(`no task ${id} in the queue`); process.exit(2); }

if (status === 'DONE') {
  // The gate decides, not the orchestrator's impression. Refuse DONE unless the floor holds.
  const run = (args) => { try { execFileSync('node', args, { stdio: 'pipe' }); return true; } catch { return false; } };
  const checks = [
    ['protected files unchanged', ['porting/tools/check_protected.mjs']],
    ['Gate A', ['porting/tools/verify.mjs', '--gate', 'A']],
    ['Gate B', ['porting/tools/verify.mjs', '--gate', 'B']],
  ];
  const failed = checks.filter(([, args]) => !run(args)).map(([name]) => name);
  const buildReport = spawnSync(process.execPath, ['porting/tools/verify.mjs', '--gate', 'D', '--json'], {
    encoding: 'utf8', maxBuffer: 1 << 26,
  });
  if (!packetBuildFloorPasses(buildReport)) failed.push('Gate D build and tests');
  if (failed.length) {
    console.error(`REFUSED: ${id} cannot be DONE while these fail: ${failed.join(', ')}`);
    console.error('Run each by hand to see why, fix or reject the work, then try again.');
    process.exit(1);
  }
  if (buildReport.status !== 0) {
    console.log('Packet build/test floor passed; full Gate D acceptance remains NOT_IMPLEMENTED. DONE is not VERIFIED.');
  }
}

task.status = status;
if (opt('--by')) task.assignee = opt('--by');
if (opt('--note')) task.notes = [task.notes, `${new Date().toISOString().slice(0, 10)} ${status}: ${opt('--note')}`].filter(Boolean).join(' | ');
writeFileSync(qPath, JSON.stringify(queue, null, 2) + '\n');

let moved = 0;
if (status === 'DONE' && task.requirements.length) {
  const want = new Set(task.requirements.filter((r) => !task.quarantined.includes(r)));
  for (const f of readdirSync('porting/spec').filter((x) => x.endsWith('.json'))) {
    const p = `porting/spec/${f}`;
    const doc = JSON.parse(readFileSync(p, 'utf8'));
    let changed = false;
    for (const r of doc.requirements || []) {
      if (want.has(r.id) && !r.blocked_by && r.status !== 'VERIFIED') {
        r.status = 'IMPLEMENTED';
        r.implemented_by_task = id;
        moved++;
        changed = true;
      }
    }
    if (changed) writeFileSync(p, JSON.stringify(doc, null, 2) + '\n');
  }
  execFileSync('node', ['porting/tools/merge_spec.mjs'], { stdio: 'pipe' });
}

const counts = queue.tasks.reduce((a, t) => { a[t.status] = (a[t.status] || 0) + 1; return a; }, {});
console.log(`${id} -> ${status}${moved ? `; ${moved} requirement(s) now IMPLEMENTED` : ''}`);
console.log(`queue: ${Object.entries(counts).map(([k, v]) => `${k} ${v}`).join(', ')}`);
