import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { prepareManifest } from './pin-manifest.mjs';
const sources = JSON.parse(fs.readFileSync(new URL('./sources.json', import.meta.url)));
const sha = 'a'.repeat(40);

test('repository recipes use resolved full commits and nearest actual licence files, including per-skill commits', () => {
    const manifest = prepareManifest({ items: [{ id: 'O3', pin: null, skills: [{ path: 'skills/fixture', pin: sha }], files: [{ path: 'skills/fixture/SKILL.md', size: 3, sha256: 'b'.repeat(64) }, { path: 'skills/fixture/LICENSE' }] }] }, sources);
    const entry = manifest.items[0].entries[0];
    assert.equal(entry.url, `https://raw.githubusercontent.com/openai/skills/${sha}/skills/fixture/SKILL.md`);
    assert.equal(entry.metadata.version.url, `https://api.github.com/repos/openai/skills/commits/${sha}`);
    assert.equal(entry.metadata.licence.url, `https://raw.githubusercontent.com/openai/skills/${sha}/skills/fixture/LICENSE`);
    assert.equal(entry.metadata.licence.format, 'text');
});

test('wheel and npm recipes read licences and versions from the packages own registry metadata', () => {
    const manifest = prepareManifest({ items: [
        { id: 'P1', artifacts: [{ name: 'fixture-1.2-py3-none-any.whl', url: 'https://files.pythonhosted.org/fixture.whl' }] },
        { id: 'P6', packages: [{ name: '@fixture/pkg', version: '1.2' }], artifacts: [{ name: 'pkg-1.2.tgz', url: 'https://registry.npmjs.org/@fixture/pkg/-/pkg-1.2.tgz' }] }
    ] }, sources);
    assert.equal(manifest.items[0].entries[0].metadata.licence.pointer, '/info/license_expression');
    assert.equal(manifest.items[1].entries[0].metadata.version.url, 'https://registry.npmjs.org/%40fixture%2Fpkg/1.2');
});

test('Python, Node and browser direct recipes reference official source metadata rather than reuse licence claims', () => {
    const manifest = prepareManifest({ items: ['T1', 'T2', 'T10'].map(id => ({ id, pin: '1.2.3', licenceName: 'untrusted-old-claim', artifacts: [{ name: 'fixture.zip', url: 'https://nodejs.org/fixture.zip' }] })) }, sources);
    for (const item of manifest.items) {
        assert.ok(item.entries[0].metadata.version.url.startsWith('https://'));
        assert.ok(item.entries[0].metadata.licence.url.startsWith('https://'));
        assert.equal(JSON.stringify(item).includes('untrusted-old-claim'), false);
    }
});

test('missing resolved artifacts, truncated commits and ambiguous licence selection remain explicit blockers', () => {
    assert.throws(() => prepareManifest({ items: [{ id: 'T1p', artifacts: [{ name: 'fixture.whl' }] }] }, sources), /resolver|URL/);
    assert.throws(() => prepareManifest({ items: [{ id: 'A1', pin: 'abc', files: [{ path: 'SKILL.md' }] }] }, sources), /full resolved commit/);
    const result = prepareManifest({ items: [{ id: 'T3', artifacts: [{ name: 'fixture.msi', url: 'https://download.documentfoundation.org/fixture.msi' }] }] }, sources);
    assert.equal(result.items[0].entries[0].metadata.version, null);
});

test('resolved version expectations are checked and multiple co-located licences need explicit source selection', () => {
    const repo = prepareManifest({ items: [{ id: 'A1', pin: sha, files: [{ path: 'skill/SKILL.md' }, { path: 'skill/LICENSE' }, { path: 'skill/COPYING' }] }] }, sources);
    assert.equal(repo.items[0].entries[0].expectedVersion, sha);
    assert.equal(repo.items[0].entries[0].metadata.licence, null);
    const packages = prepareManifest({ items: [{ id: 'P1', artifacts: [{ name: 'fixture-1.2-py3-none-any.whl', url: 'https://files.pythonhosted.org/fixture.whl' }] }] }, sources);
    assert.equal(packages.items[0].entries[0].expectedVersion, '1.2');
});
