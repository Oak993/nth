#!/usr/bin/env node
/**
 * Generates self-contained task packets for worker models from the parity matrix and the UI
 * contract. Each packet is small enough for a free model's context, carries the exact requirement
 * records it must satisfy, names the source to read and the Windows files to write, and lists
 * anything quarantined that it must NOT implement.
 *
 *   node porting/tools/gen_tasks.mjs
 *
 * Writes porting/tasks/T-*.md, porting/tasks/queue.json and porting/tasks/INDEX.md.
 * Re-running regenerates packets but PRESERVES queue status the orchestrator has set.
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync, readdirSync, unlinkSync } from 'node:fs';
import { REF } from './baseline.mjs';

const MAX_REQ = 10;          // requirements per packet: sized so the packet plus its source fit a free model
const MAX_SRC_LINES = 1400;  // source lines a worker must read per packet
const MAX_UI = 10;          // UI records per packet; they get their own packets so they never bloat a behaviour packet

const matrix = JSON.parse(readFileSync('porting/parity_matrix.json', 'utf8'));
const ui = JSON.parse(readFileSync('porting/ui_contract.json', 'utf8'));
const inv = JSON.parse(readFileSync('porting/source_inventory.json', 'utf8'));
const blocked = JSON.parse(readFileSync('porting/blocked_requirements.json', 'utf8'));
const lines = new Map(inv.items.map((i) => [i.path, i.line_count || 0]));
const openBlocks = new Map(blocked.entries.filter((e) => e.status === 'OPEN').map((e) => [e.id, e]));

// ---- where each source file lands on Windows (ENGINEERING DECISION, see HANDOFF.md) ----
function target(src) {
  const m = src.match(/^DroppyCode\/(Core|Services|UI|App)\/(.+)\.swift$/);
  if (!m) return null;
  const project = { Core: 'Marrow.Core', Services: 'Marrow.Services', UI: 'Marrow.UI', App: 'Marrow.App' }[m[1]];
  return `src/${project}/${m[2]}.cs`;
}

// ---- milestone by source layer -------------------------------------------------------
function milestone(src) {
  if (/^DroppyCode\/Core\//.test(src)) return 'M2';
  if (/^DroppyCode\/Services\/(Store|Git)\//.test(src)) return 'M3';
  if (/^DroppyCode\/Services\/(Providers|MCP)\//.test(src)) return 'M4';
  if (/^DroppyCode\/UI\//.test(src)) return 'M5';
  if (/^DroppyCode\/App\/(UpdateChecker|AppUpdater)/.test(src)) return 'M8';
  if (/^DroppyCode\/App\/Views\//.test(src)) return 'M7';
  if (/^DroppyCode\/App\//.test(src)) return 'M6';
  return 'M6';
}
const MILESTONES = {
  M2: 'Core models and pure support',
  M3: 'Services foundation: store and git',
  M4: 'Providers and MCP',
  M5: 'UI toolkit: theme, chrome, glass, markdown, sound',
  M6: 'App shell, windows, model and runtime',
  M7: 'Feature views',
  M8: 'Update and packaging',
};

// ---- risk: which packets a free model may draft but the orchestrator must review line by line
const HIGH = [
  [/App\/Runtime\/ThreadRuntime\.swift/, 'core event dispatch and ordering invariants'],
  [/Core\/Support\/(Shell|StdioProcess|StdioFramer|JSONRPC)\.swift/, 'process lifecycle, framing, termination'],
  [/Services\/Git\/(Git|WorkingTreeWatch|TurnDiff|RevertPreview)\.swift/, 'checkpoints and revert are data-loss adjacent'],
  [/App\/AppModel\+HydraMerge\.swift/, 'landing work into the user checkout'],
  [/Services\/MCP\/(MCPOAuth|MCPProxy|MCPKeychain)\.swift/, 'OAuth, loopback proxy and secrets'],
  [/Keychain\.swift/, 'secret storage'],
  [/App\/AppUpdater\.swift/, 'installs code; signature verification'],
  [/App\/Views\/Terminal\//, 'ConPTY terminal'],
  [/UI\/Chrome\/(Chrome|WindowChrome)\.swift/, 'glass compositor and window chrome'],
  [/UI\/Common\/Genie\.swift/, 'shader distortion'],
  [/App\/Window\//, 'native window construction'],
];
function risk(srcs) {
  for (const s of srcs) for (const [re, why] of HIGH) if (re.test(s)) return { level: 'HIGH', why };
  return { level: 'STANDARD', why: '' };
}

const primarySource = (r) => {
  for (const e of r.source_evidence || []) {
    const m = String(e).match(/DroppyCode\/[A-Za-z0-9_+/.-]+\.swift/);
    if (m) return m[0];
  }
  return 'misc';
};

// ---- group requirements by primary source file, then chunk ------------------------------
// Retired and not-applicable records are kept in the spec for the record but are not work.
const OUT_OF_SCOPE = new Set(['RETIRED', 'NOT_APPLICABLE_WITH_EVIDENCE']);
const DONE_STATUS = new Set(['IMPLEMENTED', 'VERIFIED']);
const byFile = new Map();
for (const r of matrix.requirements.filter((x) => !OUT_OF_SCOPE.has(x.status))) {
  const p = primarySource(r);
  if (!byFile.has(p)) byFile.set(p, []);
  byFile.get(p).push(r);
}

const uiByFile = new Map();
for (const u of ui.records.filter((x) => !OUT_OF_SCOPE.has(x.status))) {
  for (const e of u.source_evidence || []) {
    const m = String(e).match(/DroppyCode\/[A-Za-z0-9_+/.-]+\.swift/);
    if (!m) continue;
    if (!uiByFile.has(m[0])) uiByFile.set(m[0], []);
    uiByFile.get(m[0]).push(u);
    break;
  }
}

const packets = [];
let order = ['M2', 'M3', 'M4', 'M5', 'M6', 'M7', 'M8'];
const files = [...byFile.keys()].sort((a, b) => {
  const d = order.indexOf(milestone(a)) - order.indexOf(milestone(b));
  return d || (a < b ? -1 : 1);
});

const counters = {};
for (const f of files) {
  const reqs = byFile.get(f).sort((a, b) => (a.id < b.id ? -1 : 1));
  const uis = uiByFile.get(f) || [];
  const ms = milestone(f);
  const chunks = [];
  for (let i = 0; i < reqs.length; i += MAX_REQ) chunks.push(reqs.slice(i, i + MAX_REQ));
  chunks.forEach((chunk, ci) => {
    counters[ms] = (counters[ms] || 0) + 1;
    const id = `T-${ms}-${String(counters[ms]).padStart(3, '0')}`;
    const r = risk([f]);
    const uiSlice = [];
    const blockedHere = chunk.filter((q) => q.blocked_by && openBlocks.has(q.blocked_by));
    packets.push({
      id, milestone: ms, source: f, source_lines: lines.get(f) || 0,
      target: target(f), part: chunks.length > 1 ? `${ci + 1} of ${chunks.length}` : null,
      risk: r.level, risk_why: r.why,
      requirements: chunk, ui: uiSlice, blocked: blockedHere, kind: 'behaviour',
    });
  });
  // UI contract records for this file, in their own packets, after the behaviour packets.
  for (let i = 0; i < uis.length; i += MAX_UI) {
    counters[ms] = (counters[ms] || 0) + 1;
    const id = `T-${ms}-${String(counters[ms]).padStart(3, '0')}`;
    const r = risk([f]);
    packets.push({
      id, milestone: ms, source: f, source_lines: lines.get(f) || 0, target: target(f),
      part: uis.length > MAX_UI ? `UI ${Math.floor(i / MAX_UI) + 1} of ${Math.ceil(uis.length / MAX_UI)}` : 'UI',
      risk: r.level, risk_why: r.why, requirements: [], ui: uis.slice(i, i + MAX_UI), blocked: [], kind: 'ui',
    });
  }
}
// UI records whose source file has no behaviour requirements still need a home.
for (const [f, uis] of uiByFile) {
  if (byFile.has(f)) continue;
  const ms = milestone(f);
  for (let i = 0; i < uis.length; i += MAX_UI) {
    counters[ms] = (counters[ms] || 0) + 1;
    const id = `T-${ms}-${String(counters[ms]).padStart(3, '0')}`;
    packets.push({
      id, milestone: ms, source: f, source_lines: lines.get(f) || 0, target: target(f), part: 'UI',
      risk: risk([f]).level, risk_why: risk([f]).why, requirements: [], ui: uis.slice(i, i + MAX_UI), blocked: [], kind: 'ui',
    });
  }
}


// ---- M0: the probes ADR-0002 requires before production code -------------------------------
// P1 already passed (porting/adr/0002-stack-avalonia.md). These are the other ten, verbatim from the
// judge's decision, with who can run them: several need a visible, UNLOCKED desktop window, which a
// headless worker cannot provide (probe acrylic_window.ps1 proved this machine's locked session
// composites nothing).
const decision = JSON.parse(readFileSync('porting/critiques/stack_decision.json', 'utf8')).decision || {};
const NEEDS_DESKTOP = new Set(['P2', 'P3', 'P4', 'P7']);
const HIGH_PROBES = new Set(['P5', 'P6']);
const probes = (decision.probes_before_committing || []).map((text) => {
  const pid = (String(text).match(/^\s*(P\d+)/) || [])[1];
  return { pid, text: String(text) };
}).filter((x) => x.pid && x.pid !== 'P1');
const m0 = probes.map((pr, i) => ({
  id: `T-M0-${String(i + 1).padStart(3, '0')}`, milestone: 'M0', source: `probe ${pr.pid}`,
  source_lines: 0, target: `porting/probes/${pr.pid.toLowerCase()}/`, part: null,
  risk: HIGH_PROBES.has(pr.pid) ? 'HIGH' : 'STANDARD',
  risk_why: HIGH_PROBES.has(pr.pid) ? 'process control: ConPTY, Job Objects, termination' : '',
  requirements: [], ui: [], blocked: [], kind: 'probe', probe: pr,
  needs_desktop: NEEDS_DESKTOP.has(pr.pid),
}));
packets.unshift(...m0);
MILESTONES.M0 = 'Probes that must pass before production code (ADR-0002)';
order.unshift('M0');

// ---- preserve orchestrator-set status across regeneration -------------------------------
mkdirSync('porting/tasks', { recursive: true });
const qPath = 'porting/tasks/queue.json';
const prior = existsSync(qPath) ? JSON.parse(readFileSync(qPath, 'utf8')) : { tasks: [] };
const priorStatus = new Map(prior.tasks.map((t) => [t.id, t]));
for (const f of readdirSync('porting/tasks')) if (/^T-M\d-\d{3}\.md$/.test(f)) unlinkSync(`porting/tasks/${f}`);

const fmtReq = (q) => {
  const out = [];
  out.push(`### ${q.id} - ${q.title}`);
  if (q.blocked_by && openBlocks.has(q.blocked_by)) {
    out.push(`> **DO NOT IMPLEMENT. Quarantined by ${q.blocked_by} (owner decision pending).** ` +
      `Leave a clearly marked TODO referencing ${q.blocked_by} and move on. Gate C fails if you implement it.`);
  }
  out.push(`- **Source:** ${(q.source_evidence || []).slice(0, 4).join(' | ')}`);
  if (q.preconditions) out.push(`- **Preconditions:** ${q.preconditions}`);
  out.push(`- **Action:** ${q.action}`);
  out.push(`- **Expected:** ${q.expected_result}`);
  if ((q.side_effects || []).length) out.push(`- **Side effects:** ${q.side_effects.join('; ')}`);
  if (q.persistence) out.push(`- **Persistence:** ${q.persistence}`);
  if ((q.failure_cases || []).length) out.push(`- **Failure cases:** ${q.failure_cases.join('; ')}`);
  if (q.cancellation) out.push(`- **Cancellation:** ${q.cancellation}`);
  if (q.windows_path) out.push(`- **Windows path:** ${q.windows_path}`);
  out.push(`- **Verify by:** ${q.verification_method}`);
  if (q.upstream_defect) out.push(`- **Upstream defect ${q.upstream_defect}: reproduce it faithfully, do not fix it.**`);
  if (q.depends_on_unknown) out.push(`- **Depends on ${q.depends_on_unknown}**, which is unresolved. Implement only the part the source settles; flag the rest.`);
  return out.join('\n');
};

const fmtUi = (u) => {
  const parts = [`- **${u.id}** ${u.screen} / ${u.element} (${u.role || 'element'})`];
  if (u.exact_text) parts.push(`  - text, verbatim: \`${u.exact_text}\``);
  if (u.geometry) parts.push(`  - geometry: ${u.geometry}`);
  if (u.typography) parts.push(`  - type: ${u.typography}`);
  if (u.colors) parts.push(`  - colour: ${u.colors}`);
  if ((u.states || []).length) parts.push(`  - states: ${u.states.join('; ')}`);
  if ((u.interactions || []).length) parts.push(`  - interactions: ${u.interactions.join('; ')}`);
  return parts.join('\n');
};

for (const p of packets) {
  if (p.kind === 'probe') {
    const md = [];
    md.push(`# ${p.id}: ${p.probe.pid}`);
    md.push('');
    md.push(`| Field | Value |`);
    md.push(`| --- | --- |`);
    md.push(`| Milestone | M0: probes that must pass before production code |`);
    md.push(`| Risk | **${p.risk}**${p.risk_why ? ` - ${p.risk_why}` : ''} |`);
    md.push(`| Who can run it | ${p.needs_desktop ? '**A HUMAN at an unlocked Windows desktop.** It needs a visible, composited window. A headless worker cannot do it: a locked or disconnected session composites nothing, which was measured on this machine.' : 'A worker model, headless.'} |`);
    md.push(`| Output folder | \`${p.target}\` (a throwaway probe, never production code) |`);
    md.push('');
    md.push('## The probe, verbatim from the stack decision');
    md.push('');
    md.push(p.probe.text);
    md.push('');
    md.push('## Rules');
    md.push('');
    md.push('- Read `WORKER_BRIEF.md` first. Model the probe on `porting/probes/p1-glass/`, which passed.');
    md.push('- A probe PROVES or DISPROVES one capability. Report the measured result, whichever it is.');
    md.push('  A FAIL reported honestly is a valuable result. A PASS you did not measure is worthless.');
    md.push('- Build a control case that must fail when the thing under test is absent, as P1 did with');
    md.push('  its tint-only render. Without one, a PASS cannot be trusted.');
    md.push('- Never send a prompt to a real AI CLI; `--version` or `--help` only.');
    md.push('- Set `MARROW_PORT_DATA_DIR` to a throwaway folder before running anything.');
    writeFileSync(`porting/tasks/${p.id}.md`, md.join('\n') + '\n');
    continue;
  }
  const md = [];
  md.push(`# ${p.id}: ${p.source.replace(/^DroppyCode\//, '')}${p.part ? ` (part ${p.part})` : ''}`);
  md.push('');
  md.push(`| Field | Value |`);
  md.push(`| --- | --- |`);
  md.push(`| Milestone | ${p.milestone}: ${MILESTONES[p.milestone]} |`);
  md.push(`| Risk | **${p.risk}**${p.risk_why ? ` - ${p.risk_why}` : ''} |`);
  md.push(`| Read (pinned source) | \`${REF}/${p.source}\` (${p.source_lines} lines, read it ALL) |`);
  md.push(`| Write (Windows) | \`${p.target || 'decide with the orchestrator'}\` plus tests under \`tests/\` |`);
  md.push(`| Requirements | ${p.requirements.length}${p.blocked.length ? ` (${p.blocked.length} quarantined, skip them)` : ''} |`);
  md.push(`| UI records | ${p.ui.length} |`);
  md.push('');
  md.push('## Before you start');
  md.push('');
  md.push('1. Read `WORKER_BRIEF.md` at the project root. Its rules override anything you assume.');
  md.push(`2. Read the source file above completely. Every behaviour you write must trace to a line in it.`);
  md.push('3. Write the tests FIRST, from the "Expected" text below, and stop. The orchestrator reviews the');
  md.push('   tests before you implement, so a test that just restates your implementation is caught early.');
  md.push('4. Then implement until those tests pass. Do not edit a test to make it pass.');
  md.push('');
  md.push('## Done means');
  md.push('');
  md.push('- `dotnet build Marrow.sln -c Debug` succeeds with no new warnings.');
  md.push('- `dotnet test` passes, including your new tests, and none were skipped or weakened.');
  md.push('- `node porting/tools/verify.mjs --gate A` and `--gate B` still pass.');
  md.push('- `node porting/tools/check_protected.mjs` reports nothing changed.');
  md.push('- Your report lists: each requirement id and the test that proves it; anything you could not');
  md.push('  settle from source; every place you had to guess (there should be none).');
  md.push('');
  md.push('## Requirements');
  md.push('');
  md.push(p.requirements.map(fmtReq).join('\n\n'));
  if (p.ui.length) {
    md.push('');
    md.push('## UI contract records');
    md.push('');
    md.push('Exact values. Do not round, restyle or substitute. Where a value is macOS-delegated, say so.');
    md.push('');
    md.push(p.ui.map(fmtUi).join('\n'));
  }
  writeFileSync(`porting/tasks/${p.id}.md`, md.join('\n') + '\n');
}

const queue = {
  schema: 'droppy-windows-port/task_queue/1',
  note: 'Status is set by the ORCHESTRATOR only: TODO, ASSIGNED, TESTS_REVIEW, IMPLEMENTING, REVIEW, DONE, REJECTED. Regenerating packets preserves it.',
  tasks: packets.map((p) => {
    const was = priorStatus.get(p.id);
    return {
      id: p.id, milestone: p.milestone, source: p.source, target: p.target, risk: p.risk,
      requirements: p.requirements.map((q) => q.id), ui: p.ui.map((u) => u.id),
      quarantined: p.blocked.map((q) => q.id),
      needs_desktop: Boolean(p.needs_desktop), kind: p.kind,
      // Status is DERIVED from the records, never carried by packet id alone: packet ids are
      // sequential and shift when a rebaseline adds or retires requirements, so a carried status
      // could land on a different packet. DONE means every record in it is IMPLEMENTED or VERIFIED.
      status: [...p.requirements, ...p.ui].length && [...p.requirements, ...p.ui].every((x) => DONE_STATUS.has(x.status))
        ? 'DONE'
        : (was && was.status !== 'DONE' && JSON.stringify(was.requirements) === JSON.stringify(p.requirements.map((q) => q.id)) ? was.status : 'TODO'),
      assignee: was?.assignee || null, notes: was?.notes || '',
    };
  }),
};
writeFileSync(qPath, JSON.stringify(queue, null, 2) + '\n');

// ---- index ------------------------------------------------------------------------------
const idx = ['# Task index', '', 'Generated by `porting/tools/gen_tasks.mjs` from the parity matrix and UI contract.', ''];
idx.push('Work the milestones in order. Within a milestone, packets are independent unless they share a');
idx.push('target file, in which case do them in id order. HIGH-risk packets need the orchestrator to review');
idx.push('every line, not a sample.', '');
idx.push('| Milestone | Packets | Requirements | HIGH risk | Quarantined reqs |');
idx.push('| --- | --- | --- | --- | --- |');
for (const ms of order) {
  const ps = packets.filter((p) => p.milestone === ms);
  if (!ps.length) continue;
  idx.push(`| ${ms} ${MILESTONES[ms]} | ${ps.length} | ${ps.reduce((n, p) => n + p.requirements.length, 0)} | ` +
    `${ps.filter((p) => p.risk === 'HIGH').length} | ${ps.reduce((n, p) => n + p.blocked.length, 0)} |`);
}
idx.push(`| **Total** | **${packets.length}** | **${packets.reduce((n, p) => n + p.requirements.length, 0)}** | ` +
  `**${packets.filter((p) => p.risk === 'HIGH').length}** | **${packets.reduce((n, p) => n + p.blocked.length, 0)}** |`);
idx.push('');
for (const ms of order) {
  const ps = packets.filter((p) => p.milestone === ms);
  if (!ps.length) continue;
  idx.push(`## ${ms}: ${MILESTONES[ms]}`, '');
  idx.push('| Task | Source | Reqs | Risk | Target |');
  idx.push('| --- | --- | --- | --- | --- |');
  for (const p of ps) {
    idx.push(`| [${p.id}](${p.id}.md) | ${p.source.replace(/^DroppyCode\//, '')}${p.part ? ` (${p.part})` : ''} | ` +
      `${p.requirements.length}${p.blocked.length ? ` (${p.blocked.length} blocked)` : ''} | ${p.risk} | \`${p.target || '-'}\` |`);
  }
  idx.push('');
}
writeFileSync('porting/tasks/INDEX.md', idx.join('\n') + '\n');

console.log(`packets: ${packets.length}`);
for (const ms of order) {
  const ps = packets.filter((p) => p.milestone === ms);
  if (ps.length) console.log(`  ${ms} ${ps.length} packets, ${ps.reduce((n, p) => n + p.requirements.length, 0)} reqs, ${ps.filter((p) => p.risk === 'HIGH').length} HIGH`);
}
const total = packets.reduce((n, p) => n + p.requirements.length, 0);
const uiTotal = packets.reduce((n, p) => n + p.ui.length, 0);
const uiWithFile = [...uiByFile.values()].reduce((n, a) => n + a.length, 0);
const inScope = matrix.requirements.filter((x) => !OUT_OF_SCOPE.has(x.status)).length;
const uiInScope = ui.records.filter((x) => !OUT_OF_SCOPE.has(x.status)).length;
console.log(`requirements covered: ${total} of ${inScope} in scope (${matrix.requirements.length - inScope} retired or not applicable)`);
console.log(`ui records covered:   ${uiTotal} of ${uiInScope} in scope (${ui.records.length - uiWithFile} cite no .swift file)`);
if (total !== inScope) { console.error('COVERAGE MISMATCH'); process.exit(1); }
