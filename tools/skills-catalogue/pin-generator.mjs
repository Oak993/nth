// RR-13/14 Round 2. Importable, transport-injected pin generator. Importing this module has no I/O or network effects.
import { createHash } from 'node:crypto';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const common = ['api.github.com', 'raw.githubusercontent.com', 'github.com'];
const direct = {
  T1: ['www.python.org', 'docs.python.org'], T2: ['nodejs.org'], T3: ['download.documentfoundation.org', 'www.libreoffice.org'],
  T10: ['cdn.playwright.dev', 'googlechromelabs.github.io', 'chromium.googlesource.com']
};
const metadataRepos = { T1: 'python/cpython', T2: 'nodejs/node', T3: 'LibreOffice/core', T10: 'microsoft/playwright' };

function secure(url) {
  const parsed = new URL(url);
  if (parsed.protocol !== 'https:') throw new Error('Official sources must use HTTPS');
  if (parsed.username || parsed.password || parsed.hash || parsed.port && parsed.port !== '443') throw new Error('Official source URL contains credentials, a fragment or a nonstandard port');
  return parsed;
}
/** Policies come from the checked-in official source map, never from arbitrary manifest host claims. */
export function sourcePolicy(id, cfg) {
  const repo = cfg.repo ?? cfg.fetch?.repo ?? metadataRepos[id];
  const allowed = url => {
    const u = secure(url);
    if (repo && common.includes(u.hostname)) {
      const prefix = u.hostname === 'api.github.com' ? `/repos/${repo}/` : `/${repo}/`;
      return u.pathname.startsWith(prefix);
    }
    if ((direct[id] ?? []).includes(u.hostname)) return true;
    if (cfg.fetch?.type === 'pypi' || cfg.fetch?.type === 'pip') return ['pypi.org', 'files.pythonhosted.org'].includes(u.hostname);
    if (cfg.fetch?.type === 'npm') return u.hostname === 'registry.npmjs.org';
    return false;
  };
  allowed.redirect = url => allowed(url) || cfg.fetch?.type === 'release' && ['release-assets.githubusercontent.com', 'objects.githubusercontent.com'].includes(secure(url).hostname);
  return allowed;
}
function requireOfficial(url, allowed) {
  secure(url);
  if (!allowed(url)) throw new Error(`URL is outside this item's official sources: ${url}`);
}
/** Manual redirects avoid trusting a final host after bytes/credentials have already been sent elsewhere. No tokens or ambient auth are used. */
export async function requestOfficial(url, allowed, transport = globalThis.fetch) {
  for (let hop = 0; hop < 8; hop++) {
    requireOfficial(url, hop === 0 ? allowed : allowed.redirect ?? allowed);
    const response = await transport(url, { redirect: 'manual', credentials: 'omit', headers: { 'User-Agent': 'Marrow-catalogue-pin-generator/1' } });
    if ([301, 302, 303, 307, 308].includes(response.status)) {
      const location = response.headers.get('location');
      if (!location) throw new Error('Official source redirect has no location');
      url = new URL(location, url).href;
      continue;
    }
    if (!response.ok) throw new Error(`Official source HTTP ${response.status}: ${url}`);
    const final = response.url || url;
    requireOfficial(final, allowed.redirect ?? allowed);
    if (final !== url) throw new Error('Transport followed an unchecked redirect');
    return { url, chunks: response.body };
  }
  throw new Error('Official source redirect limit exceeded');
}
/** Returns only what source metadata actually says. No inferred SPDX licence, catalogue fallback, filename-derived version or user-supplied fact is accepted. */
export function readMetadata(bytes, selector) {
  const decode = b => new TextDecoder('utf-8', { fatal: true }).decode(b);
  function base64(value) {
    const compact = value.replace(/\s/g, '');
    if (!/^[A-Za-z0-9+/]*={0,2}$/.test(compact)) throw new Error('Invalid source metadata base64 encoding');
    const decoded = Buffer.from(compact, 'base64');
    if (decoded.toString('base64').replace(/=+$/, '') !== compact.replace(/=+$/, '')) throw new Error('Invalid source metadata base64 encoding');
    return decode(decoded);
  }
  let text = decode(bytes);
  if (selector.format === 'base64') text = base64(text);
  let value;
  if (selector.format === 'json') {
    function pointer(object, path) {
      if (typeof path !== 'string' || path !== '' && !path.startsWith('/')) throw new Error('JSON metadata needs an RFC 6901 pointer');
      if (path === '') return object;
      for (const segment of path.slice(1).split('/')) {
        const key = segment.replaceAll('~1', '/').replaceAll('~0', '~');
        if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) throw new Error(`Source metadata field missing: ${path}`);
        object = object[key];
      }
      return object;
    }
    value = pointer(JSON.parse(text), selector.pointer);
    if (selector.match) {
      if (!Array.isArray(value)) throw new Error('Source metadata match needs an array');
      const matches = value.filter(v => v !== null && typeof v === 'object' && Object.hasOwn(v, selector.match.field) && v[selector.match.field] === selector.match.value);
      if (matches.length !== 1) throw new Error('Source metadata match must select exactly one entry');
      value = pointer(matches[0], selector.valuePointer);
    }
    if (selector.valueEncoding === 'base64' && typeof value === 'string') value = base64(value);
  } else if (selector.format === 'text' || selector.format === 'base64') value = text;
  else throw new Error('Unsupported source metadata format');
  if (typeof value !== 'string' || !value.trim()) throw new Error('Source metadata value is empty or is not text');
  if (selector.pattern) {
    const matches = [...value.matchAll(new RegExp(selector.pattern, 'gm'))];
    if (matches.length !== 1 || !matches[0][1]?.trim()) throw new Error('Source metadata pattern must select exactly one nonempty capture');
    value = matches[0][1];
  }
  return value.trim();
}
function coverage(manifest, sources) {
  if (manifest.schema !== 1 || !Array.isArray(manifest.items)) throw new Error('Invalid pin manifest schema');
  const expected = Object.keys(sources.items).sort();
  const actual = manifest.items.map(i => i.id).sort();
  if (expected.length !== 29 || JSON.stringify(expected) !== JSON.stringify(actual)) throw new Error('Pin manifest must have exact coverage of all 29 items with no duplicate IDs');
  for (const item of manifest.items) {
    if (!Array.isArray(item.entries) || item.entries.length === 0) throw new Error(`${item.id}: no official artifacts/files were selected`);
    const names = new Set();
    for (const entry of item.entries) {
      if (typeof entry.name !== 'string' || !entry.name || /[\\:<>"|?*\x00-\x1f\x7f]/.test(entry.name) || entry.name.split('/').some(p => !p || p === '.' || p === '..' || /[. ]$/.test(p) || p.length > 255 || /^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)/i.test(p))) throw new Error('Unsafe Windows artifact path');
      const name = entry.name.toLowerCase();
      if (names.has(name)) throw new Error('Duplicate Windows artifact path');
      names.add(name);
      for (const field of ['version', 'licence']) if (!entry.metadata?.[field]?.url) throw new Error(`${item.id}/${entry.name}: ${field} requires source metadata URL and selector`);
    }
    for (const name of names) {
      const parts = name.split('/');
      for (let n = 1; n < parts.length; n++) if (names.has(parts.slice(0, n).join('/'))) throw new Error('Artifact path collides with a parent file');
    }
  }
}
/** request is mandatory in tests. Production passes requestOfficial. saveChunk is optional; the CLI persists exactly the bytes it hashes. */
export async function generatePins(manifest, { sources, request, now = () => new Date(), saveChunk = async () => {}, saveMetadata = async () => {} }) {
  coverage(manifest, sources);
  if (typeof request !== 'function') throw new Error('A source transport must be supplied explicitly');
  for (const item of manifest.items) {
    const policy = sourcePolicy(item.id, sources.items[item.id]);
    for (const entry of item.entries) {
      requireOfficial(entry.url, policy);
      for (const field of ['version', 'licence']) requireOfficial(entry.metadata[field].url, policy);
    }
  }
  const result = { schema: 1, startedAt: now().toISOString(), items: [] };
  const cache = new Map();
  async function sourceBytes(url, allowed) {
    requireOfficial(url, allowed);
    if (!cache.has(url)) {
      const reply = await request(url, allowed);
      requireOfficial(reply.url, allowed.redirect ?? allowed);
      let size = 0;
      const chunks = [];
      for await (const chunk of reply.chunks) {
        size += chunk.length;
        if (size > 16 * 1024 * 1024) throw new Error('Source metadata exceeds 16 MiB');
        chunks.push(Buffer.from(chunk));
      }
      if (!size) throw new Error('Source metadata is empty');
      const body = Buffer.concat(chunks);
      const readAt = now().toISOString();
      await saveMetadata(url, body);
      cache.set(url, { bytes: body, finalUrl: reply.url, readAt });
    }
    const read = cache.get(url);
    requireOfficial(read.finalUrl, allowed.redirect ?? allowed);
    return read;
  }
  for (const item of manifest.items) {
    const allowed = sourcePolicy(item.id, sources.items[item.id]);
    // Validate the artifact before any source requests, including its metadata.
    for (const entry of item.entries) requireOfficial(entry.url, allowed);
    const entries = [];
    for (const entry of item.entries) {
      const fields = {};
      for (const field of ['version', 'licence']) {
        const selector = entry.metadata[field];
        const read = await sourceBytes(selector.url, allowed);
        fields[field] = { value: readMetadata(read.bytes, selector), url: selector.url, finalUrl: read.finalUrl, readAt: read.readAt, sha256: hash(read.bytes), size: read.bytes.length, selector: structuredClone(selector) };
      }
      if (['UNKNOWN', 'NOASSERTION'].includes(fields.licence.value.toUpperCase())) throw new Error('Source licence metadata is explicitly unknown; select its actual licence body');
      if (entry.expectedVersion && fields.version.value !== entry.expectedVersion) throw new Error('Source metadata version differs from the selected version');
      const response = await request(entry.url, allowed);
      requireOfficial(response.url, allowed.redirect ?? allowed);
      const digest = createHash('sha256');
      let size = 0;
      for await (const chunk of response.chunks) {
        size += chunk.length;
        if (!Number.isSafeInteger(size)) throw new Error('Artifact byte size exceeds exact integer range');
        digest.update(chunk);
        await saveChunk(item.id, entry.name, chunk);
      }
      if (!size) throw new Error('Official artifact is empty');
      const sha256 = digest.digest('hex');
      if (entry.expectedSize != null && entry.expectedSize !== size) throw new Error('Computed artifact size differs from published size');
      if (entry.expectedSha256 != null && entry.expectedSha256 !== sha256) throw new Error('Computed artifact digest differs from published digest');
      entries.push({ name: entry.name, url: entry.url, finalUrl: response.url, readAt: now().toISOString(), size, sha256, ...fields });
    }
    result.items.push({ id: item.id, entries });
  }
  result.finishedAt = now().toISOString();
  return result;
}
