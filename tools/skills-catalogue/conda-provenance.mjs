#!/usr/bin/env node
// MANUAL REAL-SOURCE RUN ONLY (ED-NEW-dl-13). Proves where every file of a repackaged conda-forge build comes from: each file of the release zip must equal, byte for byte, a file of
// a conda-forge package whose download matches the SHA-256 anaconda.org publishes for it, or a file of an official data tarball. Nothing is extracted to disk and nothing read is run.
//
//   node tools/skills-catalogue/conda-provenance.mjs --item T4 --zip <release zip> --zip-sha256 <hex> --out <new folder> [--cache <folder>] [--max-candidates 12]
//
// The recipe (top folder to strip, the release time, the package names, the data tarball) is the item's "provenance" entry in sources.json; --strip, --before, --names,
// --data-url and --data-into can be given instead, for an item without one.
//
// For each package name it tries the win-64 (or noarch) files conda-forge uploaded before --before, newest first, and takes the first one that explains at least one file of the zip
// and contradicts none (a zip file at the same path with other bytes). Every zip file must be explained; the run fails otherwise.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { readZip, readConda, readTarGz } from './archives.mjs';

const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const args = {};
for (let i = 2; i < process.argv.length; i += 2) {
  if (!process.argv[i].startsWith('--')) throw new Error(`unexpected argument ${process.argv[i]}`);
  args[process.argv[i].slice(2)] = process.argv[i + 1];
}
if (args.item) {
  const recipe = JSON.parse(fs.readFileSync(new URL('./sources.json', import.meta.url), 'utf8')).items[args.item]?.provenance;
  if (recipe?.type !== 'condaForge') throw new Error(`${args.item} has no condaForge provenance recipe in sources.json`);
  args.strip ??= recipe.strip; args.before ??= recipe.before; args.names ??= recipe.names.join(',');
  if (recipe.dataUrl) { args['data-url'] ??= recipe.dataUrl; args['data-into'] ??= recipe.dataInto; }
}
for (const need of ['zip', 'zip-sha256', 'strip', 'before', 'names', 'out']) if (!args[need]) throw new Error(`--${need} is required`);
const out = path.resolve(args.out);
if (fs.existsSync(out)) throw new Error('--out must be a new folder');
const cache = path.resolve(args.cache ?? out + '.cache');
const before = new Date(args.before);
if (Number.isNaN(before.getTime())) throw new Error('--before is not a time');
const maxCandidates = Number(args['max-candidates'] ?? 12);
const firstHosts = new Set(['api.anaconda.org', 'conda.anaconda.org', 'poppler.freedesktop.org']);

async function get(url, { json = false } = {}) {
  const parsed = new URL(url);
  if (parsed.protocol !== 'https:' || !firstHosts.has(parsed.hostname)) throw new Error(`not on the host list: ${url}`);
  for (let attempt = 1; ; attempt++) {
    try {
      const res = await fetch(url, { redirect: 'follow', headers: { 'User-Agent': 'marrow-catalogue-provenance (ED-NEW-dl-13)' } });
      if (new URL(res.url).protocol !== 'https:') throw new Error(`redirected to a non-https URL: ${res.url}`);
      if (res.status === 404) return { status: 404, finalUrl: res.url };
      if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
      const bytes = Buffer.from(await res.arrayBuffer());
      return { status: 200, finalUrl: res.url, bytes, value: json ? JSON.parse(bytes.toString('utf8')) : null };
    } catch (error) {
      if (attempt >= 4) throw error;
      await new Promise((r) => setTimeout(r, 1500 * attempt));
    }
  }
}

async function cached(name, url, expected) {
  const file = path.join(cache, name);
  if (fs.existsSync(file)) {
    const bytes = fs.readFileSync(file);
    if (!expected || (bytes.length === expected.size && sha(bytes) === expected.sha256)) return { bytes, finalUrl: '(cache)' };
  }
  const got = await get(url);
  if (got.status !== 200) throw new Error(`${url}: HTTP ${got.status}`);
  if (expected && (got.bytes.length !== expected.size || sha(got.bytes) !== expected.sha256))
    throw new Error(`${url}: ${got.bytes.length} bytes, sha256 ${sha(got.bytes)}; anaconda.org publishes ${expected.size} bytes, sha256 ${expected.sha256}`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, got.bytes);
  return { bytes: got.bytes, finalUrl: got.finalUrl };
}

// ---------------------------------------------------------------- the zip
const zipBytes = fs.readFileSync(args.zip);
if (sha(zipBytes) !== args['zip-sha256']) throw new Error(`${args.zip} is not the pinned zip (sha256 ${sha(zipBytes)})`);
const strip = args.strip;
const zipFiles = new Map();
for (const entry of readZip(zipBytes)) {
  if (!entry.name.startsWith(strip)) throw new Error(`${entry.name} is outside ${strip}`);
  zipFiles.set(entry.name.slice(strip.length), { sha256: sha(entry.data()), size: entry.size, by: null });
}
console.log(`${zipFiles.size} files in ${path.basename(args.zip)}`);

