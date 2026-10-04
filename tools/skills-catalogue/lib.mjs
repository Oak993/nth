// Pure helpers of the skills catalogue builder (unit SK-2, DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7). No network, no process, no file access in
// this file: everything here is a function of its arguments, so selftest.mjs can check it offline. The builder (build-catalogue.mjs) does the I/O.
import { createHash } from 'node:crypto';

export const sha256Hex = (bytes) => createHash('sha256').update(bytes).digest('hex');

/** The git object id of a blob: SHA-1 of "blob <length>\0" and the bytes. The GitHub tree API lists it, so a downloaded file can be checked against the tree. */
export const gitBlobSha1 = (bytes) => createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');

/** The first twelve digits of a long hash, as the change report shows them. */
export const short12 = (hex) => String(hex).slice(0, 12);

/** Decimal megabytes with two digits, the form approxSizeMb is kept in. */
export const mbFromBytes = (bytes) => Math.round(bytes / 1e4) / 100;

/** The value of the `name:` key of a SKILL.md front matter block, or null when there is no block or no such key. */
export function parseFrontmatterName(text) {
  const normalized = text.replace(/^﻿/, '').replace(/\r\n?/g, '\n');
  if (!normalized.startsWith('---\n')) return null;
  const end = normalized.indexOf('\n---', 4);
  if (end < 0) return null;
  for (const line of normalized.slice(4, end).split('\n')) {
    const match = /^name:\s*(.*?)\s*$/.exec(line);
    if (!match) continue;
    let value = match[1];
    if ((value.startsWith('"') && value.endsWith('"') && value.length >= 2) || (value.startsWith("'") && value.endsWith("'") && value.length >= 2)) value = value.slice(1, -1);
    return value.length > 0 ? value : null;
  }
  return null;
}

/** SHASUMS256.txt: lines of "<64 hex>  <file name>" (a leading * before the name marks binary mode); returns a Map from name to hash. */
export function parseShasums(text) {
  const map = new Map();
  for (const line of text.split(/\r?\n/)) {
    const match = /^([0-9a-f]{64})\s+\*?(.+?)\s*$/.exec(line);
    if (match) map.set(match[2], match[1]);
  }
  return map;
}

/** The hash a single-file ".sha256" next to a download holds: the first 64-hex token, accepted only when the file names no other file than expectedName. */
export function parseSha256File(text, expectedName) {
  const match = /^\s*([0-9a-fA-F]{64})(?:\s+\*?(\S.*?))?\s*$/m.exec(text);
  if (!match) return null;
  if (match[2] && match[2] !== expectedName) return null;
  return match[1].toLowerCase();
}

/** Subresource-integrity strings ("sha512-<base64>", several separated by blanks): true when any of them matches the bytes. */
export function integrityMatches(integrity, bytes) {
  for (const part of String(integrity).split(/\s+/)) {
    const match = /^(sha256|sha384|sha512)-(.+)$/.exec(part);
    if (match && createHash(match[1]).update(bytes).digest('base64') === match[2]) return true;
  }
  return false;
}

export const rawUrl = (repo, pin, path) => `https://raw.githubusercontent.com/${repo}/${pin}/${path.split('/').map(encodeURIComponent).join('/')}`;

export const isFullCommit = (text) => /^[0-9a-f]{40}$/.test(text);

const baseName = (path) => path.slice(path.lastIndexOf('/') + 1);
const dirName = (path) => (path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '');

/** Every directory that holds a SKILL.md file directly, sorted. A skill is such a directory (the plan counts SKILL.md files). */
export function skillDirs(tree) {
  return tree.filter((e) => e.type === 'blob' && baseName(e.path) === 'SKILL.md').map((e) => dirName(e.path)).sort(cmp);
}

const cmp = (a, b) => (a < b ? -1 : a > b ? 1 : 0);

/** The immediate child directories of `parent` ("" is the root) that have a SKILL.md anywhere beneath them, sorted; a dot-directory at the root is not a plugin. */
export function childDirsWithSkills(tree, parent) {
  const prefix = parent === '' ? '' : parent + '/';
  const names = new Set();
  for (const dir of skillDirs(tree)) {
    if (parent !== '' && !dir.startsWith(prefix)) continue;
    const rest = dir.slice(prefix.length);
    if (rest === '') continue;
    const first = rest.split('/')[0];
    if (parent === '' && first.startsWith('.')) continue;
    names.add(first);
  }
  return [...names].sort(cmp);
}

/** The immediate child directories of `parent` that hold a SKILL.md directly (the layout of a folder of skills), sorted. */
export function childSkillFolders(tree, parent) {
  const prefix = parent === '' ? '' : parent + '/';
  return skillDirs(tree).filter((d) => d.startsWith(prefix) && d.slice(prefix.length) !== '' && !d.slice(prefix.length).includes('/')).map((d) => d.slice(prefix.length)).sort(cmp);
}

