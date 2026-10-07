// Round 2 / RR-13, RR-14. Synthetic source responses only; this test never imports a network transport.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { generatePins, readMetadata, requestOfficial } from './pin-generator.mjs';
const sources = JSON.parse(fs.readFileSync(new URL('./sources.json', import.meta.url)));
const catalogue = JSON.parse(fs.readFileSync(new URL('../../src/Marrow.Core/Port/Skills/Catalogue/catalogue.json', import.meta.url)));
const sha = b => createHash('sha256').update(b).digest('hex');
const date = '2026-10-07T12:00:00.000Z';
const bytes = Buffer.from('abc');
const metadata = Buffer.from('{"version":"fixture-version","license":"fixture-license"}');
const fixtures = new Map();
const manifest = { schema: 1, items: catalogue.items.map(item => {
    const cfg = sources.items[item.id];
    const repo = cfg.repo ?? cfg.fetch?.repo;
    const base = repo ? `https://raw.githubusercontent.com/${repo}/${'a'.repeat(40)}/` :
      item.id === 'P6' ? 'https://registry.npmjs.org/fixture/' : item.id.startsWith('P') || item.id === 'T1p' ? 'https://pypi.org/fixture/' :
      item.id === 'T1' ? 'https://www.python.org/fixture/' : item.id === 'T2' ? 'https://nodejs.org/fixture/' :
      item.id === 'T3' ? 'https://download.documentfoundation.org/fixture/' : 'https://cdn.playwright.dev/fixture/';
    const artifactUrl = base + item.id + '.artifact';
    const metadataUrl = base + item.id + '.metadata';
    fixtures.set(artifactUrl, bytes); fixtures.set(metadataUrl, metadata);
    return { id: item.id, entries: [{ name: 'fixture.bin', url: artifactUrl, metadata: {
      version: { url: metadataUrl, format: 'json', pointer: '/version' },
      licence: { url: metadataUrl, format: 'json', pointer: '/license' }
    } }] };
}) };
const request = async url => {
    assert.ok(fixtures.has(url), `unmapped fixture URL ${url}`);
    return { url, chunks: (async function* () { yield fixtures.get(url).subarray(0, 1); yield fixtures.get(url).subarray(1); })() };
};

