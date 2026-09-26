// Builds one work packet per B3 delta lane (B3_PLAN.md section 2.A entries A1-A43): the plan entry, the lane's Swift
// files and C# targets, every record the lane must change (b3_note set, newly created since --base, or not DONE),
// the 1.6.2 -> 1.7.0 Swift diff, and the B3 Swift source (whole when small).
//   node E:/Droppy/work/b3/make_lanes.mjs --base <commit before the Gate B spec merge> [--out E:/Droppy/work/b3/lanes] [--only A1,A2]
// Run from a port worktree whose porting/spec holds the merged B3 spec and porting/deps/units.json the B3 plan.
import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const arg = (n, d) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : d; };
const BASE = arg('--base');
const OUT = arg('--out', 'E:/Droppy/work/b3/lanes');
const ONLY = arg('--only', null)?.split(',');
const PLAN = 'E:/Droppy/work/b3/B3_PLAN.md';
const REF = 'E:/Droppy/reference/droppy-code-v1.7.0';
const B2 = '1fbf81e61e28a079508a074c01ee1830a9cc23dc';
const SMALL = 60_000;
if (!BASE) throw new Error('--base is required');
const sh = (c) => execSync(c, { encoding: 'utf8', maxBuffer: 1 << 28 });

// Records now, and the ids that existed at --base.
const records = {}, primary = {};
const SWIFT = /DroppyCode\/[A-Za-z0-9_+/.-]+\.swift/;
for (const f of fs.readdirSync('porting/spec').filter((f) => f.endsWith('.json'))) {
  const j = JSON.parse(fs.readFileSync(path.join('porting/spec', f), 'utf8'));
  for (const r of [...(j.requirements || []), ...(j.ui || [])]) {
    records[r.id] = { ...r, _area: f };
    for (const e of r.source_evidence || []) { const m = SWIFT.exec(String(e)); if (m) { primary[r.id] = m[0]; break; } }
  }
}
const baseIds = new Set();
for (const f of sh(`git -c safe.directory=* ls-tree --name-only ${BASE} porting/spec/`).split('\n').filter((p) => p.endsWith('.json'))) {
  const j = JSON.parse(sh(`git -c safe.directory=* show ${BASE}:${f}`));
  for (const r of [...(j.requirements || []), ...(j.ui || [])]) baseIds.add(r.id);
}
const units = JSON.parse(fs.readFileSync('porting/deps/units.json', 'utf8')).units.filter((u) => u.swift);
const byId = Object.fromEntries(units.map((u) => [u.id, u]));
const resolveName = (name) => {
  if (byId[name]) return byId[name];
  const hits = units.filter((u) => u.id.endsWith('.' + name));
  return hits.length === 1 ? hits[0] : null;
};

// Plan entries: from "**A<n>. " to the next entry or heading.
const lines = fs.readFileSync(PLAN, 'utf8').replace(/\r\n/g, '\n').split('\n');
const start = lines.findIndex((l) => l.startsWith('### 2.A'));
const end = lines.findIndex((l, i) => i > start && l.startsWith('### 2.B'));
const entries = [];
for (let i = start; i < end; i++) {
  const m = /^\*\*(A\d+)\. /.exec(lines[i]);
  if (!m) continue;
  let j = i + 1;
  while (j < end && !/^\*\*A\d+\. /.test(lines[j]) && !lines[j].startsWith('#')) j++;
  entries.push({ id: m[1], text: lines.slice(i, j).join('\n').trim() });
}

fs.mkdirSync(OUT, { recursive: true });
const index = [];
for (const e of entries) {
  if (ONLY && !ONLY.includes(e.id)) continue;
  // Units named in bold in the entry's heading paragraph (up to the first bullet).
  const head = e.text.split('\n- ')[0];
  const names = [...head.matchAll(/\*\*([^*]+)\*\*/g)].map((m) => m[1].replace(/^A\d+\.\s*/, '').trim());
  const lane = [...new Set(names.map(resolveName).filter(Boolean))];
  const swift = new Set(lane.map((u) => u.swift));
  const recs = Object.values(records).filter((r) => swift.has(primary[r.id]) &&
    (r.b3_note || !baseIds.has(r.id) || !['IMPLEMENTED', 'VERIFIED', 'RETIRED', 'NOT_APPLICABLE_WITH_EVIDENCE'].includes(r.status)));
  const out = [`# B3 lane ${e.id}`, '', `Units: ${lane.map((u) => u.id).join(', ') || '(none resolved: read the plan entry)'}`, ''];
  out.push('## Plan entry (E:/Droppy/work/b3/B3_PLAN.md section 2.A)', '', e.text, '');
  out.push('## Swift files and C# targets', '');
  for (const u of lane) out.push(`- \`${u.swift}\` -> \`${u.target}\` (tests: ${u.test_project})`);
  out.push('', `## Records this lane must satisfy (${recs.length}; b3_note set, created at B3, or not yet IMPLEMENTED)`, '');
  for (const r of recs.sort((a, b) => a.id.localeCompare(b.id))) {
    const { _area, ...rec } = r;
    out.push(`### ${r.id} (${_area}, ${r.status}${baseIds.has(r.id) ? '' : ', NEW at B3'})`, '', '```json', JSON.stringify(rec, null, 2), '```', '');
  }
  for (const u of lane) {
    const st = sh(`git -c safe.directory=* -C "${REF}" diff --name-status ${B2} HEAD -- "${u.swift}"`).trim();
    out.push(`## 1.6.2 -> 1.7.0 diff of ${u.swift} (${st ? st.split('\t')[0] : 'unchanged'})`, '');
    if (st) out.push('```diff', sh(`git -c safe.directory=* -C "${REF}" diff -U4 ${B2} HEAD -- "${u.swift}"`).trimEnd(), '```', '');
    const p = path.join(REF, u.swift);
    const src = fs.readFileSync(p, 'utf8');
    if (src.length <= SMALL) {
      out.push(`## B3 source: ${u.swift} (whole file, numbered)`, '', '```swift', src.replace(/\r\n/g, '\n').split('\n').map((l, k) => `${String(k + 1).padStart(4)}  ${l}`).join('\n'), '```', '');
    } else {
      out.push(`## B3 source: ${u.swift}`, '', `${src.length} bytes: read it from \`${p}\` (the diff above shows every changed line).`, '');
    }
  }
  fs.writeFileSync(path.join(OUT, `${e.id}.md`), out.join('\n'));
  index.push({ lane: e.id, units: lane.map((u) => u.id), records: recs.length, new: recs.filter((r) => !baseIds.has(r.id)).map((r) => r.id), bytes: out.join('\n').length });
}
fs.writeFileSync(path.join(OUT, 'index.json'), JSON.stringify(index, null, 1));
console.log(index.map((x) => `${x.lane}\t${x.units.join(',')}\t${x.records} recs\t${Math.round(x.bytes / 1024)} KiB`).join('\n'));
