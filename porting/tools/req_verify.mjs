#!/usr/bin/env node
/**
 * Per-requirement verification for Gate D (PORT_CONTRACT section 14).
 *
 *   node porting/tools/req_verify.mjs            build, run every test, derive requirement status
 *   node porting/tools/req_verify.mjs --no-run   derive status from the newest recorded run only
 *
 * A requirement is VERIFIED here when, and only when:
 *   - at least one test method names it with [Trait("Req", "<id>")],
 *   - every one of those test methods actually ran in the recorded run (a theory counts once per
 *     case, and every case must have passed),
 *   - nothing it names failed, was skipped or did not execute, and
 *   - the run is bound to the exact source tree being reported: git HEAD recorded, and no
 *     uncommitted change under src/ or tests/ at run time (otherwise the result is UNBOUND), and
 *   - its depends_on_unknown does not name an unknown that was closed without an answer or is still open
 *     (UNANSWERED below). Such a record whose tests all pass is PARTIAL: it is not counted in `verified`, it
 *     names the unknown, and Gate D still refuses it (Core.Support.Coding rulings, 2026-09-23).
 *
 * Status is DERIVED on every run and written to porting/verification/requirements.json. It is never
 * set by hand, so a code change that breaks a test takes the requirement out of VERIFIED on the next
 * run (contract section 14: "Code changes invalidate affected earlier results").
 *
 * What this tool cannot judge, and the reviews must: whether a test really derives from the pinned
 * source, and whether a UI requirement's test exercises the interaction or visual property its
 * verification_method names. Traits are read from the test SOURCE because the xUnit VSTest TRX
 * logger does not carry them (measured 2026-09-22: zero trait data in a TRX of 239 results).
 */

import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, statSync, mkdirSync, existsSync } from 'node:fs';
import { join, relative } from 'node:path';
import { PINNED_SHA } from './baseline.mjs';

const ROOT = process.cwd();
const noRun = process.argv.includes('--no-run');
const RUNS = join(ROOT, 'porting', 'verification', 'runs');
const OUT = join(ROOT, 'porting', 'verification', 'requirements.json');

// ---------------------------------------------------------------- requirement list
const matrix = JSON.parse(readFileSync(join(ROOT, 'porting', 'parity_matrix.json'), 'utf8'));
const ui = JSON.parse(readFileSync(join(ROOT, 'porting', 'ui_contract.json'), 'utf8'));
// UNK-0003 was closed without an answer under OD-0001 (no macOS reference run); UNK-0011 is still open.
const UNANSWERED = new Set(['UNK-0003', 'UNK-0011']);
const unanswered = (r) => (UNANSWERED.has(r.depends_on_unknown) ? r.depends_on_unknown : null);
const reqs = new Map();
for (const r of matrix.requirements) reqs.set(r.id, { kind: 'behaviour', blocked: !!r.blocked_by, retired: r.status === 'RETIRED', unknown: unanswered(r) });
for (const u of ui.records || []) if (u.id) reqs.set(u.id, { kind: 'ui', blocked: !!u.blocked_by, retired: u.status === 'RETIRED', unknown: unanswered(u) });

