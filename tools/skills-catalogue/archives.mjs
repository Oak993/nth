// Pure archive readers for the pin run's provenance checks (ED-NEW-dl-13). In memory only: nothing is written to disk and nothing read here is ever run. A zip (stored or
// deflated entries; ZIP64 sizes in the extra field, not a ZIP64 directory), a tar (ustar, pax and GNU long names) and the conda package format (a stored zip holding info-*.tar.zst and pkg-*.tar.zst).
import { inflateRawSync, gunzipSync, zstdDecompressSync } from 'node:zlib';

/** The file entries of a zip: name and a function returning the uncompressed bytes. Directories are left out. */
export function readZip(buffer) {
  const min = Math.max(0, buffer.length - 0xffff - 22);
  let eocd = -1;
  for (let i = buffer.length - 22; i >= min; i--) if (buffer.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error('not a zip: no end of central directory');
  const count = buffer.readUInt16LE(eocd + 10);
  let at = buffer.readUInt32LE(eocd + 16);
  if (count === 0xffff || at === 0xffffffff) throw new Error('ZIP64 archives are not read here');
  const entries = [];
  for (let n = 0; n < count; n++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) throw new Error('broken zip central directory');
    const method = buffer.readUInt16LE(at + 10);
    let packed = buffer.readUInt32LE(at + 20);
    let size = buffer.readUInt32LE(at + 24);
    const nameLength = buffer.readUInt16LE(at + 28), extraLength = buffer.readUInt16LE(at + 30), commentLength = buffer.readUInt16LE(at + 32);
    let local = buffer.readUInt32LE(at + 42);
    const name = buffer.subarray(at + 46, at + 46 + nameLength).toString('utf8');
    // A ZIP64 extra field (0x0001) carries, in this order, the values the fixed fields mark as 0xFFFFFFFF (conda packages written by conda-package-handling use it).
    for (let x = at + 46 + nameLength; x + 4 <= at + 46 + nameLength + extraLength;) {
      const id = buffer.readUInt16LE(x), length = buffer.readUInt16LE(x + 2);
      if (id === 0x0001) {
        let y = x + 4;
        const next = () => { const v = Number(buffer.readBigUInt64LE(y)); y += 8; return v; };
        if (size === 0xffffffff) size = next();
        if (packed === 0xffffffff) packed = next();
        if (local === 0xffffffff) local = next();
      }
      x += 4 + length;
    }
    at += 46 + nameLength + extraLength + commentLength;
    if (name.endsWith('/')) continue;
    if (buffer.readUInt32LE(local) !== 0x04034b50) throw new Error(`broken zip local header for ${name}`);
    const start = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
    const raw = buffer.subarray(start, start + packed);
    entries.push({
      name, size,
      data() {
        const bytes = method === 0 ? raw : method === 8 ? inflateRawSync(raw) : (() => { throw new Error(`${name}: zip method ${method} is not read here`); })();
        if (bytes.length !== size) throw new Error(`${name}: ${bytes.length} bytes, the zip says ${size}`);
        return bytes;
      },
    });
  }
  return entries;
}

const field = (block, from, length) => { const raw = block.subarray(from, from + length); const end = raw.indexOf(0); return (end < 0 ? raw : raw.subarray(0, end)).toString('utf8'); };

/** The entries of an uncompressed tar: name, type ('file', 'dir', 'link' or 'other') and, for files, the bytes. Pax path records and GNU long names are honoured. */
export function readTar(buffer) {
  const entries = [];
  let at = 0, longName = null, paxPath = null;
  while (at + 512 <= buffer.length) {
    const block = buffer.subarray(at, at + 512);
    if (block.every((b) => b === 0)) break;
    const size = parseInt(field(block, 124, 12).trim() || '0', 8);
    const flag = String.fromCharCode(block[156] || 48);
    const prefix = field(block, 345, 155);
    let name = field(block, 0, 100);
    if (prefix && block.subarray(257, 262).toString() === 'ustar') name = prefix + '/' + name;
    const body = buffer.subarray(at + 512, at + 512 + size);
    at += 512 + Math.ceil(size / 512) * 512;
    if (flag === 'L') { longName = body.toString('utf8').replace(/\0+$/, ''); continue; }
    if (flag === 'x') {
      for (const record of body.toString('utf8').split('\n')) { const m = /^\d+ path=(.*)$/.exec(record); if (m) paxPath = m[1]; }
      continue;
    }
    if (flag === 'g') continue;
    name = paxPath ?? longName ?? name;
    paxPath = null; longName = null;
    const type = flag === '0' || flag === '\0' || flag === '7' ? 'file' : flag === '5' ? 'dir' : flag === '1' || flag === '2' ? 'link' : 'other';
    entries.push({ name: name.replace(/^\.\//, ''), type, data: type === 'file' ? body : null });
  }
  return entries;
}

/** A gzip-compressed tar. */
export const readTarGz = (buffer) => readTar(gunzipSync(buffer));

/** A conda package (.conda): its info files (index.json, about.json, paths.json, licenses/...) and its payload files, each as a Map from path to bytes. */
export function readConda(buffer) {
  const zip = readZip(buffer);
  const part = (prefix) => {
    const entry = zip.find((e) => e.name.startsWith(prefix) && e.name.endsWith('.tar.zst'));
    if (!entry) throw new Error(`not a conda package: no ${prefix}*.tar.zst`);
    return new Map(readTar(zstdDecompressSync(entry.data())).filter((e) => e.type === 'file').map((e) => [e.name, e.data]));
  };
  const info = part('info-');
  const pkg = part('pkg-');
  return { info, pkg, json: (name) => (info.has('info/' + name) ? JSON.parse(info.get('info/' + name).toString('utf8')) : null) };
}
