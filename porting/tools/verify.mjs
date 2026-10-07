#!/usr/bin/env node
/**
 * The port's single verification entrypoint (PORT_CONTRACT section 14).
 *
 *   node porting/tools/verify.mjs [--json] [--gate A|B|C|D|E|F]
 *
 * Exits NONZERO if any mandatory check or exact-revision evidence requirement fails.
 * C..F validate real artifacts and receipts; absent evidence fails rather than gaining credit.
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
import { REF, PINNED_SHA, PINNED_TREE } from './baseline.mjs';
import { captureBinding } from './evidence-binding.mjs';
import { checkSemanticGate, releaseContext, validateRequirementReport } from './release-evidence.mjs';

const ROOT = process.cwd();

const args = process.argv.slice(2);
const asJson = args.includes('--json');
const onlyGate = (() => {
  const i = args.indexOf('--gate');
  return i >= 0 ? args[i + 1] : null;
})();

if (onlyGate && !['A','B','C','D','E','F'].includes(onlyGate)) throw new Error('unknown gate; expected A..F');

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

  // MARKER mode: the quarantined behaviour is a sub-path of code the app needs, so identifier
  // presence proves nothing. Once a host file exists it must carry the explicit BLOCKED marker.
  const markerMisses = [];
  for (const e of open.filter((x) => x.detection_mode === 'marker')) {
    for (const host of e.marker_hosts || []) {
      if (!existsSync(rel(host))) continue;           // not ported yet: nothing to check
      if (!readFileSync(rel(host), 'utf8').includes(e.marker)) {
        markerMisses.push(`${e.id}: ${host} exists but lacks "${e.marker}" at ${e.quarantined_subpath}`);
      }
    }
  }

  if (hits.length || markerMisses.length) {
    throw new Error(
      [
        hits.length ? `quarantined behaviour implemented while its owner decision is open:\n    ${hits.join('\n    ')}` : '',
        markerMisses.length ? `quarantined sub-path not marked:\n    ${markerMisses.join('\n    ')}` : '',
        '    Neither reproducing nor repairing it is approved. See porting/blocked_requirements.json.',
      ].filter(Boolean).join('\n'),
    );
  }
  const ids = open.filter((x) => x.detection_mode !== 'marker').length;
  const mks = open.length - ids;
  return `${open.length} open entries respected (${ids} by identifier, ${mks} by required marker) across ${roots.length} tree(s)`;
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
  // Longest-first alternation: "md" and "metal" and "mjs" must be tried before bare "m",
  // otherwise chrome-design-tokens.md matches as "...tokens.m". Fixed 2026-09-20 after this
  // check reported 351 false positives against its own derived artifacts.
  const EXT = 'pbxproj|xcworkspacedata|resolved|metal|swift|json|toml|html|yml|mjs|css|svg|xml|txt|md|js|py|sh|h|m';
  // A path is treated as a claim about the PINNED SOURCE TREE only when it is written like one:
  // either followed by :<line>, which is the citation form, or rooted at a real top-level source
  // directory. Evidence prose also names RUNTIME paths the app reads and writes on the user's
  // machine (library.json, .grok/auth.json, ~/.commandcode/config.json, a project's CLAUDE.md,
  // <thread-id>.json). Those are not source citations and must not be checked against the tree.
  // Narrowed 2026-09-20; merge_spec.mjs separately requires every requirement to carry at least
  // one real file:line citation, so this narrowing cannot let an unevidenced requirement through.
  const SRC_ROOTS = ['DroppyCode/', 'DroppyCode.xcodeproj/', 'scripts/', 'website/', 'docs/', 'release/', '.commandcode/'];
  const re = new RegExp(`([A-Za-z0-9_./+-]+\\.(?:${EXT}))(:\\d+)?(?![A-Za-z0-9])`, 'g');
  const missingArtifacts = new Map();
  // A citation written "B1:<path>:<line>" is historical evidence from the previous pin (for example
  // a file the rebaseline deleted, AM-0002). It is not a claim about the pinned tree, so it is
  // checked against the archived B1 inventory instead, never waved through.
  const b1Path = rel('porting/baselines/B1/source_inventory.json');
  const b1Known = existsSync(b1Path) ? new Set(JSON.parse(readFileSync(b1Path, 'utf8')).items.map((i) => i.path)) : new Set();
  // "B2:<path>:<line>" is the same for the B2 pin (1.6.2), which B3 replaced (AM-0003): a file B3 deleted keeps its
  // B2 citation, checked against the archived B2 inventory.
  const b2Path = rel('porting/baselines/B2/source_inventory.json');
  const b2Known = existsSync(b2Path) ? new Set(JSON.parse(readFileSync(b2Path, 'utf8')).items.map((i) => i.path)) : new Set();
  const scan = (records, field) => {
    for (const r of records) {
      for (const e of r[field] || []) {
        for (const match of String(e).matchAll(re)) {
          const p = match[1];
          const prefix = String(e).slice(Math.max(0, match.index - 3), match.index);
          if (prefix === 'B1:') {
            if (!b1Known.has(p)) bogus.set(r.id, `B1:${p}${match[2] || ''} (not in the B1 inventory)`);
            continue;
          }
          if (prefix === 'B2:') {
            if (!b2Known.has(p)) bogus.set(r.id, `B2:${p}${match[2] || ''} (not in the B2 inventory)`);
            continue;
          }
          // A porting/... citation is this port's own derived artifact, not a claim about the
          // pinned tree. It still has to exist, so it is checked against disk rather than waved
          // through: a citation to a file that is not there is exactly as bad either way.
          // Citations to this port's own artifacts appear both repo-relative ("porting/x.md")
          // and absolute ("E:/Droppy/port/porting/x.md"). Normalize from the porting/ segment
          // so both forms are checked the same way, against disk.
          const portIdx = p.indexOf('porting/');
          if (portIdx >= 0) {
            const normalized = p.slice(portIdx);
            if (!existsSync(rel(normalized))) missingArtifacts.set(r.id, normalized);
            continue;
          }
          const hasLine = Boolean(match[2]);
          const rooted = SRC_ROOTS.some((root) => p.startsWith(root));
          if (!hasLine && !rooted) continue; // a runtime path, not a source citation
          // A path with no line number that opens a quoted string literal in quoted code, such as
          // appendingPathComponent(".commandcode/auth.json"), is a runtime path the app builds, not a
          // citation. Added 2026-09-22 after PR-PROVCLI-255 tripped on its own code quote.
          if (!hasLine && /["'`]/.test(String(e).charAt(match.index - 1))) continue;
          if (known.has(p)) continue;
          if ([...known].some((k) => k.endsWith('/' + p) || k === p)) continue;
          bogus.set(r.id, `${p}${match[2] || ''}`);
        }
      }
    }
  };
  scan(m.requirements || [], 'source_evidence');
  scan(ui.records || [], 'source_evidence');
  if (bogus.size) {
    const sample = [...bogus.entries()].slice(0, 6).map(([id, p]) => `${id} -> ${p}`);
    throw new Error(`${bogus.size} records cite a path not tracked at the pinned commit: ${sample.join('; ')}`);
  }
  if (missingArtifacts.size) {
    const sample = [...missingArtifacts.entries()].slice(0, 6).map(([id, p]) => `${id} -> ${p}`);
    throw new Error(`${missingArtifacts.size} records cite a port artifact that does not exist: ${sample.join('; ')}`);
  }
  return `all cited source paths exist at ${PINNED_SHA.slice(0, 8)}; all cited port artifacts exist on disk`;
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
  const knownIds = new Set();
  for (const block of unknowns.split(/^## /m)) {
    const id = (block.match(/^(UNK-\d+)/) || [])[1];
    if (!id) continue;
    knownIds.add(id);
    if (/\*\*Status:\*\*\s*(OPEN|INVESTIGATING|BLOCKED_ON_OWNER|BLOCKED_ON_ACCESS)/.test(block)) openIds.add(id);
  }
  const m = readJson('porting/parity_matrix.json');
  const declared = (m.requirements || []).filter((r) => r.depends_on_unknown).length;
  // Retain dependency provenance after resolution; a closed entry remains a real named dependency.
  const bogus = (m.requirements || [])
    .filter((r) => r.depends_on_unknown && !knownIds.has(r.depends_on_unknown))
    .map((r) => `${r.id} -> ${r.depends_on_unknown}`);
  if (bogus.length) throw new Error(`${bogus.length} requirement(s) name an unknown with no record: ${bogus.slice(0, 4).join('; ')}`);
  return `${openIds.size} open unknowns, ${declared} requirement(s) declare a dependency`;
});

check('B', 'every completeness-audit finding has been adjudicated', () => {
  const p = rel('porting/critiques/spec_audit.json');
  if (!existsSync(p)) throw new Error('no spec_audit.json; the completeness critics have not run');
  const a = JSON.parse(readFileSync(p, 'utf8'));
  const raised =
    (a.audit_missing || []).length + (a.audit_wrong || []).length +
    (a.audit_too_coarse || []).length + (a.audit_paraphrased || []).length;
  const dir = rel('porting/reports/spec_repair');
  if (!existsSync(dir)) {
    return { status: 'FAIL', detail: `${raised} audit findings raised, spec repair pass has produced no output yet` };
  }
  let resolved = 0;
  let scopes = 0;
  for (const f of readdirSync(dir).filter((x) => x.endsWith('.json'))) {
    try {
      const doc = JSON.parse(readFileSync(join(dir, f), 'utf8'));
      resolved += (doc.resolutions || []).length;
      scopes++;
    } catch { /* counted as unresolved */ }
  }
  const verdicts = (a.audit_verdicts || []).length;
  if (scopes < verdicts) {
    return { status: 'FAIL', detail: `${scopes}/${verdicts} audit scopes repaired, ${resolved} findings adjudicated of ${raised}` };
  }
  if (resolved < raised) {
    return { status: 'FAIL', detail: `${resolved} adjudicated of ${raised} raised` };
  }
  return `${resolved} findings adjudicated across ${scopes} scopes`;
});

