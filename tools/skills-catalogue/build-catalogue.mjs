#!/usr/bin/env node
// The skills catalogue builder (unit SK-2, DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7).
//
// RUN BY HAND ONLY. This is a developer tool, not a test: it uses the network (to the hosts in sources.json), downloads every pinned artifact and every file of every
// skill set, checks what can be checked (git blob ids against GitHub's trees, sizes, the digests the sources publish, Sigstore, the Node SHASUMS signature, the
// LibreOffice .asc), and writes a new catalogue.json plus a change report for the owner into a folder of its own. It never writes into the repository: the lead reads the
// report, and only then copies catalogue.json over src/Marrow.Core/Port/Skills/Catalogue/catalogue.json (ONDEMAND-PLAN.md 2.1: "a pin changes only with the owner's approval
// of its change report"). See README.md. Offline check of the pure parts: node tools/skills-catalogue/selftest.mjs
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import * as L from './lib.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');

// ---------------------------------------------------------------- command line
function parseArgs(argv) {
  const a = {
    previous: path.join(repoRoot, 'src/Marrow.Core/Port/Skills/Catalogue/catalogue.json'), sources: path.join(here, 'sources.json'), out: null, cache: null,
    readOn: new Date().toISOString().slice(0, 10), only: null, tokenEnv: 'GITHUB_TOKEN', sigstoreIdentity: null, sigstoreIssuer: null, nodeKeys: null, tdfKey: null,
    skipSignatures: false, dryRun: false, reuseCache: false, strict: false, maxFiles: null, pythonExe: null, npmExe: 'npm', gpgExe: 'gpg', concurrency: 6,
  };
  const flags = new Set(['skip-signatures', 'dry-run', 'reuse-cache', 'strict']);
  for (let i = 2; i < argv.length; i++) {
    const m = /^--([a-z-]+)$/.exec(argv[i]);
    if (!m) throw new Error(`unexpected argument ${argv[i]}`);
    const key = m[1].replace(/-([a-z])/g, (_, c) => c.toUpperCase());
    if (!(key in a)) throw new Error(`unknown option --${m[1]}`);
    if (flags.has(m[1])) a[key] = true;
    else { a[key] = argv[++i]; if (a[key] === undefined) throw new Error(`--${m[1]} needs a value`); }
  }
  if (a.only) a.only = a.only.split(',').map((s) => s.trim()).filter(Boolean);
  if (a.maxFiles !== null) a.maxFiles = Number(a.maxFiles);
  a.concurrency = Number(a.concurrency);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(a.readOn)) throw new Error('--read-on must be YYYY-MM-DD');
  return a;
}

const args = parseArgs(process.argv);
if (!args.out) {
  console.error(`usage: node tools/skills-catalogue/build-catalogue.mjs --out <new folder> [--previous <catalogue.json>] [--sources <sources.json>] [--only A1,T3]
  [--sigstore-identity <identity> --sigstore-issuer <issuer>] [--node-keys <gpg key file>] [--tdf-key <gpg key file>] [--skip-signatures]
  [--read-on YYYY-MM-DD] [--reuse-cache] [--dry-run] [--strict] [--max-files N] [--python-exe <python>] [--npm-exe <npm>] [--gpg-exe <gpg>] [--concurrency 6]
GITHUB_TOKEN (or the variable named by --token-env) raises GitHub's API limit; it is sent to api.github.com only and never printed.`);
  process.exit(2);
}

const previous = JSON.parse(fs.readFileSync(args.previous, 'utf8'));
const sources = JSON.parse(fs.readFileSync(args.sources, 'utf8'));
const hosts = sources.scriptHosts;
const outDir = path.resolve(args.out);
const cacheDir = args.cache ? path.resolve(args.cache) : path.join(outDir, 'cache');

// ---------------------------------------------------------------- run record and log
const record = { startedAt: new Date().toISOString(), readOn: args.readOn, node: process.version, previous: path.resolve(args.previous), options: { only: args.only, skipSignatures: args.skipSignatures }, items: {}, errors: [] };
const entry = (id) => (record.items[id] ??= { handler: null, warnings: [], notes: [], verification: [], sources: [] });
let logStream = null;
const log = (message) => { console.log(message); logStream?.write(message + '\n'); };
const warn = (id, message) => { entry(id).warnings.push(message); log(`WARNING ${id}: ${message}`); };

// ---------------------------------------------------------------- network
const userAgent = 'marrow-catalogue-builder (unit SK-2)';
const token = process.env[args.tokenEnv] || null;

