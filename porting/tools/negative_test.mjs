#!/usr/bin/env node
/**
 * Negative checks for the verification gate (PORT_CONTRACT section 14):
 * "Add negative checks proving the gate fails when evidence is missing, a requirement is
 *  unverified, or an intentional controlled regression is introduced. Restore the regression
 *  after confirming detection."
 *
 * Each case deliberately damages one piece of evidence, asserts that verify.mjs detects it by
 * name, then restores the exact original bytes and re-asserts the baseline. If any case is NOT
 * detected, this script exits nonzero: an undetected regression means the gate is worthless.
 *
 *   node porting/tools/negative_test.mjs
 */

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

const INV = 'porting/source_inventory.json';
const LEDGER = 'porting/read_ledger.jsonl';

const digest = (p) => createHash('sha256').update(readFileSync(p)).digest('hex');

const invBefore = readFileSync(INV);
const ledBefore = readFileSync(LEDGER);
const invDigest = digest(INV);
const ledDigest = digest(LEDGER);

function runVerify() {
  try {
    const out = execFileSync('node', ['porting/tools/verify.mjs', '--json', '--gate', 'A'], {
      encoding: 'utf8', maxBuffer: 1 << 26,
    });
    return { code: 0, doc: JSON.parse(out) };
  } catch (e) {
    // nonzero exit still prints the JSON report on stdout
    const out = e.stdout ? e.stdout.toString() : '';
    return { code: e.status ?? 1, doc: out ? JSON.parse(out) : null };
  }
}

/**
 * Restore with retry.
 *
 * Windows-specific hazard found while running this very script on 2026-09-19: writeFileSync
 * against porting/read_ledger.jsonl threw UNKNOWN/errno -4094 immediately after a child
 * `node verify.mjs` had read it, leaving the ledger holding an injected regression. On Windows a
 * file handle can still be briefly held after the owning process exits (and scanners open files
 * opportunistically), so a single write is not a reliable restore. Retry with backoff, verify the
 * bytes, and fail loudly rather than leaving damaged evidence behind.
 *
 * This is the same class of defect the port contract calls out for the product itself
 * (section 11: "file locks"), so it is recorded in the adaptation register too.
 */
function restore() {
  const attempts = 12;
  let lastError = null;
  for (let i = 0; i < attempts; i++) {
    try {
      writeFileSync(INV, invBefore);
      writeFileSync(LEDGER, ledBefore);
      if (digest(INV) === invDigest && digest(LEDGER) === ledDigest) return;
      lastError = new Error('bytes did not match after write');
    } catch (e) {
      lastError = e;
    }
    // Synchronous backoff: this must not yield to anything that could damage more evidence.
    const until = Date.now() + 100 * (i + 1);
    while (Date.now() < until) { /* spin */ }
  }
  throw new Error(
    `RESTORE FAILED after ${attempts} attempts: ${lastError && lastError.message}. ` +
    'Rebuild evidence with: node porting/tools/ledger_apply.mjs porting/source_inventory.json ' +
    'porting/read_ledger.jsonl porting/reports/*.json',
  );
}