// ---------------------------------------------------------------- conda-forge packages
const packages = [], notInZip = [], failures = [];
for (const name of args.names.split(',').map((s) => s.trim()).filter(Boolean)) {
  const listing = await get(`https://api.anaconda.org/package/conda-forge/${encodeURIComponent(name)}/files`, { json: true });
  if (listing.status === 404) { notInZip.push({ name, why: 'no such conda-forge package' }); continue; }
  const candidates = listing.value
    .filter((f) => ['win-64', 'noarch'].includes(f.attrs?.subdir) && (f.labels ?? []).includes('main') && new Date(f.upload_time) <= before && f.basename.endsWith('.conda'))
    .sort((a, b) => new Date(b.upload_time) - new Date(a.upload_time) || (b.attrs.build_number ?? 0) - (a.attrs.build_number ?? 0));
  const tried = [];
  let chosen = null;
  for (const file of candidates.slice(0, maxCandidates)) {
    const url = `https://conda.anaconda.org/conda-forge/${file.basename}`;
    const got = await cached(file.basename, url, { size: file.size, sha256: file.sha256 });
    const conda = readConda(got.bytes);
    let explained = 0, contradicted = 0;
    const hits = [];
    for (const [p, bytes] of conda.pkg) {
      const z = zipFiles.get(p);
      if (!z) continue;
      if (z.sha256 === sha(bytes)) { explained++; hits.push(p); } else contradicted++;
    }
    tried.push(`${file.basename}: ${explained} equal, ${contradicted} differ`);
    if (explained > 0 && contradicted === 0) {
      for (const p of hits) if (!zipFiles.get(p).by) zipFiles.get(p).by = file.basename;
      const about = conda.json('about.json') ?? {};
      const index = conda.json('index.json') ?? {};
      const licences = [...conda.info.keys()].filter((k) => k.startsWith('info/licenses/')).map((k) => ({ path: k, size: conda.info.get(k).length, sha256: sha(conda.info.get(k)) }));
      chosen = {
        name, version: file.version, build: file.attrs.build, file: file.basename, url, finalUrl: got.finalUrl, size: file.size, sha256: file.sha256, uploaded: file.upload_time,
        licence: index.license ?? file.attrs.license ?? null, licenceFamily: index.license_family ?? file.attrs.license_family ?? null, home: about.home ?? null,
        depends: file.attrs.depends ?? [], licenceFiles: licences, filesInZip: hits.length,
      };
      break;
    }
    if (explained === 0 && contradicted === 0) break; // the package has no file at any path of the zip: no older build will either
  }
  if (chosen) { packages.push(chosen); console.log(`${name}: ${chosen.file} explains ${chosen.filesInZip} file(s); licence ${chosen.licence}`); }
  else if (tried.length > 0 && tried.every((t) => t.includes(': 0 equal, 0 differ'))) notInZip.push({ name, why: 'none of its files is in the zip', tried });
  else { failures.push({ name, tried }); console.log(`${name}: NOT MATCHED (${tried.join('; ')})`); }
}

// ---------------------------------------------------------------- the official data tarball
let data = null;
if (args['data-url']) {
  const into = args['data-into'].replace(/\/?$/, '/');
  const got = await cached(path.basename(new URL(args['data-url']).pathname), args['data-url'], null);
  let explained = 0, contradicted = 0;
  for (const entry of readTarGz(got.bytes)) {
    if (entry.type !== 'file') continue;
    const p = into + entry.name.split('/').slice(1).join('/');
    const z = zipFiles.get(p);
    if (!z) continue;
    if (z.sha256 === sha(entry.data)) { explained++; z.by ??= path.basename(args['data-url']); } else contradicted++;
  }
  data = { url: args['data-url'], finalUrl: got.finalUrl, size: got.bytes.length, sha256: sha(got.bytes), explained, contradicted };
  console.log(`${path.basename(args['data-url'])}: ${explained} equal, ${contradicted} differ`);
  if (contradicted > 0) failures.push({ name: args['data-url'], tried: [`${contradicted} files differ`] });
}

const unexplained = [...zipFiles].filter(([, z]) => !z.by).map(([p]) => p);
const used = new Set(packages.map((p) => p.name));
const dependsOutside = [...new Set(packages.flatMap((p) => p.depends.map((d) => d.split(/\s/)[0])))].filter((d) => !used.has(d)).sort();
const result = {
  zip: { file: path.basename(args.zip), sha256: args['zip-sha256'], files: zipFiles.size }, readOn: new Date().toISOString(), before: before.toISOString(),
  packages, data, notInZip, failures, unexplained, dependsOutside,
  files: Object.fromEntries([...zipFiles].map(([p, z]) => [p, { sha256: z.sha256, by: z.by }])),
};
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, 'provenance.json'), JSON.stringify(result, null, 2) + '\n');
console.log(`\n${packages.length} packages; ${unexplained.length} unexplained file(s)${unexplained.length ? ': ' + unexplained.slice(0, 20).join(', ') : ''}; failures ${failures.length}`);
console.log(`depends outside the matched set: ${dependsOutside.join(', ') || '(none)'}`);
process.exit(unexplained.length === 0 && failures.length === 0 ? 0 : 1);
