// nth MCP Apps bridge (DESIGN.md 7.6 "Bridge", "Compatibility"; ED-NEW-web-w19-1). Runs before any page script in the top
// document of an MCP App, only on nth's own m-*.invalid origins. An MCP App talks to its host with JSON-RPC 2.0 posted to
// window.parent; in nth the app is the top document, so its parent is its own window. JSON-RPC messages it posts there go to
// nth through chrome.webview, and nth's messages come back as message events from that same window, which is what the
// apps' own transport listens for. Everything is checked again by nth (origin, handshake, tool, consent); nothing here is
// trusted. window.openai maps the older Apps SDK names (toolInput, toolOutput, callTool, sendFollowUpMessage,
// setWidgetState, requestDisplayMode, theme, maxHeight) onto the same protocol.
(() => {
  'use strict';
  if (!/^https:\/\/m-[a-z0-9-]+\.invalid$/.test(location.origin) || window.top !== window) return;
  const webview = window.chrome && window.chrome.webview;
  if (!webview || window.__nthMcpApp) return;
  Object.defineProperty(window, '__nthMcpApp', { value: true, writable: false, configurable: false, enumerable: false });
  const toHost = webview.postMessage.bind(webview);
  const nativePost = window.postMessage.bind(window);
  const isRpc = (m) => !!m && typeof m === 'object' && m.jsonrpc === '2.0';
  const plain = (m) => JSON.parse(JSON.stringify(m));

  // The app's transport posts to window.parent, which is this window: JSON-RPC goes to nth, anything else keeps its meaning.
  const post = function (message, targetOrigin, transfer) {
    if (isRpc(message)) { toHost(plain(message)); return undefined; }
    return nativePost(message, targetOrigin, transfer);
  };
  try { Object.defineProperty(window, 'postMessage', { value: post, writable: false, configurable: false }); } catch (_) { window.postMessage = post; }
  const deliver = (data) => window.dispatchEvent(new MessageEvent('message', { data, origin: location.origin, source: window }));

  // ---- window.openai, over the same protocol ----
  const globals = {
    toolInput: null, toolOutput: null, toolResponseMetadata: null, widgetState: null,
    theme: matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light',
    displayMode: 'inline', maxHeight: null, locale: navigator.language, safeArea: { insets: { top: 0, right: 0, bottom: 0, left: 0 } },
    userAgent: { device: { type: 'desktop' }, capabilities: { hover: true, touch: false } },
  };
  const waiting = new Map();
  let next = 1;
  let started = false;
  const ownId = (id) => typeof id === 'string' && id.startsWith('nth-openai-');
  const request = (method, params) => new Promise((resolve, reject) => {
    const id = 'nth-openai-' + next++;
    waiting.set(id, { resolve, reject });
    toHost({ jsonrpc: '2.0', id, method, params: params || {} });
  });
  const announce = (changed) => {
    try { window.dispatchEvent(new CustomEvent('openai:set_globals', { detail: { globals: changed } })); } catch (_) { /* a listener's own error */ }
  };
  const applyContext = (context) => {
    if (!context || typeof context !== 'object') return;
    const changed = {};
    if (context.theme === 'dark' || context.theme === 'light') changed.theme = context.theme;
    if (typeof context.displayMode === 'string') changed.displayMode = context.displayMode;
    if (typeof context.locale === 'string') changed.locale = context.locale;
    const d = context.containerDimensions;
    if (d && typeof d === 'object') changed.maxHeight = typeof d.maxHeight === 'number' ? d.maxHeight : (typeof d.height === 'number' ? d.height : null);
    if (Object.keys(changed).length) { Object.assign(globals, changed); announce(changed); }
  };
  const observe = (message) => {
    if (message.method === 'ui/notifications/tool-input' && message.params) {
      globals.toolInput = message.params.arguments || {};
      announce({ toolInput: globals.toolInput });
    } else if (message.method === 'ui/notifications/tool-result' && message.params) {
      globals.toolOutput = message.params.structuredContent === undefined ? null : message.params.structuredContent;
      globals.toolResponseMetadata = message.params._meta === undefined ? null : message.params._meta;
      announce({ toolOutput: globals.toolOutput, toolResponseMetadata: globals.toolResponseMetadata });
    } else if (message.method === 'ui/notifications/host-context-changed') {
      applyContext(message.params);
    } else if (message.result && message.result.hostContext) {
      applyContext(message.result.hostContext);
    }
  };
  const reportSize = () => {
    let last = '';
    const send = () => {
      const root = document.documentElement;
      if (!root) return;
      const size = { width: Math.ceil(root.scrollWidth), height: Math.ceil(root.scrollHeight) };
      const key = size.width + 'x' + size.height;
      if (key === last) return;
      last = key;
      toHost({ jsonrpc: '2.0', method: 'ui/notifications/size-changed', params: size });
    };
    let queued = false;
    const queue = () => { if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; send(); }); } };
    if (typeof ResizeObserver === 'function') new ResizeObserver(queue).observe(document.documentElement);
    queue();
  };
  // An Apps SDK page never starts the handshake itself: the shim does it the first time the page touches window.openai.
  const start = () => {
    if (started) return;
    started = true;
    request('ui/initialize', { protocolVersion: '2026-01-26', clientInfo: { name: 'openai-compat', version: '1' }, appCapabilities: {} })
      .then((result) => {
        if (result && result.hostContext) applyContext(result.hostContext);
        toHost({ jsonrpc: '2.0', method: 'ui/notifications/initialized' });
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', reportSize, { once: true }); else reportSize();
      }, () => { started = false; });
  };
  const text = (value) => String(value === undefined || value === null ? '' : value);
  const api = {
    callTool: (name, args) => { start(); return request('tools/call', { name: text(name), arguments: args && typeof args === 'object' ? args : {} }); },
    sendFollowUpMessage: (options) => { start(); return request('ui/message', { role: 'user', content: { type: 'text', text: text(options && options.prompt) } }).then(() => undefined); },
    requestDisplayMode: (options) => { start(); return request('ui/request-display-mode', { mode: text(options && options.mode) || 'inline' }); },
    openExternal: (options) => { start(); return request('ui/open-link', { url: text(options && options.href) }).then(() => undefined); },
    setWidgetState: (state) => { globals.widgetState = state === undefined ? null : state; announce({ widgetState: globals.widgetState }); return Promise.resolve(); },
  };
  const openai = {};
  for (const name of Object.keys(globals)) Object.defineProperty(openai, name, { enumerable: true, get() { start(); return globals[name]; } });
  for (const [name, fn] of Object.entries(api)) Object.defineProperty(openai, name, { enumerable: true, value: fn });
  if (!('openai' in window)) Object.defineProperty(window, 'openai', { value: Object.freeze(openai), writable: false, configurable: false, enumerable: true });

  webview.addEventListener('message', (event) => {
    const message = event.data;
    if (!isRpc(message)) return;
    observe(message);
    if (message.method === undefined && ownId(message.id)) {
      const entry = waiting.get(message.id);
      if (!entry) return;
      waiting.delete(message.id);
      if (message.error) { const e = new Error(message.error.message || 'failed'); e.code = message.error.code; entry.reject(e); } else entry.resolve(message.result);
      return;
    }
    deliver(message);
  });
})();