async function request(url, { api = false, accept = null } = {}) {
  if (!L.hostAllowed(url, hosts)) throw new Error(`the first URL is not on the script's host list: ${url}`);
  const headers = { 'User-Agent': userAgent };
  if (accept) headers.Accept = accept;
  if (api) { headers.Accept = 'application/vnd.github+json'; headers['X-GitHub-Api-Version'] = '2022-11-28'; if (token) headers.Authorization = `Bearer ${token}`; }
  let last;
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      const res = await fetch(url, { headers, redirect: 'follow' });
      if (new URL(res.url).protocol !== 'https:') throw new Error(`redirected to a non-https URL: ${res.url}`);
      if ([429, 500, 502, 503, 504].includes(res.status)) { last = new Error(`${url}: HTTP ${res.status}`); await new Promise((r) => setTimeout(r, 1500 * attempt)); continue; }
      if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
      return res;
    } catch (error) {
      last = error;
      if (/HTTP 4\d\d|non-https|host list/.test(String(error.message))) throw error;
      await new Promise((r) => setTimeout(r, 1500 * attempt));
    }
  }
  throw last;
}
const getJson = async (url, opts) => (await request(url, opts)).json();
const getText = async (url, opts) => (await request(url, opts)).text();
const getBuffer = async (url, opts) => Buffer.from(await (await request(url, opts)).arrayBuffer());

/** Downloads a URL to a file while hashing it; returns size, sha256 and the host the bytes really came from. */
async function downloadTo(url, file) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  if (args.reuseCache && fs.existsSync(file)) {
    const bytes = fs.readFileSync(file);
    return { size: bytes.length, sha256: L.sha256Hex(bytes), finalHost: '(cache)' };
  }
  const res = await request(url);
  const hash = createHash('sha256');
  let size = 0;
  const part = file + '.part';
  await pipeline(Readable.fromWeb(res.body), async function* (source) { for await (const chunk of source) { hash.update(chunk); size += chunk.length; yield chunk; } }, fs.createWriteStream(part));
  fs.renameSync(part, file);
  return { size, sha256: hash.digest('hex'), finalHost: new URL(res.url).hostname };
}

const treeCache = new Map();
async function getTree(repo, sha) {
  const key = `${repo}@${sha}`;
  if (!treeCache.has(key)) treeCache.set(key, (async () => {
    const data = await getJson(`https://api.github.com/repos/${repo}/git/trees/${sha}?recursive=1`, { api: true });
    if (data.truncated) throw new Error(`${key}: GitHub truncated the tree; this repository is too large for the one-call listing`);
    return data.tree;
  })());
  return treeCache.get(key);
}

// ---------------------------------------------------------------- running tools
function run(cmd, cmdArgs, options = {}) {
  const r = spawnSync(cmd, cmdArgs, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, shell: L.needsShell(cmd, process.platform), ...options });
  return { status: r.status, stdout: r.stdout ?? '', stderr: r.stderr ?? '', error: r.error };
}
const available = (cmd, probe = ['--version']) => { const r = run(cmd, probe); return !r.error && r.status === 0; };

function gpgVerify(id, keyFile, signatureFile, signedFile) {
  if (!keyFile) return { ok: false, detail: 'no key file was given (--node-keys or --tdf-key)' };
  if (!available(args.gpgExe)) return { ok: false, detail: `${args.gpgExe} is not available` };
  const home = fs.mkdtempSync(path.join(os.tmpdir(), 'marrow-gpg-'));
  try {
    const imported = run(args.gpgExe, ['--homedir', home, '--batch', '--import', keyFile]);
    if (imported.status !== 0) return { ok: false, detail: `could not import ${keyFile}: ${imported.stderr.trim().slice(0, 300)}` };
    const verified = run(args.gpgExe, ['--homedir', home, '--batch', '--status-fd', '1', '--verify', signatureFile, signedFile]);
    const good = /\[GNUPG:\] GOODSIG/.test(verified.stdout) && /\[GNUPG:\] VALIDSIG ([0-9A-F]{40})/.exec(verified.stdout);
    return verified.status === 0 && good ? { ok: true, detail: `good signature by key ${good[1]} (the key file's own trust is the lead's to establish)` } : { ok: false, detail: `gpg did not accept the signature: ${(verified.stdout + verified.stderr).trim().slice(0, 300)}` };
  } finally { fs.rmSync(home, { recursive: true, force: true }); }
}

function sigstoreVerify(bundleFile, artifactFile) {
  if (!args.sigstoreIdentity || !args.sigstoreIssuer) return { ok: false, detail: 'no --sigstore-identity and --sigstore-issuer were given (the release manager identity published by python.org)' };
  const common = ['--bundle', bundleFile];
  if (available('cosign', ['version'])) {
    const r = run('cosign', ['verify-blob', ...common, '--certificate-identity', args.sigstoreIdentity, '--certificate-oidc-issuer', args.sigstoreIssuer, artifactFile]);
    return r.status === 0 ? { ok: true, detail: `cosign verify-blob accepted the bundle for ${args.sigstoreIdentity}` } : { ok: false, detail: `cosign refused: ${(r.stdout + r.stderr).trim().slice(0, 300)}` };
  }
  const py = args.pythonExe ?? (process.platform === 'win32' ? 'python' : 'python3');
  if (available(py, ['-m', 'sigstore', '--version'])) {
    const r = run(py, ['-m', 'sigstore', 'verify', 'identity', ...common, '--cert-identity', args.sigstoreIdentity, '--cert-oidc-issuer', args.sigstoreIssuer, artifactFile]);
    return r.status === 0 ? { ok: true, detail: `python -m sigstore accepted the bundle for ${args.sigstoreIdentity}` } : { ok: false, detail: `sigstore refused: ${(r.stdout + r.stderr).trim().slice(0, 300)}` };
  }
  return { ok: false, detail: 'neither cosign nor python -m sigstore is available' };
}

