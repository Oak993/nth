// Re-cites Swift line references in porting/spec/*.json from B2 to B3 using the exact git diff line map (AM-0003).
// Mechanical facts only: a citation is remapped when both endpoints fall on unchanged lines; it is FLAGGED (and left
// as it is) when an endpoint sits on a changed or deleted line, and flagged (but remapped) when code inside the cited
// range changed. Deleted files are flagged. Flags go to E:/Droppy/work/b3/recite_flags.json for the spec agents.
//   node recite.mjs <integration worktree> [--write]
import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const WT = process.argv[2];
const WRITE = process.argv.includes('--write');
const REF = 'E:/Droppy/reference/droppy-code-v1.7.0';
const B2 = '1fbf81e61e28a079508a074c01ee1830a9cc23dc';
const git = (c) => execSync(`git -C "${REF}" ${c}`, { encoding: 'utf8', maxBuffer: 1 << 28 });

// Changed and deleted Swift files, and each one's hunks.
const status = {};
for (const line of git(`diff --name-status ${B2} HEAD`).trim().split('\n')) {
  const parts = line.split('\t');
  const p = parts[parts.length - 1];
  if (p.endsWith('.swift')) status[p] = parts[0][0];
}
const hunks = {};
for (const [p, st] of Object.entries(status)) {
  if (st !== 'M') continue;
  const hs = [];
  for (const m of git(`diff -U0 ${B2} HEAD -- "${p}"`).matchAll(/^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/gm)) {
    hs.push({ os: +m[1], ol: m[2] === undefined ? 1 : +m[2], ns: +m[3], nl: m[4] === undefined ? 1 : +m[4] });
  }
  hunks[p] = hs;
}

// B2 line -> B3 line, or null when the line itself changed or was deleted.
function mapLine(p, n) {
  let delta = 0;
  for (const h of hunks[p]) {
    if (h.ol === 0) { if (n > h.os) { delta += h.nl; continue; } break; }
    if (n < h.os) break;
    if (n <= h.os + h.ol - 1) return null;
    delta += h.nl - h.ol;
  }
  return n + delta;
}
function changedInside(p, a, b) {
  return hunks[p].some((h) => (h.ol > 0 ? h.os <= b && h.os + h.ol - 1 >= a : h.os >= a && h.os < b));
}

// Resolve a cited file: full path, path without DroppyCode/, or a bare file name unique among the changed files.
const byBase = {};
for (const p of Object.keys(status)) (byBase[path.posix.basename(p)] ||= []).push(p);
function resolve(cited) {
  if (status[cited]) return cited;
  if (status['DroppyCode/' + cited]) return 'DroppyCode/' + cited;
  const b = byBase[path.posix.basename(cited)];
  return b && b.length === 1 && !cited.includes('/') ? b[0] : null;
}

const RX = /((?:DroppyCode\/)?(?:[A-Za-z0-9_+.-]+\/)*[A-Za-z0-9_+.-]+\.swift):(\d+)(?:-(\d+))?/g;
const flags = [];
const stats = { remapped: 0, unchanged_numbers: 0, flagged_endpoint: 0, flagged_inside: 0, flagged_deleted: 0, files: 0 };

function reciteString(s, recId, area) {
  return s.replace(RX, (whole, cited, a, b) => {
    const p = resolve(cited);
    if (!p) return whole;
    if (status[p] === 'D') { stats.flagged_deleted++; flags.push({ area, id: recId, cite: whole, why: 'file_deleted' }); return whole; }
    if (status[p] !== 'M') return whole;
    const A = +a, B = b ? +b : A;
    const ma = mapLine(p, A), mb = mapLine(p, B);
    if (ma === null || mb === null) {
      stats.flagged_endpoint++;
      flags.push({ area, id: recId, cite: whole, why: 'endpoint_on_changed_line' });
      return whole;
    }
    if (changedInside(p, A, B)) { stats.flagged_inside++; flags.push({ area, id: recId, cite: whole, new_cite: `${cited}:${ma}${b ? '-' + mb : ''}`, why: 'code_changed_inside_range' }); }
    if (ma === A && mb === B) { stats.unchanged_numbers++; return whole; }
    stats.remapped++;
    return `${cited}:${ma}${b ? '-' + mb : ''}`;
  });
}
function walk(v, recId, area) {
  if (typeof v === 'string') return reciteString(v, recId, area);
  if (Array.isArray(v)) return v.map((x) => walk(x, recId, area));
  if (v && typeof v === 'object') {
    const id = v.id || recId;
    const out = {};
    for (const [k, x] of Object.entries(v)) out[k] = walk(x, id, area);
    return out;
  }
  return v;
}

const specDir = path.join(WT, 'porting', 'spec');
let formatMismatch = [];
for (const f of fs.readdirSync(specDir).filter((f) => f.endsWith('.json'))) {
  const file = path.join(specDir, f);
  const raw = fs.readFileSync(file, 'utf8');
  const nl = raw.includes('\r\n') ? '\r\n' : '\n';
  const obj = JSON.parse(raw);
  // Refuse to rewrite a file whose formatting JSON.stringify would not reproduce byte for byte.
  const again = JSON.stringify(obj, null, 2).replace(/\n/g, nl) + (raw.endsWith(nl) ? nl : '');
  if (again !== raw) { formatMismatch.push(f); continue; }
  const next = walk(obj, null, f.replace('.json', ''));
  const text = JSON.stringify(next, null, 2).replace(/\n/g, nl) + (raw.endsWith(nl) ? nl : '');
  if (text !== raw) { stats.files++; if (WRITE) fs.writeFileSync(file, text); }
}
fs.writeFileSync('E:/Droppy/work/b3/recite_flags.json', JSON.stringify(flags, null, 1));
console.log(JSON.stringify({ ...stats, flags: flags.length, format_mismatch_skipped: formatMismatch, write: WRITE }));
