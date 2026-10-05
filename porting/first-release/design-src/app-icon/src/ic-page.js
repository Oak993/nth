// The app-icon decision page: builds the comparison and one card per registered direction, draws every canvas once it
// comes near the viewport, runs the launch and idle animations on one clock, and keeps the pick. Sends nothing.
(function () {
  const Kit = window.Kit, M = window.MarrowIcons;
  const mods = M.all();
  const DPR = () => Math.min(3, Math.max(1, window.devicePixelRatio || 1));
  const rmq = window.matchMedia ? matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };
  let motion = Kit.store.get('icon.motion', '') || (rmq.matches ? 'reduced' : 'full');
  if (motion !== 'full' && motion !== 'reduced') motion = 'full';
  const theme = () => (Kit.isDark() ? 'dark' : 'light');
  function el(tag, cls, html) { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; }
  function txt(tag, cls, t) { const e = el(tag, cls); e.textContent = t; return e; }

  // ------------------------------------------------------------------ canvases, drawn when they come near the viewport
  const reg = [];
  // a still canvas: size is the design size; css its CSS size; th 'dark' | 'light' | 'page'; true1: one canvas pixel per
  // Windows pixel (the honest small sizes), otherwise drawn at the screen's density
  function cv(m, size, th, css, true1) {
    const c = el('canvas', 'cv' + (true1 ? ' px' : ''));
    c.width = c.height = true1 ? size : Math.round(css * DPR());
    c.style.width = c.style.height = css + 'px';
    c.setAttribute('aria-hidden', 'true');
    const e = { c, m, size, th, css, true1, done: false };
    reg.push(e); c.__e = e;
    return c;
  }
  function draw(e) {
    const th = e.th === 'page' ? theme() : e.th;
    try { M.render(e.c, e.m, e.size, th, null, e.true1 ? 1 : (e.css * DPR()) / e.size); } catch (err) { console.error(e.m.letter + ' ' + e.size + ': ' + err.message); }
    e.done = true; e.c.classList.add('on');
  }
  const io = 'IntersectionObserver' in window ? new IntersectionObserver((ents) => {
    ents.forEach((en) => {
      if (!en.isIntersecting) return;
      io.unobserve(en.target);
      en.target.querySelectorAll('canvas.cv').forEach((c) => { if (c.__e && !c.__e.done) draw(c.__e); });
      (en.target.__stages || []).forEach((s) => s.drawStill());
    });
  }, { rootMargin: '700px 0px' }) : null;
  function lazy(node) { if (io) io.observe(node); else requestAnimationFrame(() => { node.querySelectorAll('canvas.cv').forEach((c) => c.__e && draw(c.__e)); (node.__stages || []).forEach((s) => s.drawStill()); }); }

  // ------------------------------------------------------------------ the clock for every animation
  const running = new Set();
  let rafOn = false;
  function frame(now) {
    running.forEach((a) => { try { a.step(now); } catch (err) { console.error(err.message); running.delete(a); } });
    if (running.size) requestAnimationFrame(frame); else rafOn = false;
  }
  function run(a) { running.add(a); if (!rafOn) { rafOn = true; requestAnimationFrame(frame); } }

  // a stage: a still canvas with a live canvas over it; the launch ends on the still, the idle fades back to it
  const stages = [];
  function Stage(m, size, host) {
    const s = { m, size, host, idleOn: false, onIdle: null };
    s.still = el('canvas', 'still'); s.live = el('canvas', 'live');
    s.still.setAttribute('aria-hidden', 'true'); s.live.setAttribute('aria-hidden', 'true');
    host.append(s.still, s.live);
    s.dpr = () => ((host.clientWidth || 300) * DPR()) / size;
    s.drawStill = () => { M.render(s.still, m, size, theme(), null, s.dpr()); s.drawn = true; };
    let cur = null;
    function stopAnim() { if (cur) { running.delete(cur); cur = null; } }
    s.launch = () => {
      s.idle(false, true);
      stopAnim();
      if (!s.drawn) s.drawStill();
      if (motion === 'reduced') {
        // Reduce Motion: the still icon fades in, nothing moves
        s.live.style.opacity = '0';
        s.still.style.transition = 'none'; s.still.style.opacity = '0'; void s.still.offsetWidth;
        s.still.style.transition = 'opacity .6s ease'; s.still.style.opacity = '1';
        return;
      }
      const dpr = s.dpr(), t0 = performance.now(), dur = m.launch.ms;
      M.render(s.live, m, size, theme(), { launch: 0, idle: 0 }, dpr);
      s.live.classList.remove('fade'); s.live.style.opacity = '1';
      s.still.style.transition = 'opacity .2s ease'; s.still.style.opacity = '0';
      const a = { step(now) {
        const p = Math.min(1, (now - t0) / dur);
        M.render(s.live, m, size, theme(), { launch: p, idle: 0 }, dpr);
        if (p >= 1) {
          running.delete(a); cur = null;
          // the last frame is the still icon, so the swap is invisible
          s.still.style.transition = 'none'; s.still.style.opacity = '1';
          s.live.style.opacity = '0';
        }
      } };
      cur = a; run(a);
    };
    s.idle = (on, quiet) => {
      if (on && (!m.idle || motion === 'reduced')) on = false;
      if (on === s.idleOn) return;
      s.idleOn = on;
      if (s.onIdle && !quiet) s.onIdle(on);
      if (on) {
        stopAnim();
        if (!s.drawn) s.drawStill();
        s.still.style.transition = 'none'; s.still.style.opacity = '1';
        const dpr = s.dpr(), t0 = performance.now();
        M.render(s.live, m, size, theme(), { launch: 1, idle: 0 }, dpr);
        s.live.classList.remove('fade'); s.live.style.opacity = '1';
        const a = { step(now) { M.render(s.live, m, size, theme(), { launch: 1, idle: (now - t0) / 1000 }, dpr); } };
        cur = a; run(a);
      } else {
        // back to the still: the loop fades out over it (no jump to frame 0)
        const a = cur;
        s.live.classList.add('fade'); s.live.style.opacity = '0';
        setTimeout(() => { if (cur === a) stopAnim(); }, 480);
      }
      if (s.onIdle && quiet) s.onIdle(on);
    };
    stages.push(s);
    (host.closest('[data-lazy]').__stages = host.closest('[data-lazy]').__stages || []).push(s);
    return s;
  }

  // ------------------------------------------------------------------ Windows 11 contexts
  const SEARCH = '<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="7" cy="7" r="4.6" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M10.4 10.4 14 14" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>';
  const ARROW = '<svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3 9 8.2 3.8M4.6 3.6h3.8v3.8" fill="none" stroke="#1a5fd0" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  function taskbar(m, th) {
    const t = el('div', 'task ' + th);
    t.setAttribute('role', 'img'); t.setAttribute('aria-label', 'Windows 11 taskbar, ' + th + ', with Marrow running');
    const slot = (cls, inner) => { const s = el('div', 'slot ' + (cls || '')); if (inner) s.appendChild(inner); else s.appendChild(el('i')); return s; };
    t.appendChild(slot('x'));
    const sr = el('div', 'srch', SEARCH + '<span>Search</span>'); t.appendChild(sr);
    t.appendChild(slot('x'));
    const o1 = slot(''); o1.appendChild(el('span', 'dot')); t.appendChild(o1);
    t.appendChild(slot('me', cv(m, 24, th, 24, false)));
    t.appendChild(slot(''));
    const o2 = slot('x'); o2.appendChild(el('span', 'dot')); t.appendChild(o2);
    t.appendChild(el('div', 'clock', '<span>10:42</span><span>05/10/2026</span>'));
    return t;
  }
  function startMenu(m) {
    const s = el('div', 'start'); s.setAttribute('role', 'img'); s.setAttribute('aria-label', 'Start menu, pinned apps, with Marrow');
    s.appendChild(el('div', 'sh', '<span>Pinned</span><span>All &#8250;</span>'));
    const g = el('div', 'gr');
    ['Files', 'Marrow', 'Settings', 'Photos', 'Notes', 'Terminal'].forEach((n) => {
      const ti = el('div', 'ti' + (n === 'Marrow' ? ' me' : ''));
      ti.appendChild(n === 'Marrow' ? cv(m, 32, 'page', 32, false) : el('i'));
      ti.appendChild(txt('span', '', n)); g.appendChild(ti);
    });
    s.appendChild(g); return s;
  }
  function altTab(m) {
    const a = el('div', 'alt'); a.setAttribute('role', 'img'); a.setAttribute('aria-label', 'Alt-Tab switcher with the Marrow window selected');
    const c1 = el('div', 'card me'), t1 = el('div', 'tt'); t1.appendChild(cv(m, 16, 'page', 16, false)); t1.appendChild(txt('span', '', 'Marrow'));
    c1.append(t1, el('div', 'win', '<div class="sb"><b></b><b></b><b></b><b style="width:70%"></b></div><div class="mn"><b class="u"></b><b class="a"></b><b class="a" style="width:60%"></b><b class="c"></b></div>'));
    const c2 = el('div', 'card other'), t2 = el('div', 'tt', '<i></i>'); t2.appendChild(txt('span', '', 'Files'));
    c2.append(t2, el('div', 'win blank', '<div class="sb"><b></b><b></b><b></b></div><div class="mn"><b style="width:90%"></b><b style="width:80%"></b><b style="width:85%"></b></div>'));
    a.append(c1, c2); return a;
  }
  function desktop(m) {
    const d = el('div', 'desk'); d.setAttribute('role', 'img'); d.setAttribute('aria-label', 'Desktop shortcut on the Dichroic wallpaper');
    d.append(el('div', 'wl d'), el('div', 'wl l'));
    const ic = el('div', 'icons');
    const other = el('div', 'dicon', '<div class="iw"><i></i></div><span>Files</span>');
    const me = el('div', 'dicon me'), iw = el('div', 'iw');
    iw.appendChild(cv(m, 48, 'page', 48, false)); iw.appendChild(el('span', 'arw', ARROW));
    me.append(iw, txt('span', '', 'Marrow'));
    ic.append(other, me); d.appendChild(ic); return d;
  }

  // ------------------------------------------------------------------ the pick
  let pick = Kit.store.get('icon.pick', '');
  if (pick && !M.get(pick)) pick = '';
  const CK = '<svg viewBox="0 0 12 12"><path d="M2.5 6.3 5 8.6l4.6-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  function pickBtn(m) {
    const b = el('button', 'dk-pick', '<span class="ck">' + CK + '</span><span class="lb"><span class="a"></span><span class="b">Picked</span></span>');
    b.type = 'button'; b.dataset.l = m.letter; b.setAttribute('aria-pressed', 'false');
    b.setAttribute('aria-label', 'Pick ' + m.letter + ', ' + m.name);
    b.querySelector('.lb .a').textContent = 'Pick ' + m.letter;
    b.addEventListener('click', () => { pick = pick === m.letter ? '' : m.letter; Kit.store.set('icon.pick', pick); showPick(); });
    return b;
  }
  function line() { return 'app-icon: ' + (pick || '?'); }
  function showPick() {
    document.querySelectorAll('.dk-pick[data-l]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.l === pick)));
    document.querySelectorAll('.t256').forEach((t) => t.classList.toggle('picked', t.dataset.l === pick));
    const code = document.getElementById('pline'), nm = document.getElementById('pname');
    if (code) { code.textContent = line(); code.classList.toggle('on', !!pick); }
    if (nm) nm.textContent = pick ? M.get(pick).name + ': ' + M.get(pick).meaning : 'Pick one direction below.';
  }

  // ------------------------------------------------------------------ build
  const root = document.getElementById('cards');
  const cmpHost = document.getElementById('cmp');
  function compare() {
    const box = el('div', ''); box.setAttribute('data-lazy', '');
    box.appendChild(txt('p', 'lab', 'At 32 px, true Windows pixels'));
    const c32 = el('div', 'c32');
    ['dark', 'light'].forEach((th) => {
      const s = el('div', 'strip ' + th);
      mods.forEach((m) => {
        const it = el('div', 'it'), b = el('button');
        b.type = 'button'; b.setAttribute('aria-label', m.letter + ', ' + m.name + ': go to its card');
        b.appendChild(cv(m, 32, th, 32, true)); b.addEventListener('click', () => goTo(m.letter));
        it.append(b, txt('span', '', m.letter)); s.appendChild(it);
      });
      c32.appendChild(s);
    });
    box.appendChild(c32);
    const hb = el('div', 'hctl'); hb.style.margin = '14px 0 8px';
    hb.appendChild(txt('p', 'lab', 'At 256 px')).style.margin = '0';
    const play = el('button', 'dk-btn pri', '<svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.5 2.2 9.8 6 3.5 9.8Z" fill="currentColor"/></svg><span>Play every launch</span>');
    play.type = 'button'; hb.appendChild(play); box.appendChild(hb);
    const grid = el('div', 'c256'), list = [];
    mods.forEach((m) => {
      const t = el('button', 't256'); t.type = 'button'; t.dataset.l = m.letter;
      t.setAttribute('aria-label', m.letter + ', ' + m.name + ': go to its card');
      const w = el('div', 'cvw'); t.appendChild(w);
      t.appendChild(el('span', 'nm', '<b>' + m.letter + '</b>' + m.name.replace(/[<&]/g, '')));
      t.addEventListener('click', () => goTo(m.letter));
      grid.appendChild(t);
      list.push([m, w]);
    });
    box.appendChild(grid);
    cmpHost.appendChild(box);
    const st = list.map(([m, w]) => Stage(m, 256, w));
    play.addEventListener('click', () => st.forEach((s) => s.launch()));
    lazy(box);
  }
  function goTo(l) { const c = document.getElementById('ic-' + l); if (c) c.scrollIntoView({ behavior: motion === 'reduced' ? 'auto' : 'smooth', block: 'start' }); }

  function card(m) {
    const sec = el('section', 'ic dk-q dk-glass'); sec.id = 'ic-' + m.letter; sec.setAttribute('aria-labelledby', 'h-' + m.letter); sec.setAttribute('data-lazy', '');
    const ih = el('div', 'ih');
    ih.appendChild(txt('span', 'dk-qn', m.letter));
    const h = txt('h2', '', m.name); h.id = 'h-' + m.letter; ih.appendChild(h);
    ih.appendChild(pickBtn(m));
    sec.appendChild(ih);
    sec.appendChild(txt('p', 'mean', m.meaning));
    sec.appendChild(txt('p', 'story', m.story));
    const ib = el('div', 'ib');
    // the hero and its animations
    const left = el('div', '');
    const hero = el('div', 'hero'); left.appendChild(hero); ib.appendChild(left); sec.appendChild(ib);
    const s = Stage(m, 512, hero);
    const hc = el('div', 'hctl');
    const bL = el('button', 'dk-btn pri', '<svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3.5 2.2 9.8 6 3.5 9.8Z" fill="currentColor"/></svg><span>Replay launch</span>'); bL.type = 'button';
    const bI = el('button', 'dk-btn', '<span>Idle loop</span>'); bI.type = 'button'; bI.setAttribute('aria-pressed', 'false');
    bL.addEventListener('click', () => s.launch());
    bI.addEventListener('click', () => s.idle(!s.idleOn));
    s.onIdle = (on) => bI.setAttribute('aria-pressed', String(on));
    s.bI = bI;
    hc.append(bL, bI); left.appendChild(hc);
    const an = el('div', 'anim');
    const l1 = el('div', ''); l1.appendChild(txt('b', '', 'Launch, ' + (m.launch.ms / 1000).toFixed(2).replace(/0$/, '') + ' s. ')); l1.appendChild(document.createTextNode(m.launch.note));
    const l2 = el('div', ''); l2.appendChild(txt('b', '', m.idle ? 'Idle, a ' + (m.idle.ms / 1000).toFixed(1).replace(/\.0$/, '') + ' s loop. ' : 'Idle: none. ')); l2.appendChild(document.createTextNode(m.idle ? m.idle.note : ''));
    an.append(l1, l2); left.appendChild(an);
    // the sizes, the loupes, the contexts
    const col = el('div', 'col');
    const sz = el('div', '');
    sz.appendChild(txt('p', 'lab', 'Windows sizes: 16, 24, 32 and 48 at true pixels, 256 at screen density'));
    const sizes = el('div', 'sizes');
    ['dark', 'light'].forEach((th) => {
      const r = el('div', 'srow ' + th);
      [16, 24, 32, 48, 256].forEach((n) => { const c = el('div', 'sc'); c.appendChild(cv(m, n, th, n, n <= 48)); c.appendChild(txt('span', '', n + '')); r.appendChild(c); });
      sizes.appendChild(r);
    });
    sz.appendChild(sizes); col.appendChild(sz);
    const lp = el('div', 'lpw');
    lp.appendChild(txt('p', 'lab', '16 and 24 px exactly as Windows draws them at 100%, enlarged 5x'));
    const lps = el('div', 'loupes');
    [16, 24].forEach((n) => ['dark', 'light'].forEach((th) => {
      const w = el('div', 'lp'), lw = el('div', 'lw ' + th);
      lw.appendChild(cv(m, n, th, n * 5, false));
      const c = lw.firstChild; c.width = c.height = n; c.__e.true1 = true; c.classList.add('px');
      w.append(lw, txt('span', '', n + ' px, ' + th)); lps.appendChild(w);
    }));
    lp.appendChild(lps); left.appendChild(lp);
    const cx = el('div', '');
    cx.appendChild(txt('p', 'lab', 'In Windows 11: the taskbar in both themes; Start, Alt-Tab and the desktop follow the page theme'));
    const ctx = el('div', 'ctx');
    const tk = el('div', 'cx wide'); tk.append(taskbar(m, 'dark'), taskbar(m, 'light')); tk.style.gap = '6px'; ctx.appendChild(tk);
    const c1 = el('div', 'cx'); c1.append(txt('p', 'lab', 'Start, pinned'), startMenu(m)); ctx.appendChild(c1);
    const c2 = el('div', 'cx'); c2.append(txt('p', 'lab', 'Alt-Tab'), altTab(m)); ctx.appendChild(c2);
    const c3 = el('div', 'cx'); c3.append(txt('p', 'lab', 'Desktop shortcut, Dichroic wallpaper'), desktop(m)); ctx.appendChild(c3);
    cx.appendChild(ctx); col.appendChild(cx);
    ib.appendChild(col);
    root.appendChild(sec);
    lazy(sec);
    // a card that leaves the screen stops its idle loop
    if (io) new IntersectionObserver((e) => { if (!e[0].isIntersecting && s.idleOn) s.idle(false); }).observe(hero);
  }

  // the links to every card
  const links = document.getElementById('links');
  mods.forEach((m) => { const a = el('a', ''); a.href = '#ic-' + m.letter; a.textContent = m.letter; a.title = m.name; links.appendChild(a); });
  compare();
  mods.forEach(card);
  if (mods.length < 20) root.appendChild(txt('div', 'empty', (20 - mods.length) + ' of the twenty directions are still being drawn; they join this page when they are ready.'));

  // ------------------------------------------------------------------ the toolbar and the bar
  Kit.seg(document.getElementById('segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], Kit.themeMode(), (v) => Kit.theme(v));
  Kit.seg(document.getElementById('segM'), [{ v: 'full', t: 'Full' }, { v: 'reduced', t: 'Reduced' }], motion, (v) => {
    motion = v; Kit.store.set('icon.motion', v);
    stages.forEach((s) => { if (v === 'reduced' && s.idleOn) s.idle(false); if (s.bI) { s.bI.disabled = v === 'reduced' || !s.m.idle; s.bI.title = v === 'reduced' ? 'Off under Reduce Motion' : ''; } });
  });
  stages.forEach((s) => { if (s.bI) { s.bI.disabled = motion === 'reduced' || !s.m.idle; } });
  if (rmq.addEventListener) rmq.addEventListener('change', () => { /* the viewer's own choice on the page stays */ });
  Kit.onTheme(() => {
    reg.forEach((e) => { if (e.done && e.th === 'page') draw(e); });
    stages.forEach((s) => { if (s.drawn) s.drawStill(); });
  });
  let rz = 0;
  window.addEventListener('resize', () => { cancelAnimationFrame(rz); rz = requestAnimationFrame(() => stages.forEach((s) => { if (s.drawn) s.drawStill(); })); });
  document.getElementById('copy').addEventListener('click', async (ev) => {
    const b = ev.currentTarget, t = line();
    try { await navigator.clipboard.writeText(t); b.textContent = 'Copied'; }
    catch (e) { b.textContent = 'Select it'; const r = document.createRange(); r.selectNodeContents(document.getElementById('pline')); const sel = getSelection(); sel.removeAllRanges(); sel.addRange(r); }
    setTimeout(() => { b.textContent = 'Copy'; }, 1600);
  });
  document.getElementById('clear').addEventListener('click', () => { pick = ''; Kit.store.set('icon.pick', ''); showPick(); });
  showPick();
  window.__icons = { stages, reg, draw, mods: () => mods.map((m) => m.letter), setMotion: (v) => { motion = v; } };
})();
