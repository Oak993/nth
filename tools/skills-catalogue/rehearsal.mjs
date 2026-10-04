// Offline rehearsal of the whole pin run (unit SK-2): the real build-catalogue.mjs and the real CatalogueTool.cs, against a FAKE GitHub and a FAKE release server made of Marrow-
// written fixtures. It proves the pieces fit (tree listing, per-file download with the git blob check, release asset digest, hashing, finalize, change report) without the
// network and without any vendor file. Run: node tools/skills-catalogue/rehearsal.mjs   (needs the .NET 10 SDK; about half a minute). Writes only into a temp folder.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import * as L from './lib.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'marrow-rehearsal-'));
const PIN = '0123456789abcdef0123456789abcdef01234567';

// ---- the fake world
const files = {
  'LICENSE': 'Fixture licence text\n',
  'skills/alpha-skill/SKILL.md': '---\nname: alpha-skill\ndescription: fixture\n---\nbody\n',
  'skills/alpha-skill/run.py': 'print("fixture")\n',
  'skills/beta/SKILL.md': '---\nname: beta\n---\nbody\n',
  'skills/gamma/SKILL.md': '---\nname: gamma\n---\nnot in the plan\n',
};
const tree = Object.entries(files).map(([p, text]) => ({ path: p, type: 'blob', mode: '100644', sha: L.gitBlobSha1(Buffer.from(text)), size: Buffer.byteLength(text) }));
const toolBytes = Buffer.from('fixture tool archive bytes');
const routes = new Map([
  [`https://api.github.com/repos/example/fixtureskills/git/trees/${PIN}?recursive=1`, () => JSON.stringify({ sha: PIN, tree, truncated: false })],
  ['https://api.github.com/repos/example/fixturetool/releases/tags/1.0.0', () => JSON.stringify({ assets: [{ name: 'tool.zip', size: toolBytes.length, browser_download_url: 'https://github.com/example/fixturetool/releases/download/1.0.0/tool.zip', digest: 'sha256:' + L.sha256Hex(toolBytes) }] })],
  ['https://github.com/example/fixturetool/releases/download/1.0.0/tool.zip', () => toolBytes],
]);
for (const [p, text] of Object.entries(files)) routes.set(L.rawUrl('example/fixtureskills', PIN, p), () => Buffer.from(text));
const requested = [];
globalThis.fetch = async (url) => {
  requested.push(String(url));
  const make = routes.get(String(url));
  if (!make) return Object.defineProperty(new Response('not found', { status: 404 }), 'url', { value: String(url) });
  return Object.defineProperty(new Response(make(), { status: 200 }), 'url', { value: String(url) });
};

// ---- the previous catalogue and the sources config, in the schema of SK-1
const item = (over) => ({ countState: 'plan', pinState: 'unpinned', pinHint: undefined, licenceNotes: undefined, needs: [], provides: [], artifacts: [], files: [], approxSizeMb: 1, vendor: 'Fixture', summary: 'A fixture.', ...over });
const previous = { schema: 1, readOn: '2026-10-04', items: [
  item({ id: 'X1', kind: 'tool', title: 'Fixture tool', pin: '1.0.0', sourceUrl: 'https://github.com/example/fixturetool/releases', licenceName: 'MIT', licenceUrl: 'https://github.com/example/fixturetool/blob/main/LICENSE', licenceFile: 'LICENSE', termsClass: 'open', artifacts: [{ name: 'tool.zip', url: null, size: null, sha256: null }], provides: [{ kind: 'exe', name: 'fixturetool' }] }),
  item({ id: 'X3', kind: 'skillset', title: 'Fixture skills', pin: PIN, sourceUrl: 'https://github.com/example/fixtureskills', licenceName: 'Apache-2.0', licenceUrl: null, licenceFile: null, termsClass: 'open', needs: ['X1'], skillCount: 2, skillsListed: true,
    skills: [{ folder: 'alpha-skill', name: null, path: 'skills/alpha-skill', needs: ['X1'] }, { folder: 'beta', name: null, path: 'skills/beta', needs: [] }] }),
] };
const sources = { schema: 1, scriptHosts: ['api.github.com', 'github.com', 'raw.githubusercontent.com'], items: {
  X1: { fetch: { type: 'release', repo: 'example/fixturetool', tag: '1.0.0' }, verify: { type: 'githubDigest' } },
  X3: { repo: 'example/fixtureskills', layout: { type: 'folders', parents: ['skills'] } },
} };
fs.writeFileSync(path.join(work, 'previous.json'), JSON.stringify(previous, null, 2));
fs.writeFileSync(path.join(work, 'sources.json'), JSON.stringify(sources, null, 2));

