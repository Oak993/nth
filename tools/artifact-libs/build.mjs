// PORT-ONLY: web surfaces W13, Tabula's offline libraries (DESIGN.md 6.5; ED-NEW-web-w13-3). Builds
// src/Marrow.Services/Assets/lib from the pinned npm packages: each library's browser file, its licence, the ESM wrapper
// the import map points at, and manifest.json, which ArtifactLibraries reads. Nothing here runs in the app.
//
// Usage (node 20 or later), with the packages installed by npm into a folder of their own, --ignore-scripts:
//   npm install --ignore-scripts <every name@version in PACKAGES below> esbuild@0.28.2
//   node tools/artifact-libs/build.mjs --packages <that folder>/node_modules --out src/Marrow.Services/Assets/lib
// The output is deterministic: the same packages give the same bytes (no dates, sorted keys, LF line ends).
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import vm from 'node:vm';
import { createRequire } from 'node:module';

const args = Object.fromEntries(process.argv.slice(2).reduce((pairs, value, index, all) =>
  value.startsWith('--') ? [...pairs, [value.slice(2), all[index + 1]]] : pairs, []));
if (!args.packages || !args.out) throw new Error('usage: build.mjs --packages <node_modules> --out <Assets/lib>');
const packages = path.resolve(args.packages);
const out = path.resolve(args.out);

// The pinned versions; the build stops when an installed package differs.
const PACKAGES = {
  'react': '18.3.1', 'react-dom': '18.3.1', 'prop-types': '15.8.1', 'sucrase': '3.35.1',
  '@tailwindcss/browser': '4.3.3', 'recharts': '2.15.4', 'lucide-react': '0.577.0', 'lucide': '1.55.0',
  'd3': '7.9.0', 'chart.js': '4.5.1', 'mermaid': '11.16.1', 'three': '0.186.0', 'katex': '0.16.47',
  '@highlightjs/cdn-assets': '11.12.0', 'papaparse': '5.7.0', 'lodash': '4.18.1', 'mathjs': '15.2.0',
};

