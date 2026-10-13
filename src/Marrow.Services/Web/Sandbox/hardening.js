// nth sandbox hardening (DESIGN.md 6.5, 8.1, 8.4; ED-NEW-web-w13-1). Runs before any page script in every document
// of a sandboxed view, and only on nth's own sandboxed origins. It closes what the Content-Security-Policy cannot:
// WebRTC, DNS prefetch and preconnect hints, script-initiated clipboard writes, fullscreen and pointer lock, service
// workers, windows. Every request that still leaves goes through nth's resource handler, which answers only nth's hosts.
// + ED-NEW-web-w26-2 (W26 security review): also in a sandboxed frame inside one (its origin is opaque, "null"), and a
// frame this script never ran in (an iframe's first about:blank) is scrubbed as soon as the page can reach it.
// + ED-NEW-web-sec-artifacts-2 (security review 2026-10-13, M5): such a frame is a realm of its own, with its own DOM
// methods, observer and window, so the whole hardening runs in it (once per realm): a frame added through that frame's
// own document, at any depth, is hardened the moment it appears, as one added through the page's.
// + ED-NEW-web-sec-artifacts-8 (the review's residual): reopening a document (document.open, or a write that opens it) no longer
// drops the frame load listener, and the body setter, Range.surroundContents, select and options add and a table's caption,
// tHead and tFoot setters are hooked too.
(() => {
  'use strict';
  const nth = /^https:\/\/[amw]-[a-z0-9-]+\.invalid$/;
  const ancestors = () => { try { return Array.from(location.ancestorOrigins || []); } catch (_) { return []; } };
  if (!/^https:\/\/[amw]-[a-z0-9-]+\.invalid$/.test(location.origin) && !(location.origin === 'null' && ancestors().some((origin) => nth.test(origin)))) return;
  const define = (target, name, value) => {
    try { Object.defineProperty(target, name, { value, writable: false, configurable: false, enumerable: false }); } catch (_) { /* already locked */ }
  };
  // W19: an MCP App that declared camera or microphone keeps navigator.mediaDevices (each use still asks the user).
  const keepMedia = /*NTH_KEEP_MEDIA*/false;
  const block = (name) => () => { throw new DOMException(name + ' is not available in nth’s sandbox.', 'NotAllowedError'); };
  const refuse = (message) => () => Promise.reject(new DOMException(message, 'NotAllowedError'));
  const root = window;
  // One mark per realm, keyed by its document: a frame that navigates gets a new realm and document behind the same
  // window, and is hardened again; a realm reached again is not (no second observer or listener).
  const seen = new WeakSet();

  // Every frame this page can reach, at any depth: hardened right after anything that can add one (each DOM insertion,
  // document.write, an HTML setter, in any realm), at a frame's load before the page's own handler, and on every mutation
  // record. ED-NEW-web-w26-2: window[0], window.frames and named frames reach a child without the frame getters.
  const scrubFrames = (win) => {
    let count = 0;
    try { count = win.length; } catch (_) { return; }
    for (let i = 0; i < count; i++) {
      let child = null;
      try { child = win[i]; } catch (_) { continue; }
      if (!child || child === win) continue;
      harden(child);
      scrubFrames(child);
    }
  };
  const settle = () => scrubFrames(root);

  // + ED-NEW-web-sec-artifacts-8: one capture listener on every document re-walks the frames at any load event there (a frame
  // with no src loads during its insertion, and this runs before the page's own handlers; any load, as a page can redefine
  // tagName). document.open, and a write that opens the document itself, erase every listener on it, so it is added again,
  // through the addEventListener the page found before any script of its own ran (the same function twice is one listener).
  const onLoad = () => settle();
  const listen = root.EventTarget && root.EventTarget.prototype && root.EventTarget.prototype.addEventListener;
  const relisten = (doc) => {
    try { (typeof listen === 'function' ? listen : doc.addEventListener).call(doc, 'load', onLoad, true); } catch (_) { /* not a document */ }
  };

  // DNS prefetch, preconnect, prefetch, prerender and speculation rules: removed as they are added, before they act.
  const hint = /(^|\s)(dns-prefetch|preconnect|prefetch|prerender)(\s|$)/i;
  const strip = (node) => {
    if (node.nodeType !== 1) return;
    const tag = node.tagName;
    if (tag === 'LINK' && hint.test(node.getAttribute('rel') || '')) node.remove();
    else if (tag === 'META' && /x-dns-prefetch-control/i.test(node.getAttribute('http-equiv') || '')) node.remove();
    else if (tag === 'SCRIPT' && /speculationrules/i.test(node.getAttribute('type') || '')) node.remove();
    else if (node.querySelectorAll) node.querySelectorAll('link[rel],meta[http-equiv],script[type]').forEach(strip);
  };

  const hardenRealm = (win) => {
    // WebRTC: gone before any page script runs, here and in every frame this page reaches.
    const RTC = ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel', 'RTCSessionDescription', 'RTCIceCandidate',
      'RTCRtpSender', 'RTCRtpReceiver', 'RTCRtpTransceiver', 'RTCDtlsTransport', 'RTCIceTransport', 'RTCSctpTransport'];
    for (const name of RTC) { try { delete win[name]; } catch (_) { } define(win, name, undefined); }
    if (!keepMedia && win.navigator && win.navigator.mediaDevices) define(win.navigator, 'mediaDevices', undefined);
    if (win.ServiceWorkerContainer) define(win.ServiceWorkerContainer.prototype, 'register', () => Promise.reject(new DOMException('Service workers are not available in nth’s sandbox.', 'SecurityError')));
    define(win, 'open', () => null);
    define(win, 'showOpenFilePicker', undefined);
    define(win, 'showSaveFilePicker', undefined);
    define(win, 'showDirectoryPicker', undefined);

    // A frame reached through its element is hardened before the caller gets it.
    const getter = (type, name, reach) => {
      const proto = type && type.prototype;
      const original = proto && Object.getOwnPropertyDescriptor(proto, name);
      if (!original || !original.get || !original.configurable) return;
      Object.defineProperty(proto, name, { configurable: false, enumerable: original.enumerable, get() { const value = original.get.call(this); reach(value); return value; } });
    };
    const reachWindow = (child) => { if (child) { harden(child); scrubFrames(child); } };
    for (const type of [win.HTMLIFrameElement, win.HTMLFrameElement, win.HTMLObjectElement]) {
      getter(type, 'contentWindow', reachWindow);
      getter(type, 'contentDocument', (doc) => { if (doc) reachWindow(doc.defaultView); });
    }

    // Anything that can add a frame, in this realm's own DOM methods.
    const after = (type, name) => {
      const proto = type && type.prototype;
      const original = proto && Object.getOwnPropertyDescriptor(proto, name);
      if (!original || typeof original.value !== 'function') return;
      define(proto, name, function (...args) { try { return original.value.apply(this, args); } finally { settle(); } });
    };
    const setter = (type, name) => {
      const proto = type && type.prototype;
      const original = proto && Object.getOwnPropertyDescriptor(proto, name);
      if (!original || !original.set || !original.configurable) return;
      Object.defineProperty(proto, name, {
        configurable: false, enumerable: original.enumerable, get: original.get,
        set(value) { try { original.set.call(this, value); } finally { settle(); } },
      });
    };
    for (const name of ['appendChild', 'insertBefore', 'replaceChild']) after(win.Node, name);
    for (const type of [win.Element, win.Document, win.DocumentFragment]) {
      for (const name of ['append', 'prepend', 'replaceChildren', 'moveBefore']) after(type, name);
    }
    for (const type of [win.Element, win.CharacterData, win.DocumentType]) {
      for (const name of ['after', 'before', 'replaceWith']) after(type, name);
    }
    for (const name of ['insertAdjacentElement', 'insertAdjacentHTML', 'setHTMLUnsafe', 'setHTML']) after(win.Element, name);
    after(win.ShadowRoot, 'setHTMLUnsafe');
    after(win.ShadowRoot, 'setHTML');
    // + ED-NEW-web-sec-artifacts-8: a write or writeln on a document no parser has open opens it first, and so erases the load
    // listener before it parses a frame. An empty write first makes that very open (or none: a write from a parsing script, an
    // ignored write, a document already open), the listener is back before the real write parses anything, and after each call.
    const documentProto = win.Document && win.Document.prototype;
    const write = documentProto && Object.getOwnPropertyDescriptor(documentProto, 'write');
    for (const name of ['write', 'writeln', 'open']) {
      const original = documentProto && Object.getOwnPropertyDescriptor(documentProto, name);
      if (!original || typeof original.value !== 'function') continue;
      define(documentProto, name, function (...args) {
        if (name !== 'open' && write && typeof write.value === 'function') {
          try { write.value.call(this, ''); } catch (_) { /* the real call throws the same way */ }
          relisten(this);
        }
        try { return original.value.apply(this, args); } finally { relisten(this); settle(); }
      });
    }
    after(win.Range, 'insertNode');
    for (const name of ['innerHTML', 'outerHTML']) setter(win.Element, name);
    setter(win.ShadowRoot, 'innerHTML');
    // + ED-NEW-web-sec-artifacts-8: the other calls that connect a subtree the page built (a frame with a src fires no load event
    // as it is inserted). select[i] = option and select.options[i] = option are indexed setters no script can hook: a frame they
    // connect is reached by the load listener (no src) or the mutation observer (the next microtask).
    setter(win.Document, 'body');
    after(win.Range, 'surroundContents');
    after(win.HTMLSelectElement, 'add');
    after(win.HTMLOptionsCollection, 'add');
    for (const name of ['caption', 'tHead', 'tFoot']) setter(win.HTMLTableElement, name);
    const doc = win.document;
    if (doc) relisten(doc);
    if (doc && win.MutationObserver) {
      new win.MutationObserver((records) => {
        for (const record of records) {
          if (record.type === 'attributes') strip(record.target);
          else record.addedNodes.forEach(strip);
        }
        settle();
      }).observe(doc, { childList: true, subtree: true, attributes: true, attributeFilter: ['rel', 'http-equiv', 'type'] });
    }

    // Clipboard writes from script, fullscreen and pointer lock: denied (Permissions-Policy denies them too).
    const execCommand = win.Document && win.Document.prototype.execCommand;
    if (typeof execCommand === 'function') {
      define(win.Document.prototype, 'execCommand', function (command, ...rest) {
        if (/^(copy|cut)$/i.test(String(command))) return false;
        try { return execCommand.call(this, command, ...rest); } finally { settle(); } // insertHTML can add a frame
      });
    }
    if (win.Clipboard) {
      define(win.Clipboard.prototype, 'write', refuse('Copy with nth’s Copy button.'));
      define(win.Clipboard.prototype, 'writeText', refuse('Copy with nth’s Copy button.'));
    }
    if (win.Element) {
      define(win.Element.prototype, 'requestFullscreen', refuse('Fullscreen is not available in nth’s sandbox.'));
      define(win.Element.prototype, 'webkitRequestFullscreen', block('Fullscreen'));
      define(win.Element.prototype, 'requestPointerLock', refuse('Pointer lock is not available in nth’s sandbox.'));
    }
    if (win.Notification) define(win, 'Notification', undefined);
    const navigator = win.navigator;
    if (navigator && navigator.credentials) define(navigator, 'credentials', undefined);
    if (win.Navigator && navigator && navigator.share) define(win.Navigator.prototype, 'share', refuse('Sharing is not available in nth’s sandbox.'));
    if (win.Navigator && navigator && navigator.sendBeacon) define(win.Navigator.prototype, 'sendBeacon', () => false);
  };

  // A realm is hardened once; a cross-origin frame cannot be reached (and cannot load either: frame-src 'none').
  const harden = (win) => {
    if (!win) return win;
    let key = null;
    try { key = win.document || win; } catch (_) { return win; }
    if (seen.has(key)) return win;
    seen.add(key);
    try { hardenRealm(win); } catch (_) { /* a frame that went away while it was hardened */ }
    return win;
  };

  harden(root);
})();
