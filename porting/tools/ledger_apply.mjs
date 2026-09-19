#!/usr/bin/env node
// Applies evidence-bearing reader reports to the read ledger and the inventory.
//
// This is the ONLY code permitted to advance read_status. It refuses a report that
// does not carry real evidence, so that "read" in the inventory means a reader
// actually saw the bytes of the pinned file and said something specific about them.
//
// Checks enforced (contract section 5):
//  1. The path exists in the inventory at the pinned SHA.
//  2. The report's sha256 matches the inventory's sha256 for that path. A reader that
//     read a different revision, or guessed, fails here.
//  3. Reported ranges cover [1..line_count] with no gap. Partial coverage stays UNREAD.
//     Coverage is reconciled against the ACTUAL line count, not the reader's claim.
//  4. The report carries substantive, file-specific findings: a purpose, at least one
//     key symbol, and no findings text duplicated verbatim across different files
//     (catches a reader emitting one generic summary for a whole bundle).
//  5. Binary assets take an asset_inspection record instead of ranges.
//
// Usage: ledger_apply.mjs <inventory.json> <ledger.jsonl> <report.json> [...more reports]

import { readFileSync, writeFileSync, existsSync } from 'node:fs';

const [invPath, ledgerPath, ...reportPaths] = process.argv.slice(2);
if (!invPath || !ledgerPath || !reportPaths.length) {
  console.error('usage: ledger_apply.mjs <inventory.json> <ledger.jsonl> <report.json>...');
  process.exit(2);
}

const inv = JSON.parse(readFileSync(invPath, 'utf8'));
const byPath = new Map(inv.items.map((i) => [i.path, i]));

// Build a duplicate-detector over findings text already in the ledger.
// The ledger is keyed by path and rewritten whole, so applying the same report twice
// replaces that path's record instead of appending a duplicate. Idempotency added
// 2026-09-19 after re-running the tool on overlapping report batches produced 744
// duplicate lines for 433 files.
const purposeSeen = new Map(); // normalized purpose -> first path that used it
const ledger = new Map(); // path -> record
if (existsSync(ledgerPath)) {
  for (const line of readFileSync(ledgerPath, 'utf8').split('\n')) {
    if (!line.trim()) continue;
    try {
      const rec = JSON.parse(line);
      ledger.set(rec.path, rec);
      if (rec.purpose) purposeSeen.set(norm(rec.purpose), rec.path);
    } catch { /* tolerate a partially written line */ }
  }
}

function norm(s) {
  return String(s).toLowerCase().replace(/\s+/g, ' ').trim();
}

function coversFully(ranges, total) {
  if (total === 0) return true;
  const sorted = [...ranges].map(([a, b]) => [Number(a), Number(b)]).sort((x, y) => x[0] - y[0]);
  let reached = 0;
  for (const [a, b] of sorted) {
    if (a > reached + 1) return false; // gap
    reached = Math.max(reached, b);
  }
  return reached >= total;
}

const accepted = [];
const rejected = [];