// One entry per library. script: the classic browser file that sets `global`. deps: libraries whose globals it needs.
// specifiers: the import-map names (and require names) it answers; cdn: the names cdnjs, unpkg and jsDelivr use.
const LIBRARIES = [
  { id: 'react', name: 'React', package: 'react', global: 'React', copy: { 'react.production.min.js': 'umd/react.production.min.js' },
    script: 'react.production.min.js', specifiers: ['react'], cdn: ['react'] },
  { id: 'react-dom', name: 'ReactDOM', package: 'react-dom', global: 'ReactDOM', deps: ['react'],
    copy: { 'react-dom.production.min.js': 'umd/react-dom.production.min.js' }, script: 'react-dom.production.min.js',
    specifiers: ['react-dom', 'react-dom/client'], cdn: ['react-dom'] },
  { id: 'prop-types', name: 'prop-types', package: 'prop-types', global: 'PropTypes', deps: ['react'],
    copy: { 'prop-types.min.js': 'prop-types.min.js' }, script: 'prop-types.min.js', specifiers: ['prop-types'], cdn: ['prop-types'] },
  { id: 'sucrase', name: 'Sucrase', package: 'sucrase', global: 'sucrase', bundle: 'sucrase', script: 'sucrase.min.js', specifiers: [], cdn: ['sucrase'] },
  { id: 'tailwindcss', name: 'Tailwind CSS (browser build)', package: '@tailwindcss/browser', global: null,
    copy: { 'tailwind.browser.js': 'dist/index.global.js' }, script: 'tailwind.browser.js', specifiers: [],
    cdn: ['tailwindcss', '@tailwindcss/browser'] },
  { id: 'recharts', name: 'Recharts', package: 'recharts', global: 'Recharts', deps: ['react', 'react-dom', 'prop-types'],
    copy: { 'Recharts.min.js': 'umd/Recharts.js', 'Recharts.min.js.LICENSE.txt': 'umd/Recharts.js.LICENSE.txt' },
    script: 'Recharts.min.js', specifiers: ['recharts'], cdn: ['recharts'] },
  { id: 'lucide-react', name: 'Lucide React', package: 'lucide-react', global: 'LucideReact', deps: ['react'],
    copy: { 'lucide-react.min.js': 'dist/umd/lucide-react.min.js' }, script: 'lucide-react.min.js', prelude: 'globalThis.react = globalThis.React;', specifiers: ['lucide-react'], cdn: ['lucide-react'] },
  { id: 'lucide', name: 'Lucide', package: 'lucide', global: 'lucide', copy: { 'lucide.min.js': 'dist/umd/lucide.min.js' },
    script: 'lucide.min.js', specifiers: ['lucide'], cdn: ['lucide'] },
  { id: 'd3', name: 'D3', package: 'd3', global: 'd3', copy: { 'd3.min.js': 'dist/d3.min.js' }, script: 'd3.min.js', specifiers: ['d3'], cdn: ['d3'] },
  { id: 'chart.js', name: 'Chart.js', package: 'chart.js', global: 'Chart', copy: { 'chart.umd.min.js': 'dist/chart.umd.min.js' },
    script: 'chart.umd.min.js', specifiers: ['chart.js', 'chart.js/auto'], cdn: ['chart.js', 'Chart.js'] },
  { id: 'mermaid', name: 'Mermaid', package: 'mermaid', global: 'mermaid', copy: { 'mermaid.min.js': 'dist/mermaid.min.js' },
    script: 'mermaid.min.js', specifiers: ['mermaid'], cdn: ['mermaid'] },
  { id: 'three', name: 'three.js', package: 'three', global: 'THREE', bundle: 'three', script: 'three.min.js', specifiers: ['three'], cdn: ['three', 'three.js'] },
  { id: 'three-orbit-controls', name: 'three.js OrbitControls', package: 'three', global: 'THREE.OrbitControls', deps: ['three'],
    bundle: 'orbit', script: 'three-orbit-controls.min.js', exportAs: 'OrbitControls',
    specifiers: ['three/addons/controls/OrbitControls.js', 'three/examples/jsm/controls/OrbitControls.js'], cdn: [] },
  { id: 'katex', name: 'KaTeX', package: 'katex', global: 'katex', copy: { 'katex.min.js': 'dist/katex.min.js' },
    script: 'katex.min.js', css: ['katex.min.css'], fonts: true, specifiers: ['katex'], cdn: ['katex', 'KaTeX'] },
  { id: 'katex-auto-render', name: 'KaTeX auto-render', package: 'katex', global: 'renderMathInElement', deps: ['katex'],
    copy: { 'auto-render.min.js': 'dist/contrib/auto-render.min.js' }, script: 'auto-render.min.js', dir: 'katex',
    specifiers: ['katex/contrib/auto-render', 'katex/dist/contrib/auto-render.js'], cdn: [] },
  { id: 'highlight.js', name: 'highlight.js', package: '@highlightjs/cdn-assets', global: 'hljs',
    copy: { 'highlight.min.js': 'highlight.min.js', 'github.min.css': 'styles/github.min.css', 'github-dark.min.css': 'styles/github-dark.min.css' },
    script: 'highlight.min.js', css: ['github.min.css', 'github-dark.min.css'], specifiers: ['highlight.js', 'highlight.js/lib/common'],
    cdn: ['highlight.js', '@highlightjs/cdn-assets'] },
  { id: 'papaparse', name: 'Papa Parse', package: 'papaparse', global: 'Papa', copy: { 'papaparse.min.js': 'papaparse.min.js' },
    script: 'papaparse.min.js', specifiers: ['papaparse'], cdn: ['papaparse', 'PapaParse'] },
  { id: 'lodash', name: 'Lodash', package: 'lodash', global: '_', copy: { 'lodash.min.js': 'lodash.min.js' },
    script: 'lodash.min.js', specifiers: ['lodash', 'lodash-es'], cdn: ['lodash', 'lodash.js', 'lodash-es'] },
  { id: 'mathjs', name: 'math.js', package: 'mathjs', global: 'math',
    copy: { 'math.min.js': 'lib/browser/math.js', 'math.min.js.LICENSE.txt': 'lib/browser/math.js.LICENSE.txt' },
    script: 'math.min.js', specifiers: ['mathjs'], cdn: ['mathjs'] },
];

const LICENCE_NAMES = ['LICENSE', 'LICENSE.md', 'LICENSE.txt', 'LICENCE', 'license'];
const RESERVED = new Set(('break case catch class const continue debugger default delete do else enum export extends false finally ' +
  'for function if import in instanceof new null return super switch this throw true try typeof var void while with yield let static ' +
  'implements interface package private protected public await arguments eval').split(' '));
const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;

function pkgDir(name) { return path.join(packages, ...name.split('/')); }
function lf(text) { return text.replace(/\r\n?/g, '\n'); }
function write(relative, data) {
  const target = path.join(out, ...relative.split('/'));
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, data);
}