test('all 29 item families get independently computed bytes/hash and source metadata provenance', async () => {
    const result = await generatePins(manifest, { sources, request, now: () => new Date(date) });
    assert.equal(result.items.length, 29);
    for (const item of result.items) {
      const pin = item.entries[0];
      assert.equal(pin.size, 3);
      assert.equal(pin.sha256, 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
      assert.equal(pin.version.value, 'fixture-version');
      assert.equal(pin.licence.value, 'fixture-license');
      assert.equal(pin.version.sha256, sha(metadata));
      assert.equal(pin.licence.readAt, date);
      assert.equal(pin.licence.selector.pointer, '/license');
      assert.ok(pin.version.url.startsWith('https://'));
    }
});

test('missing source licence and version never fall back to previous catalogue values', async () => {
    const broken = structuredClone(manifest);
    broken.items[0].entries[0].metadata.licence.pointer = '/missing';
    await assert.rejects(generatePins(broken, { sources, request, now: () => new Date(date) }), /missing|empty/);
    delete broken.items[0].entries[0].metadata.version;
    await assert.rejects(generatePins(broken, { sources, request, now: () => new Date(date) }), /version/);
});

test('published metadata mismatch and omitted/duplicate item coverage fail closed', async () => {
    const broken = structuredClone(manifest);
    broken.items[0].entries[0].expectedSha256 = '0'.repeat(64);
    await assert.rejects(generatePins(broken, { sources, request, now: () => new Date(date) }), /digest/);
    broken.items.pop();
    await assert.rejects(generatePins(broken, { sources, request }), /29|coverage/);
    broken.items.push(broken.items[0]);
    await assert.rejects(generatePins(broken, { sources, request }), /coverage|duplicate/);
});

test('metadata supports exact text, base64 source files and JSON pointer escapes without licence classification', () => {
    assert.equal(readMetadata(Buffer.from('Version: 1.2\nLicence: Fixture only\n'), { format: 'text', pattern: '^Licence: (.+)$' }), 'Fixture only');
    assert.equal(readMetadata(Buffer.from(Buffer.from('Fixture licence text\n').toString('base64')), { format: 'base64' }), 'Fixture licence text');
    assert.equal(readMetadata(Buffer.from('{"a/b":{"~c":"value"}}'), { format: 'json', pointer: '/a~1b/~0c' }), 'value');
    assert.throws(() => readMetadata(Buffer.from('{"license":null}'), { format: 'json', pointer: '/license' }), /empty/);
});

test('insecure/off-source/cross-repository URLs are rejected before a transport sees them', async () => {
    for (const url of ['http://raw.githubusercontent.com/anthropics/skills/file', 'https://raw.githubusercontent.com/evil/skills/file', 'https://user:secret@raw.githubusercontent.com/anthropics/skills/file']) {
      const broken = structuredClone(manifest); broken.items[0].entries[0].url = url;
      let called = false;
      await assert.rejects(generatePins(broken, { sources, request: async () => { called = true; throw new Error('transport reached'); } }), /official|HTTPS|credential/);
      assert.equal(called, false);
    }
});

test('manual redirect transport checks every hop and sends no credentials', async () => {
    const seen = [];
    const transport = async (url, options) => {
      seen.push(url); assert.equal(options.redirect, 'manual'); assert.equal(options.headers.Authorization, undefined);
      return new Response(null, { status: 302, headers: { Location: 'https://evil.test/artifact' } });
    };
    await assert.rejects(requestOfficial('https://nodejs.org/dist/fixture.zip', u => new URL(u).hostname === 'nodejs.org', transport), /official/);
    assert.deepEqual(seen, ['https://nodejs.org/dist/fixture.zip']);
});

test('redirected/injected byte sources, empty artifacts and wrong published sizes are rejected', async () => {
    const broken = structuredClone(manifest);
    await assert.rejects(generatePins(broken, { sources, request: async url => ({ ...(await request(url)), url: 'https://evil.test/file' }) }), /official|redirect/);
    broken.items[0].entries[0].expectedSize = 1;
    await assert.rejects(generatePins(broken, { sources, request }), /size/);
    await assert.rejects(generatePins(manifest, { sources, request: async url => ({ url, chunks: (async function* () {})() }) }), /empty/);
});

test('official array metadata is selected by an observed field, with ambiguity rejected', () => {
    const selector = { format: 'json', pointer: '/versions', match: { field: 'version', value: 'fixture' }, valuePointer: '/version' };
    assert.equal(readMetadata(Buffer.from('{"versions":[{"version":"other"},{"version":"fixture"}]}'), selector), 'fixture');
    assert.throws(() => readMetadata(Buffer.from('{"versions":[{"version":"fixture"},{"version":"fixture"}]}'), selector), /exactly one/);
});

test('saved artifact chunks are the exact bytes hashed, and mismatched version metadata is rejected', async () => {
    const saved = new Map();
    await generatePins(manifest, { sources, request, saveChunk: async (id, name, chunk) => {
      const key = id + '/' + name;
      saved.set(key, Buffer.concat([saved.get(key) ?? Buffer.alloc(0), chunk]));
    } });
    assert.equal(saved.size, 29);
    for (const value of saved.values()) assert.deepEqual(value, bytes);
    const broken = structuredClone(manifest);
    broken.items[0].entries[0].expectedVersion = 'different';
    await assert.rejects(generatePins(broken, { sources, request }), /version/);
});

test('raw metadata bodies are available to the evidence sink under their computed hashes', async () => {
    const saved = [];
    await generatePins(manifest, { sources, request, saveMetadata: async (url, body) => saved.push({ url, body: Buffer.from(body) }) });
    assert.equal(saved.length, 29);
    for (const item of saved) assert.deepEqual(item.body, metadata);
});

test('release CDN hosts can only be redirect destinations from an official repository endpoint', async () => {
    const item = manifest.items.find(i => i.id === 'T4');
    const broken = structuredClone(manifest);
    broken.items.find(i => i.id === 'T4').entries[0].url = 'https://release-assets.githubusercontent.com/arbitrary';
    await assert.rejects(generatePins(broken, { sources, request }), /official/);
    const { sourcePolicy } = await import('./pin-generator.mjs');
    const allowed = sourcePolicy(item.id, sources.items[item.id]);
    const seen = [];
    const reply = await requestOfficial('https://github.com/oschwartz10612/poppler-windows/releases/download/fixture/file.zip', allowed, async url => {
      seen.push(url);
      return seen.length === 1 ? new Response(null, { status: 302, headers: { Location: 'https://release-assets.githubusercontent.com/official-redirect' } }) : new Response(bytes);
    });
    assert.equal(reply.url, 'https://release-assets.githubusercontent.com/official-redirect');
    assert.equal(seen.length, 2);
});

test('malformed metadata encoding and explicitly unknown licence metadata are blockers', async () => {
    assert.throws(() => readMetadata(Buffer.from([255]), { format: 'text' }), /UTF|encoding|decode/i);
    assert.throws(() => readMetadata(Buffer.from('not%base64'), { format: 'base64' }), /base64|encoding/);
    const unknown = Buffer.from('{"version":"fixture-version","license":"NOASSERTION"}');
    await assert.rejects(generatePins(manifest, { sources, request: async url => ({ url, chunks: (async function* () { yield url.endsWith('.metadata') ? unknown : bytes; })() }) }), /unknown|licence/);
});

test('artifact output names respect Windows paths and case-insensitive collisions', async () => {
    for (const name of ['CON.txt', 'bad?.zip', 'bad./file.zip', 'directory/../file.zip']) {
      const broken = structuredClone(manifest); broken.items[0].entries[0].name = name;
      await assert.rejects(generatePins(broken, { sources, request }), /artifact|Windows|path/);
    }
    const broken = structuredClone(manifest);
    broken.items[0].entries.push({ ...broken.items[0].entries[0], name: 'FIXTURE.BIN' });
    await assert.rejects(generatePins(broken, { sources, request }), /duplicate|collision/i);
});

test('each artifact and cached source metadata records its actual read time across a date boundary', async () => {
    const moments = ['2026-10-07T23:59:00Z', '2026-10-08T00:01:00Z', '2026-10-08T00:02:00Z'];
    let at = 0;
    const result = await generatePins(manifest, { sources, request, now: () => new Date(moments[Math.min(at++, moments.length - 1)]) });
    const pin = result.items[0].entries[0];
    assert.equal(pin.version.readAt, '2026-10-08T00:01:00.000Z');
    assert.equal(pin.licence.readAt, pin.version.readAt);
    assert.equal(pin.readAt, '2026-10-08T00:02:00.000Z');
});