// ---------------------------------------------------------------- one verification step per tool
async function verifyArtifact(id, verifyCfg, artifact, local, extra = {}) {
  const note = (method, ok, detail) => { entry(id).verification.push({ artifact: artifact.name, method, ok, detail }); log(`${ok ? 'verified' : 'NOT VERIFIED'} ${id} ${artifact.name} [${method}]: ${detail}`); return ok; };
  if (artifact.sha256Hint && !local.sha256.startsWith(artifact.sha256Hint)) throw new Error(`${id} ${artifact.name}: the sha256 ${local.sha256} does not start with the digest prefix the plan prints (${artifact.sha256Hint})`);
  const type = verifyCfg?.type ?? 'none';
  if (type === 'none') return note('none', true, verifyCfg?.reason ?? 'no signature is published');
  if (type === 'githubDigest') {
    if (!extra.digest) return note('githubDigest', true, 'GitHub published no digest for this asset; the hash is the one this run computed');
    const ok = extra.digest === `sha256:${local.sha256}`;
    if (!ok) throw new Error(`${id} ${artifact.name}: GitHub's digest ${extra.digest} is not the downloaded file's sha256 ${local.sha256}`);
    return note('githubDigest', true, `matches the digest GitHub publishes for the asset (${extra.digest})`);
  }
  if (type === 'pypiDigest') {
    if (extra.digest !== local.sha256) throw new Error(`${id} ${artifact.name}: PyPI's sha256 ${extra.digest} is not the downloaded file's ${local.sha256}`);
    return note('pypiDigest', true, 'matches the sha256 PyPI lists for the file');
  }
  const gate = (ok, method, detail) => { if (!ok && !args.skipSignatures) throw new Error(`${id} ${artifact.name}: ${method} failed: ${detail} (use --skip-signatures only to see the rest of the run; the item is then reported as NOT VERIFIED)`); return note(method, ok, ok ? detail : `${detail} (skipped by --skip-signatures)`); };
  const tmp = path.join(cacheDir, 'sig', id);
  fs.mkdirSync(tmp, { recursive: true });
  if (type === 'sigstore') {
    const bundle = path.join(tmp, artifact.name + '.sigstore');
    await downloadTo(artifact.url + '.sigstore', bundle);
    const r = sigstoreVerify(bundle, local.file);
    return gate(r.ok, 'sigstore', r.detail);
  }
  if (type === 'nodeShasums') {
    const base = `https://nodejs.org/dist/v${extra.version}/`;
    const sums = path.join(tmp, 'SHASUMS256.txt'), sig = path.join(tmp, 'SHASUMS256.txt.sig');
    await downloadTo(base + 'SHASUMS256.txt', sums);
    await downloadTo(base + 'SHASUMS256.txt.sig', sig);
    const r = gpgVerify(id, args.nodeKeys, sig, sums);
    const listed = L.parseShasums(fs.readFileSync(sums, 'utf8')).get(artifact.name);
    if (listed !== local.sha256) throw new Error(`${id} ${artifact.name}: SHASUMS256.txt lists ${listed ?? '(nothing)'}, the file's sha256 is ${local.sha256}`);
    return gate(r.ok, 'nodeShasums', r.ok ? `${r.detail}; SHASUMS256.txt lists the same sha256` : r.detail);
  }
  if (type === 'gpgAsc') {
    const asc = path.join(tmp, artifact.name + '.asc'), sha = path.join(tmp, artifact.name + '.sha256');
    await downloadTo(artifact.url + '.asc', asc);
    await downloadTo(artifact.url + '.sha256', sha);
    const published = L.parseSha256File(fs.readFileSync(sha, 'utf8'), artifact.name);
    if (published !== local.sha256) throw new Error(`${id} ${artifact.name}: the published .sha256 is ${published ?? '(unreadable)'}, the file's sha256 is ${local.sha256}`);
    const r = gpgVerify(id, args.tdfKey, asc, local.file);
    return gate(r.ok, 'gpgAsc', r.ok ? `${r.detail}; the published .sha256 is the same` : r.detail);
  }
  throw new Error(`${id}: unknown verify type ${type}`);
}

