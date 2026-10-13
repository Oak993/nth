// nth sandbox bridge (DESIGN.md 6.5 "the only way out is the bridge"; ED-NEW-web-w13-4). Runs before any page script in
// the top document of a sandboxed view, only on nth's own sandboxed origins. window.nth is the artifact's one channel to
// nth: per-artifact storage, a context-free call to the chat's model, MCP tools the user granted, files the user picks
// (never a path), a draft for the composer, and nth's theme. Every request is checked again by nth (origin, consent,
// limits); nothing here is trusted. Compatibility shims map window.claude.complete and window.storage onto the same calls.
(() => {
  'use strict';
  if (!/^https:\/\/[amw]-[a-z0-9-]+\.invalid$/.test(location.origin) || window.top !== window) return;
  const webview = window.chrome && window.chrome.webview;
  if (!webview || window.nth) return;
  const post = webview.postMessage.bind(webview);
  const listen = webview.addEventListener.bind(webview);
  const define = (target, name, value) => Object.defineProperty(target, name, { value, writable: false, configurable: false, enumerable: true });
  const pending = new Map();
  let discardSaves = null;
  let discardOperation = null;
  let next = 1;
  // A token per document: replies meant for an earlier document of this view (before a reload) are ignored.
  const session = Array.from(crypto.getRandomValues(new Uint32Array(3)), (n) => n.toString(36)).join('');

  const failure = (error) => {
    const code = (error && error.code) || 'failed';
    const out = new Error((error && error.message) || code);
    out.code = code;
    return out;
  };
  const request = (type, payload, onDelta) => {
    const call = new Promise((resolve, reject) => {
    const id = next++;
    pending.set(id, { resolve, reject, onDelta });
    post(Object.assign({}, payload, { nth: 1, s: session, id, type }));
    });
    if (discardSaves && type.startsWith('storage.')) discardSaves.push(call);
    return call;
  };
  const listeners = { theme: new Set(), willDiscard: new Set() };
  const beforeDiscard = async (token) => {
    if (typeof token !== 'string') return;
    if (discardOperation) discardOperation.cancel();
    const saves = [];
    const operation = { token, cancelled: false, cancel: null };
    const cancelled = new Promise(resolve => { operation.cancel = () => { operation.cancelled = true; resolve(); }; });
    discardOperation = operation;
    discardSaves = saves;
    let ok = true;
    const detail = Object.freeze({ waitUntil: (save) => saves.push(Promise.resolve(save)) });
    try {
      for (const fn of listeners.willDiscard) {
        const result = fn(detail);
        if (result && typeof result.then === 'function') saves.push(Promise.resolve(result));
      }
      if (typeof CustomEvent === 'function' && typeof window.dispatchEvent === 'function')
        window.dispatchEvent(new CustomEvent('nth:willDiscard', { detail }));
      let cursor = 0;
      while (cursor < saves.length && !operation.cancelled) {
        const batch = saves.slice(cursor);
        cursor = saves.length;
        await Promise.race([Promise.all(batch), cancelled]);
      }
    } catch (_) { ok = false; }
    finally {
      if (discardOperation === operation) { discardOperation = null; discardSaves = null; }
    }
    if (!operation.cancelled) post({ nth: 1, s: session, type: 'discard.ready', token, ok });
  };
  let theme = { dark: matchMedia('(prefers-color-scheme: dark)').matches, variables: {} };
  const applyTheme = (value) => {
    theme = { dark: !!value.dark, variables: Object.assign({}, value.variables || {}) };
    const root = document.documentElement;
    if (root) {
      for (const [name, v] of Object.entries(theme.variables)) {
        if (/^--nth-[a-z0-9-]+$/.test(name)) root.style.setProperty(name, String(v));
      }
      root.style.colorScheme = theme.dark ? 'dark' : 'light';
    }
    for (const fn of listeners.theme) { try { fn(theme); } catch (e) { setTimeout(() => { throw e; }); } }
  };

  listen('message', (event) => {
    const message = event.data;
    if (!message || message.nth !== 1) return;
    if (message.event === 'willDiscard') { beforeDiscard(message.token); return; }
    if (message.event === 'discard.cancel') {
      if (discardOperation && discardOperation.token === message.token) discardOperation.cancel();
      return;
    }
    if (message.event === 'theme') { applyTheme(message.theme || {}); return; }
    if (message.event === 'morph' && typeof message.html === 'string') { morph(message.html); return; }
    if (message.s !== session) return;
    const entry = pending.get(message.id);
    if (!entry) return;
    if (typeof message.delta === 'string') { if (entry.onDelta) entry.onDelta(message.delta); return; }
    pending.delete(message.id);
    if (message.ok) entry.resolve(message.result);
    else entry.reject(failure(message.error));
  });

  const key = (k) => { if (typeof k !== 'string' || k.length === 0) throw new TypeError('A storage key is a non-empty string.'); return k; };
  const storage = Object.freeze({
    get: (k) => request('storage.get', { key: key(k) }),
    set: (k, value) => request('storage.set', { key: key(k), value: typeof value === 'string' ? value : JSON.stringify(value) }).then(() => undefined),
    remove: (k) => request('storage.remove', { key: key(k) }).then(() => undefined),
    keys: (prefix) => request('storage.keys', { prefix: typeof prefix === 'string' ? prefix : '' }),
  });

  // nth.complete(prompt | messages, { system?, max_tokens?, stream? })
  const complete = (input, options) => {
    const o = options || {};
    const payload = {
      messages: Array.isArray(input) ? input.map((m) => ({ role: String(m && m.role), content: typeof (m && m.content) === 'string' ? m.content : JSON.stringify(m && m.content) }))
        : [{ role: 'user', content: String(input) }],
      system: typeof o.system === 'string' ? o.system : null,
      max_tokens: Number.isFinite(o.max_tokens) ? Math.floor(o.max_tokens) : null,
      stream: !!o.stream,
    };
    if (!o.stream) return request('complete', payload).then((r) => r.text);
    const queue = [];
    let wake = null;
    let done = false;
    let error = null;
    let id = 0;
    const push = () => { if (wake) { const w = wake; wake = null; w(); } };
    const call = request('complete', payload, (delta) => { queue.push(delta); push(); });
    id = next - 1;
    call.then(() => { done = true; push(); }, (e) => { error = e; done = true; push(); });
    const iterator = {
      async next() {
        for (;;) {
          if (queue.length) return { value: queue.shift(), done: false };
          if (error) { const e = error; error = null; throw e; }
          if (done) return { value: undefined, done: true };
          await new Promise((r) => { wake = r; });
        }
      },
      async return() { if (!done) post({ nth: 1, s: session, type: 'complete.cancel', target: id }); done = true; return { value: undefined, done: true }; },
      [Symbol.asyncIterator]() { return this; },
    };
    return iterator;
  };

  const toFile = (f) => {
    const raw = atob(f.bytes || '');
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    return { name: String(f.name), type: String(f.type || ''), size: bytes.length, bytes };
  };
  const pick = (options) => {
    const o = options || {};
    const multiple = !!o.multiple;
    return request('files.pick', { accept: typeof o.accept === 'string' ? o.accept : '', multiple, reason: o.reason === 'drop' ? 'drop' : 'pick' })
      .then((files) => { const list = (files || []).map(toFile); return multiple ? list : (list[0] || null); });
  };

  const themeApi = Object.freeze({
    get dark() { return theme.dark; },
    get variables() { return Object.assign({}, theme.variables); },
    addEventListener(type, fn) { if (type === 'change' && typeof fn === 'function') listeners.theme.add(fn); },
    removeEventListener(type, fn) { if (type === 'change') listeners.theme.delete(fn); },
  });

  const nth = {
    storage,
    complete,
    callTool: (server, tool, args) => request('callTool', { server: String(server), tool: String(tool), args: args === undefined ? {} : args }),
    files: Object.freeze({ pick }),
    send: (text) => request('send', { text: String(text) }).then(() => undefined),
    theme: themeApi,
  };
  Object.defineProperties(nth, {
    addEventListener: { value(type, fn) { if (type === 'willDiscard' && typeof fn === 'function') listeners.willDiscard.add(fn); } },
    removeEventListener: { value(type, fn) { if (type === 'willDiscard') listeners.willDiscard.delete(fn); } },
  });
  Object.freeze(nth);
  define(window, 'nth', nth);

  // Compatibility shims, so artifacts made for other apps run (vendor names never appear in nth's UI).
  if (!('claude' in window)) define(window, 'claude', Object.freeze({ complete: (prompt) => nth.complete(prompt) }));
  const shared = (flag) => (flag ? 'shared:' : 'personal:');
  define(window, 'storage', Object.freeze({
    get: async (k, isShared) => { const value = await storage.get(shared(isShared) + key(k)); return value === null || value === undefined ? null : { key: k, value, shared: !!isShared }; },
    set: async (k, value, isShared) => { await storage.set(shared(isShared) + key(k), value); return { key: k, value, shared: !!isShared }; },
    delete: async (k, isShared) => { await storage.remove(shared(isShared) + key(k)); return { key: k, deleted: true, shared: !!isShared }; },
    list: async (prefix, isShared) => {
      const keys = await storage.keys(shared(isShared) + (prefix || ''));
      return { keys: keys.map((k) => k.slice(shared(isShared).length)), prefix: prefix || '', shared: !!isShared };
    },
  }));

  // Runtime errors reach the pane's calm error bar ("This artifact hit an error", Ask to fix).
  let reported = 0;
  const report = (message, stack) => {
    if (reported++ > 20) return;
    post({ nth: 1, s: session, type: 'error', message: String(message || 'Unknown error').slice(0, 2000), stack: String(stack || '').slice(0, 8000) });
  };
  window.addEventListener('error', (e) => report(e.message, e.error && e.error.stack ? e.error.stack : (e.filename ? e.filename + ':' + e.lineno + ':' + e.colno : '')), true);
  window.addEventListener('unhandledrejection', (e) => report(e.reason && e.reason.message ? e.reason.message : String(e.reason), e.reason && e.reason.stack));

  // The native file picker is off in the sandbox (DESIGN 8.4): a file input opens nth's own sheet instead, and the page
  // gets the files the user chose, never a path.
  const fill = (input, files) => {
    const transfer = new DataTransfer();
    for (const f of files) transfer.items.add(new File([f.bytes], f.name, { type: f.type }));
    input.files = transfer.files;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const open = (input) => {
    pick({ accept: input.accept, multiple: input.multiple }).then((result) => {
      const files = Array.isArray(result) ? result : result ? [result] : [];
      if (files.length) fill(input, files);
      else input.dispatchEvent(new Event('cancel', { bubbles: false }));
    }, (e) => report(e.message, e.stack));
  };
  window.addEventListener('click', (e) => {
    const input = e.target;
    if (input instanceof HTMLInputElement && input.type === 'file' && !input.disabled) { e.preventDefault(); open(input); }
  }, true);
  const showPicker = HTMLInputElement.prototype.showPicker;
  Object.defineProperty(HTMLInputElement.prototype, 'showPicker', {
    configurable: false, writable: false,
    value: function () { if (this.type === 'file') { open(this); return; } return showPicker && showPicker.call(this); },
  });

  // Files dropped from the desktop never reach the page directly; nth's sheet offers them instead (DESIGN 8.4).
  const synthetic = new WeakSet();
  const hasFiles = (e) => e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
  window.addEventListener('dragover', (e) => { if (!synthetic.has(e) && hasFiles(e)) e.preventDefault(); }, true);
  window.addEventListener('drop', (e) => {
    if (synthetic.has(e) || !hasFiles(e)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    const target = e.target;
    pick({ multiple: true, reason: 'drop' }).then((files) => {
      if (!files.length) return;
      const transfer = new DataTransfer();
      for (const f of files) transfer.items.add(new File([f.bytes], f.name, { type: f.type }));
      const drop = new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer, clientX: e.clientX, clientY: e.clientY });
      synthetic.add(drop);
      (target && target.isConnected ? target : document.body).dispatchEvent(drop);
    }, (err) => report(err.message, err.stack));
  }, true);

  // Streaming preview (DESIGN 6.4): nth sends the newest document and the page morphs into it, so state and scroll
  // survive; nodes are matched by id, else by tag and position.
  const sameNode = (a, b) => a.nodeType === b.nodeType && a.nodeName === b.nodeName && (a.nodeType !== 1 || (a.id || '') === (b.id || ''));
  const syncAttributes = (from, to) => {
    for (const attr of Array.from(from.attributes)) if (!to.hasAttribute(attr.name)) from.removeAttribute(attr.name);
    for (const attr of Array.from(to.attributes)) if (from.getAttribute(attr.name) !== attr.value) from.setAttribute(attr.name, attr.value);
  };
  const morphNode = (from, to) => {
    if (from.nodeType === 3 || from.nodeType === 8) { if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue; return; }
    if (from.nodeName === 'SCRIPT') return; // scripts run once, at the full load
    syncAttributes(from, to);
    if (from.nodeName === 'INPUT' || from.nodeName === 'TEXTAREA' || from.nodeName === 'SELECT') return; // keep what the user typed
    morphChildren(from, to);
  };
  const morphChildren = (from, to) => {
    const wanted = Array.from(to.childNodes);
    let cursor = from.firstChild;
    for (const node of wanted) {
      let match = null;
      for (let probe = cursor; probe && !match; probe = probe.nextSibling) if (sameNode(probe, node)) match = probe;
      if (match) {
        while (cursor && cursor !== match) { const gone = cursor; cursor = cursor.nextSibling; gone.remove(); }
        morphNode(match, node);
        cursor = match.nextSibling;
      } else {
        from.insertBefore(document.importNode(node, true), cursor);
      }
    }
    while (cursor) { const gone = cursor; cursor = cursor.nextSibling; gone.remove(); }
  };
  const morph = (html) => {
    const next = new DOMParser().parseFromString(html, 'text/html');
    if (document.body && next.body) morphNode(document.body, next.body);
    if (next.title && document.title !== next.title) document.title = next.title;
  };

  const ready = () => {
    request('theme.get', {}).then(applyTheme, () => { });
    post({ nth: 1, s: session, type: 'ready', title: document.title || '' });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, { once: true });
  else ready();
})();
