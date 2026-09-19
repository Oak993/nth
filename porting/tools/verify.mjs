#!/usr/bin/env node
/**
 * The port's single verification entrypoint (PORT_CONTRACT section 14).
 *
 *   node porting/tools/verify.mjs [--json] [--gate A|B|C|D|E|F]
 *
 * Exits NONZERO if any mandatory check for a claimed gate fails, and nonzero while any
 * mandatory gate remains unimplemented. A gate that has not been built yet reports
 * NOT_IMPLEMENTED and fails; it never reports success by omission.
 *
 * This file is written by the same agent that does the porting work and therefore proves
 * nothing on its own (contract section 3: "Do not claim these controls prove comprehension
 * or are tamper-proof while you have write access to them"). It is a mechanical consistency
 * check over evidence that exists on disk, nothing more. Semantic review is separate.
 */

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();
const REF = 'E:/Droppy/reference/droppy-code';
const PINNED_SHA = 'dd2b86efef9e77168f304a86e8a8411877578bbe';
const PINNED_TREE = 'f761e5ef3745c4f68958ffa4014ab0b618c9d419';

const args = process.argv.slice(2);
const asJson = args.includes('--json');
const onlyGate = (() => {
  const i = args.indexOf('--gate');
  return i >= 0 ? args[i + 1] : null;
})();

const results = [];
let hardFail = false;

function check(gate, name, fn) {
  if (onlyGate && gate !== onlyGate) return;
  let status = 'PASS';
  let detail = '';
  try {
    const out = fn();
    if (out && typeof out === 'object' && out.status) {
      status = out.status;
      detail = out.detail || '';
    } else {
      detail = out || '';
    }
  } catch (e) {
    status = 'FAIL';
    detail = e.message;
  }
  if (status === 'FAIL' || status === 'NOT_IMPLEMENTED') hardFail = true;
  results.push({ gate, name, status, detail });
}

const rel = (p) => join(ROOT, p);
const readJson = (p) => JSON.parse(readFileSync(rel(p), 'utf8'));

// ---------------------------------------------------------------- Gate A