// ---------------------------------------------------------------- tools
async function buildTool(prev, cfg) {
  const id = prev.id, fetchCfg = cfg.fetch;
  const e = entry(id);
  e.handler = `tool/${fetchCfg.type}`;
  let releaseAssets = null, pypiFiles = null;
  if (fetchCfg.type === 'release') releaseAssets = (await getJson(`https://api.github.com/repos/${fetchCfg.repo}/releases/tags/${encodeURIComponent(fetchCfg.tag)}`, { api: true })).assets;
  if (fetchCfg.type === 'pypi') pypiFiles = (await getJson(`https://pypi.org/pypi/${fetchCfg.project}/${prev.pin}/json`)).urls;
  const artifacts = [];
  for (const old of prev.artifacts) {
    const art = { ...old };
    let extra = { version: prev.pin };
    if (fetchCfg.type === 'release') {
      const asset = releaseAssets.find((a) => a.name === old.name);
      if (!asset) throw new Error(`${id}: release ${fetchCfg.repo}@${fetchCfg.tag} has no asset named ${old.name}`);
      art.url = asset.browser_download_url; extra = { ...extra, digest: asset.digest ?? null, expectedSize: asset.size };
    } else if (fetchCfg.type === 'pypi') {
      const file = pypiFiles.find((f) => f.filename === old.name);
      if (!file) throw new Error(`${id}: PyPI ${fetchCfg.project} ${prev.pin} has no file named ${old.name}`);
      art.url = file.url; extra = { ...extra, digest: file.digests.sha256, expectedSize: file.size };
    } else if (!old.url) throw new Error(`${id}: ${old.name} has no URL in the previous catalogue and the source config has no way to find one`);
    const file = path.join(cacheDir, 'tools', id, old.name);
    const local = await downloadTo(art.url, file);
    local.file = file;
    if (old.size != null && old.size !== local.size) throw new Error(`${id} ${old.name}: ${local.size} bytes, the plan prints ${old.size}`);
    if (extra.expectedSize != null && extra.expectedSize !== local.size) throw new Error(`${id} ${old.name}: ${local.size} bytes, the source lists ${extra.expectedSize}`);
    art.size = local.size; art.sha256 = local.sha256;
    e.sources.push({ url: art.url, finalHost: local.finalHost, size: local.size, sha256: local.sha256 });
    await verifyArtifact(id, cfg.verify, art, local, extra);
    artifacts.push(art);
  }
  const next = { ...prev, artifacts, approxSizeMb: L.mbFromBytes(artifacts.reduce((n, a) => n + a.size, 0)) };
  if (next.sourceUrl == null && artifacts[0]?.url) { next.sourceUrl = new URL(artifacts[0].url).origin + '/'; e.notes.push(`sourceUrl was empty; set to the host the file came from (${next.sourceUrl})`); }
  const off = L.percentOff(prev.approxSizeMb, next.approxSizeMb);
  if (off !== null && Math.abs(off) > 15) warn(id, `measured ${next.approxSizeMb} MB differs from the plan's ${prev.approxSizeMb} MB by ${off}%`);
  return next;
}

// ---------------------------------------------------------------- package sets
const merge = (prevPackages, found) => found.map((f) => {
  const old = prevPackages.find((p) => L.normalizePyName(p.name) === L.normalizePyName(f.name));
  return { name: old?.name ?? f.name, version: f.version, licence: old?.licence ?? null };
});

async function buildPyset(prev, cfg) {
  const id = prev.id, f = cfg.fetch, e = entry(id);
  e.handler = 'pyset/pip';
  const py = args.pythonExe ?? (process.platform === 'win32' ? 'python' : 'python3');
  if (!available(py)) throw new Error(`${id}: ${py} is not available to run pip (--python-exe)`);
  const dir = path.join(cacheDir, 'wheels', id);
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });
  const req = path.join(dir, 'requirements.txt');
  fs.writeFileSync(req, prev.packages.map((p) => (p.version ? `${p.name}==${p.version}` : p.name)).join('\n') + '\n');
  const pipArgs = ['-m', 'pip', 'download', '--only-binary=:all:', '--no-input', '--disable-pip-version-check', '--python-version', f.python, '--implementation', 'cp', '--platform', f.platform, ...f.abis.flatMap((a) => ['--abi', a]), '-r', req, '-d', dir];
  log(`${id}: ${py} ${pipArgs.join(' ')}`);
  const r = run(py, pipArgs);
  if (r.status !== 0) throw new Error(`${id}: pip download failed: ${(r.stderr || r.stdout).trim().slice(-600)}`);
  const wheels = fs.readdirSync(dir).filter((n) => n.endsWith('.whl')).sort();
  const found = [], artifacts = [];
  await L.mapLimit(wheels, args.concurrency, async (name) => {
    const info = L.wheelInfo(name);
    if (!info) throw new Error(`${id}: ${name} is not a wheel file name`);
    const json = await getJson(`https://pypi.org/pypi/${info.dist}/${info.version}/json`);
    const file = json.urls.find((u) => u.filename === name);
    if (!file) throw new Error(`${id}: PyPI ${info.dist} ${info.version} lists no file ${name}`);
    const bytes = fs.readFileSync(path.join(dir, name));
    const sha = L.sha256Hex(bytes);
    if (sha !== file.digests.sha256 || bytes.length !== file.size) throw new Error(`${id} ${name}: what pip downloaded (${bytes.length} bytes, ${sha}) is not what PyPI lists (${file.size} bytes, ${file.digests.sha256})`);
    artifacts.push({ name, url: file.url, size: bytes.length, sha256: sha });
    found.push(info);
    entry(id).sources.push({ url: file.url, finalHost: 'pip + pypi.org JSON', size: bytes.length, sha256: sha });
    entry(id).verification.push({ artifact: name, method: 'pypiDigest', ok: true, detail: 'pip\'s download matches the size and sha256 PyPI lists' });
  });
  artifacts.sort((a, b) => (a.name < b.name ? -1 : 1));
  found.sort((a, b) => (a.dist < b.dist ? -1 : 1)).forEach((x) => { x.name = x.dist; });
  for (const p of prev.packages) if (!found.some((x) => L.normalizePyName(x.name) === L.normalizePyName(p.name))) warn(id, `the package ${p.name} listed in the plan is not among the wheels pip chose`);
  const lines = artifacts.map((a) => `${a.name} ${a.sha256}`);
  return { ...prev, pin: L.lockPin(lines), sourceUrl: prev.sourceUrl ?? 'https://files.pythonhosted.org/', artifacts, packages: merge(prev.packages, found), approxSizeMb: L.mbFromBytes(artifacts.reduce((n, a) => n + a.size, 0)) };
}