for (const [name, version] of Object.entries(PACKAGES)) {
  const found = JSON.parse(fs.readFileSync(path.join(pkgDir(name), 'package.json'), 'utf8')).version;
  if (found !== version) throw new Error(`${name}: installed ${found}, pinned ${version}`);
}

fs.rmSync(out, { recursive: true, force: true });
fs.mkdirSync(out, { recursive: true });

const requireFromPackages = createRequire(path.join(packages, 'noop.js'));
const esbuild = requireFromPackages('esbuild');

async function bundle(kind) {
  // Entry files live in memory (stdin), resolved against the packages folder.
  const entries = {
    sucrase: "export { transform } from 'sucrase';",
    three: "export * from 'three';",
    orbit: "import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'; globalThis.THREE.OrbitControls = OrbitControls;",
  };
  const external = kind === 'orbit' ? [{
    name: 'three-global', setup(build) {
      build.onResolve({ filter: /^three$/ }, () => ({ path: 'three', namespace: 'three-global' }));
      build.onLoad({ filter: /.*/, namespace: 'three-global' }, () => ({ contents: 'module.exports = globalThis.THREE;', loader: 'js' }));
    },
  }] : [];
  const result = await esbuild.build({
    stdin: { contents: entries[kind], resolveDir: packages, loader: 'js', sourcefile: kind + '-entry.js' },
    bundle: true, minify: true, write: false, format: 'iife', target: 'es2020', platform: 'browser', legalComments: 'inline',
    globalName: kind === 'orbit' ? undefined : (kind === 'three' ? 'THREE' : 'sucrase'),
    plugins: external, logLevel: 'silent', charset: 'utf8',
  });
  return lf(result.outputFiles[0].text);
}

// The names a global exposes, read by running the browser file in a bare context (no network, no DOM beyond stubs).
function exportedNames(files, globalPath, preludes = []) {
  const element = () => ({ style: {}, setAttribute() {}, appendChild() {}, addEventListener() {}, getContext: () => null });
  const context = { console: { log() {}, warn() {}, error() {}, info() {}, debug() {} }, setTimeout, clearTimeout, setInterval, clearInterval,
    navigator: { userAgent: 'node', platform: 'Win32', language: 'en-US' }, location: { href: 'https://lib-nth.invalid/', protocol: 'https:' },
    document: { createElement: element, createElementNS: element, documentElement: { style: {} }, addEventListener() {}, querySelector: () => null,
      querySelectorAll: () => [], head: element(), body: element(), readyState: 'complete' },
    addEventListener() {}, removeEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }),
    requestAnimationFrame: () => 0, performance: { now: () => 0 }, queueMicrotask, structuredClone,
    TextEncoder, TextDecoder, URL, URLSearchParams, Intl, WeakRef };
  context.window = context; context.self = context; context.globalThis = context;
  vm.createContext(context);
  for (const [index, file] of files.entries()) {
    try {
      if (preludes[index]) vm.runInContext(preludes[index], context);
      vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: path.basename(file), timeout: 20000 });
    }
    catch (error) { return null; }
  }
  let value = context;
  for (const part of globalPath.split('.')) value = value?.[part];
  if (value == null) return null;
  return Object.keys(value).filter(key => IDENTIFIER.test(key) && !RESERVED.has(key)).sort();
}

const manifest = { format: 1, generatedBy: 'tools/artifact-libs/build.mjs', libraries: [] };
const scriptPath = {};

