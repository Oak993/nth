// nth React runner (DESIGN.md 6.1 kind "react", 6.5; ED-NEW-web-w13-3). Compiles the artifact's component (JSX or TSX)
// with Sucrase inside the sandbox, loads the libraries it imports from the offline library host, runs it, and renders
// its default export into #root. Errors go to the page's error handler, which the bridge reports to nth.
(() => {
  'use strict';
  const holder = document.getElementById('nth-source');
  if (!holder || !window.sucrase || !window.__nthLibs) return;
  const source = JSON.parse(holder.textContent);
  const root = document.getElementById('root');
  const raise = (error) => setTimeout(() => { throw error; });

  const compile = (code, file) => window.sucrase.transform(code, {
    transforms: ['jsx', 'typescript', 'imports'], jsxRuntime: 'automatic', production: true, filePath: file,
  }).code;

  const requiredBy = (code) => Array.from(new Set(Array.from(code.matchAll(/\brequire\((['"])([^'"\n]+)\1\)/g), (m) => m[2])));

  const evaluate = (code, name) => {
    const module = { exports: {} };
    const slot = '__nthModule' + Math.random().toString(36).slice(2);
    window[slot] = { module, require: window.__nthLibs.require, ran: false };
    const script = document.createElement('script');
    script.textContent = '(function (module, exports, require) {\n' + code + '\n;window["' + slot + '"].ran = true;\n})(window["' + slot + '"].module, window["' + slot + '"].module.exports, window["' + slot + '"].require);\n//# sourceURL=' + name;
    document.head.appendChild(script);
    const ran = window[slot].ran;
    delete window[slot];
    return ran ? module.exports : null;
  };

  const boundary = (React) => class NthBoundary extends React.Component {
    constructor(props) { super(props); this.state = { error: null }; }
    static getDerivedStateFromError(error) { return { error }; }
    componentDidCatch(error) { raise(error); }
    render() {
      if (!this.state.error) return this.props.children;
      return React.createElement('pre', { style: { margin: '16px', whiteSpace: 'pre-wrap', font: '13px/1.5 ui-monospace, Consolas, monospace', color: '#b42318' } },
        String(this.state.error && this.state.error.message || this.state.error));
    }
  };

  const run = async () => {
    const code = compile(source.code, source.file || 'App.jsx');
    const missing = await window.__nthLibs.prepare(['react', 'react-dom', ...requiredBy(code)]);
    if (missing.length) throw new Error('This artifact imports ' + missing.map((m) => '"' + m + '"').join(', ') + ', which nth does not have offline.');
    const exported = evaluate(code, source.file || 'App.jsx');
    if (!exported) return; // the page's error handler already has the error
    const Component = typeof exported.default === 'function' || (exported.default && typeof exported.default === 'object') ? exported.default
      : Object.values(exported).find((value) => typeof value === 'function');
    if (!Component) throw new Error('This artifact exports no component. Export one with "export default".');
    const React = window.React;
    const Boundary = boundary(React);
    window.__nthReactRoot = window.__nthReactRoot || window.ReactDOM.createRoot(root);
    window.__nthReactRoot.render(React.createElement(Boundary, null, React.createElement(Component)));
  };

  run().catch(raise);
})();
