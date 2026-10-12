// nth Mermaid runner (DESIGN.md 6.1 kind "mermaid", 6.4; ED-NEW-web-w13-3). Renders the artifact's diagram with the
// vendored Mermaid, offline, in the strict security level (no HTML labels, no click handlers that run script).
(async () => {
  'use strict';
  const holder = document.getElementById('nth-source');
  const target = document.getElementById('diagram');
  if (!holder || !target || !window.mermaid) return;
  const source = JSON.parse(holder.textContent);
  const dark = matchMedia('(prefers-color-scheme: dark)').matches;
  try {
    window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: dark ? 'dark' : 'default', fontFamily: 'Segoe UI Variable, Segoe UI, sans-serif' });
    const { svg } = await window.mermaid.render('nth-diagram', source.code);
    target.innerHTML = svg;
  } catch (error) {
    target.textContent = '';
    setTimeout(() => { throw error instanceof Error ? error : new Error(String(error)); });
  }
})();
