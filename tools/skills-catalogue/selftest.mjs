// Offline check of lib.mjs (unit SK-2). Run: node tools/skills-catalogue/selftest.mjs . No network, no files read or written. Exits 1 on the first failed expectation.
import assert from 'node:assert/strict';
import * as L from './lib.mjs';

const t = async (name, fn) => { await fn(); console.log('ok  ' + name); };
const tree = (paths) => paths.map((p) => ({ path: p, type: 'blob', mode: '100644', sha: 'x', size: 1 }));

await t('sha256 and git blob sha1', () => {
  assert.equal(L.sha256Hex(Buffer.from('abc')), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  // git hash-object of the empty file, and of "hello\n" (known object ids)
  assert.equal(L.gitBlobSha1(Buffer.from('')), 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391');
  assert.equal(L.gitBlobSha1(Buffer.from('hello\n')), 'ce013625030ba8dba906f756967f9e9ca394464a');
  assert.equal(L.short12('0123456789abcdef0123'), '0123456789ab');
  assert.equal(L.mbFromBytes(8_123_456), 8.12);
  assert.equal(L.mbFromBytes(280_000), 0.28);
});

await t('front matter name', () => {
  assert.equal(L.parseFrontmatterName('---\nname: docx\ndescription: x\n---\nbody'), 'docx');
  assert.equal(L.parseFrontmatterName('---\r\nname: "quoted-name"\r\n---\r\n'), 'quoted-name');
  assert.equal(L.parseFrontmatterName("﻿---\nname: 'single'\n---\n"), 'single');
  assert.equal(L.parseFrontmatterName('---\ndescription: x\n---\nname: not-in-block'), null);
  assert.equal(L.parseFrontmatterName('no front matter\nname: x'), null);
  assert.equal(L.parseFrontmatterName('---\nname:\n---\n'), null);
  assert.equal(L.parseFrontmatterName('---\nname: unterminated\n'), null);
});

await t('SHASUMS and single-file sha256', () => {
  const a = 'a'.repeat(64), b = 'b'.repeat(64);
  const map = L.parseShasums(`${a}  node-v24.21.0-win-x64.zip\n${b} *node-v24.21.0-x64.msi\n\nnot a line\n`);
  assert.equal(map.get('node-v24.21.0-win-x64.zip'), a);
  assert.equal(map.get('node-v24.21.0-x64.msi'), b);
  assert.equal(map.size, 2);
  assert.equal(L.parseSha256File(`${a.toUpperCase()}  x.msi\n`, 'x.msi'), a);
  assert.equal(L.parseSha256File(`${a}  other.msi\n`, 'x.msi'), null);
  assert.equal(L.parseSha256File(`${a}\n`, 'x.msi'), a);
  assert.equal(L.parseSha256File('garbage', 'x.msi'), null);
});

await t('integrity', async () => {
  const bytes = Buffer.from('tarball bytes');
  const good = 'sha512-' + (await import('node:crypto')).createHash('sha512').update(bytes).digest('base64');
  assert.equal(L.integrityMatches(good, bytes), true);
  assert.equal(L.integrityMatches('sha512-AAAA ' + good, bytes), true);
  assert.equal(L.integrityMatches('sha512-AAAA', bytes), false);
  assert.equal(L.integrityMatches('md5-xyz', bytes), false);
  assert.equal(L.integrityMatches('', bytes), false);
});

await t('raw url encodes each segment and keeps slashes', () => {
  assert.equal(L.rawUrl('openai/skills', 'a'.repeat(40), 'skills/.curated/my skill/SKILL.md'), `https://raw.githubusercontent.com/openai/skills/${'a'.repeat(40)}/skills/.curated/my%20skill/SKILL.md`);
  assert.equal(L.isFullCommit('a'.repeat(40)), true);
  assert.equal(L.isFullCommit('a'.repeat(39)), false);
  assert.equal(L.isFullCommit('A'.repeat(40)), false);
});

await t('skill directories and folders', () => {
  const tr = tree(['LICENSE', 'README.md', '.github/x/SKILL.md', 'sales/skills/a/SKILL.md', 'sales/skills/b/SKILL.md', 'sales/commands/c.md', 'legal/skills/a/SKILL.md', 'plugins/x/SKILL.md', 'top/SKILL.md', 'skills/.curated/doc/SKILL.md', 'skills/.curated/doc/scripts/s.py', 'skills/.curated/figma-a/SKILL.md', 'skills/.system/plugin-creator/SKILL.md']);
  assert.deepEqual(L.skillDirs(tr), ['.github/x', 'legal/skills/a', 'plugins/x', 'sales/skills/a', 'sales/skills/b', 'skills/.curated/doc', 'skills/.curated/figma-a', 'skills/.system/plugin-creator', 'top']);
  assert.deepEqual(L.childDirsWithSkills(tr, ''), ['legal', 'plugins', 'sales', 'skills', 'top']);
  assert.deepEqual(L.childDirsWithSkills(tr, 'plugins'), ['x']);
  assert.deepEqual(L.childDirsWithSkills(tr, 'sales'), ['skills']);
  assert.deepEqual(L.childDirsWithSkills(tr, 'nothing'), []);
  assert.deepEqual(L.childSkillFolders(tr, 'skills/.curated'), ['doc', 'figma-a']);
  assert.deepEqual(L.childSkillFolders(tr, 'skills/.system'), ['plugin-creator']);
  assert.deepEqual(L.childSkillFolders(tr, 'sales'), []);
});

await t('files under a directory, with excluded segments', () => {
  const tr = tree(['p/a.md', 'p/fixtures/f.md', 'p/sub/fixtures/g.md', 'p/sub/h.md', 'q/z.md', 'p/fixtures']);
  assert.deepEqual(L.filesUnder(tr, 'p').map((e) => e.path), ['p/a.md', 'p/fixtures', 'p/fixtures/f.md', 'p/sub/fixtures/g.md', 'p/sub/h.md']);
  assert.deepEqual(L.filesUnder(tr, 'p', ['fixtures']).map((e) => e.path), ['p/a.md', 'p/fixtures', 'p/sub/h.md']);
  assert.deepEqual(L.filesUnder(tr, '').length, 6);
  assert.deepEqual(L.filesUnder(tr, 'pp'), []);
});

await t('symlinks and submodules are unsupported, licences are found', () => {
  const entries = [{ path: 'a', type: 'blob', mode: '120000' }, { path: 'b', type: 'commit', mode: '160000' }, { path: 'c', type: 'blob', mode: '100644' }];
  assert.deepEqual(L.unsupportedEntries(entries).map((e) => e.path), ['a', 'b']);
  assert.deepEqual(L.rootLicenceFiles(tree(['COPYING', 'LICENSE.md', 'sub/LICENSE'])), ['LICENSE.md', 'COPYING']);
  assert.deepEqual(L.rootLicenceFiles(tree(['sub/LICENSE'])), []);
  assert.deepEqual(L.folderLicenceFiles(tree(['s/LICENSE.txt', 's/NOTICE', 's/licence.md', 's/a.py', 's/COPYINGS'])), ['s/LICENSE.txt', 's/NOTICE', 's/licence.md']);
});

await t('python names, wheel names and lock pins', () => {
  assert.equal(L.normalizePyName('Xlsx_Writer.x'), 'xlsx-writer-x');
  assert.deepEqual(L.wheelInfo('pip-26.2.1-py3-none-any.whl'), { dist: 'pip', version: '26.2.1' });
  assert.deepEqual(L.wheelInfo('python_docx-1.2.0-py3-none-any.whl'), { dist: 'python-docx', version: '1.2.0' });
  assert.deepEqual(L.wheelInfo('numpy-2.5.3-cp314-cp314-win_amd64.whl'), { dist: 'numpy', version: '2.5.3' });
  assert.deepEqual(L.wheelInfo('pkg-1.0-1-py3-none-any.whl'), { dist: 'pkg', version: '1.0' });
  assert.equal(L.wheelInfo('not-a-wheel.zip'), null);
  const pin = L.lockPin(['b.whl ' + 'b'.repeat(64), 'a.whl ' + 'a'.repeat(64)]);
  assert.match(pin, /^lock-[0-9a-f]{16}$/);
  assert.equal(pin, L.lockPin(['a.whl ' + 'a'.repeat(64), 'b.whl ' + 'b'.repeat(64)]));
  assert.notEqual(pin, L.lockPin(['a.whl ' + 'a'.repeat(64)]));
});

await t('npm lock filtering by platform', () => {
  assert.equal(L.npmConstraintAllows(undefined, 'win32'), true);
  assert.equal(L.npmConstraintAllows(['darwin'], 'win32'), false);
  assert.equal(L.npmConstraintAllows(['win32', 'linux'], 'win32'), true);
  assert.equal(L.npmConstraintAllows(['!win32'], 'win32'), false);
  assert.equal(L.npmConstraintAllows(['!darwin'], 'win32'), true);
  const lock = { packages: {
    '': { name: 'root' },
    'node_modules/docx': { version: '9.8.1', resolved: 'https://registry.npmjs.org/docx/-/docx-9.8.1.tgz', integrity: 'sha512-x' },
    'node_modules/@img/sharp-win32-x64': { version: '0.35.5', resolved: 'https://registry.npmjs.org/@img/sharp-win32-x64/-/sharp-win32-x64-0.35.5.tgz', integrity: 'sha512-y', os: ['win32'], cpu: ['x64'] },
    'node_modules/@img/sharp-darwin-arm64': { version: '0.35.5', resolved: 'https://registry.npmjs.org/@img/sharp-darwin-arm64/-/sharp-darwin-arm64-0.35.5.tgz', integrity: 'sha512-z', os: ['darwin'], cpu: ['arm64'] },
    'node_modules/linked': { version: '1.0.0' },
  } };
  const { kept, skipped } = L.filterNpmLock(lock);
  assert.deepEqual(kept.map((k) => k.name), ['docx', '@img/sharp-win32-x64']);
  assert.deepEqual(skipped.map((s) => s.name + ':' + s.reason), ['@img/sharp-darwin-arm64:platform', 'linked:no tarball or integrity']);
  assert.equal(L.tarballName('https://registry.npmjs.org/@img/sharp-win32-x64/-/sharp-win32-x64-0.35.5.tgz'), 'sharp-win32-x64-0.35.5.tgz');
});

await t('host allow-list is exact and https only', () => {
  const hosts = ['github.com', 'api.github.com'];
  assert.equal(L.hostAllowed('https://github.com/x', hosts), true);
  assert.equal(L.hostAllowed('http://github.com/x', hosts), false);
  assert.equal(L.hostAllowed('https://github.com.evil.example/x', hosts), false);
  assert.equal(L.hostAllowed('https://sub.github.com/x', hosts), false);
  assert.equal(L.hostAllowed('https://user@github.com/x', hosts), false);
  assert.equal(L.hostAllowed('not a url', hosts), false);
});

await t('percent off and the bounded pool', async () => {
  assert.equal(L.percentOff(8.1, 8.9), 10);
  assert.equal(L.percentOff(null, 8), null);
  assert.equal(L.percentOff(8, null), null);
});

const order = [];
const results = await L.mapLimit([30, 5, 10, 1], 2, async (ms, i) => { await new Promise((r) => setTimeout(r, ms)); order.push(i); return ms * 2; });
assert.deepEqual(results, [60, 10, 20, 2]);
assert.equal(order.length, 4);
console.log('ok  mapLimit keeps order');
console.log('selftest passed');
