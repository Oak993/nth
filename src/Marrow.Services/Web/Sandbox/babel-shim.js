// nth's stand-in for in-browser Babel (DESIGN.md 6.5 "CDN URLs from other apps' artifacts ... are rewritten to the local
// copy"; ED-NEW-web-w13-3). Pages made for other apps load Babel from a CDN and write their components in
// <script type="text/babel">. nth has no Babel offline; this compiles those scripts with the vendored Sucrase instead
// (JSX and TypeScript, React.createElement over the page's React global) and runs them in order, as Babel would.
(() => {
  'use strict';
  const sucraseUrl = 'https://lib-nth.invalid/sucrase/sucrase.min.js';
  const selector = 'script[type="text/babel"],script[type="text/jsx"],script[type="text/tsx"]';
  const raise = (error) => setTimeout(() => { throw error; });
  const ready = () => window.sucrase ? Promise.resolve() : new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = sucraseUrl;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('nth could not load its JSX compiler.'));
    (document.head || document.documentElement).appendChild(script);
  });
  const run = () => {
    for (const original of document.querySelectorAll(selector)) {
      if (original.dataset.nthDone) continue;
      original.dataset.nthDone = '1';
      if (original.src) { raise(new Error('nth cannot load ' + original.src + ' as a JSX script; put the code inside the page.')); continue; }
      try {
        const typescript = /tsx|typescript/i.test(original.type + ' ' + (original.dataset.presets || '') + ' ' + (original.getAttribute('data-type') || ''));
        const module = original.getAttribute('data-type') === 'module';
        const code = window.sucrase.transform(original.textContent, {
          transforms: typescript ? ['jsx', 'typescript'] : ['jsx'], jsxRuntime: 'classic', production: true,
          filePath: typescript ? 'inline.tsx' : 'inline.jsx',
        }).code;
        const script = document.createElement('script');
        if (module) script.type = 'module';
        script.textContent = code;
        original.after(script);
      } catch (error) { raise(error); }
    }
  };
  const start = () => ready().then(run).catch(raise);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