check('A', 'reference checkout is at the pinned commit', () => {
  const sha = execFileSync('git', ['-C', REF, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  if (sha !== PINNED_SHA) throw new Error(`reference HEAD is ${sha}, expected ${PINNED_SHA}`);
  return sha;
});

check('A', 'reference tree hash matches the pinned tree', () => {
  const tree = execFileSync('git', ['-C', REF, 'rev-parse', 'HEAD^{tree}'], { encoding: 'utf8' }).trim();
  if (tree !== PINNED_TREE) throw new Error(`tree is ${tree}, expected ${PINNED_TREE}`);
  return tree;
});

check('A', 'reference working tree is unmodified', () => {
  const st = execFileSync('git', ['-C', REF, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
  if (st) throw new Error(`reference has local modifications:\n${st}`);
  return 'clean';
});

check('A', 'inventory reconciles against the pinned git tree', () => {
  const inv = readJson('porting/source_inventory.json');
  const tracked = execFileSync('git', ['-C', REF, 'ls-files'], { encoding: 'utf8' })
    .split('\n').filter(Boolean);
  const invPaths = new Set(inv.items.map((i) => i.path));
  const missing = tracked.filter((p) => !invPaths.has(p));
  const extra = [...invPaths].filter((p) => !tracked.includes(p));
  if (missing.length) throw new Error(`${missing.length} tracked files absent from inventory: ${missing.slice(0, 5).join(', ')}`);
  if (extra.length) throw new Error(`${extra.length} inventory entries not tracked: ${extra.slice(0, 5).join(', ')}`);
  return `${tracked.length} files reconciled`;
});

check('A', 'every inventory sha256 matches the file on disk', () => {
  const inv = readJson('porting/source_inventory.json');
  const bad = [];
  for (const i of inv.items) {
    const buf = readFileSync(join(REF, i.path));
    const sha = createHash('sha256').update(buf).digest('hex');
    if (sha !== i.sha256) bad.push(i.path);
  }
  if (bad.length) throw new Error(`${bad.length} hash mismatches: ${bad.slice(0, 5).join(', ')}`);
  return `${inv.items.length} digests verified`;
});

check('A', 'every first-party file is marked READ', () => {
  const inv = readJson('porting/source_inventory.json');
  const unread = inv.items.filter((i) => i.read_status !== 'READ');
  if (unread.length) {
    throw new Error(`${unread.length} unread: ${unread.slice(0, 8).map((u) => u.path).join(', ')}`);
  }
  return `${inv.items.length}/${inv.items.length} read`;
});

check('A', 'read ledger has exactly one record per inventory path', () => {
  const inv = readJson('porting/source_inventory.json');
  const lines = readFileSync(rel('porting/read_ledger.jsonl'), 'utf8').split('\n').filter(Boolean);
  const byPath = new Map();
  const dupes = [];
  for (const l of lines) {
    const r = JSON.parse(l);
    if (byPath.has(r.path)) dupes.push(r.path);
    byPath.set(r.path, r);
  }
  if (dupes.length) throw new Error(`${dupes.length} duplicate ledger records: ${dupes.slice(0, 5).join(', ')}`);
  const invPaths = new Set(inv.items.map((i) => i.path));
  const orphan = [...byPath.keys()].filter((p) => !invPaths.has(p));
  const absent = [...invPaths].filter((p) => !byPath.has(p));
  if (orphan.length) throw new Error(`${orphan.length} ledger records for unknown paths`);
  if (absent.length) throw new Error(`${absent.length} inventory paths with no ledger record`);
  return `${byPath.size} records, 1:1 with inventory`;
});

check('A', 'ledger digests match the pinned inventory digests', () => {
  const inv = readJson('porting/source_inventory.json');
  const byPath = new Map(inv.items.map((i) => [i.path, i]));
  const lines = readFileSync(rel('porting/read_ledger.jsonl'), 'utf8').split('\n').filter(Boolean);
  const bad = [];
  for (const l of lines) {
    const r = JSON.parse(l);
    const item = byPath.get(r.path);
    if (!item || r.sha256 !== item.sha256) bad.push(r.path);
  }
  if (bad.length) throw new Error(`${bad.length} ledger digest mismatches: ${bad.slice(0, 5).join(', ')}`);
  return `${lines.length} ledger digests match`;
});

check('A', 'text-file coverage reaches the true last line', () => {
  const inv = readJson('porting/source_inventory.json');
  const byPath = new Map(inv.items.map((i) => [i.path, i]));
  const lines = readFileSync(rel('porting/read_ledger.jsonl'), 'utf8').split('\n').filter(Boolean);
  const bad = [];
  for (const l of lines) {
    const r = JSON.parse(l);
    const item = byPath.get(r.path);
    if (!item || item.kind === 'binary') continue;
    const total = item.line_count || 0;
    if (total === 0) continue;
    const ranges = (r.ranges_read || []).map(([a, b]) => [Number(a), Number(b)]).sort((x, y) => x[0] - y[0]);
    let reached = 0;
    let gap = false;
    for (const [a, b] of ranges) {
      if (a > reached + 1) { gap = true; break; }
      reached = Math.max(reached, b);
    }
    if (gap || reached < total) bad.push(`${r.path} (reached ${reached}/${total})`);
  }
  if (bad.length) throw new Error(`${bad.length} files with incomplete coverage: ${bad.slice(0, 5).join(', ')}`);
  return 'all text files covered end to end';
});

check('A', 'binary assets carry an inspection record', () => {
  const inv = readJson('porting/source_inventory.json');
  const bins = inv.items.filter((i) => i.kind === 'binary');
  const missing = bins.filter((i) => !i.asset_inspection || String(i.asset_inspection).trim().length < 20);
  if (missing.length) throw new Error(`${missing.length} binaries without inspection: ${missing.slice(0, 5).map((m) => m.path).join(', ')}`);
  return `${bins.length} binary assets inspected`;
});

check('A', 'no generic finding reused across different files', () => {
  const lines = readFileSync(rel('porting/read_ledger.jsonl'), 'utf8').split('\n').filter(Boolean);
  const seen = new Map();
  const dupes = [];
  for (const l of lines) {
    const r = JSON.parse(l);
    const key = String(r.purpose || '').toLowerCase().replace(/\s+/g, ' ').trim();
    if (!key) continue;
    if (seen.has(key) && seen.get(key) !== r.path) dupes.push(`${r.path} == ${seen.get(key)}`);
    seen.set(key, r.path);
  }
  if (dupes.length) throw new Error(`${dupes.length} duplicated purpose texts: ${dupes.slice(0, 3).join('; ')}`);
  return 'every purpose is file-specific';
});

check('A', 'every critic finding has been adjudicated', () => {
  const cPath = rel('porting/critiques/critiques.json');
  if (!existsSync(cPath)) throw new Error('no critiques.json; the reconciliation pass has not run');
  const crit = JSON.parse(readFileSync(cPath, 'utf8'));
  const open = crit.reduce((n, f) => n + (f.gaps || []).length + (f.contradictions || []).length, 0);
  const repairDir = rel('porting/reports/repair');
  if (!existsSync(repairDir)) {
    return { status: 'FAIL', detail: `${open} critic findings raised, repair pass has produced no output yet` };
  }
  const files = readdirSync(repairDir).filter((f) => f.endsWith('.json'));
  let resolved = 0;
  for (const f of files) {
    try {
      const doc = JSON.parse(readFileSync(join(repairDir, f), 'utf8'));
      resolved += (doc.resolutions || []).length;
    } catch { /* counted as unresolved */ }
  }
  if (files.length < crit.length) {
    return { status: 'FAIL', detail: `${files.length}/${crit.length} families repaired, ${resolved} findings adjudicated of ${open}` };
  }
  if (resolved < open) {
    return { status: 'FAIL', detail: `${resolved} adjudicated of ${open} raised` };
  }
  return `${resolved} findings adjudicated across ${files.length} families`;
});

// ------------------------------------------------- Gates B..F (not yet built)

// Quarantine guard. Live from now on, before any implementation exists, because its job is to stop
// a blocked behavior being implemented by drift. Required by porting/contract_amendments.md AM-0001.
check('C', 'no quarantined behavior is implemented while its owner decision is open', () => {
  const doc = readJson('porting/blocked_requirements.json');
  const open = (doc.entries || []).filter((e) => e.status === 'OPEN');
  if (!open.length) return 'no open quarantine entries';

  // The Windows implementation tree. Absent until the stack is chosen; absence is not a pass for
  // Gate C as a whole, only for this specific guard.
  const treeCandidates = ['src', 'app', 'windows', 'Marrow'];
  const roots = treeCandidates.filter((d) => existsSync(rel(d)) && statSync(rel(d)).isDirectory());
  if (!roots.length) {
    return `${open.length} open quarantine entr${open.length === 1 ? 'y' : 'ies'} (${open.map((e) => e.id).join(', ')}); no implementation tree yet, nothing to scan`;
  }

  const CODE_EXT = new Set(['.ts', '.tsx', '.js', '.jsx', '.mjs', '.cs', '.cpp', '.h', '.rs', '.swift', '.xaml']);
  const hits = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (name === 'node_modules' || name === '.git' || name === 'dist' || name === 'build') continue;
      const p = join(dir, name);
      const st = statSync(p);
      if (st.isDirectory()) { walk(p); continue; }
      const dot = name.lastIndexOf('.');
      if (dot < 0 || !CODE_EXT.has(name.slice(dot))) continue;
      const text = readFileSync(p, 'utf8');
      for (const e of open) {
        for (const pat of e.detection_patterns || []) {
          if (text.includes(pat)) hits.push(`${e.id}: "${pat}" in ${p.replace(ROOT + '\\', '').replace(ROOT + '/', '')}`);
        }
      }
    }
  };
  for (const r of roots) walk(rel(r));

  if (hits.length) {
    throw new Error(
      `quarantined behavior implemented while its owner decision is open:\n    ${hits.join('\n    ')}\n` +
      '    Neither reproducing nor repairing it is approved. See porting/blocked_requirements.json.',
    );
  }
  return `${open.length} open quarantine entr${open.length === 1 ? 'y' : 'ies'} respected across ${roots.length} tree(s)`;
});

// ---------------------------------------------------------------- Gate B

const specExists = () => existsSync(rel('porting/parity_matrix.json'));

check('B', 'architecture record exists and is substantive', () => {
  const p = rel('porting/architecture.md');
  if (!existsSync(p)) throw new Error('porting/architecture.md missing');
  const text = readFileSync(p, 'utf8');
  if (text.length < 8000) throw new Error(`architecture.md is only ${text.length} bytes; too thin to be the real architecture`);
  const cites = (text.match(/\.swift:\d+/g) || []).length;
  if (cites < 40) throw new Error(`architecture.md cites only ${cites} source lines; it must be evidenced, not narrated`);
  return `${text.length} bytes, ${cites} source citations`;
});

check('B', 'parity matrix exists with well-formed requirements', () => {
  if (!specExists()) throw new Error('porting/parity_matrix.json missing; run porting/tools/merge_spec.mjs');
  const m = readJson('porting/parity_matrix.json');
  const rs = m.requirements || [];
  if (!rs.length) throw new Error('parity matrix is empty');
  const bad = [];
  const seen = new Set();
  for (const r of rs) {
    if (!/^PR-[A-Z]+-\d{3,}$/.test(r.id || '')) bad.push(`${r.id}: malformed id`);
    else if (seen.has(r.id)) bad.push(`${r.id}: duplicate`);
    seen.add(r.id);
    if (!(r.source_evidence || []).length) bad.push(`${r.id}: no source evidence`);
    if (!r.verification_method) bad.push(`${r.id}: no verification method`);
    if (!r.expected_result) bad.push(`${r.id}: no expected result`);
  }
  if (bad.length) throw new Error(`${bad.length} malformed requirements: ${bad.slice(0, 5).join('; ')}`);
  return `${rs.length} requirements across ${Object.keys(m.counts?.by_area || {}).length} areas`;
});

check('B', 'every cited source path exists in the pinned inventory', () => {
  if (!specExists()) throw new Error('parity matrix missing');
  const inv = readJson('porting/source_inventory.json');
  const known = new Set(inv.items.map((i) => i.path));
  const m = readJson('porting/parity_matrix.json');
  const ui = existsSync(rel('porting/ui_contract.json')) ? readJson('porting/ui_contract.json') : { records: [] };
  const bogus = new Map();
  const scan = (records, field) => {
    for (const r of records) {
      for (const e of r[field] || []) {
        const match = String(e).match(/([A-Za-z0-9_./+-]+\.(?:swift|h|m|json|md|yml|py|sh|js|html|css|metal|pbxproj|txt|toml|xml|svg))/);
        if (!match) continue;
        let p = match[1];
        // Accept a repo-relative path, or one that ends with a known path.
        if (known.has(p)) continue;
        if ([...known].some((k) => k.endsWith('/' + p) || k === p)) continue;
        bogus.set(r.id, p);
      }
    }
  };
  scan(m.requirements || [], 'source_evidence');
  scan(ui.records || [], 'source_evidence');
  if (bogus.size) {
    const sample = [...bogus.entries()].slice(0, 6).map(([id, p]) => `${id} -> ${p}`);
    throw new Error(`${bogus.size} records cite a path not tracked at the pinned commit: ${sample.join('; ')}`);
  }
  return `all cited paths exist at ${PINNED_SHA.slice(0, 8)}`;
});

check('B', 'UI contract exists with well-formed records', () => {
  const p = rel('porting/ui_contract.json');
  if (!existsSync(p)) throw new Error('porting/ui_contract.json missing');
  const ui = readJson('porting/ui_contract.json');
  const rs = ui.records || [];
  if (!rs.length) throw new Error('UI contract is empty');
  const bad = [];
  const seen = new Set();
  for (const r of rs) {
    if (!/^UI-[A-Z]+-\d{3,}$/.test(r.id || '')) bad.push(`${r.id}: malformed id`);
    else if (seen.has(r.id)) bad.push(`${r.id}: duplicate`);
    seen.add(r.id);
    if (!r.screen || !r.element) bad.push(`${r.id}: missing screen or element`);
    if (!(r.source_evidence || []).length) bad.push(`${r.id}: no source evidence`);
  }
  if (bad.length) throw new Error(`${bad.length} malformed UI records: ${bad.slice(0, 5).join('; ')}`);
  return `${rs.length} UI records`;
});

check('B', 'quarantined behavior is not marked ready to implement', () => {
  if (!specExists()) throw new Error('parity matrix missing');
  const blocked = readJson('porting/blocked_requirements.json');
  const open = new Set((blocked.entries || []).filter((e) => e.status === 'OPEN').map((e) => e.id));
  if (!open.size) return 'no open quarantine entries';
  const m = readJson('porting/parity_matrix.json');
  const wrong = (m.requirements || []).filter(
    (r) => r.blocked_by && open.has(r.blocked_by) && !['DISCOVERED', 'BLOCKED'].includes(r.status),
  );
  if (wrong.length) {
    throw new Error(`${wrong.length} requirement(s) blocked by an open owner decision but marked ${wrong.map((w) => `${w.id}=${w.status}`).join(', ')}`);
  }
  const covered = (m.requirements || []).filter((r) => r.blocked_by && open.has(r.blocked_by)).length;
  return `${open.size} open quarantine entr${open.size === 1 ? 'y' : 'ies'}, ${covered} requirement(s) correctly held at DISCOVERED`;
});

check('B', 'no requirement silently depends on an unresolved unknown', () => {
  if (!specExists()) throw new Error('parity matrix missing');
  const unknowns = readFileSync(rel('porting/unknowns.md'), 'utf8');
  const openIds = new Set();
  for (const block of unknowns.split(/^## /m)) {
    const id = (block.match(/^(UNK-\d+)/) || [])[1];
    if (!id) continue;
    if (/\*\*Status:\*\*\s*(OPEN|INVESTIGATING|BLOCKED_ON_OWNER|BLOCKED_ON_ACCESS)/.test(block)) openIds.add(id);
  }
  const m = readJson('porting/parity_matrix.json');
  const declared = (m.requirements || []).filter((r) => r.depends_on_unknown).length;
  // Not a completeness proof: it checks that declared dependencies name a real open unknown.
  const bogus = (m.requirements || [])
    .filter((r) => r.depends_on_unknown && !openIds.has(r.depends_on_unknown))
    .map((r) => `${r.id} -> ${r.depends_on_unknown}`);
  if (bogus.length) throw new Error(`${bogus.length} requirement(s) name an unknown that is not open: ${bogus.slice(0, 4).join('; ')}`);
  return `${openIds.size} open unknowns, ${declared} requirement(s) declare a dependency`;
});

check('B', 'platform adaptation register covers the identified boundaries', () => {
  const p = rel('porting/platform_adaptations.md');
  if (!existsSync(p)) throw new Error('porting/platform_adaptations.md missing');
  const text = readFileSync(p, 'utf8');
  const entries = (text.match(/^## PA-\d+/gm) || []).length;
  if (entries < 10) throw new Error(`only ${entries} adaptation entries; the boundaries found so far are not all recorded`);
  return `${entries} adaptation entries`;
});

const PENDING = [
  ['C', 'no fake-success paths, dead controls or unapproved product changes'],
  ['D', 'source-derived tests, interaction tests, visual comparison, persistence and cancellation'],
  ['E', 'packaged artifact installs, launches, cleans up children, upgrades and uninstalls'],
  ['F', 'independent parity review with no unresolved required discrepancy'],
];
for (const [gate, name] of PENDING) {
  check(gate, name, () => ({
    status: 'NOT_IMPLEMENTED',
    detail: 'gate not built yet; it fails by design rather than passing by omission',
  }));
}

// ---------------------------------------------------------------- report

const counts = results.reduce((a, r) => { a[r.status] = (a[r.status] || 0) + 1; return a; }, {});

if (asJson) {
  console.log(JSON.stringify({
    pinned_sha: PINNED_SHA,
    pinned_tree: PINNED_TREE,
    reference: REF,
    counts,
    ok: !hardFail,
    results,
  }, null, 2));
} else {
  console.log(`Droppy Code -> Windows port verification`);
  console.log(`pinned source ${PINNED_SHA}`);
  console.log(`reference     ${REF}`);
  console.log('');
  let gate = null;
  for (const r of results) {
    if (r.gate !== gate) { gate = r.gate; console.log(`Gate ${gate}`); }
    const mark = r.status === 'PASS' ? 'PASS' : r.status === 'NOT_IMPLEMENTED' ? 'TODO' : 'FAIL';
    console.log(`  [${mark}] ${r.name}`);
    if (r.detail) console.log(`         ${r.detail}`);
  }
  console.log('');
  console.log(Object.entries(counts).map(([k, v]) => `${k}=${v}`).join('  '));
  console.log(hardFail ? 'RESULT: NOT ACCEPTABLE (mandatory checks unmet)' : 'RESULT: all implemented checks pass');
}

process.exit(hardFail ? 1 : 0);