check('B', 'platform adaptation register covers the identified boundaries', () => {
  const p = rel('porting/platform_adaptations.md');
  if (!existsSync(p)) throw new Error('porting/platform_adaptations.md missing');
  const text = readFileSync(p, 'utf8');
  const entries = (text.match(/^## PA-\d+/gm) || []).length;
  if (entries < 10) throw new Error(`only ${entries} adaptation entries; the boundaries found so far are not all recorded`);
  return `${entries} adaptation entries`;
});

// Live from the first line of production code: contract section 13 requires temporary stubs to be
// "visibly tracked as incomplete and removed or replaced before acceptance". This lists every one.
check('C', 'no temporary stub remains in the implementation', () => {
  if (!existsSync(rel('src'))) return { status: 'FAIL', detail: 'no implementation tree yet' };
  const MARKERS = [
    [/throw\s+new\s+NotImplementedException/, 'NotImplementedException'],
    [/MARROW_STUB/, 'MARROW_STUB marker'],
    [/\/\/\s*(TODO|FIXME|HACK|STUB)\b/i, 'TODO/FIXME/HACK/STUB comment'],
  ];
  const hits = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (['bin', 'obj', 'node_modules', '.git'].includes(name)) continue;
      const p = join(dir, name);
      if (statSync(p).isDirectory()) { walk(p); continue; }
      if (!/\.(cs|axaml)$/.test(name)) continue;
      readFileSync(p, 'utf8').split('\n').forEach((line, i) => {
        for (const [re, what] of MARKERS) {
          if (re.test(line)) hits.push(`${p.replace(ROOT, '').replace(/^[\\/]/, '')}:${i + 1} ${what}`);
        }
      });
    }
  };
  walk(rel('src'));
  if (hits.length) {
    throw new Error(`${hits.length} stub(s) remain:\n    ${hits.slice(0, 12).join('\n    ')}` +
      (hits.length > 12 ? `\n    ... and ${hits.length - 12} more` : ''));
  }
  return 'no stubs in src/';
});

