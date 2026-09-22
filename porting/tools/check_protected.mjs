#!/usr/bin/env node
/**
 * Detects changes to files worker models must not touch.
 *
 *   node porting/tools/check_protected.mjs            compare the working tree with HEAD
 *   node porting/tools/check_protected.mjs <ref>      compare with another commit
 *
 * WHY THIS EXISTS. Worker models driven through Marrow's API tool loop have write_file, edit_file
 * and run_command over the whole project folder. That is enough to edit the verification gate, the
 * specification, the quarantine list or a test's expectation until a failing task "passes".
 * PORT_CONTRACT section 14 forbids exactly that ("do not edit guard configuration to escape a
 * failure") and section 13 forbids changing an expectation merely because the port fails it.
 *
 * HOW IT WORKS. Workers never commit; the orchestrator reviews and commits. So every edit a worker
 * makes to a protected path is visible as a difference against HEAD, including an edit to THIS
 * script, which is itself protected. Exit 1 on any hit.
 *
 * WHAT IT DOES NOT DO. It is not tamper-proof: a worker that could also commit, or rewrite HEAD,
 * could defeat it. That is why workers must not commit (WORKER_BRIEF.md), and why the orchestrator
 * reads `git diff --stat` before every commit rather than trusting a green result.
 */

import { execFileSync } from 'node:child_process';
import { REF, HISTORY } from './baseline.mjs';

const ref = process.argv[2] || 'HEAD';

// Paths only the orchestrator may change. Directories end with a slash.
const PROTECTED = [
  // the contract and the owner's rulings
  'porting/PORT_CONTRACT.md', 'porting/contract_amendments.md', 'porting/owner_decisions.md',
  'porting/owner_decisions_pending.md', 'porting/instruction_conflicts.md', 'porting/baseline.md',
  // the specification: workers implement it, they do not edit it
  'porting/parity_matrix.json', 'porting/ui_contract.json', 'porting/spec/',
  'porting/blocked_requirements.json', 'porting/architecture.md', 'porting/platform_adaptations.md',
  'porting/unknowns.md', 'porting/upstream_defects.md', 'porting/dead_code.md',
  'porting/port_impacts.md', 'porting/engineering_decisions.md', 'porting/adr/',
  // the reading evidence
  'porting/source_inventory.json', 'porting/read_ledger.jsonl', 'porting/reports/',
  'porting/baseline.json', 'porting/baselines/',
  'porting/critiques/', 'porting/extracts/', 'porting/reference_captures/',
  // the gates and the tools that feed them
  'porting/tools/',
  // the work queue and packets
  'porting/tasks/',
  // build configuration and architecture guards
  'Directory.Build.props', 'Directory.Packages.props', 'Marrow.sln',
  'src/Marrow.Core/Marrow.Core.csproj', 'src/Marrow.Services/Marrow.Services.csproj',
  'src/Marrow.UI/Marrow.UI.csproj', 'src/Marrow.App/Marrow.App.csproj',
  'tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj',
  'tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj',
  'tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj',
  'tests/Marrow.App.Tests/Marrow.App.Tests.csproj',
  'tests/Marrow.Core.Tests/LayeringTests.cs', 'tests/Marrow.Services.Tests/LayeringTests.cs',
  'tests/Marrow.UI.Tests/LayeringTests.cs',
  // the instructions workers are given
  'HANDOFF.md', 'WORKER_BRIEF.md', 'CLAUDE.md', 'AGENTS.md', 'marrow.json',
  '.gitattributes', '.gitignore',
];

const git = (...a) => execFileSync('git', a, { encoding: 'utf8' }).split('\n').filter(Boolean);

let changed = [];
try {
  changed = git('diff', '--name-only', ref, '--', ...PROTECTED);
} catch (e) {
  console.error(`git diff against ${ref} failed: ${e.message}`);
  process.exit(2);
}
const untracked = git('ls-files', '--others', '--exclude-standard', '--', ...PROTECTED);
const staged = git('diff', '--name-only', '--cached', '--', ...PROTECTED);
const hits = [...new Set([...changed, ...untracked, ...staged])].sort();

// The pinned macOS reference must never be written to either, nor any earlier baseline's checkout
// (porting/baseline.json history), whose evidence the archived ledgers still point at.
let refDirty = '';
for (const refPath of [REF, ...HISTORY.map((h) => h.reference_path)]) {
  try {
    const st = execFileSync('git', ['-C', refPath, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
    if (st) refDirty += `${refPath}:\n${st}\n`;
  } catch { /* reference absent on this machine: reported by verify.mjs Gate A instead */ }
}
refDirty = refDirty.trim();

if (!hits.length && !refDirty) {
  console.log(`protected paths unchanged against ${ref}; reference checkout clean`);
  process.exit(0);
}
if (hits.length) {
  console.log(`PROTECTED PATHS CHANGED against ${ref} (${hits.length}):`);
  for (const h of hits) console.log(`  ${h}`);
  console.log('');
  console.log('A worker may not change these. Revert them before review:');
  console.log(`  git checkout ${ref} -- <path>        (tracked files)`);
  console.log('  and delete any new untracked file listed above.');
  console.log('If the change is genuinely needed, the ORCHESTRATOR makes it, with a reason in the commit.');
}
if (refDirty) {
  console.log('');
  console.log('THE PINNED macOS REFERENCE HAS BEEN MODIFIED. It is read-only:');
  console.log(refDirty.split('\n').map((l) => `  ${l}`).join('\n'));
}
process.exit(1);
