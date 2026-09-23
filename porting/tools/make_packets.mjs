// Builds one small work packet per unit part, so an implementation agent reads what its part needs instead of the
// whole rulebook, the whole spec and the whole Swift file.
//
//   node porting/tools/make_packets.mjs [--out E:/Droppy/work/packets] [--rulings E:/Droppy/work/rulings/unit_rulings.json] [--units id,id,...]
//
// A packet holds: the part's records (exact JSON), the Swift source (whole file when small; otherwise a declaration
// outline plus the cited line ranges), the IMPLEMENTING.md sections for the constructs the Swift uses, every ED, OD
// and PA entry that names the file or the records, the public C# signatures of the unit's dependencies already in
// the tree, and any rulings recorded for the unit. Run from the port root. It reads only; it writes only --out.
import fs from 'node:fs';
import path from 'node:path';

const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : dflt; };
const OUT = arg('--out', 'E:/Droppy/work/packets');
const RULINGS = arg('--rulings', null);
const ONLY = arg('--units', null)?.split(',');
const REF = JSON.parse(fs.readFileSync('porting/baseline.json', 'utf8')).current.reference_path;
const SMALL_SWIFT = 60_000;   // bytes: below this the whole Swift file goes in the packet
const CONTEXT = 12;           // lines of context around each cited range

const plan = JSON.parse(fs.readFileSync('porting/deps/units.json', 'utf8'));
const byId = Object.fromEntries(plan.units.map(u => [u.id, u]));
const foundation = JSON.parse(fs.readFileSync('porting/deps/foundation_units.json', 'utf8')).units;
const fById = Object.fromEntries(foundation.map(f => [f.id, f]));
const rulings = RULINGS && fs.existsSync(RULINGS) ? JSON.parse(fs.readFileSync(RULINGS, 'utf8')) : {};

// ---- records ----
const records = {};
for (const f of fs.readdirSync('porting/spec').filter(f => f.endsWith('.json'))) {
  const j = JSON.parse(fs.readFileSync(path.join('porting/spec', f), 'utf8'));
  for (const r of [...(j.requirements || []), ...(j.ui || [])]) records[r.id] = r;
}

