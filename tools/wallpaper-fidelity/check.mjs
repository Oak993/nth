// Offline fixture validation. Does not start Chromium or use the network.
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { PNG } from 'pngjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const folder = join(root, 'tests/fixtures/wallpapers/reference');
const rawManifest = await readFile(join(folder, 'manifest.json'));
const manifest = JSON.parse(rawManifest);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
assert.equal(manifest.schemaVersion, 1);
assert.equal(manifest.width, 160); assert.equal(manifest.height, 100); assert.equal(manifest.dpr, 1);
assert.match(manifest.staticRenderer, /SwiftShader/); assert.match(manifest.animatedRenderer, /SwiftShader/);
for (const [path, expected] of Object.entries(manifest.sources)) {
  assert.equal(hash(await readFile(join(root, 'porting/first-release/design-src/wallpaper-library', path))), expected, `snapshot changed: ${path}`);
}
assert.equal(manifest.decision, 'ED-NEW-wl-reconcile');
assert.equal(manifest.omitted, 'none');
assert.equal(hash(await readFile(join(root, manifest.policy.file))), manifest.policy.sha256);
const files = new Map(), keys = new Set(), names = new Set();
for (const collection of ['static', 'animated']) {
  const data = JSON.parse(await readFile(join(root, 'porting/first-release/design-src/wallpaper-library', collection, 'wallpapers.json')));
  const cases = manifest.renders.filter(r => r.collection === collection);
  assert.equal(new Set(cases.map(r => r.wallpaper)).size, collection === 'static' ? 90 : 72);
  for (const w of data.wallpapers) {
    const entries = cases.filter(r => r.wallpaper === w.id);
    assert.ok(entries.length, `missing ${collection}/${w.id}`);
    names.add(w.name);
    for (const variant of ['default', 'quiet', 'rich', 'dynamic-dusk', 'match-accent', 'full-focus', 'perspective', 'intermediate-focus', 'authored-focus']) {
      const rows = entries.filter(r => r.variant === variant);
      const times = collection === 'animated' ? [0, w.period / 2] : [0];
      const samples = variant === 'dynamic-dusk' || variant === 'match-accent' ? 2 : 1;
      assert.equal(rows.length, 2 * times.length * samples, `${w.id}/${variant}: matrix row count`);
      for (const dark of [true, false]) for (const time of times) assert.equal(rows.filter(r => r.dark === dark && r.time === time).length, samples);
    }
    for (const dark of [true, false]) for (const time of collection === 'animated' ? [0, w.period / 2] : [0]) {
      assert.ok(entries.some(r => r.variant === 'default' && r.dark === dark && r.time === time), `${w.id}: default theme/phase missing`);
    }
    for (const r of entries) {
      assert.equal(r.name, w.name); assert.equal(r.period, w.period ?? 0); assert.ok([18, 15.25].includes(r.clockHours)); assert.ok(['#005FB8', '#D4501A'].includes(r.accent));
      assert.equal(r.dynamic, r.variant === 'dynamic-dusk'); assert.equal(r.focus, r.variant === 'full-focus' ? 1 : r.variant === 'intermediate-focus' ? .37 : r.variant === 'authored-focus' ? (collection === 'static' ? w.focus.def : w.focus.default) : 0); assert.deepEqual(r.tilt, r.variant === 'perspective' ? [.65, -.4] : [0, 0]);
      assert.equal(r.uniforms.palette.length, 18); assert.ok(r.uniforms.palette.every(Number.isFinite));
      assert.match(r.file, /^[a-f0-9]{64}\.png$/); assert.equal(r.file, `${r.sha256}.png`);
      const key = `${collection}/${w.id}/${r.variant}/${r.dark}/${r.time}/${r.clockHours}/${r.accent}/${r.focus}/${r.tilt}`;
      assert.ok(!keys.has(key), `duplicate case ${key}`); keys.add(key);
      if (r.variant !== 'match-accent') assert.equal(r.packId, w.packs[r.packIndex].id ?? w.packs[r.packIndex].name);
      if (r.variant === 'default') assert.equal(r.packId, collection === 'static' ? w.packs[w.defaultPack].name : w.defaults.pack);
      if (!files.has(r.file)) {
        const png = await readFile(join(folder, r.file));
        assert.equal(hash(png), r.sha256, key);
        const decoded = PNG.sync.read(png);
        assert.equal(decoded.width, 160); assert.equal(decoded.height, 100);
        assert.equal(hash(decoded.data), r.rgbaSha256, `pixels ${key}`);
        assert.ok(decoded.data.every((v, i) => i % 4 !== 3 || v === 255), `alpha ${key}`);
        files.set(r.file, png.length);
      }
      assert.equal(files.get(r.file), r.bytes);
    }
  }
}
const pngBytes = [...files.values()].reduce((a, b) => a + b, 0);
assert.equal(names.size, 162); assert.equal(keys.size, manifest.counts.renders);
assert.equal(files.size, manifest.counts.uniquePngs); assert.equal(pngBytes, manifest.counts.pngBytes);
assert.ok(pngBytes + rawManifest.length < 100_000_000);
assert.deepEqual((await readdir(folder)).sort(), ['manifest.json', ...files.keys()].sort());
console.log(`PASS: ${keys.size} cases, all 162 wallpapers, ${files.size} lossless PNGs, ${pngBytes + rawManifest.length} bytes; source/PNG/pixel hashes, coverage and inputs verified offline`);
