#!/usr/bin/env node
/**
 * Assembles porting/spec/<AREA>.json into porting/parity_matrix.json and
 * porting/ui_contract.json, refusing anything malformed.
 *
 * Like ledger_apply.mjs, this is a gate, not a copier. A requirement without source evidence,
 * without a verification method, or with a duplicate id does not enter the matrix. The point is
 * that "SPECIFIED" means a record a Windows implementation can actually be built and judged
 * against, not a row that exists.
 *
 *   node porting/tools/merge_spec.mjs [--strict]
 *
 * Exits nonzero if any record is rejected.
 */

import { readFileSync, writeFileSync, readdirSync, existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { PINNED_SHA } from './baseline.mjs';

const SPEC_DIR = 'porting/spec';
const OUT_MATRIX = 'porting/parity_matrix.json';
const OUT_UI = 'porting/ui_contract.json';

if (!existsSync(SPEC_DIR)) {
  console.error(`no ${SPEC_DIR}; the specification pass has not produced output`);
  process.exit(2);
}

const VALID_STATUS = new Set([
  'DISCOVERED', 'SPECIFIED', 'IMPLEMENTED', 'VERIFIED', 'FAILED', 'BLOCKED',
  'NOT_APPLICABLE_WITH_EVIDENCE',
  // A behaviour the pinned source no longer has after a rebaseline. Kept, never deleted, so its id
  // is never reused; it must say why and cite the new baseline (porting/tools/rebaseline.py, AM-0002).
  'RETIRED',
]);

const blocked = existsSync('porting/blocked_requirements.json')
  ? JSON.parse(readFileSync('porting/blocked_requirements.json', 'utf8'))
  : { entries: [] };
const openBlocks = new Set(blocked.entries.filter((e) => e.status === 'OPEN').map((e) => e.id));

const requirements = [];
const uiRecords = [];
const rejected = [];
const seenReqIds = new Set();
const seenUiIds = new Set();
const areas = [];

const files = readdirSync(SPEC_DIR).filter((f) => f.endsWith('.json')).sort();
for (const f of files) {
  let doc;
  try {
    doc = JSON.parse(readFileSync(join(SPEC_DIR, f), 'utf8'));
  } catch (e) {
    rejected.push({ file: f, id: '(whole file)', reason: `unparseable: ${e.message}` });
    continue;
  }
  const area = doc.area || f.replace(/\.json$/, '');
  areas.push(area);

  for (const r of doc.requirements || []) {
    const bad = (reason) => rejected.push({ file: f, id: r.id || '(no id)', reason });
    if (!r.id || !/^PR-[A-Z]+-\d{3,}$/.test(r.id)) { bad(`malformed id "${r.id}"`); continue; }
    if (seenReqIds.has(r.id)) { bad('duplicate requirement id'); continue; }
    if (!Array.isArray(r.source_evidence) || !r.source_evidence.length) {
      bad('no source_evidence; a requirement without evidence is not a requirement'); continue;
    }
    if (!r.source_evidence.some((e) => /:\d/.test(String(e)))) {
      bad('source_evidence cites no file:line'); continue;
    }
    if (!r.action || !String(r.action).trim()) { bad('no action'); continue; }
    if (!r.expected_result || String(r.expected_result).trim().length < 15) {
      bad('expected_result missing or too thin to test against'); continue;
    }
    if (!r.verification_method || !String(r.verification_method).trim()) {
      bad('no verification_method; contract section 6 requires one per requirement'); continue;
    }
    const status = r.status || 'DISCOVERED';
    if (!VALID_STATUS.has(status)) { bad(`invalid status "${status}"`); continue; }
    if (status === 'RETIRED' && !(r.retired && r.retired.reason && r.retired.evidence)) {
      bad('RETIRED needs retired.reason and retired.evidence citing the new baseline'); continue;
    }
    if (r.blocked_by) {
      if (!openBlocks.has(r.blocked_by) && !blocked.entries.some((e) => e.id === r.blocked_by)) {
        bad(`blocked_by "${r.blocked_by}" is not in blocked_requirements.json`); continue;
      }
      if (openBlocks.has(r.blocked_by) && status !== 'DISCOVERED' && status !== 'BLOCKED') {
        bad(`blocked by open ${r.blocked_by} but status is ${status}; a quarantined behavior cannot be SPECIFIED for implementation`);
        continue;
      }
    }
    seenReqIds.add(r.id);
    requirements.push({ area, ...r, status });
  }

  for (const u of doc.ui || []) {
    const bad = (reason) => rejected.push({ file: f, id: u.id || '(no id)', reason });
    if (!u.id || !/^UI-[A-Z]+-\d{3,}$/.test(u.id)) { bad(`malformed ui id "${u.id}"`); continue; }
    if (seenUiIds.has(u.id)) { bad('duplicate ui id'); continue; }
    if (!Array.isArray(u.source_evidence) || !u.source_evidence.length) {
      bad('no source_evidence'); continue;
    }
    if (!u.screen || !u.element) { bad('ui record needs screen and element'); continue; }
    if (u.status && !VALID_STATUS.has(u.status)) { bad(`invalid ui status "${u.status}"`); continue; }
    if (u.status === 'RETIRED' && !(u.retired && u.retired.reason && u.retired.evidence)) {
      bad('RETIRED needs retired.reason and retired.evidence citing the new baseline'); continue;
    }
    seenUiIds.add(u.id);
    uiRecords.push({ area, ...u });
  }
}

const byStatus = requirements.reduce((a, r) => { a[r.status] = (a[r.status] || 0) + 1; return a; }, {});
const byArea = requirements.reduce((a, r) => { a[r.area] = (a[r.area] || 0) + 1; return a; }, {});

mkdirSync('porting', { recursive: true });
writeFileSync(OUT_MATRIX, JSON.stringify({
  schema: 'droppy-windows-port/parity_matrix/1',
  baseline: { commit_sha: PINNED_SHA },
  note: 'Every requirement carries file:line evidence from the pinned source and a verification method. Status SPECIFIED means specified, not implemented and not verified.',
  counts: { total: requirements.length, by_status: byStatus, by_area: byArea, areas: areas.length },
  requirements,
}, null, 2) + '\n');

writeFileSync(OUT_UI, JSON.stringify({
  schema: 'droppy-windows-port/ui_contract/1',
  baseline: { commit_sha: PINNED_SHA },
  note: 'Source-defined geometry, typography, colors and interactions. Values delegated to macOS rendering are marked and remain UNVERIFIED per UNK-0003.',
  counts: {
    total: uiRecords.length,
    by_area: uiRecords.reduce((a, u) => { a[u.area] = (a[u.area] || 0) + 1; return a; }, {}),
  },
  records: uiRecords,
}, null, 2) + '\n');

console.log(`areas merged        ${areas.length}`);
console.log(`requirements        ${requirements.length}`);
console.log(`ui records          ${uiRecords.length}`);
console.log(`rejected            ${rejected.length}`);
console.log('');
console.log('by status: ' + JSON.stringify(byStatus));
console.log('');
console.log('by area:');
for (const [a, n] of Object.entries(byArea).sort((x, y) => y[1] - x[1])) {
  console.log(`  ${a.padEnd(12)} ${n}`);
}
if (rejected.length) {
  console.log('');
  console.log('REJECTED (these did not enter the matrix):');
  for (const r of rejected.slice(0, 40)) console.log(`  [${r.file}] ${r.id}: ${r.reason}`);
  if (rejected.length > 40) console.log(`  ... and ${rejected.length - 40} more`);
}
process.exit(rejected.length ? 1 : 0);
