// nth inline HTML widget size (DESIGN.md 7.6 "Inline: height from size-changed"; ED-NEW-web-w19-3). Runs after the hardening
// and bridge scripts in the top document of an inline widget, only on nth's own w-*.invalid origins. It tells nth how tall
// the page wants to be, once per animation frame at most and only when it changed; nth clamps it (80 px to 60 % of the
// thread) and the widget scrolls inside beyond that.
(() => {
  'use strict';
  if (!/^https:\/\/w-[a-z0-9-]+\.invalid$/.test(location.origin) || window.top !== window) return;
  const webview = window.chrome && window.chrome.webview;
  if (!webview) return;
  const post = webview.postMessage.bind(webview);
  let last = -1;
  let queued = false;
  const send = () => {
    queued = false;
    const root = document.documentElement;
    if (!root) return;
    const body = document.body;
    const height = Math.ceil(Math.max(root.scrollHeight, body ? body.scrollHeight : 0));
    if (height === last) return;
    last = height;
    post({ nth: 1, s: '', type: 'size', height });
  };
  const queue = () => { if (!queued) { queued = true; requestAnimationFrame(send); } };
  const start = () => {
    if (typeof ResizeObserver === 'function') {
      const observer = new ResizeObserver(queue);
      observer.observe(document.documentElement);
      if (document.body) observer.observe(document.body);
    }
    queue();
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
