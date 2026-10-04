// Customization board runtime: the canvas stand-in used by the published Composer Lights page (it re-renders on
// setState and patches the live DOM in place, so CSS transitions run as they would under the real runtime), with two
// changes: the component comes from window.__DCComponent only (no eval, no new Function), and the component's
// componentDidUpdate() runs after every patch (the segmented controls measure their buttons there).
(function () {
  const HTML_NS = 'http://www.w3.org/1999/xhtml';
  let inst = null, root = null, tpl = [], queued = false;
  class DCLogic {
    constructor(props) { this.props = props || {}; this.state = this.state || {}; }
    setState(p) { Object.assign(this.state, typeof p === 'function' ? p(this.state) : p); schedule(); }
    forceUpdate() { schedule(); }
  }
  window.DCLogic = DCLogic;
  const RX = /\{\{\s*([^}]+?)\s*\}\}/g;
  const resolve = (p, s) => p.trim().split('.').reduce((o, k) => (o == null ? undefined : o[k]), s);
  const single = (str) => { const m = /^\s*\{\{\s*([^}]+?)\s*\}\}\s*$/.exec(str || ''); return m ? m[1] : null; };
  const interp = (s, sc) => s.replace(RX, (_, p) => { const v = resolve(p, sc); return v == null ? '' : String(v); });

  function build(node, scope) {
    if (node.nodeType === 3) return [{ text: interp(node.nodeValue, scope) }];
    if (node.nodeType !== 1) return [];
    const tag = node.localName;
    if (tag === 'sc-for') {
      const list = resolve(single(node.getAttribute('list')), scope) || [];
      const as = node.getAttribute('as') || 'item';
      const out = [];
      list.forEach((it, i) => {
        const sc = Object.assign(Object.create(scope), { [as]: it, $index: i });
        for (const c of node.childNodes) out.push(...build(c, sc));
      });
      return out;
    }
    if (tag === 'sc-if') {
      if (!resolve(single(node.getAttribute('value')), scope)) return [];
      const out = [];
      for (const c of node.childNodes) out.push(...build(c, scope));
      return out;
    }
    const v = { tag, ns: node.namespaceURI, attrs: {}, events: {}, ref: null, children: [] };
    for (const a of node.attributes) {
      const n = a.name, s = single(a.value);
      if (n.indexOf('data-on') === 0) { v.events[n.slice(7)] = s ? resolve(s, scope) : null; continue; }
      if (n === 'data-ref') { v.ref = s ? resolve(s, scope) : null; continue; }
      if (s !== null) {
        const val = resolve(s, scope);
        if (val === false || val == null || typeof val === 'function') continue;
        v.attrs[n] = val === true ? 'true' : String(val);
      } else v.attrs[n] = interp(a.value, scope);
    }
    for (const c of node.childNodes) v.children.push(...build(c, scope));
    return [v];
  }

  function create(v) {
    if (v.text !== undefined) return document.createTextNode(v.text);
    const el = v.ns && v.ns !== HTML_NS ? document.createElementNS(v.ns, v.tag) : document.createElement(v.tag);
    el.__ev = {};
    patchEl(el, v);
    return el;
  }
  function patchEl(el, v) {
    const old = el.__attrs || {};
    for (const k in old) if (!(k in v.attrs)) el.removeAttribute(k);
    for (const k in v.attrs) {
      if (old[k] === v.attrs[k]) continue;
      if (k === 'value' && el.localName === 'input') { if (el.value !== v.attrs[k]) el.value = v.attrs[k]; }
      else el.setAttribute(k, v.attrs[k]);
    }
    el.__attrs = v.attrs;
    for (const t in el.__ev) if (!(t in v.events)) el.__ev[t] = null;
    for (const t in v.events) {
      if (!(t in el.__ev)) el.addEventListener(t, (e) => { const f = el.__ev[t]; if (f) f(e); });
      el.__ev[t] = v.events[t];
    }
    if (v.ref) v.ref(el);
    // data-keep: a host whose children belong to a library mounted into it (the voice glow), never patched here
    if (!(v.attrs && 'data-keep' in v.attrs)) patchChildren(el, v.children);
  }
  function patchChildren(parent, vs) {
    const kids = Array.from(parent.childNodes);
    for (let i = 0; i < vs.length; i++) {
      const v = vs[i], k = kids[i];
      if (!k) { parent.appendChild(create(v)); continue; }
      if (v.text !== undefined) {
        if (k.nodeType === 3) { if (k.nodeValue !== v.text) k.nodeValue = v.text; }
        else parent.replaceChild(create(v), k);
        continue;
      }
      if (k.nodeType === 1 && k.localName === v.tag) patchEl(k, v);
      else parent.replaceChild(create(v), k);
    }
    for (let i = kids.length - 1; i >= vs.length; i--) parent.removeChild(kids[i]);
  }
  function render() {
    queued = false;
    const scope = inst.renderVals();
    const vs = [];
    for (const n of tpl) vs.push(...build(n, scope));
    patchChildren(root, vs);
    window.__renders = (window.__renders || 0) + 1;
    if (inst.componentDidUpdate) inst.componentDidUpdate();
  }
  function schedule() { if (!queued && inst) { queued = true; queueMicrotask(render); } }

  function boot() {
    const src = document.querySelector('template#cz-tpl');
    const Component = window.__DCComponent;
    if (!src || !Component) return;
    tpl = Array.from(src.content.childNodes);
    root = document.getElementById('cz-root');
    let dark = true;
    try {
      const t = document.documentElement.getAttribute('data-theme');
      dark = t === 'dark' ? true : t === 'light' ? false : !window.matchMedia('(prefers-color-scheme: light)').matches;
    } catch (e) {}
    inst = new Component({ dark });
    render();
    if (inst.componentDidMount) inst.componentDidMount();
    window.__dcReady = true;
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