for (const library of LIBRARIES) {
  const dir = library.dir ?? library.id;
  const root = pkgDir(library.package);
  const files = [];
  const add = (relative, data) => {
    write(`${dir}/${relative}`, data);
    const bytes = Buffer.isBuffer(data) ? data : Buffer.from(data, 'utf8');
    files.push({ path: `${dir}/${relative}`, bytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex') });
  };
  if (library.bundle) add(library.script, await bundle(library.bundle));
  for (const [target, source] of Object.entries(library.copy ?? {})) {
    let text = fs.readFileSync(path.join(root, ...source.split('/')));
    if (target.endsWith('.css') && library.fonts) {
      // Only the woff2 faces ship; the browser takes the first face it can load.
      text = Buffer.from(text.toString('utf8').replace(/,url\(fonts\/[^)]+\.woff\) format\("woff"\),url\(fonts\/[^)]+\.ttf\) format\("truetype"\)/g, ''), 'utf8');
    }
    if (/\.(js|css|txt)$/.test(target)) text = Buffer.from(lf(text.toString('utf8')), 'utf8');
    add(target, text);
  }
  if (library.css && library.fonts) {
    const css = lf(fs.readFileSync(path.join(root, 'dist', 'katex.min.css'), 'utf8'))
      .replace(/,url\(fonts\/[^)]+\.woff\) format\("woff"\),url\(fonts\/[^)]+\.ttf\) format\("truetype"\)/g, '');
    add('katex.min.css', css);
    for (const font of fs.readdirSync(path.join(root, 'dist', 'fonts')).filter(name => name.endsWith('.woff2')).sort())
      add(`fonts/${font}`, fs.readFileSync(path.join(root, 'dist', 'fonts', font)));
  }
  if (!library.dir) {
    const licence = LICENCE_NAMES.find(name => fs.existsSync(path.join(root, name)));
    if (!licence) throw new Error(`${library.package}: no licence file`);
    add(licence === 'license' ? 'LICENSE' : licence, Buffer.from(lf(fs.readFileSync(path.join(root, licence), 'utf8')), 'utf8'));
  }
  scriptPath[library.id] = `${dir}/${library.script}`;
  const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
  manifest.libraries.push({
    id: library.id, name: library.name, package: library.package, version: pkg.version,
    license: typeof pkg.license === 'string' ? pkg.license : pkg.license?.type ?? 'SEE LICENSE',
    licenseFile: files.find(f => /\/LICEN[CS]E(\.md|\.txt)?$/.test(f.path) && !f.path.includes('.min.js'))?.path ?? null,
    global: library.global, script: scriptPath[library.id], css: (library.css ?? []).map(name => `${dir}/${name}`),
    deps: library.deps ?? [], specifiers: library.specifiers, cdn: library.cdn, esm: library.global ? `esm/${library.id}.js` : null,
    prelude: library.prelude ?? null, exportAs: library.exportAs ?? null, exports: [], files,
  });
}

// Exported names, read with every dependency loaded first.
const byId = Object.fromEntries(manifest.libraries.map(entry => [entry.id, entry]));
function chain(id, seen = new Set()) {
  if (seen.has(id)) return [];
  seen.add(id);
  return [...byId[id].deps.flatMap(dep => chain(dep, seen)), id];
}
for (const entry of manifest.libraries.filter(e => e.global)) {
  const ids = chain(entry.id);
  const names = exportedNames(ids.map(id => path.join(out, ...byId[id].script.split('/'))), entry.global, ids.map(id => byId[id].prelude));
  entry.exports = names ?? [];
}

// ESM wrappers: each loads its classic file once (as a script element, so `this` is the window, as UMD builds expect),
// waits for it with top-level await, and re-exports the global. Dependencies load first through their own wrappers.
for (const entry of manifest.libraries.filter(e => e.global)) {
  const deps = entry.deps.map(dep => `import "./${dep}.js";\n`).join('');
  const names = entry.exports.filter(name => name !== 'default');
  const body = `// Generated by tools/artifact-libs/build.mjs: ${entry.name} ${entry.version} (${entry.license}) as an ES module.\n` +
    deps +
    `const read = () => ${entry.global.split('.').reduce((expr, part) => `${expr}?.[${JSON.stringify(part)}]`, 'globalThis')};\n` +
    (entry.prelude ? `if (read() === undefined) { ${entry.prelude} }\n` : '') +
    `if (read() === undefined) await (globalThis.__nthLoad ?? ((src) => new Promise((resolve, reject) => {\n` +
    `  const script = document.createElement("script"); script.src = src; script.onload = resolve;\n` +
    `  script.onerror = () => reject(new Error("nth could not load " + src)); document.head.appendChild(script);\n` +
    `})))(new URL("../${entry.script}", import.meta.url).href);\n` +
    `const value = read();\nexport default value;\n` +
    (entry.exportAs ? `export { value as ${entry.exportAs} };\n` : '') +
    (names.length ? `export const { ${names.join(', ')} } = value;\n` : '');
  write(`esm/${entry.id}.js`, body);
}

manifest.libraries.sort((a, b) => a.id.localeCompare(b.id));
write('manifest.json', JSON.stringify(manifest, null, 2) + '\n');
const total = manifest.libraries.flatMap(e => e.files).reduce((sum, f) => sum + f.bytes, 0);
console.log(`${manifest.libraries.length} libraries, ${(total / 1048576).toFixed(2)} MB, exports: ` +
  manifest.libraries.filter(e => e.global).map(e => `${e.id}=${e.exports.length}`).join(' '));
