// nth sandbox hardening (DESIGN.md 6.5, 8.1, 8.4; ED-NEW-web-w13-1). Runs before any page script in every document
// of a sandboxed view, and only on nth's own sandboxed origins. It closes what the Content-Security-Policy cannot:
// WebRTC, DNS prefetch and preconnect hints, script-initiated clipboard writes, fullscreen and pointer lock, service
// workers, windows. Every request that still leaves goes through nth's resource handler, which answers only nth's hosts.
(() => {
  'use strict';
  if (!/^https:\/\/[amw]-[a-z0-9-]+\.invalid$/.test(location.origin)) return;
  const define = (target, name, value) => {
    try { Object.defineProperty(target, name, { value, writable: false, configurable: false, enumerable: false }); } catch (_) { /* already locked */ }
  };
  const block = (name) => () => { throw new DOMException(name + ' is not available in nth’s sandbox.', 'NotAllowedError'); };

  // WebRTC: gone before any page script runs, here and in every frame this page reaches.
  const RTC = ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel', 'RTCSessionDescription', 'RTCIceCandidate',
    'RTCRtpSender', 'RTCRtpReceiver', 'RTCRtpTransceiver', 'RTCDtlsTransport', 'RTCIceTransport', 'RTCSctpTransport'];
  const scrub = (win) => {
    if (!win) return win;
    try {
      for (const name of RTC) { try { delete win[name]; } catch (_) { } define(win, name, undefined); }
      if (win.navigator && win.navigator.mediaDevices) define(win.navigator, 'mediaDevices', undefined);
      if (win.ServiceWorkerContainer) define(win.ServiceWorkerContainer.prototype, 'register', () => Promise.reject(new DOMException('Service workers are not available in nth’s sandbox.', 'SecurityError')));
      define(win, 'open', () => null);
      define(win, 'showOpenFilePicker', undefined);
      define(win, 'showSaveFilePicker', undefined);
      define(win, 'showDirectoryPicker', undefined);
    } catch (_) { /* a cross-origin frame cannot be reached, and cannot load either (frame-src 'none') */ }
    return win;
  };
  scrub(window);
  for (const [proto, prop] of [[HTMLIFrameElement.prototype, 'contentWindow'], [HTMLFrameElement.prototype, 'contentWindow'], [HTMLObjectElement.prototype, 'contentWindow']]) {
    const original = Object.getOwnPropertyDescriptor(proto, prop);
    if (original && original.get) {
      Object.defineProperty(proto, prop, { configurable: false, enumerable: original.enumerable, get() { return scrub(original.get.call(this)); } });
    }
  }
  const docGetter = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, 'contentDocument');
  if (docGetter && docGetter.get) {
    Object.defineProperty(HTMLIFrameElement.prototype, 'contentDocument', {
      configurable: false, enumerable: docGetter.enumerable,
      get() { const doc = docGetter.get.call(this); if (doc) scrub(doc.defaultView); return doc; },
    });
  }

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
  new MutationObserver((records) => {
    for (const record of records) {
      if (record.type === 'attributes') strip(record.target);
      else record.addedNodes.forEach(strip);
    }
  }).observe(document, { childList: true, subtree: true, attributes: true, attributeFilter: ['rel', 'http-equiv', 'type'] });

  // Clipboard writes from script, fullscreen and pointer lock: denied (Permissions-Policy denies them too).
  const execCommand = Document.prototype.execCommand;
  define(Document.prototype, 'execCommand', function (command, ...rest) {
    if (/^(copy|cut)$/i.test(String(command))) return false;
    return execCommand.call(this, command, ...rest);
  });
  if (window.Clipboard) {
    define(Clipboard.prototype, 'write', () => Promise.reject(new DOMException('Copy with nth’s Copy button.', 'NotAllowedError')));
    define(Clipboard.prototype, 'writeText', () => Promise.reject(new DOMException('Copy with nth’s Copy button.', 'NotAllowedError')));
  }
  define(Element.prototype, 'requestFullscreen', () => Promise.reject(new DOMException('Fullscreen is not available in nth’s sandbox.', 'NotAllowedError')));
  define(Element.prototype, 'webkitRequestFullscreen', block('Fullscreen'));
  define(Element.prototype, 'requestPointerLock', () => Promise.reject(new DOMException('Pointer lock is not available in nth’s sandbox.', 'NotAllowedError')));
  if (window.Notification) define(window, 'Notification', undefined);
  if (navigator.credentials) define(navigator, 'credentials', undefined);
  if (navigator.share) define(Navigator.prototype, 'share', () => Promise.reject(new DOMException('Sharing is not available in nth’s sandbox.', 'NotAllowedError')));
  if (navigator.sendBeacon) define(Navigator.prototype, 'sendBeacon', () => false);
})();
