// nth project runner (DESIGN.md 6.1 kind "project", 6.5; ED-NEW-web-w13-3). A project whose page loads JSX, TSX or
// TypeScript modules runs them here: each file is compiled with Sucrase in the sandbox, relative imports resolve between
// the project's own files (with the usual extensions and index files), CSS and JSON imports work, and bare imports load
// the offline libraries. Plain HTML, CSS and JavaScript projects never come here; the browser runs them as they are.
(() => {
  'use strict';
  const holder = document.getElementById('nth-files');
  if (!holder || !window.sucrase || !window.__nthLibs) return;
  const project = JSON.parse(holder.textContent);
  const files = project.files || {};
  const raise = (error) => setTimeout(() => { throw error; });
  const code = /\.(jsx?|tsx?|mjs|cjs)$/i;
  const tried = ['', '.tsx', '.ts', '.jsx', '.js', '.mjs', '.json', '.css', '/index.tsx', '/index.ts', '/index.jsx', '/index.js'];

  const normalize = (path) => {
    const parts = [];
    for (const part of path.split('/')) {
      if (part === '' || part === '.') continue;
      if (part === '..') parts.pop(); else parts.push(part);
    }
    return parts.join('/');
  };
  const resolve = (from, specifier) => {
    if (!/^(\.{1,2}\/|\/)/.test(specifier)) return null;
    const base = specifier.startsWith('/') ? '' : from.split('/').slice(0, -1).join('/');
    const path = normalize((base ? base + '/' : '') + specifier.split(/[?#]/)[0]);
    for (const suffix of tried) if (Object.prototype.hasOwnProperty.call(files, path + suffix)) return path + suffix;
    throw new Error('"' + from + '" imports "' + specifier + '", which is not in this project.');
  };

  const compiled = new Map();
  const compile = (path) => {
    if (!compiled.has(path)) {
      compiled.set(path, window.sucrase.transform(files[path], {
        transforms: /\.tsx?$/i.test(path) ? ['jsx', 'typescript', 'imports'] : ['jsx', 'imports'],
        jsxRuntime: 'automatic', production: true, filePath: path,
      }).code);
    }
    return compiled.get(path);
  };
  const requires = (text) => Array.from(text.matchAll(/\brequire\((['"])([^'"\n]+)\1\)/g), (m) => m[2]);

  // Every bare import of every reachable module, so the libraries load before anything runs.
  const collect = (entry) => {
    const seen = new Set();
    const bare = new Set();
    const visit = (path) => {
      if (seen.has(path)) return;
      seen.add(path);
      if (!code.test(path)) return;
      for (const specifier of requires(compile(path))) {
        const local = resolve(path, specifier);
        if (local) visit(local); else bare.add(specifier);
      }
    };
    visit(entry);
    return Array.from(bare);
  };

  const modules = new Map();
  const load = (path) => {
    if (modules.has(path)) return modules.get(path).exports;
    const module = { exports: {} };
    modules.set(path, module);
    if (/\.css$/i.test(path)) {
      const style = document.createElement('style');
      style.dataset.nthFile = path;
      style.textContent = files[path];
      document.head.appendChild(style);
      return module.exports;
    }
    if (/\.json$/i.test(path)) { module.exports = JSON.parse(files[path]); return module.exports; }
    if (!code.test(path)) { module.exports = files[path]; return module.exports; }
    const slot = '__nthProject' + Math.random().toString(36).slice(2);
    window[slot] = { module, require: (specifier) => { const local = resolve(path, specifier); return local ? load(local) : window.__nthLibs.require(specifier); }, ran: false };
    const script = document.createElement('script');
    script.textContent = '(function (module, exports, require) {\n' + compile(path) + '\n;window["' + slot + '"].ran = true;\n})(window["' + slot + '"].module, window["' + slot + '"].module.exports, window["' + slot + '"].require);\n//# sourceURL=' + path;
    document.head.appendChild(script);
    const ran = window[slot].ran;
    delete window[slot];
    if (!ran) throw new Error('"' + path + '" did not run.');
    return module.exports;
  };

  const run = async () => {
    for (const entry of project.entries || []) {
      const missing = await window.__nthLibs.prepare(collect(entry));
      if (missing.length) throw new Error('This project imports ' + missing.map((m) => '"' + m + '"').join(', ') + ', which nth does not have offline.');
      const exported = load(entry);
      // An entry that is a component (no createRoot of its own) is rendered into #root.
      const root = document.getElementById('root');
      if (project.mount && root && !root.hasChildNodes() && exported && (typeof exported.default === 'function' || typeof exported.default === 'object') && window.React) {
        await window.__nthLibs.prepare(['react-dom']);
        window.ReactDOM.createRoot(root).render(window.React.createElement(exported.default));
      }
    }
  };

  const start = () => run().catch(raise);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
