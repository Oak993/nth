// Offline recipe preparation. No values below are asserted as observed licence/version facts.
import * as L from './lib.mjs';
const json = (url, pointer) => ({ url, format: 'json', pointer });
const text = url => ({ url, format: 'text' });
const complete = sha => /^[a-f0-9]{40}$/.test(sha ?? '');

/** Null selectors are deliberate blockers: the owner must select the source's own metadata, never fill in a claimed fact. */
export function prepareManifest(catalogue, sources) {
  return { schema: 1, note: 'Source recipes only; no real facts until generate-pins runs. Review selectors and licence scope. Null metadata blocks the run.', items: catalogue.items.map(item => {
    const cfg = sources.items[item.id];
    if (!cfg) throw new Error(`No official source recipe for ${item.id}`);
    const repo = cfg.repo;
    if (repo) {
      const licences = L.folderLicenceFiles(item.files ?? []);
      return { id: item.id, entries: (item.files ?? []).map(file => {
        const skill = (item.skills ?? []).filter(s => file.path.startsWith(s.path + '/')).sort((a, b) => b.path.length - a.path.length)[0];
        const commit = skill?.pin ?? item.pin ?? cfg.pin;
        if (!complete(commit)) throw new Error(`${item.id}/${file.path}: no full resolved commit`);
        const parent = p => p.includes('/') ? p.slice(0, p.lastIndexOf('/') + 1) : '';
        const applicable = licences.filter(p => file.path.startsWith(parent(p))).sort((a, b) => parent(b).length - parent(a).length);
        const closest = applicable.length ? applicable.filter(p => parent(p).length === parent(applicable[0]).length) : [];
        const nearest = closest.length === 1 ? closest[0] : closest.length > 1 ? null : item.licenceFile;
        return { name: file.path, expectedVersion: commit, url: L.rawUrl(repo, commit, file.path), expectedSize: file.size, expectedSha256: file.sha256,
          metadata: { version: json(`https://api.github.com/repos/${repo}/commits/${commit}`, '/sha'), licence: nearest ? text(L.rawUrl(repo, commit, nearest)) : null } };
      }) };
    }
    return { id: item.id, entries: (item.artifacts ?? []).map(artifact => {
      if (!artifact.url) throw new Error(`${item.id}/${artifact.name}: run the resolver first; no official artifact URL`);
      let version = null, licence = null, expectedVersion = null;
      if (cfg.fetch.type === 'release') {
        expectedVersion = cfg.fetch.tag;
        version = json(`https://api.github.com/repos/${cfg.fetch.repo}/releases/tags/${encodeURIComponent(cfg.fetch.tag)}`, '/tag_name');
        // A tag pins the licence lookup; source's full text is retained rather than classified by the generator.
        licence = json(`https://api.github.com/repos/${cfg.fetch.repo}/license?ref=${encodeURIComponent(cfg.fetch.tag)}`, '/content');
        licence.valueEncoding = 'base64';
      } else if (cfg.fetch.type === 'pip' || cfg.fetch.type === 'pypi') {
        const info = L.wheelInfo(artifact.name);
        if (!info) throw new Error(`${artifact.name}: not a resolved wheel`);
        expectedVersion = info.version;
        const url = `https://pypi.org/pypi/${info.dist}/${info.version}/json`;
        version = json(url, '/info/version');
        licence = json(url, '/info/license_expression');
        // Older PyPI metadata may use /info/license; a null expression must block until its own field is reviewed.
      } else if (cfg.fetch.type === 'npm') {
        const pkg = (item.packages ?? []).find(p => {
          const encoded = p.name.startsWith('@') ? p.name : encodeURIComponent(p.name);
          return artifact.url.startsWith(`https://registry.npmjs.org/${encoded}/-/`);
        });
        if (!pkg?.version) throw new Error(`${artifact.name}: no resolved npm package/version`);
        expectedVersion = pkg.version;
        const url = `https://registry.npmjs.org/${encodeURIComponent(pkg.name)}/${encodeURIComponent(pkg.version)}`;
        version = json(url, '/version'); licence = json(url, '/license');
      }
      if (['T1', 'T2', 'T10'].includes(item.id)) expectedVersion = item.pin;
      if (item.id === 'T1') {
        version = { ...json(`https://api.github.com/repos/python/cpython/git/ref/tags/v${item.pin}`, '/ref'), pattern: '^refs/tags/v(.+)$' };
        licence = text(`https://raw.githubusercontent.com/python/cpython/v${item.pin}/LICENSE`);
      } else if (item.id === 'T2') {
        version = { ...json('https://nodejs.org/dist/index.json', ''), match: { field: 'version', value: `v${item.pin}` }, valuePointer: '/version', pattern: '^v(.+)$' };
        licence = text(`https://raw.githubusercontent.com/nodejs/node/v${item.pin}/LICENSE`);
      } else if (item.id === 'T10') {
        version = { ...json('https://googlechromelabs.github.io/chrome-for-testing/known-good-versions.json', '/versions'), match: { field: 'version', value: item.pin }, valuePointer: '/version' };
        licence = { url: `https://chromium.googlesource.com/chromium/src/+/refs/tags/${item.pin}/LICENSE?format=TEXT`, format: 'base64' };
      }
      return { name: artifact.name, expectedVersion, url: artifact.url, expectedSize: artifact.size, expectedSha256: artifact.sha256, metadata: { version, licence } };
    }) };
  }) };
}