for (const rp of reportPaths) {
  const report = JSON.parse(readFileSync(rp, 'utf8'));
  const reader = report.reader || report.bundle_id || rp;
  for (const f of report.files || []) {
    const item = byPath.get(f.path);
    const fail = (reason) => rejected.push({ path: f.path, reader, reason });

    if (!item) { fail('path not in inventory at pinned SHA'); continue; }
    if (!f.sha256 || f.sha256 !== item.sha256) {
      fail(`sha256 mismatch: report ${f.sha256 || '(none)'} vs inventory ${item.sha256}`);
      continue;
    }

    const isBinary = item.kind === 'binary';
    if (isBinary) {
      if (!f.asset_inspection || norm(f.asset_inspection).length < 20) {
        fail('binary asset requires a substantive asset_inspection record');
        continue;
      }
    } else {
      const ranges = f.ranges_read || [];
      // An empty file has no lines to cover, so it legitimately has no ranges.
      // Tool fix 2026-09-19: the original rule rejected the one 0-byte tracked file
      // (.commandcode/taste/taste.md). This is not a weakening of the standard: a file
      // with zero lines is fully covered by definition, and full coverage is still
      // enforced for every file that has any content.
      const isEmpty = (item.line_count || 0) === 0 && item.size_bytes === 0;
      if (!ranges.length && !isEmpty) { fail('no ranges_read reported'); continue; }
      if (!coversFully(ranges, item.line_count || 0)) {
        fail(`coverage gap: ranges ${JSON.stringify(ranges)} do not cover 1..${item.line_count}`);
        continue;
      }
      if (f.lines_total != null && Number(f.lines_total) !== item.line_count) {
        fail(`reader reported ${f.lines_total} lines, actual is ${item.line_count}`);
        continue;
      }
    }

    if (!f.purpose || norm(f.purpose).length < 25) {
      fail('purpose missing or too thin to count as a finding');
      continue;
    }
    const prior = purposeSeen.get(norm(f.purpose));
    if (prior && prior !== f.path) {
      fail(`purpose text duplicated verbatim from ${prior}; generic summaries are not findings`);
      continue;
    }
    // Symbols are required for code, where their absence means the reader did not engage
    // with the file's contents. Data, config, markup and prose files legitimately declare
    // no symbols; for those the substantive-purpose check above plus at least one other
    // evidence field is the bar. Tool fix 2026-09-19: the original blanket rule rejected
    // .gitignore, which cannot have symbols. The standard is not lowered for code.
    const CODE_EXT = new Set(['swift', 'h', 'm', 'metal', 'js', 'mjs', 'py', 'sh']);
    if (!isBinary && CODE_EXT.has(item.ext) && !(f.key_symbols || []).length) {
      fail('no key_symbols reported for a code file');
      continue;
    }
    if (!isBinary && !CODE_EXT.has(item.ext) && !(f.key_symbols || []).length) {
      const otherEvidence = [
        f.calls_into, f.state_owned, f.side_effects, f.platform_assumptions,
        f.persisted_fields, f.ui_elements, f.feature_notes,
      ].some((a) => (a || []).length);
      if (!otherEvidence) {
        fail('no symbols and no other evidence field populated for a non-code file');
        continue;
      }
    }

    purposeSeen.set(norm(f.purpose), f.path);
    const rec = {
      path: f.path,
      sha256: f.sha256,
      line_count: item.line_count,
      classification: item.classification,
      build_inclusion: item.build_inclusion,
      reader,
      bundle_id: report.bundle_id || null,
      ranges_read: f.ranges_read || null,
      asset_inspection: f.asset_inspection || null,
      purpose: f.purpose,
      key_symbols: f.key_symbols || [],
      calls_into: f.calls_into || [],
      state_owned: f.state_owned || [],
      side_effects: f.side_effects || [],
      platform_assumptions: f.platform_assumptions || [],
      persisted_fields: f.persisted_fields || [],
      ui_elements: f.ui_elements || [],
      feature_notes: f.feature_notes || [],
      unresolved_questions: f.unresolved_questions || [],
    };
    ledger.set(f.path, rec);
    item.read_status = 'READ';
    item.ranges_read = f.ranges_read || null;
    item.reader = reader;
    item.asset_inspection = f.asset_inspection || null;
    item.findings_ref = `${ledgerPath}#${f.path}`;
    accepted.push(f.path);
  }
}

const ledgerOut = [...ledger.entries()]
  .sort((a, b) => (a[0] < b[0] ? -1 : 1))
  .map(([, rec]) => JSON.stringify(rec));
writeFileSync(ledgerPath, ledgerOut.join('\n') + '\n');
writeFileSync(invPath, JSON.stringify(inv, null, 2) + '\n');

const total = inv.items.length;
const read = inv.items.filter((i) => i.read_status === 'READ').length;
console.log(`accepted ${accepted.length}, rejected ${rejected.length}`);
if (rejected.length) {
  console.log('\nREJECTED (these files stay UNREAD):');
  for (const r of rejected) console.log(`  ${r.path} [${r.reader}]: ${r.reason}`);
}
console.log(`\nread ${read}/${total} files`);
process.exit(rejected.length ? 1 : 0);
