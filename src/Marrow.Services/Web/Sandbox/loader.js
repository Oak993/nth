// nth library loader (DESIGN.md 6.5 "Libraries"; ED-NEW-web-w13-3). Served at https://lib-nth.invalid/nth/loader.js with
// the vendored library table filled in. It loads a library's classic file (and its dependencies first) once, and gives
// nth's runners a CommonJS-style require over the loaded globals, so a component's imports resolve offline.
(() => {
  'use strict';
  if (window.__nthLibs) return;
  const table = /*NTH_TABLE*/{ "specifiers": {}, "libraries": {} };
  const preludes = /*NTH_PRELUDES*/{};
  const loading = new Map();
  const read = (path) => path.split('.').reduce((value, part) => (value == null ? undefined : value[part]), window);

  const loadScript = (src) => {
    if (loading.has(src)) return loading.get(src);
    const promise = new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = src;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('nth could not load ' + src));
      (document.head || document.documentElement).appendChild(script);
    });
    loading.set(src, promise);
    return promise;
  };
  window.__nthLoad = loadScript;

  const loadLibrary = async (id) => {
    const entry = table.libraries[id];
    if (!entry) throw new Error('nth has no library ' + id);
    for (const dep of entry.deps) await loadLibrary(dep);
    for (const href of entry.css) {
      if (!document.querySelector('[data-nth-lib="' + id + '"]')) {
        const link = document.createElement('link');
        link.rel = 'stylesheet'; link.href = href; link.dataset.nthLib = id;
        (document.head || document.documentElement).appendChild(link);
      }
    }
    if (entry.global && read(entry.global) !== undefined) return;
    if (preludes[id]) preludes[id]();
    await loadScript(entry.script);
  };

  // React's automatic JSX runtime over the UMD global.
  const jsxRuntime = () => {
    const React = window.React;
    const jsx = (type, props, key) => React.createElement(type, key === undefined ? props : Object.assign({}, props, { key }));
    return { __esModule: true, Fragment: React.Fragment, jsx, jsxs: jsx, jsxDEV: jsx, default: { Fragment: React.Fragment, jsx, jsxs: jsx } };
  };

  const libraryOf = (specifier) => {
    if (specifier === 'react/jsx-runtime' || specifier === 'react/jsx-dev-runtime') return 'react';
    if (table.specifiers[specifier]) return table.specifiers[specifier];
    const bare = specifier.replace(/^(@[^/]+\/[^/]+|[^/]+).*$/, '$1');
    return table.specifiers[bare] || null;
  };

  // Loads what a list of import specifiers needs; unknown ones are reported, not fetched.
  const isStyle = (specifier) => /\.css$/i.test(specifier);
  const prepare = async (specifiers) => {
    const missing = [];
    const ids = [];
    for (const specifier of specifiers) {
      const id = libraryOf(specifier);
      if (id) ids.push(id); else if (!isStyle(specifier)) missing.push(specifier);
    }
    for (const id of ids) await loadLibrary(id);
    return missing;
  };

  const requireLibrary = (specifier) => {
    if (specifier === 'react/jsx-runtime' || specifier === 'react/jsx-dev-runtime') return jsxRuntime();
    if (isStyle(specifier)) return {}; // a library's style sheet is loaded with the library; others are not available
    const id = libraryOf(specifier);
    if (!id) throw new Error('This artifact imports "' + specifier + '", which nth does not have offline.');
    const entry = table.libraries[id];
    const value = entry.global ? read(entry.global) : undefined;
    if (value === undefined) throw new Error('nth has not loaded ' + specifier + '.');
    if (entry.exportAs) return { __esModule: true, default: value, [entry.exportAs]: value };
    return value;
  };

  window.__nthLibs = Object.freeze({ table, prepare, require: requireLibrary, load: loadLibrary, loadScript });
})();