// ---- run the real script
process.argv = ['node', 'build-catalogue.mjs', '--previous', path.join(work, 'previous.json'), '--sources', path.join(work, 'sources.json'), '--out', path.join(work, 'out'), '--read-on', '2026-11-01', '--skip-signatures'];
const mod = await import(pathToFileURL(path.join(here, 'build-catalogue.mjs')).href);
await mod.done;
assert.ok(!process.exitCode, 'the run reported a failure (see above)');

const out = path.join(work, 'out');
for (const name of ['catalogue.raw.json', 'catalogue.json', 'change-report.md', 'run-record.json', 'run.log']) assert.ok(fs.existsSync(path.join(out, name)), `${name} was not written`);
const result = JSON.parse(fs.readFileSync(path.join(out, 'catalogue.json'), 'utf8'));
const x3 = result.items.find((i) => i.id === 'X3'), x1 = result.items.find((i) => i.id === 'X1');
assert.equal(result.readOn, '2026-11-01');
assert.deepEqual(x3.files.map((f) => f.path), ['LICENSE', 'skills/alpha-skill/SKILL.md', 'skills/alpha-skill/run.py', 'skills/beta/SKILL.md', 'skills/gamma/SKILL.md']);
assert.equal(x3.files.find((f) => f.path === 'LICENSE').sha256, L.sha256Hex(Buffer.from(files.LICENSE)));
assert.deepEqual(x3.skills.map((s) => [s.folder, s.name, s.path]), [['alpha-skill', 'alpha-skill', 'skills/alpha-skill'], ['beta', 'beta', 'skills/beta'], ['gamma', 'gamma', 'skills/gamma']]);
assert.deepEqual(x3.skills[0].needs, ['X1']);
assert.equal(x3.skillCount, 3);
assert.equal(x3.licenceFile, 'LICENSE');
assert.equal(x3.licenceUrl, L.rawUrl('example/fixtureskills', PIN, 'LICENSE'));
assert.equal(x3.pinState, 'pinned');
assert.equal(x1.pinState, 'pinned');
assert.deepEqual(x1.artifacts[0], { name: 'tool.zip', url: 'https://github.com/example/fixturetool/releases/download/1.0.0/tool.zip', size: toolBytes.length, sha256: L.sha256Hex(toolBytes) });
const report = fs.readFileSync(path.join(out, 'change-report.md'), 'utf8');
assert.match(report, /- Items changed: 2 \(X1, X3\)/);
assert.match(report, /- hash: artifacts\[tool\.zip\]\.sha256: \(none\) -> /);
assert.match(report, /- skills: skills\[gamma\]: added/);
assert.match(report, /- skills: skillCount: 2 -> 3/);
assert.match(report, /## Pin run/);
assert.match(report, /WARNING: 3 skills found, the plan counts 2/);
assert.match(report, /NOT VERIFIED|verified: tool\.zip \[githubDigest\]/);
const record = JSON.parse(fs.readFileSync(path.join(out, 'run-record.json'), 'utf8'));
assert.deepEqual(record.errors, []);
assert.ok(requested.every((u) => u.startsWith('https://')), 'a request was not https');
assert.ok(requested.every((u) => ['api.github.com', 'github.com', 'raw.githubusercontent.com'].includes(new URL(u).hostname)));
console.log(`rehearsal passed (${requested.length} fake requests; files in ${work})`);
