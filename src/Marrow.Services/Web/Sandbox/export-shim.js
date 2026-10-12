// nth's offline bridge for exported pages (DESIGN.md 6.3 Export, 6.5; ED-NEW-web-w13-5). A page exported from Tabula runs
// anywhere, outside nth, so window.nth is replaced by what a plain browser can do: storage in localStorage, a file input
// for files, and clear errors for the model, tools and the composer, which only exist inside nth.
(() => {
  'use strict';
  if (window.nth) return;
  const unavailable = (what) => () => Promise.reject(Object.assign(new Error(what + ' works only inside nth.'), { code: 'unavailable' }));
  const prefix = 'nth:';
  const local = (() => { try { return window.localStorage; } catch (_) { return null; } })();
  const memory = new Map();
  const store = {
    get: async (k) => (local ? local.getItem(prefix + k) : memory.has(k) ? memory.get(k) : null),
    set: async (k, v) => { const value = typeof v === 'string' ? v : JSON.stringify(v); if (local) local.setItem(prefix + k, value); else memory.set(k, value); },
    remove: async (k) => { if (local) local.removeItem(prefix + k); else memory.delete(k); },
    keys: async (p) => {
      const all = local ? Array.from({ length: local.length }, (_, i) => local.key(i)).filter((k) => k && k.startsWith(prefix)).map((k) => k.slice(prefix.length)) : Array.from(memory.keys());
      return all.filter((k) => k.startsWith(p || '')).sort();
    },
  };
  const pick = (options) => new Promise((resolve) => {
    const o = options || {};
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = o.accept || '';
    input.multiple = !!o.multiple;
    input.onchange = async () => {
      const files = await Promise.all(Array.from(input.files || []).map(async (f) => ({ name: f.name, type: f.type, size: f.size, bytes: new Uint8Array(await f.arrayBuffer()) })));
      resolve(o.multiple ? files : files[0] || null);
    };
    input.click();
  });
  const dark = matchMedia('(prefers-color-scheme: dark)');
  const nth = Object.freeze({
    storage: Object.freeze(store),
    complete: unavailable('The model'),
    callTool: unavailable('Tools'),
    files: Object.freeze({ pick }),
    send: unavailable('Sending to the chat'),
    theme: Object.freeze({ get dark() { return dark.matches; }, get variables() { return {}; }, addEventListener(t, fn) { if (t === 'change') dark.addEventListener('change', () => fn({ dark: dark.matches, variables: {} })); }, removeEventListener() { } }),
  });
  Object.defineProperty(window, 'nth', { value: nth, enumerable: true });
  if (!('claude' in window)) Object.defineProperty(window, 'claude', { value: Object.freeze({ complete: (p) => nth.complete(p) }), enumerable: true });
  if (!('storage' in window)) {
    const scope = (s) => (s ? 'shared:' : 'personal:');
    Object.defineProperty(window, 'storage', { enumerable: true, value: Object.freeze({
      get: async (k, s) => { const v = await store.get(scope(s) + k); return v === null ? null : { key: k, value: v, shared: !!s }; },
      set: async (k, v, s) => { await store.set(scope(s) + k, v); return { key: k, value: v, shared: !!s }; },
      delete: async (k, s) => { await store.remove(scope(s) + k); return { key: k, deleted: true, shared: !!s }; },
      list: async (p, s) => ({ keys: (await store.keys(scope(s) + (p || ''))).map((k) => k.slice(scope(s).length)), prefix: p || '', shared: !!s }),
    }) });
  }
})();