/** The blobs under a directory ("" is the whole tree), minus any whose path below it has a segment named in excludeSegments; sorted by path. */
export function filesUnder(tree, dir, excludeSegments = []) {
  const prefix = dir === '' ? '' : dir + '/';
  return tree
    .filter((e) => e.type === 'blob' && e.path.startsWith(prefix))
    .filter((e) => !e.path.slice(prefix.length).split('/').slice(0, -1).some((s) => excludeSegments.includes(s)))
    .sort((a, b) => cmp(a.path, b.path));
}

/** Entries a set cannot hold: a symbolic link (mode 120000) or a submodule (type commit). The runtime verifies "no reparse point", so the builder refuses them. */
export const unsupportedEntries = (entries) => entries.filter((e) => e.mode === '120000' || e.type === 'commit');

export const ROOT_LICENCE_NAMES = ['LICENSE', 'LICENSE.md', 'LICENSE.txt', 'COPYING', 'COPYING.md', 'COPYING.txt'];

/** The licence files that sit at the root of the repository, in the order of ROOT_LICENCE_NAMES. */
export function rootLicenceFiles(tree) {
  return ROOT_LICENCE_NAMES.filter((name) => tree.some((e) => e.type === 'blob' && e.path === name));
}

/** Licence or notice files inside the given files, for the run record (a set whose folders carry their own licences has no single item-level licence file). */
export const folderLicenceFiles = (files) => files.filter((f) => /^(LICENSE|LICENCE|COPYING|NOTICE)(\.|$)/i.test(baseName(f.path))).map((f) => f.path);

/** PEP 503 name normalisation, so "XlsxWriter", "xlsxwriter" and "Xlsx_Writer" compare equal. */
export const normalizePyName = (name) => name.toLowerCase().replace(/[-_.]+/g, '-');

/** The distribution name and version of a wheel file name ("pip-26.2.1-py3-none-any.whl"); null when it is not one. */
export function wheelInfo(filename) {
  const match = /^([A-Za-z0-9_.]+)-([A-Za-z0-9_.!+]+)(?:-\d[^-]*)?-[^-]+-[^-]+-[^-]+\.whl$/.exec(filename);
  return match ? { dist: match[1].replace(/_/g, '-'), version: match[2] } : null;
}

/** The pin of a package set: "lock-" and the first 16 digits of the SHA-256 of its sorted "<file name> <sha256>" lines. A new set of files is a new pin. */
export function lockPin(lines) {
  return 'lock-' + sha256Hex([...lines].sort(cmp).join('\n')).slice(0, 16);
}

/** npm's os and cpu lists: allowed names, or names with a leading ! that are blocked. */
export function npmConstraintAllows(list, target) {
  if (!Array.isArray(list) || list.length === 0) return true;
  const blocked = list.filter((x) => x.startsWith('!')).map((x) => x.slice(1));
  const allowed = list.filter((x) => !x.startsWith('!'));
  if (blocked.includes(target)) return false;
  return allowed.length === 0 || allowed.includes(target);
}

/** The packages of a package-lock.json that belong to one platform: every entry but the root, with a registry tarball, whose os and cpu allow the target. */
export function filterNpmLock(lock, target = { os: 'win32', cpu: 'x64' }) {
  const kept = [];
  const skipped = [];
  for (const [path, entry] of Object.entries(lock.packages ?? {})) {
    if (path === '') continue;
    const name = path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length);
    if (!npmConstraintAllows(entry.os, target.os) || !npmConstraintAllows(entry.cpu, target.cpu)) { skipped.push({ name, reason: 'platform' }); continue; }
    if (!entry.resolved || !entry.integrity) { skipped.push({ name, reason: 'no tarball or integrity' }); continue; }
    kept.push({ path, name, version: entry.version, resolved: entry.resolved, integrity: entry.integrity });
  }
  return { kept, skipped };
}

/** The file name of a registry tarball URL ("https://registry.npmjs.org/@img/sharp-win32-x64/-/sharp-win32-x64-0.35.5.tgz" is "sharp-win32-x64-0.35.5.tgz"). */
export const tarballName = (url) => decodeURIComponent(new URL(url).pathname.split('/').pop());

/** True when the host is on the list (exact match, no subdomain). The first URL of every download must pass; redirects may go anywhere over https. */
export function hostAllowed(url, hosts) {
  try {
    const u = new URL(url);
    return u.protocol === 'https:' && !u.username && !u.password && hosts.includes(u.hostname);
  } catch {
    return false;
  }
}

/** The percentage by which a measured size differs from the plan's estimate, rounded; null when either is unknown. */
export const percentOff = (planned, measured) => (planned && measured != null ? Math.round(((measured - planned) / planned) * 100) : null);

/** Runs fn over items with at most `limit` at a time, keeping the order of the results. */
export async function mapLimit(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await fn(items[i], i);
    }
  });
  await Promise.all(workers);
  return results;
}

/** True when a command must go through the shell to start: on Windows npm and npx are .cmd files, which spawnSync cannot start directly (ENOENT for "npm", EINVAL for "npm.cmd"). */
export const needsShell = (cmd, platform) => platform === 'win32' && /^(npm|npx)$|\.(cmd|bat)$/i.test(cmd);