// ---- IMPLEMENTING.md sections ----
const DIGEST = arg('--digest', 'E:/Droppy/work/packets/IMPLEMENTING.digest.md');
const useDigest = fs.existsSync(DIGEST);
const impLines = fs.readFileSync(useDigest ? DIGEST : 'porting/IMPLEMENTING.md', 'utf8').replace(/\r\n/g, '\n').split('\n');
const sections = {};
{
  let key = null, buf = [];
  const flush = () => { if (key) sections[key] = buf.join('\n').trimEnd(); };
  for (const line of impLines) {
    const h3 = line.match(/^### (\d+(?:\.\d+)?)\.? /), h2 = line.match(/^## (\d+)\. /), app = line.match(/^## Appendix/);
    if (h3 || h2 || app) { flush(); key = h3 ? h3[1] : h2 ? h2[1] : null; buf = []; }
    if (key) buf.push(line);
  }
  flush();
}
const ALWAYS = ['1.3', '1.5', '2.1', '2.2', '2.3', '2.4', '2.5', '2.6', '2.7', '5.1', '6'];
const TRIGGERS = [
  ['3.2', /@MainActor|\bMainActor\b|\bnonisolated\b/],
  ['3.3', /\basync\b|\bawait\b|\bTask\s*[{.(]|withTaskGroup|\.sleep\(/],
  ['3.4', /\bactor\s+[A-Z]/],
  ['3.5', /AsyncStream|AsyncThrowingStream|withChecked(Throwing)?Continuation/],
  ['3.6', /DispatchQueue|NSLock|\bMutex\b|UnfairLock|NotificationCenter|DispatchSource|FSEvent|\bThread\./],
  ['3.7', /@Observable|@ObservationIgnored|:\s*View\b|some View|@State\b|@Binding\b|@Environment\b|withAnimation/],
  ['3.8', /\bCodable\b|\bDecodable\b|\bEncodable\b|JSONDecoder|JSONEncoder|JSONSerialization|CodingKeys/],
  ['3.9', /\benum\s|\bprotocol\s|\bstruct\s|\bextension\s/],
  ['3.10', /\bString\b|\bCharacter\b|\bRegex\b|NSRegularExpression|hasPrefix|lowercased|trimmingCharacters|\.split\(/],
  ['3.11', /\bDouble\b|\bInt\(|formatted|NumberFormatter|rounded|\bCGFloat\b/],
  ['3.12', /\bUUID\b|\bDate\b|ISO8601|TimeInterval|\bDuration\b|ContinuousClock/],
  ['3.13', /FileManager|\bURL\b|\bProcess\(|UserDefaults|SecItem|URLSession|Bundle\.main/],
  ['3.14', /\bthrows\b|\bthrow\s|\btry[?!]?\s|\bdefer\b|\bguard\b/],
];

// ---- decision entries ----
function entries(file, prefix) {
  const out = {};
  if (!fs.existsSync(file)) return out;
  const text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  const re = new RegExp(`^## (${prefix}-\\d+)[^\\n]*$`, 'gm');
  const heads = [...text.matchAll(re)];
  heads.forEach((m, i) => { out[m[1]] = text.slice(m.index, i + 1 < heads.length ? heads[i + 1].index : text.length).trim(); });
  return out;
}
const EDS = entries('porting/engineering_decisions.md', 'ED');
const ODS = entries('porting/owner_decisions.md', 'OD');
const PAS = entries('porting/platform_adaptations.md', 'PA');
const ALL_DECISIONS = { ...EDS, ...ODS, ...PAS };

// ---- C# signatures of what a unit depends on ----
function signatures(target) {
  if (!target || !fs.existsSync(target)) return null;
  const keep = [];
  for (const line of fs.readFileSync(target, 'utf8').split('\n')) {
    const t = line.trim();
    if (/^namespace\s/.test(t)) keep.push(t);
    else if (/^(public|internal|protected)\b/.test(t) && !/^(public|internal)\s+(override\s+)?string\?\s+ErrorDescription\s*=>/.test(t)) keep.push(line.replace(/\s*(=>|\{)\s*[^;]*$/, t.endsWith(';') && !t.includes('=>') ? '' : ' { ... }').trimEnd());
    if (keep.length >= 160) { keep.push('    ... (more; read the file for the rest)'); break; }
  }
  return keep.join('\n');
}

// ---- Swift source for a part ----
function swiftFor(u, partIds) {
  const file = path.join(REF, u.swift);
  const text = fs.readFileSync(file, 'utf8');
  const lines = text.split('\n');
  const num = (i) => `${String(i + 1).padStart(5)}  ${lines[i]}`;
  if (Buffer.byteLength(text) <= SMALL_SWIFT) return { mode: 'whole file', body: lines.map((_, i) => num(i)).join('\n') };
  const base = u.swift.replace(/\\/g, '/');
  const want = new Set();
  const cite = new RegExp(base.replace(/[.+]/g, m => '\\' + m) + ':(\\d+)(?:-(\\d+))?', 'g');
  for (const id of partIds) for (const m of JSON.stringify(records[id] || {}).matchAll(cite)) {
    const a = +m[1], b = +(m[2] || m[1]);
    for (let i = Math.max(1, a - CONTEXT); i <= Math.min(lines.length, b + CONTEXT); i++) want.add(i - 1);
  }
  const outline = [];
  lines.forEach((l, i) => { if (/^\s*((public|private|fileprivate|internal|static|final|override|nonisolated|@MainActor|mutating)\s+)*(class|struct|enum|actor|protocol|extension|func|init|case|(static\s+)?(let|var))\b/.test(l) && !/^\s{12,}/.test(l)) outline.push(num(i)); });
  const ranges = [...want].sort((a, b) => a - b);
  const body = [];
  let prev = -2;
  for (const i of ranges) { if (i !== prev + 1) body.push('   ...'); body.push(num(i)); prev = i; }
  return { mode: `large file (${lines.length} lines): declaration outline, then the lines the records cite with ${CONTEXT} lines of context; read other lines with sed -n 'a,bp' "${file}"`,
           body: `--- outline ---\n${outline.join('\n')}\n--- cited lines ---\n${body.join('\n')}` };
}

function decisionIdsIn(text) { return [...new Set([...text.matchAll(/\b(ED-\d{3}|OD-\d{4}|PA-\d{4})\b/g)].map(m => m[1]))]; }

function packet(u, k) {
  const part = u.parts[k] || [];
  const isF = u.kind === 'foundation';
  const f = isF ? fById[u.id] : null;
  const out = [];
  out.push(`# Work packet: ${u.id}, part ${k + 1} of ${u.parts.length}`);
  out.push('');
  out.push(`- Target: ${u.target} (project ${u.project}); tests in ${u.test_project}. Risk ${u.risk}.`);
  if (!isF) out.push(`- Swift: ${u.swift} (B2 1fbf81e) in ${REF}.`);
  out.push(`- Records in this part: ${part.length ? part.join(', ') : '(foundation unit: the spec below)'}.`);
  out.push('- This packet is your context. Do not read porting/IMPLEMENTING.md, porting/engineering_decisions.md or porting/spec/*.json in full: grep them for a specific id or term only when the packet does not answer a question.');
  out.push('');
  const ruling = rulings[u.id];
  if (ruling) { out.push('## Rulings for this unit (binding; they override anything older)'); out.push(''); out.push(typeof ruling === 'string' ? ruling : JSON.stringify(ruling, null, 1)); out.push(''); }

  let swiftText = '';
  if (isF) {
    out.push('## Foundation spec'); out.push(''); out.push(f.spec); out.push('');
    const sites = [];
    const walk = (dir) => { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { const p = path.join(dir, e.name); if (e.isDirectory()) walk(p); else if (p.endsWith('.swift')) { const lines = fs.readFileSync(p, 'utf8').split('\n'); lines.forEach((l, i) => { if (f.patterns.some(pt => l.includes(pt)) && sites.length < 40) sites.push(`${path.relative(REF, p).replace(/\\/g, '/')}:${i + 1}: ${l.trim().slice(0, 160)}`); }); } } };
    if (f.patterns.length) walk(path.join(REF, 'DroppyCode'));
    if (sites.length) { out.push(`## Swift call sites of the replaced API (first ${sites.length}; grep the reference for more)`); out.push(''); out.push('```'); out.push(sites.join('\n')); out.push('```'); out.push(''); }
    swiftText = f.spec + sites.join('\n');
  } else {
    out.push('## Records (exact)'); out.push('');
    for (const id of part) {
      // one line per record, without empty or bookkeeping fields
      const r = records[id] ?? { id, missing: true };
      const c = Object.fromEntries(Object.entries(r).filter(([key, v]) => v !== null && v !== '' && !(Array.isArray(v) && !v.length) && !['status', 'verification_method'].includes(key)));
      out.push('```json'); out.push(JSON.stringify(c)); out.push('```');
    }
    out.push('');
    const sw = swiftFor(u, part);
    out.push(`## Swift source: ${sw.mode}`); out.push(''); out.push('```swift'); out.push(sw.body); out.push('```'); out.push('');
    swiftText = sw.body;
  }

  const recText = part.map(id => JSON.stringify(records[id] || {})).join('\n');
  const want = new Set(ALWAYS);
  for (const [sec, re] of TRIGGERS) if (re.test(swiftText)) want.add(sec);
  if (isF) { want.add('4.1'); want.add('4.3'); }
  out.push(useDigest ? '## Conventions that apply (digest of IMPLEMENTING.md; grep porting/IMPLEMENTING.md for an R-id when you need its full text)' : '## Conventions that apply (IMPLEMENTING.md sections)'); out.push('');
  for (const sec of ['1.3', '1.5', '2.1', '2.2', '2.3', '2.4', '2.5', '2.6', '2.7', '3.2', '3.3', '3.4', '3.5', '3.6', '3.7', '3.8', '3.9', '3.10', '3.11', '3.12', '3.13', '3.14', '4.1', '4.3', '5.1', '6'])
    if (want.has(sec) && sections[sec]) { out.push(sections[sec]); out.push(''); }

  const base = isF ? null : path.basename(u.swift);
  const ids = new Set([...decisionIdsIn(recText), ...decisionIdsIn(isF ? f.spec : '')]);
  for (const [id, text] of Object.entries(ALL_DECISIONS)) if ((base && text.includes(base)) || part.some(r => text.includes(r))) ids.add(id);
  const dec = [...ids].filter(id => ALL_DECISIONS[id]).sort();
  if (dec.length) { out.push(`## Decisions that name this file, its records or its spec (${dec.join(', ')})`); out.push(''); for (const id of dec) { out.push(ALL_DECISIONS[id]); out.push(''); } }

  const deps = [...(u.deps || []), ...(u.intra_deps || [])];
  const sigs = [];
  for (const d of deps) {
    const target = byId[d]?.target || fById[d]?.target;
    const s = signatures(target);
    sigs.push(s ? `### ${d} (${target})\n\`\`\`csharp\n${s}\n\`\`\`` : `### ${d} (${target || '?'}): not in the tree yet; use the API IMPLEMENTING.md's naming rules give its Swift declarations.`);
  }
  if (sigs.length) { out.push('## C# this unit depends on (public signatures now in the tree)'); out.push(''); out.push(sigs.join('\n\n')); out.push(''); }
  return out.join('\n');
}

fs.mkdirSync(OUT, { recursive: true });
const sizes = [];
for (const u of plan.units) {
  if (u.status !== 'TODO' || (ONLY && !ONLY.includes(u.id))) continue;
  const dir = path.join(OUT, u.id);
  fs.mkdirSync(dir, { recursive: true });
  for (let k = 0; k < u.parts.length; k++) {
    const text = packet(u, k);
    const p = path.join(dir, `part${k + 1}.md`);
    fs.writeFileSync(p, text);
    sizes.push([u.id, k + 1, Buffer.byteLength(text)]);
  }
}
const total = sizes.reduce((s, x) => s + x[2], 0);
sizes.sort((a, b) => b[2] - a[2]);
console.log(`${sizes.length} packets, ${(total / 1e6).toFixed(1)} MB, mean ${(total / sizes.length / 1000).toFixed(0)} KB; largest:`);
for (const [id, k, b] of sizes.slice(0, 8)) console.log(`  ${id} part ${k}: ${(b / 1000).toFixed(0)} KB`);