async function buildNodeset(prev, cfg) {
  const id = prev.id, f = cfg.fetch, e = entry(id);
  e.handler = 'nodeset/npm';
  if (!available(args.npmExe)) throw new Error(`${id}: ${args.npmExe} is not available`);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'marrow-npm-'));
  try {
    const dependencies = Object.fromEntries(prev.packages.filter((p) => p.version).map((p) => [p.name, p.version]));
    fs.writeFileSync(path.join(dir, 'package.json'), JSON.stringify({ name: 'marrow-lock', version: '0.0.0', private: true, dependencies }, null, 2));
    const r = run(args.npmExe, ['install', '--package-lock-only', '--ignore-scripts', '--no-audit', '--no-fund'], { cwd: dir, env: { ...process.env, npm_config_os: f.os, npm_config_cpu: f.cpu } });
    if (r.status !== 0) throw new Error(`${id}: npm install --package-lock-only failed: ${(r.stderr || r.stdout).trim().slice(-600)}`);
    const { kept, skipped } = L.filterNpmLock(JSON.parse(fs.readFileSync(path.join(dir, 'package-lock.json'), 'utf8')), { os: f.os, cpu: f.cpu });
    // RR-14 (ED-NEW-dl-7): the node_modules position of every kept package, so the nodeset installer can lay the tarballs out as the lock does.
    e.npmTree = kept.map((p) => ({ path: p.path, name: p.name, version: p.version, resolved: p.resolved, integrity: p.integrity }));
    e.notes.push(`${kept.length} packages for ${f.os}/${f.cpu}; skipped ${skipped.length} (${skipped.map((s) => `${s.name}: ${s.reason}`).slice(0, 12).join('; ')}${skipped.length > 12 ? '; ...' : ''})`);
    const artifacts = [];
    await L.mapLimit(kept, args.concurrency, async (p) => {
      const name = L.tarballName(p.resolved);
      if (artifacts.some((a) => a.name === name)) return;
      const bytes = await getBuffer(p.resolved);
      if (!L.integrityMatches(p.integrity, bytes)) throw new Error(`${id} ${name}: the tarball does not match the integrity ${p.integrity} the lock records`);
      artifacts.push({ name, url: p.resolved, size: bytes.length, sha256: L.sha256Hex(bytes) });
      entry(id).sources.push({ url: p.resolved, finalHost: new URL(p.resolved).hostname, size: bytes.length, sha256: L.sha256Hex(bytes) });
      entry(id).verification.push({ artifact: name, method: 'npmIntegrity', ok: true, detail: `matches ${p.integrity.slice(0, 20)}... from the lock` });
    });
    artifacts.sort((a, b) => (a.name < b.name ? -1 : 1));
    for (const p of prev.packages) if (!kept.some((k) => k.name === p.name)) warn(id, `the package ${p.name} listed in the plan is not in the resolved lock for ${f.os}/${f.cpu}`);
    const packages = kept.map((k) => ({ name: k.name, version: k.version, licence: prev.packages.find((p) => p.name === k.name)?.licence ?? null }));
    return { ...prev, pin: L.lockPin(artifacts.map((a) => `${a.name} ${a.sha256}`)), sourceUrl: prev.sourceUrl ?? 'https://registry.npmjs.org/', artifacts, packages, approxSizeMb: L.mbFromBytes(artifacts.reduce((n, a) => n + a.size, 0)) };
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

// ---------------------------------------------------------------- repository sets (skill sets and plugins)
/** Decides which directories of a tree the item takes, from the layout rule of sources.json. Returns [{ dir, plugin? , exclude? }] and notes. */
function chooseRoots(prev, layout, tree, id) {
  const skillSet = new Set(L.skillDirs(tree));
  switch (layout.type) {
    case 'listedSkills': {
      const roots = prev.skills.map((s) => { if (!s.path) throw new Error(`${id}: skill ${s.folder} has no path in the previous catalogue`); return { dir: s.path }; });
      for (const r of roots) if (!skillSet.has(r.dir)) throw new Error(`${id}: ${r.dir} is not a skill folder at this pin (no SKILL.md)`);
      return roots;
    }
    case 'skillFolders':
      return [...skillSet].map((dir) => ({ dir }));
    case 'folders': {
      const roots = [];
      for (const parent of layout.parents) {
        for (const name of L.childSkillFolders(tree, parent)) {
          if (layout.excludeNames?.includes(name)) continue;
          if (layout.excludeNamePrefixes?.some((p) => name.startsWith(p))) continue;
          if (layout.includeNamePrefixes && !layout.includeNamePrefixes.some((p) => name.startsWith(p))) continue;
          roots.push({ dir: `${parent}/${name}` });
        }
      }
      for (const extra of layout.extraFolders ?? []) {
        if (!skillSet.has(extra)) throw new Error(`${id}: the extra folder ${extra} is not a skill folder at this pin`);
        roots.push({ dir: extra });
      }
      return roots;
    }
    case 'plugins': {
      let parent = null, children = [];
      for (const candidate of layout.parents) { children = L.childDirsWithSkills(tree, candidate); if (children.length > 0) { parent = candidate; break; } }
      if (parent === null) throw new Error(`${id}: no plugin folders (directories holding a SKILL.md) under ${layout.parents.map((p) => `"${p}"`).join(' or ')}`);
      entry(id).notes.push(`plugin folders are the children of "${parent}" that hold a SKILL.md${layout.unverified ? ` (${layout.unverified})` : ''}`);
      let names = children;
      if (layout.namesFromPrevious) {
        names = prev.folders.map((f) => f.name);
        const missing = names.filter((n) => !children.includes(n));
        if (missing.length > 0) throw new Error(`${id}: the plan's plugin folders ${missing.join(', ')} are not under "${parent}" (found: ${children.join(', ')})`);
        const extra = children.filter((n) => !names.includes(n));
        if (extra.length > 0) warn(id, `plugin folders not in the plan's list were left out: ${extra.join(', ')}`);
      }
      return names.map((name) => ({ dir: parent === '' ? name : `${parent}/${name}`, plugin: name, exclude: layout.excludeSegmentsIn?.[name] ?? [] }));
    }
    default:
      throw new Error(`${id}: unknown layout ${layout.type}`);
  }
}

/** Fetches every file of the chosen roots, checking each against the tree (size and git blob id) and hashing it. */
async function fetchFiles(id, repo, pin, entries) {
  const bad = L.unsupportedEntries(entries);
  if (bad.length > 0) throw new Error(`${id}: ${bad.length} symbolic link or submodule entries (${bad.slice(0, 3).map((b) => b.path).join(', ')}); a skill set cannot hold them`);
  const texts = new Map();
  const files = await L.mapLimit(entries, args.concurrency, async (e) => {
    const bytes = await getBuffer(L.rawUrl(repo, pin, e.path));
    if (bytes.length !== e.size) throw new Error(`${id}: ${e.path} is ${bytes.length} bytes, the tree says ${e.size}`);
    if (L.gitBlobSha1(bytes) !== e.sha) throw new Error(`${id}: ${e.path} does not match the git blob id ${e.sha} the tree lists`);
    if (e.path.endsWith('/SKILL.md') || e.path === 'SKILL.md') texts.set(e.path, bytes.toString('utf8'));
    return { path: e.path, size: bytes.length, sha256: L.sha256Hex(bytes) };
  });
  return { files, texts };
}

async function buildRepoItem(prev, cfg) {
  const id = prev.id, e = entry(id), layout = cfg.layout;
  e.handler = `repo/${layout.type}`;
  const repo = cfg.repo;
  let pin = cfg.pin ?? prev.pin;
  if (cfg.pin && prev.pinHint && !cfg.pin.startsWith(prev.pinHint)) throw new Error(`${id}: the pin in sources.json (${cfg.pin}) does not start with the catalogue's hint ${prev.pinHint}`);
  const skills = [];
  let files = [], folders = [], licenceFile = null, rootsUsed = [], perSkillLicenceUrl = null;
  if (layout.type === 'perSkillPins') {
    pin = null;
    for (const old of prev.skills) {
      if (!old.pin || !L.isFullCommit(old.pin)) throw new Error(`${id}: skill ${old.folder} has no full commit pin`);
      const tree = await getTree(repo, old.pin);
      const dirs = L.skillDirs(tree).filter((d) => d.split('/').pop() === old.folder);
      if (dirs.length !== 1) throw new Error(`${id}: at ${old.pin} the skill folder ${old.folder} was found ${dirs.length} times (${dirs.join(', ')})`);
      const entries = L.filesUnder(tree, dirs[0]);
      const got = await fetchFiles(id, repo, old.pin, entries);
      files.push(...got.files);
      skills.push({ ...old, path: dirs[0], name: L.parseFrontmatterName(got.texts.get(`${dirs[0]}/SKILL.md`) ?? '') });
      rootsUsed.push(dirs[0]);
    }
    // ED-NEW-dl-11 (7): a set at per-skill pins names its licence file in sources.json; it is fetched at the pin of the skill whose folder holds it.
    if (cfg.licenceFile) {
      const owner = skills.find((s) => cfg.licenceFile.startsWith(s.path + '/'));
      if (!owner || !files.some((f) => f.path === cfg.licenceFile)) throw new Error(`${id}: the licence file ${cfg.licenceFile} is not in any pinned skill folder`);
      licenceFile = cfg.licenceFile;
      perSkillLicenceUrl = L.rawUrl(repo, owner.pin, licenceFile);
    }
  } else {
    if (!L.isFullCommit(pin)) throw new Error(`${id}: no full commit pin (the catalogue has ${pin ?? 'none'}; put it in sources.json)`);
    const tree = await getTree(repo, pin);
    const roots = chooseRoots(prev, layout, tree, id);
    rootsUsed = roots.map((r) => r.dir);
    const byPath = new Map();
    for (const r of roots) for (const entryOfTree of L.filesUnder(tree, r.dir, r.exclude ?? [])) byPath.set(entryOfTree.path, entryOfTree);
    const rootLicences = L.rootLicenceFiles(tree);
    licenceFile = cfg.licenceFile ?? rootLicences[0] ?? null;
    if (licenceFile) { const lic = tree.find((t) => t.path === licenceFile && t.type === 'blob'); if (!lic) throw new Error(`${id}: the licence file ${licenceFile} is not in the tree`); byPath.set(lic.path, lic); }
    const entries = [...byPath.values()].sort((a, b) => (a.path < b.path ? -1 : 1));
    const got = await fetchFiles(id, repo, pin, entries);
    files = got.files;
    for (const [p, text] of got.texts) {
      const dir = p === 'SKILL.md' ? '' : p.slice(0, -'/SKILL.md'.length);
      const folder = dir.split('/').pop();
      const old = (prev.skills ?? []).find((s) => s.folder === folder);
      skills.push({ folder, name: L.parseFrontmatterName(text), path: dir, needs: old?.needs ?? [], ...(old?.termsClass ? { termsClass: old.termsClass } : {}), ...(old?.pin ? { pin: old.pin } : {}) });
    }
    if (layout.type === 'plugins') folders = roots.map((r) => ({ name: r.plugin, skillCount: skills.filter((s) => s.path === r.dir || s.path.startsWith(r.dir + '/')).length }));
    const perFolder = L.folderLicenceFiles(files).filter((p) => p !== licenceFile);
    if (!licenceFile) e.notes.push(`no licence file at the repository root; licence or notice files inside the folders (${perFolder.length}): ${perFolder.slice(0, 20).join(', ')}${perFolder.length > 20 ? ', ...' : ''}. The catalogue holds one licenceFile per item: choose one with "licenceFile" in sources.json, or change the schema`);
    else if (perFolder.length > 0) e.notes.push(`the licence file is ${licenceFile}; ${perFolder.length} more licence or notice files sit inside the folders`);
  }
  skills.sort((a, b) => (a.path < b.path ? -1 : 1));
  const dup = skills.map((s) => s.folder).filter((f, i, all) => all.indexOf(f) !== i);
  if (dup.length > 0) warn(id, `skill folder names repeated inside the item: ${[...new Set(dup)].join(', ')} (SK-1's gate allows a folder name once per item)`);
  for (const s of skills) if (s.name === null) warn(id, `skill ${s.path} has no name in its front matter`);
  else if (s.name !== s.folder) warn(id, `skill ${s.path}: front matter name "${s.name}" is not its folder name "${s.folder}"`);
  if (prev.skillCount != null && skills.length !== prev.skillCount) warn(id, `${skills.length} skills found, the plan counts ${prev.skillCount}`);
  if ((prev.skills ?? []).length > 0 && layout.type !== 'perSkillPins') {
    const was = prev.skills.map((s) => s.folder).sort(), now = skills.map((s) => s.folder).sort();
    if (was.join() !== now.join()) warn(id, `the skill folders differ from the plan's list: missing ${was.filter((x) => !now.includes(x)).join(', ') || '(none)'}; new ${now.filter((x) => !was.includes(x)).join(', ') || '(none)'}`);
  }
  const total = files.reduce((n, f) => n + f.size, 0);
  const off = L.percentOff(prev.approxSizeMb, L.mbFromBytes(total));
  if (off !== null && Math.abs(off) > 15) warn(id, `measured ${L.mbFromBytes(total)} MB differs from the plan's ${prev.approxSizeMb} MB by ${off}%`);
  e.notes.push(`${files.length} files, ${total} bytes, from ${rootsUsed.length} folder(s)`);
  return {
    ...prev, pin, sourceUrl: prev.sourceUrl ?? `https://github.com/${repo}`, licenceFile, licenceUrl: licenceFile && pin ? L.rawUrl(repo, pin, licenceFile) : perSkillLicenceUrl,
    files, skillCount: skills.length, skillsListed: true, skills, folders, approxSizeMb: L.mbFromBytes(total),
  };
}

// ---------------------------------------------------------------- the run
function plan(id, prev, cfg) {
  const needs = [];
  const v = cfg?.verify?.type;
  if (v === 'sigstore' && !args.skipSignatures && !(args.sigstoreIdentity && args.sigstoreIssuer)) needs.push('--sigstore-identity and --sigstore-issuer');
  if (v === 'nodeShasums' && !args.skipSignatures && !args.nodeKeys) needs.push('--node-keys');
  if (v === 'gpgAsc' && !args.skipSignatures && !args.tdfKey) needs.push('--tdf-key');
  return needs;
}

async function main() {
  const ids = previous.items.map((i) => i.id).filter((id) => !args.only || args.only.includes(id));
  if (args.only) for (const id of args.only) if (!previous.items.some((i) => i.id === id)) throw new Error(`--only names an item the catalogue does not have: ${id}`);
  const missingFlags = new Set();
  for (const id of ids) {
    const cfg = sources.items[id];
    if (!cfg) { log(`${id}: no entry in sources.json; kept as it is`); continue; }
    for (const n of plan(id, previous.items.find((i) => i.id === id), cfg)) missingFlags.add(`${id} needs ${n}`);
  }
  if (missingFlags.size > 0) { console.error([...missingFlags].join('\n') + '\n(or --skip-signatures, which reports those items as NOT VERIFIED)'); process.exit(2); }
  if (args.dryRun) {
    for (const id of ids) { const cfg = sources.items[id]; log(`${id}: ${cfg ? JSON.stringify(cfg.layout ?? cfg.fetch) : 'not configured (kept)'}`); }
    log('dry run: nothing was fetched or written');
    return;
  }
  if (fs.existsSync(outDir) && fs.readdirSync(outDir).some((n) => n !== 'cache') && !args.reuseCache) throw new Error(`${outDir} is not empty; give --out a new folder`);
  fs.mkdirSync(outDir, { recursive: true });
  logStream = fs.createWriteStream(path.join(outDir, 'run.log'));

  const next = structuredClone(previous);
  next.readOn = args.readOn;
  for (const id of ids) {
    const cfg = sources.items[id];
    const prev = previous.items.find((i) => i.id === id);
    if (!cfg) continue;
    log(`--- ${id} ${prev.title}`);
    try {
      let built;
      if (prev.kind === 'skillset' || prev.kind === 'plugin') built = await buildRepoItem(prev, cfg);
      else if (prev.kind === 'tool') built = await buildTool(prev, cfg);
      else if (prev.kind === 'pyset') built = await buildPyset(prev, cfg);
      else if (prev.kind === 'nodeset') built = await buildNodeset(prev, cfg);
      else throw new Error(`unknown kind ${prev.kind}`);
      next.items[next.items.findIndex((i) => i.id === id)] = built;
    } catch (error) {
      record.errors.push({ id, message: error.message });
      log(`ERROR ${id}: ${error.message}`);
    }
  }

  fs.writeFileSync(path.join(outDir, 'catalogue.raw.json'), JSON.stringify(next, null, 2) + '\n');
  record.finishedAt = new Date().toISOString();
  fs.writeFileSync(path.join(outDir, 'run-record.json'), JSON.stringify(record, null, 2) + '\n');
  if (record.errors.length > 0) {
    log(`\n${record.errors.length} item(s) failed; catalogue.raw.json holds the others and the previous values of the failed ones. No catalogue.json and no change report were written: fix the cause and run again (--reuse-cache keeps the downloads).`);
    process.exitCode = 1;
    return;
  }

  const tool = path.join(here, 'CatalogueTool.cs');
  const dotnet = (toolArgs) => spawnSync('dotnet', ['run', tool, '--', ...toolArgs], { stdio: 'inherit', cwd: repoRoot });
  const finalPath = path.join(outDir, 'catalogue.json'), reportPath = path.join(outDir, 'change-report.md');
  const fin = dotnet(['finalize', path.join(outDir, 'catalogue.raw.json'), finalPath, ...(args.strict ? ['--strict'] : [])]);
  if (!fs.existsSync(finalPath)) { log('the .NET tool did not write catalogue.json; is the .NET 10 SDK installed?'); process.exitCode = 1; return; }
  const rep = dotnet(['report', args.previous, finalPath, reportPath, ...(args.maxFiles != null ? ['--max-files', String(args.maxFiles)] : [])]);
  if (!fs.existsSync(reportPath)) { log('the .NET tool did not write the change report'); process.exitCode = 1; return; }
  fs.appendFileSync(reportPath, pinRunSection());
  log(`\nwrote ${finalPath} and ${reportPath}`);
  if (fin.status !== 0) { log('The gate has findings (above). catalogue.json was written so it can be read; it is not fit to ship until they are resolved.'); process.exitCode = 1; }
  if (rep.status !== 0) process.exitCode = 1;
}

function pinRunSection() {
  const lines = ['', '## Pin run', '', `Run on ${record.startedAt} with Node ${record.node}. Signatures: ${args.skipSignatures ? 'SKIPPED (--skip-signatures)' : 'checked where a method is configured'}.`, ''];
  for (const [id, e] of Object.entries(record.items)) {
    lines.push(`### ${id} (${e.handler})`, '');
    for (const v of e.verification) lines.push(`- ${v.ok ? 'verified' : 'NOT VERIFIED'}: ${v.artifact} [${v.method}] ${v.detail}`);
    for (const n of e.notes) lines.push(`- note: ${n}`);
    for (const w of e.warnings) lines.push(`- WARNING: ${w}`);
    const hosts2 = [...new Set(e.sources.map((s) => s.finalHost))];
    if (hosts2.length > 0) lines.push(`- bytes came from: ${hosts2.join(', ')}`);
    lines.push('');
  }
  return lines.join('\n');
}

/** Resolves when the run is over (the rehearsal harness waits on it). */
export const done = main().catch((error) => { console.error(error); process.exitCode = 1; }).finally(() => logStream?.end());