// ---------------------------------------------------------------- traits from test source
function walk(dir, out = []) {
  for (const n of readdirSync(dir)) {
    if (n === 'bin' || n === 'obj') continue;
    const p = join(dir, n);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (n.endsWith('.cs')) out.push(p);
  }
  return out;
}
const byReq = new Map();      // req id -> Set of fully qualified test names
const unknownIds = [];        // traits naming an id the spec does not have
const testFiles = existsSync(join(ROOT, 'tests')) ? walk(join(ROOT, 'tests')) : [];
for (const f of testFiles) {
  const text = readFileSync(f, 'utf8');
  const ns = (text.match(/^\s*namespace\s+([\w.]+)\s*;/m) || text.match(/^\s*namespace\s+([\w.]+)/m) || [])[1] || '';
  const lines = text.split(/\r?\n/);
  let cls = null;
  let pending = [];
  let isTest = false;
  for (const line of lines) {
    const c = line.match(/\b(?:class|record)\s+(\w+)/);
    if (c && !/^\s*\/\//.test(line)) cls = c[1];
    if (/^\s*\[/.test(line)) {
      for (const m of line.matchAll(/Trait\(\s*"Req"\s*,\s*"([^"]+)"\s*\)/g)) pending.push(m[1]);
      if (/\b(Fact|Theory|AvaloniaFact|AvaloniaTheory)\b/.test(line)) isTest = true;
      continue;
    }
    const m = line.match(/^\s*public\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?)\s+(\w+)\s*\(/);
    if (m) {
      if (isTest && pending.length && cls) {
        const fqn = `${ns}.${cls}.${m[1]}`;
        for (const id of pending) {
          if (!reqs.has(id)) unknownIds.push(`${id} in ${relative(ROOT, f)}`);
          if (!byReq.has(id)) byReq.set(id, new Set());
          byReq.get(id).add(fqn);
        }
      }
      pending = [];
      isTest = false;
    } else if (line.trim() && !/^\s*\/\//.test(line)) {
      // Any other code line ends the attribute run, so a trait can never leak onto a later method.
      pending = [];
      isTest = false;
    }
  }
}

// ---------------------------------------------------------------- run
const git = (...a) => execFileSync('git', a, { encoding: 'utf8' }).trim();
let runDir;
if (!noRun) {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  runDir = join(RUNS, stamp);
  mkdirSync(runDir, { recursive: true });
  const head = git('rev-parse', 'HEAD');
  const dirty = git('status', '--porcelain', '--', 'src', 'tests');
  const dataDir = join(process.env.TEMP || '/tmp', `marrow-reqverify-${process.pid}`);
  const res = spawnSync('dotnet', ['test', 'Marrow.sln', '-c', 'Debug', '--logger', 'trx', '--results-directory', runDir],
    { encoding: 'utf8', env: { ...process.env, MARROW_PORT_DATA_DIR: dataDir }, maxBuffer: 1 << 28 });
  const meta = {
    baseline: PINNED_SHA, head, dirty_src_tests: dirty ? dirty.split('\n') : [],
    command: 'dotnet test Marrow.sln -c Debug --logger trx', exit_code: res.status,
    dotnet: execFileSync('dotnet', ['--version'], { encoding: 'utf8' }).trim(),
    os: execFileSync('cmd', ['/c', 'ver'], { encoding: 'utf8' }).trim(),
    started_at: stamp, output_tail: (res.stdout + res.stderr).split('\n').slice(-25).join('\n'),
  };
  writeFileSync(join(runDir, 'run.json'), JSON.stringify(meta, null, 2) + '\n');
} else {
  if (!existsSync(RUNS)) { console.error('no recorded run; run without --no-run first'); process.exit(2); }
  const dirs = readdirSync(RUNS).filter((d) => existsSync(join(RUNS, d, 'run.json'))).sort();
  if (!dirs.length) { console.error('no recorded run'); process.exit(2); }
  runDir = join(RUNS, dirs[dirs.length - 1]);
}
const meta = JSON.parse(readFileSync(join(runDir, 'run.json'), 'utf8'));

// ---------------------------------------------------------------- results from TRX
const results = new Map(); // testName -> [outcome]
for (const f of walk(runDir).concat(readdirSync(runDir).filter((n) => n.endsWith('.trx')).map((n) => join(runDir, n)))) {
  if (!f.endsWith('.trx')) continue;
  const xml = readFileSync(f, 'utf8');
  for (const m of xml.matchAll(/<UnitTestResult\b[^>]*?testName="([^"]*)"[^>]*?outcome="([^"]*)"/g)) {
    const name = m[1].replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
    if (!results.has(name)) results.set(name, []);
    results.get(name).push(m[2]);
  }
}
const outcomesFor = (fqn) => {
  const out = [];
  for (const [name, list] of results) if (name === fqn || name.startsWith(fqn + '(')) out.push(...list);
  return out;
};

const bound = meta.exit_code !== null && meta.dirty_src_tests.length === 0 && meta.head === git('rev-parse', 'HEAD')
  && !git('status', '--porcelain', '--', 'src', 'tests');
const status = {};
const counts = {};
for (const [id, r] of reqs) {
  let s;
  const tests = [...(byReq.get(id) || [])];
  const per = tests.map((t) => ({ test: t, outcomes: outcomesFor(t) }));
  if (r.retired) s = 'RETIRED';
  else if (r.blocked) s = 'BLOCKED';
  else if (!tests.length) s = 'NO_TEST';
  else if (per.some((p) => p.outcomes.some((o) => o !== 'Passed'))) s = 'FAILING';
  else if (per.some((p) => !p.outcomes.length)) s = 'NOT_RUN';
  else if (!bound) s = 'UNBOUND';
  else s = r.unknown ? 'PARTIAL' : 'VERIFIED';
  status[id] = r.unknown ? { kind: r.kind, status: s, unknown: r.unknown, tests: per } : { kind: r.kind, status: s, tests: per };
  counts[s] = (counts[s] || 0) + 1;
}

const applicable = [...reqs.values()].filter((r) => !r.retired && !r.blocked).length;
const doc = {
  schema: 'droppy-windows-port/requirement_verification/1',
  run: { dir: relative(ROOT, runDir), ...meta, bound_to_current_head: bound },
  counts, verified: counts.VERIFIED || 0, applicable, total: reqs.size,
  unknown_ids_in_tests: unknownIds,
  requirements: status,
};
mkdirSync(join(ROOT, 'porting', 'verification'), { recursive: true });
writeFileSync(OUT, JSON.stringify(doc, null, 1) + '\n');
console.log(`run ${relative(ROOT, runDir)} exit ${meta.exit_code} head ${meta.head.slice(0, 8)} ${bound ? 'BOUND' : 'UNBOUND (dirty or stale)'}`);
console.log(`VERIFIED ${doc.verified} / ${applicable} applicable (${reqs.size} total): ${JSON.stringify(counts)}`);
if (unknownIds.length) console.log(`traits naming unknown ids: ${unknownIds.slice(0, 10).join('; ')}`);