// Gate D runs the four solution test projects and dependencies, never Marrow.Setup.
// --no-run validates captured evidence only. No packaging, installer execution, signing or publication.
check('D', 'the solution builds and every test passes, none skipped', () => {
  if (!args.includes('--no-run')) {
    try { execFileSync(process.execPath, ['porting/tools/req_verify.mjs'], { cwd: ROOT, encoding: 'utf8', maxBuffer: 1 << 28 }); }
    catch (e) { throw new Error(`test/evidence run failed: ${String(e.stdout || e.message).split('\n').slice(-5).join(' ')}`); }
  }
  const doc = readJson('porting/verification/requirements.json');
  return validateRequirementReport(doc, releaseContext(ROOT));
});

check('D', 'every applicable requirement is VERIFIED by a bound test run', () =>
  validateRequirementReport(readJson('porting/verification/requirements.json'), releaseContext(ROOT)));

const SEMANTIC = [
  ['C', 'no fake-success paths, dead controls or unapproved product changes'],
  ['D', 'interaction tests, visual comparison, persistence/restart and cancellation evidence per verification_method'],
  ['E', 'packaged artifact installs, launches, cleans up children, upgrades and uninstalls'],
  ['F', 'independent parity review with no unresolved required discrepancy'],
];
for (const [gate, name] of SEMANTIC) check(gate, name, () => checkSemanticGate(gate, ROOT));

// An isolated final gate must also see the actual A..E checks, including marker/quarantine guards.
check('F', 'all antecedent gates pass at this exact revision', () => {
  for (const gate of ['A','B','C','D','E']) {
    try { execFileSync(process.execPath, ['porting/tools/verify.mjs','--gate',gate,'--json','--no-run'], {cwd:ROOT,encoding:'utf8',maxBuffer:1<<26}); }
    catch { throw new Error(`antecedent Gate ${gate} fails; final acceptance is incomplete`); }
  }
  return 'A..E pass at the current exact revision';
});

// ---------------------------------------------------------------- report

const counts = results.reduce((a, r) => { a[r.status] = (a[r.status] || 0) + 1; return a; }, {});

if (asJson) {
  console.log(JSON.stringify({
    implementation: captureBinding(ROOT),
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
