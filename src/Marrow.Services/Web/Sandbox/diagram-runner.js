// nth hidden diagram runner (DESIGN.md 7.2 "Diagrams (Mermaid) ... rendered to SVG once per completed statement by the vendored
// Mermaid library inside a hidden, sandboxed artifacts-profile view (no network, no bridge)"; ED-NEW-web-w16c-1). The page holds the
// vendored Mermaid and this file, nothing else: no bridge, no storage, no address to reach. nth asks for a drawing through the
// DevTools Protocol (Runtime.evaluate of nthRenderDiagram) and reads back a plain object; the page never calls out.
//
// Mermaid draws with a stylesheet (classes and a <style> block) and, for some diagrams, HTML inside foreignObject. nth draws the
// SVG natively from shapes, paths, text and gradients only, so before the SVG leaves the page every style that applies to a
// shape or text is written into the element as an attribute (read from getComputedStyle, which applies the cascade exactly as
// the browser draws it), every foreignObject label becomes plain text, and the style block, classes, scripts and foreign objects
// are removed. nth then sanitises the result again on its own side; nothing here is trusted.
(() => {
  'use strict';
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const MAX_SVG = 3900000;
  let counter = 0;

  const PAINT = ['fill', 'fill-opacity', 'fill-rule', 'stroke', 'stroke-width', 'stroke-opacity', 'stroke-dasharray', 'stroke-dashoffset',
    'stroke-linecap', 'stroke-linejoin', 'stroke-miterlimit'];
  const TEXT = ['font-family', 'font-size', 'font-weight', 'font-style', 'text-anchor', 'dominant-baseline', 'letter-spacing', 'text-decoration-line'];
  const MARKERS = ['marker-start', 'marker-mid', 'marker-end'];
  const SHAPES = new Set(['rect', 'circle', 'ellipse', 'line', 'polyline', 'polygon', 'path', 'text', 'tspan']);
  const LINES = new Set(['path', 'line', 'polyline', 'polygon']);

  const holder = document.createElement('div');
  holder.setAttribute('aria-hidden', 'true');
  // Off screen but laid out and visible to the style engine (a hidden element would write visibility into every shape).
  holder.style.cssText = 'position:fixed;left:-100000px;top:0;width:2000px;height:2000px;overflow:hidden;pointer-events:none';
  document.body.appendChild(holder);

  const number = (value) => {
    const n = parseFloat(value);
    return Number.isFinite(n) ? n : 0;
  };

  const message = (error) => {
    const text = error && (error.str || error.message) ? String(error.str || error.message) : String(error);
    return text.length > 600 ? text.slice(0, 600) : text;
  };

  const lineOf = (error) => {
    const text = error && error.hash && Number.isFinite(error.hash.line) ? 'line ' + (error.hash.line + 1) : message(error);
    const found = /line\s+(\d+)/i.exec(text);
    return found ? parseInt(found[1], 10) : null;
  };

  // A foreignObject label as plain text lines with the style that drew it.
  const labelOf = (fo) => {
    const probe = fo.querySelector('span,p,div,li') || fo;
    const style = getComputedStyle(probe);
    const raw = (fo.innerText || fo.textContent || '');
    const lines = raw.split(/\n+/).map((line) => line.replace(/\s+/g, ' ').trim()).filter((line) => line.length > 0);
    return {
      node: fo,
      x: number(fo.getAttribute('x')),
      y: number(fo.getAttribute('y')),
      width: number(fo.getAttribute('width')),
      height: number(fo.getAttribute('height')),
      lines,
      fill: style.color,
      family: style.fontFamily,
      size: style.fontSize,
      weight: style.fontWeight,
      align: style.textAlign,
    };
  };

  // Phase 1 reads every computed style while the stylesheet and the classes are still in place; phase 2 writes the attributes
  // and removes what nth does not draw. Writing while reading would change the cascade for the elements not yet read.
  const flatten = (svg) => {
    const reads = [];
    const labels = [];
    for (const el of svg.querySelectorAll('*')) {
      const tag = el.localName;
      if (tag === 'foreignObject') {
        labels.push(labelOf(el));
        continue;
      }
      const style = getComputedStyle(el);
      const read = { el, tag, set: [] };
      // Definitions (defs, marker, gradients, symbol) are never drawn where they stand and say nothing about display.
      if (style.display === 'none' && (tag === 'g' || tag === 'svg' || SHAPES.has(tag))) read.set.push(['display', 'none']);
      if (tag === 'g' || tag === 'svg' || tag === 'symbol' || tag === 'marker') {
        if (style.opacity !== '1') read.set.push(['opacity', style.opacity]);
        if (style.visibility === 'hidden') read.set.push(['visibility', 'hidden']);
      } else if (SHAPES.has(tag)) {
        for (const name of PAINT) read.set.push([name, style.getPropertyValue(name)]);
        if (style.opacity !== '1') read.set.push(['opacity', style.opacity]);
        if (style.visibility === 'hidden') read.set.push(['visibility', 'hidden']);
        if (LINES.has(tag)) for (const name of MARKERS) read.set.push([name, style.getPropertyValue(name)]);
        if (tag === 'text' || tag === 'tspan') for (const name of TEXT) read.set.push([name, style.getPropertyValue(name)]);
      } else if (tag === 'stop') {
        read.set.push(['stop-color', style.getPropertyValue('stop-color')], ['stop-opacity', style.getPropertyValue('stop-opacity')]);
      }
      reads.push(read);
    }

    for (const { el, set } of reads) {
      for (const [name, value] of set) {
        if (value !== '' && value != null) el.setAttribute(name, value);
      }
    }
    for (const label of labels) {
      const parent = label.node.parentNode;
      if (!parent) continue;
      const anchor = label.align === 'center' ? 'middle' : label.align === 'right' || label.align === 'end' ? 'end' : 'start';
      const x = anchor === 'middle' ? label.x + label.width / 2 : anchor === 'end' ? label.x + label.width : label.x;
      const size = number(label.size) || 14;
      const lineHeight = size * 1.2;
      const first = label.y + label.height / 2 - ((label.lines.length - 1) * lineHeight) / 2;
      label.lines.forEach((line, index) => {
        const text = document.createElementNS(SVG_NS, 'text');
        text.setAttribute('x', String(x));
        text.setAttribute('y', String(first + index * lineHeight));
        text.setAttribute('text-anchor', anchor);
        text.setAttribute('dominant-baseline', 'central');
        text.setAttribute('fill', label.fill);
        text.setAttribute('font-family', label.family);
        text.setAttribute('font-size', label.size);
        text.setAttribute('font-weight', label.weight);
        text.textContent = line;
        parent.insertBefore(text, label.node);
      });
      label.node.remove();
    }
    for (const el of svg.querySelectorAll('style,script,link,meta')) el.remove();
    for (const el of [svg, ...svg.querySelectorAll('*')]) {
      el.removeAttribute('class');
      el.removeAttribute('style');
      for (const attribute of [...el.attributes]) {
        if (/^on/i.test(attribute.name)) el.removeAttribute(attribute.name);
      }
    }
  };

  const draw = async (source, options) => {
    const mermaid = window.mermaid;
    if (!mermaid) return { ok: false, kind: 'unavailable', error: 'Mermaid is not loaded.' };
    if (typeof source !== 'string' || source.length === 0) return { ok: false, kind: 'syntax', error: 'The diagram is empty.' };
    const o = options || {};
    try {
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        logLevel: 'fatal',
        suppressErrorRendering: true,
        theme: 'base',
        darkMode: !!o.dark,
        themeVariables: Object.assign({}, o.variables || {}),
        fontFamily: o.fontFamily,
        fontSize: o.fontSize,
        htmlLabels: false,
        maxTextSize: 262144,
        maxEdges: 600,
        deterministicIds: true,
        deterministicIDSeed: 'nth',
        flowchart: { htmlLabels: false, useMaxWidth: false, curve: 'basis', padding: 12 },
        sequence: { useMaxWidth: false, wrap: false },
        class: { htmlLabels: false, useMaxWidth: false },
        state: { useMaxWidth: false },
        er: { useMaxWidth: false },
        journey: { useMaxWidth: false },
        gantt: { useMaxWidth: false },
        pie: { useMaxWidth: false },
        timeline: { useMaxWidth: false },
        mindmap: { useMaxWidth: false },
        gitGraph: { useMaxWidth: false },
        requirement: { useMaxWidth: false },
        c4: { useMaxWidth: false },
        sankey: { useMaxWidth: false },
        xyChart: { useMaxWidth: false },
        quadrantChart: { useMaxWidth: false },
        block: { useMaxWidth: false },
        packet: { useMaxWidth: false },
        architecture: { useMaxWidth: false },
      });
    } catch (error) {
      return { ok: false, kind: 'unavailable', error: message(error) };
    }

    let rendered;
    try {
      rendered = await mermaid.render('nth-d' + (++counter), source);
    } catch (error) {
      return { ok: false, kind: 'syntax', error: message(error), line: lineOf(error) };
    }

    const cell = document.createElement('div');
    holder.appendChild(cell);
    try {
      cell.innerHTML = rendered.svg;
      const svg = cell.querySelector('svg');
      if (!svg) return { ok: false, kind: 'syntax', error: 'Mermaid drew nothing.' };
      flatten(svg);
      const text = new XMLSerializer().serializeToString(svg);
      if (text.length > MAX_SVG) return { ok: false, kind: 'toolarge', error: 'The drawing is larger than nth draws.' };
      return { ok: true, svg: text };
    } catch (error) {
      return { ok: false, kind: 'syntax', error: message(error) };
    } finally {
      cell.remove();
    }
  };

  // One drawing at a time inside the page too: Mermaid keeps its configuration globally, so a drawing nth gave up waiting for
  // must finish before the next one changes the theme under it.
  let chain = Promise.resolve();
  window.nthRenderDiagram = (source, options) => {
    const run = chain.then(() => draw(source, options));
    chain = run.catch(() => undefined);
    return run;
  };

  window.nthDiagramReady = typeof window.mermaid !== 'undefined';
})();