const cases = [
  {
    name: 'an unread file is detected',
    expectCheck: 'every first-party file is marked READ',
    damage() {
      const inv = JSON.parse(invBefore.toString());
      inv.items[0].read_status = 'UNREAD';
      writeFileSync(INV, JSON.stringify(inv, null, 2) + '\n');
      return inv.items[0].path;
    },
  },
  {
    name: 'a tampered ledger digest is detected',
    expectCheck: 'ledger digests match the pinned inventory digests',
    damage() {
      const lines = ledBefore.toString().split('\n').filter(Boolean);
      const r = JSON.parse(lines[0]);
      r.sha256 = '0'.repeat(64);
      lines[0] = JSON.stringify(r);
      writeFileSync(LEDGER, lines.join('\n') + '\n');
      return r.path;
    },
  },
  {
    name: 'a duplicated ledger record is detected',
    expectCheck: 'read ledger has exactly one record per inventory path',
    damage() {
      const lines = ledBefore.toString().split('\n').filter(Boolean);
      lines.push(lines[0]);
      writeFileSync(LEDGER, lines.join('\n') + '\n');
      return JSON.parse(lines[0]).path;
    },
  },
  {
    name: 'partial line coverage is detected',
    expectCheck: 'text-file coverage reaches the true last line',
    damage() {
      const lines = ledBefore.toString().split('\n').filter(Boolean);
      const idx = lines.findIndex((l) => {
        const r = JSON.parse(l);
        return r.ranges_read && r.ranges_read.length && (r.line_count || 0) > 50;
      });
      const r = JSON.parse(lines[idx]);
      r.ranges_read = [[1, Math.floor(r.line_count / 2)]];
      lines[idx] = JSON.stringify(r);
      writeFileSync(LEDGER, lines.join('\n') + '\n');
      return `${r.path} (truncated to half)`;
    },
  },
  {
    name: 'a generic finding reused across files is detected',
    expectCheck: 'no generic finding reused across different files',
    damage() {
      const lines = ledBefore.toString().split('\n').filter(Boolean);
      const a = JSON.parse(lines[0]);
      const b = JSON.parse(lines[1]);
      b.purpose = a.purpose;
      lines[1] = JSON.stringify(b);
      writeFileSync(LEDGER, lines.join('\n') + '\n');
      return `${b.path} given ${a.path}'s purpose text`;
    },
  },
  {
    name: 'an inventory entry diverging from the reference is detected',
    expectCheck: 'every inventory sha256 matches the file on disk',
    damage() {
      const inv = JSON.parse(invBefore.toString());
      inv.items[5].sha256 = 'f'.repeat(64);
      writeFileSync(INV, JSON.stringify(inv, null, 2) + '\n');
      return inv.items[5].path;
    },
  },
];

// Baseline: the only Gate A failure right now must not be one of the checks we are testing.
const baseline = runVerify();
const baselineFailing = new Set(
  (baseline.doc?.results || []).filter((r) => r.status !== 'PASS').map((r) => r.name),
);

let undetected = 0;
console.log('Negative checks against porting/tools/verify.mjs (Gate A)\n');

for (const c of cases) {
  if (baselineFailing.has(c.expectCheck)) {
    console.log(`[SKIP] ${c.name} - "${c.expectCheck}" already fails at baseline, so detection proves nothing`);
    continue;
  }
  let target;
  try {
    target = c.damage();
    const { doc } = runVerify();
    const hit = (doc?.results || []).find((r) => r.name === c.expectCheck);
    const detected = hit && hit.status === 'FAIL';
    if (detected) {
      console.log(`[DETECTED] ${c.name}`);
      console.log(`           target: ${target}`);
      console.log(`           gate said: ${hit.detail}`);
    } else {
      undetected++;
      console.log(`[MISSED]   ${c.name}  <-- THE GATE DID NOT CATCH THIS`);
      console.log(`           target: ${target}`);
    }
  } finally {
    restore();
  }
}

const after = runVerify();
const afterFailing = new Set(
  (after.doc?.results || []).filter((r) => r.status !== 'PASS').map((r) => r.name),
);
const sameAsBaseline =
  afterFailing.size === baselineFailing.size &&
  [...afterFailing].every((n) => baselineFailing.has(n));

console.log('');
console.log(`evidence restored byte-for-byte: ${digest(INV) === invDigest && digest(LEDGER) === ledDigest}`);
console.log(`gate state matches baseline after restore: ${sameAsBaseline}`);
console.log(undetected === 0 ? 'RESULT: every injected regression was detected' : `RESULT: ${undetected} regression(s) went undetected`);

process.exit(undetected === 0 && sameAsBaseline ? 0 : 1);
