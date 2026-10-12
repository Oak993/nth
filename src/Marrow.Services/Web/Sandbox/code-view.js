// nth code view (DESIGN.md 6.1 kinds "code", "markdown", "nth-ui" when opened in Visus; ED-NEW-web-w13-3). Highlights
// the page's one code block with the vendored highlight.js, offline.
(() => {
  'use strict';
  const block = document.querySelector('pre code');
  if (!block || !window.hljs) return;
  try { window.hljs.highlightElement(block); } catch (error) { setTimeout(() => { throw error; }); }
})();
