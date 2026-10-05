// Welcome voice question, the interactive page: plays the whole first run on one frame clock: the locked Getting
// ready sheet (look I, W1, D3), its hand-over into WelcomeView, the voice question folding in at the chosen place, the
// answer (pressed by the clock, or by a click on the drawn buttons), the background download and speed test after Yes,
// and on a slow PC the one-time question. The window markup is the boards' own (wv_lib.py), cloned from a <template>
// per place; this script sets its numbers (--p, --h, --dg), states (data-st, data-vq, data-press) and words.
(function () {
  const D = JSON.parse(document.getElementById('wvdata').textContent);
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const K = window.Kit;
  const reduce = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const S = K.store;
  const cfg = { v: S.get('wv.v', D.rec.place), w: S.get('wv.w', D.rec.words), ans: S.get('wv.ans', 'yes'), mach: S.get('wv.mach', 'slow'), pick: S.get('wv.pick', 'acc'), speed: +S.get('wv.speed', '1') };
  if (!D.variants.some((x) => x[0] === cfg.v)) cfg.v = D.rec.place;
  if (!D.words[cfg.w]) cfg.w = D.rec.words;
  if (!['yes', 'no'].includes(cfg.ans)) cfg.ans = 'yes';
  if (!D.mach[cfg.mach]) cfg.mach = 'slow';
  if (!['live', 'acc'].includes(cfg.pick)) cfg.pick = 'acc';
  if (![0.5, 1, 2, 4].includes(cfg.speed)) cfg.speed = 1;
  const VAR = (k) => D.variants.find((x) => x[0] === k);

  // ---------------------------------------------------------------- the plan: when each thing happens
  const HAND = 1.1, HOLD = 0.15, QIN = 0.45, ASK = 2.8, DLV = 6.0, TESTV = 1.4, SLOWASK = 3.6, DONEAFTER = 3.4, PRESS = 0.28;
  let plan = null;
  function makePlan() {
    const P = D.profiles[cfg.mach].steps, at = {}; let t = 0;
    for (const s of D.screen) { at[s] = [t, t + P[s].d]; t += P[s].d; }
    const p = { at, workEnd: t };
    p.handStart = t + HOLD; p.welcome = p.handStart + HAND; p.qIn = p.welcome + QIN; p.tAns = p.qIn + ASK;
    const sheet = VAR(cfg.v)[4] === 'drop';
    if (cfg.ans === 'yes') {
      p.dl0 = p.tAns + 0.25; p.dl1 = p.dl0 + DLV; p.ok = p.dl1 + TESTV;
      if (D.mach[cfg.mach].live) p.ready = p.ok; else { p.slowIn = p.ok; p.slowAns = p.slowIn + SLOWASK; }
    }
    if (sheet) { p.done1 = p.tAns + DONEAFTER; if (p.slowAns) p.done2 = p.slowAns + DONEAFTER; }
    p.end = Math.max(p.tAns + 3.6, p.done1 ? p.done1 + 1.4 : 0, p.ok ? p.ok + 2.6 : 0, p.slowAns ? p.slowAns + 2.8 : 0, p.done2 ? p.done2 + 1.4 : 0);
    return p;
  }
  function vqState(tt) {
    const p = plan;
    if (tt < p.qIn) return ['none', ''];
    if (tt < p.tAns) return ['ask', tt > p.tAns - PRESS ? cfg.ans : ''];
    if (cfg.ans === 'no') {
      if (p.done1 && tt >= p.done1) return ['gone', ''];
      return ['no', p.done1 && tt > p.done1 - PRESS ? 'done' : ''];
    }
    if (p.slowIn != null && tt >= p.slowIn) {
      if (tt < p.slowAns) return ['slow', tt > p.slowAns - PRESS ? cfg.pick : ''];
      if (p.done2 && tt >= p.done2) return ['gone', ''];
      return ['slow' + cfg.pick, p.done2 && tt > p.done2 - PRESS ? 'done' : ''];
    }
    if (p.done1 && tt >= p.done1) return ['gone', ''];
    if (p.ready != null && tt >= p.ready) return ['ready', ''];
    return ['yes', p.done1 && tt > p.done1 - PRESS ? 'done' : ''];
  }
  const ease = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2);

  // ---------------------------------------------------------------- one question block: crossfade + eased height
  const vis = (s) => s !== 'none' && s !== 'gone';
  function setVq(root, st, instant) {
    const prev = root.dataset.vq;
    if (prev === st) return;
    const stk = $('.stk2', root), fold = $('.fold', root), hfold = root.dataset.fold === 'h';
    if (vis(st)) {
      const nOn = $('.ly[data-ly="' + st + '"]', stk), oOn = $('.ly.on', stk);
      if (nOn && nOn !== oOn) {
        if (!vis(prev) || instant) {
          stk.style.transition = 'none';
          $$('.ly', stk).forEach((l) => { l.style.transition = 'none'; });
          if (oOn) oOn.classList.remove('on');
          nOn.classList.add('on');
          stk.style.height = nOn.offsetHeight + 'px';
          void stk.offsetHeight;
          stk.style.transition = '';
          $$('.ly', stk).forEach((l) => { l.style.transition = ''; });
        } else {
          const h0 = stk.offsetHeight, f0 = $('.fin', root).offsetHeight;
          stk.style.height = h0 + 'px';
          void stk.offsetHeight;
          if (oOn) oOn.classList.remove('on');
          nOn.classList.add('on');
          const h1 = nOn.offsetHeight, dur = Math.min(0.62, Math.max(0.36, 0.3 + Math.abs(h1 - h0) / 700)).toFixed(2) + 's';
          stk.style.transitionDuration = dur;
          stk.style.height = h1 + 'px';
          // the fold keeps a pixel height and follows the new layer's height in step with the stack
          if (hfold && /px$/.test(fold.style.height)) {
            fold.style.transitionDuration = dur;
            fold.style.height = (f0 - h0 + h1) + 'px';
          }
        }
      }
    }
    if (hfold) {
      if (vis(st) && !vis(prev)) {
        const h = $('.fin', root).offsetHeight;
        if (instant) { fold.style.transition = 'none'; fold.style.height = 'auto'; void fold.offsetHeight; fold.style.transition = ''; }
        else {
          fold.style.transitionDuration = Math.min(0.62, Math.max(0.45, 0.3 + h / 700)).toFixed(2) + 's, 0.35s';
          fold.style.height = '0px'; void fold.offsetHeight; fold.style.height = h + 'px';
        }
      } else if (!vis(st) && vis(prev)) {
        if (instant) { fold.style.transition = 'none'; fold.style.height = '0px'; void fold.offsetHeight; fold.style.transition = ''; }
        else { fold.style.height = fold.offsetHeight + 'px'; void fold.offsetHeight; fold.style.height = '0px'; }
      }
    }
    root.dataset.vq = st;
  }

  // ---------------------------------------------------------------- words and sizes
  function applyWords(w) {
    const Wd = D.words[cfg.w];
    $$('[data-wk]', w).forEach((e) => { const h = Wd[e.dataset.wk]; if (h != null) e.innerHTML = h; });
    const M = D.mach[cfg.mach];
    $$('[data-mb="main"]', w).forEach((e) => { e.textContent = M.mb; });
    $$('[data-mb="small"]', w).forEach((e) => { e.textContent = M.small || '264 MB'; });
  }

  // ---------------------------------------------------------------- the stage: two slots, a change crossfades
  const stage = $('#stage'), slots = $('#slots');
  let win = null, hooks = null;
  function build() {
    const tpl = $('#tpl-' + cfg.v);
    const wrap = document.createElement('div');
    wrap.className = 'slot';
    wrap.appendChild(tpl.content.cloneNode(true));
    const w = wrap.firstElementChild;
    applyWords(w);
    const rows = {};
    D.steps.forEach((s) => { rows[s] = $$('[data-s="' + s + '"]', w); });
    const vq = $('.vq', w);
    if (vq.dataset.fold === 'h') $('.fold', vq).style.height = '0px';
    hooks = { rows, prov: $$('.prow', w), vq, mbt: $$('[data-mbt]', w), last: {} };
    return { wrap, w };
  }
  function swap() {
    plan = makePlan();
    const next = build(), old = win;
    win = next;
    next.wrap.style.opacity = '0';
    slots.appendChild(next.wrap);
    layout();
    frame(t, true);
    requestAnimationFrame(() => { next.wrap.style.opacity = '1'; if (old) old.wrap.style.opacity = '0'; });
    if (old) setTimeout(() => old.wrap.remove(), 420);
    drawStrip();
  }
  function layout() {
    const avail = stage.clientWidth - 2, s = Math.min(1, (avail - 48) / 960);
    slots.style.setProperty('--sc', s.toFixed(4));
    stage.style.height = Math.round(680 * s + 48 * s) + 'px';
  }
  if (window.ResizeObserver) new ResizeObserver(layout).observe(stage); else addEventListener('resize', layout);

  // ---------------------------------------------------------------- one frame
  let t = 0, playing = false, last = null;
  function stepState(s, tt) {
    const r = plan.at[s];
    if (!r) return ['skip', 0];
    if (tt < r[0]) return ['wait', 0];
    if (tt < r[1]) return ['run', (tt - r[0]) / (r[1] - r[0])];
    return ['done', 1];
  }
  function setAttr(el, k, v) { if (el.getAttribute(k) !== v) el.setAttribute(k, v); }
  function frame(tt, force) {
    if (!win) return;
    const w = win.w, H = hooks;
    let prog = 0;
    for (const s of D.steps) {
      const [st, sp] = stepState(s, tt);
      if (st === 'done') prog += 1; else if (st === 'run') prog += sp;
      const key = st + (st === 'run' ? Math.round(sp * 200) : '');
      if (H.last[s] === key && !force) continue;
      H.last[s] = key;
      for (const el of H.rows[s]) { setAttr(el, 'data-st', st); el.style.setProperty('--sp', sp.toFixed(3)); }
    }
    const p = prog / D.screen.length;
    const h = reduce ? (tt >= plan.handStart ? 1 : 0) : ease((tt - plan.handStart) / HAND);
    w.style.setProperty('--p', p.toFixed(4));
    w.style.setProperty('--h', h.toFixed(4));
    H.prov.forEach((r, i) => setAttr(r, 'data-st', tt >= plan.handStart + 0.7 + i * 0.13 ? 'done' : 'run'));
    const [vs, press] = vqState(tt);
    setVq(H.vq, vs, false);
    setAttr(H.vq, 'data-press', press);
    let g = 0;
    if (plan.dl0 != null) g = tt < plan.dl0 ? 0 : tt < plan.dl1 ? (tt - plan.dl0) / (plan.dl1 - plan.dl0) : 1;
    H.vq.style.setProperty('--dg', g.toFixed(3));
    const mb = parseInt(D.mach[cfg.mach].mb, 10);
    const txt = plan.dl1 != null && tt >= plan.dl1 ? 'Testing speed…' : Math.round(mb * g) + ' of ' + mb + ' MB';
    for (const e of H.mbt) if (e.textContent !== txt) e.textContent = txt;
    readout(tt, h, vs);
  }

  // ---------------------------------------------------------------- the readout strip
  const strip = $('#strip');
  const fmt = (s) => (s < 1 ? Math.round(s * 1000) + ' ms' : s.toFixed(1) + ' s');
  function phases() {
    const p = plan, M = D.mach[cfg.mach], out = [];
    out.push({ id: 'gr', t: 'Getting ready (I, W1)', d: fmt(p.workEnd) + (cfg.mach === 'pc' ? ' measured' : ' est.'), a: 0, b: p.workEnd });
    out.push({ id: 'hand', t: 'Hand-over', d: fmt(HAND), a: p.handStart, b: p.welcome });
    out.push({ id: 'ask', t: 'The question', d: 'waits for you', a: p.qIn - 0.3, b: p.tAns });
    if (cfg.ans === 'yes') {
      out.push({ id: 'dl', t: 'Download, behind Welcome', d: M.mb + ', real ' + M.dl_real, a: p.dl0, b: p.dl1 });
      out.push({ id: 'test', t: 'Speed test', d: M.test, a: p.dl1, b: p.ok });
      if (p.slowIn != null) out.push({ id: 'slow', t: 'Slow PC asks once', d: 'Q-V-1', a: p.slowIn - 0.05, b: p.slowAns });
      else out.push({ id: 'ready', t: 'Ready (fast PC)', d: 'never asked', a: p.ok - 0.05, b: p.ok + 0.6 });
    } else out.push({ id: 'no', t: 'No: nothing downloads', d: 'the orb starts it later', a: p.tAns - 0.05, b: p.tAns + 0.6 });
    return out;
  }
  function drawStrip() {
    strip.innerHTML = '';
    for (const it of phases()) {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'sc'; b.dataset.a = it.a; b.dataset.b = it.b;
      b.innerHTML = '<i></i><span class="n"></span><span class="d"></span>';
      b.querySelector('.n').textContent = it.t; b.querySelector('.d').textContent = it.d;
      b.addEventListener('click', () => play(Math.max(0, it.a - 0.02)));
      strip.appendChild(b);
    }
    $('#scrub').max = plan.end.toFixed(2);
    $('#mach').textContent = D.mach[cfg.mach].line;
  }
  function readout(tt, h, vs) {
    for (const b of strip.children) {
      const a = +b.dataset.a, z = +b.dataset.b, f = Math.min(1, Math.max(0, (tt - a) / Math.max(0.01, z - a)));
      b.style.setProperty('--f', f.toFixed(3));
      b.classList.toggle('on', f > 0 && f < 1);
    }
    const sc = $('#scrub');
    if (document.activeElement !== sc) sc.value = tt.toFixed(2);
    $('#clock').textContent = tt.toFixed(2) + ' s';
    $('#ph').textContent = tt < plan.workEnd ? 'getting ready' : h < 1 ? 'hand-over ' + Math.round(h * 100) + ' %' : 'Welcome · ' + vs;
  }

  // ---------------------------------------------------------------- the clock
  function tick(now) {
    if (!playing) return;
    if (now != null) {
      const dt = last == null ? 0 : Math.min(0.1, (now - last) / 1000);
      last = now;
      t = Math.min(plan.end, t + dt * cfg.speed);
    }
    frame(t);
    if (t >= plan.end) { playing = false; $('#playLab').textContent = 'Play again'; return; }
    requestAnimationFrame(tick);
  }
  function play(from) { t = from || 0; last = null; const was = playing; playing = true; $('#playLab').textContent = 'Playing…'; frame(t, true); if (!was) requestAnimationFrame(tick); }
  $('#play').addEventListener('click', () => play(0));
  $('#jumpQ').addEventListener('click', () => play(Math.max(0, plan.qIn - 0.5)));
  $('#scrub').addEventListener('input', (e) => { playing = false; t = +e.target.value; frame(t, true); $('#playLab').textContent = 'Play'; });

  // the drawn buttons work: an answer pressed in the window plays on from that moment
  stage.addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]');
    if (!b || !stage.contains(b)) return;
    const a = b.dataset.act;
    if (a === 'yes' || a === 'no') { cfg.ans = a; S.set('wv.ans', a); segAns.set(a); plan = makePlan(); drawStrip(); play(plan.tAns); }
    else if (a === 'live' || a === 'acc') { cfg.pick = a; S.set('wv.pick', a); segPick.set(a); if (plan.slowAns) play(plan.slowAns); }
    else if (a === 'done') { const target = plan.done2 && t > (plan.slowIn || 1e9) ? plan.done2 : plan.done1; if (target) play(target); }
  });

  // ---------------------------------------------------------------- the controls
  const V = D.variants;
  K.seg($('#segV'), V.map((x) => ({ v: x[0], t: x[0] + ' ' + x[1] })), cfg.v, (v) => { cfg.v = v; S.set('wv.v', v); lineV(); swap(); play(Math.max(0, plan.qIn - 0.5)); }, { wrap: true });
  K.seg($('#segW'), Object.keys(D.words).map((k) => ({ v: k, t: k + ' ' + D.words[k].name })), cfg.w, (v) => { cfg.w = v; S.set('wv.w', v); lineW(); swap(); play(Math.max(0, plan.qIn - 0.5)); cm.words(); }, { wrap: true });
  const segAns = K.seg($('#segA'), [{ v: 'yes', t: 'Yes' }, { v: 'no', t: 'No' }], cfg.ans, (v) => { cfg.ans = v; S.set('wv.ans', v); plan = makePlan(); drawStrip(); play(Math.max(0, plan.qIn - 0.5)); });
  K.seg($('#segM'), Object.keys(D.mach).map((k) => ({ v: k, t: D.mach[k].name })), cfg.mach, (v) => { cfg.mach = v; S.set('wv.mach', v); swap(); play(Math.max(0, plan.qIn - 0.5)); });
  const segPick = K.seg($('#segP'), [{ v: 'acc', t: 'After you stop' }, { v: 'live', t: 'Live words' }], cfg.pick, (v) => { cfg.pick = v; S.set('wv.pick', v); if (plan.slowIn != null && t >= plan.slowIn) play(plan.slowIn); });
  K.seg($('#segS'), [{ v: '0.5', t: '½x' }, { v: '1', t: '1x' }, { v: '2', t: '2x' }, { v: '4', t: '4x' }], String(cfg.speed), (v) => { cfg.speed = +v; S.set('wv.speed', v); });
  K.seg($('#segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], K.themeMode(), (v) => K.theme(v));
  function crossLine(el, text) {
    const sp = el.children, off = sp[0].classList.contains('on') ? sp[1] : sp[0], on = off === sp[0] ? sp[1] : sp[0];
    if (on.textContent === text) return;
    off.textContent = text; off.classList.add('on'); on.classList.remove('on');
  }
  function lineV() { const x = VAR(cfg.v); crossLine($('#lineV'), x[0] + ' ' + x[1] + ': ' + x[2]); pv.place.dataset.pickV = cfg.v; picks.render(); }
  function lineW() { const x = D.words[cfg.w]; crossLine($('#lineW'), cfg.w + ' ' + x.name + ': ' + x.line); pv.words.dataset.pickV = cfg.w; picks.render(); }
  const pv = {};
  [['place', '#pickV'], ['words', '#pickW']].forEach(([q, id]) => { pv[q] = K.pickButton(q, '', 'Pick this'); $(id).appendChild(pv[q]); });
  const picks = K.picks('welcome-voice', [{ id: 'place', label: 'Place' }, { id: 'words', label: 'Words' }]);

  // ---------------------------------------------------------------- the composer: what the orb shows later
  const cm = (function () {
    const host = $('#cmStage'), box = $('#cmSlots');
    let el = null, st = S.get('wv.cm', 'dl'), got = 0.36, raf = null, lastT = null;
    if (!D.cmStates.some((x) => x[0] === st)) st = 'dl';
    function fit() {
      const s = Math.min(1, (host.clientWidth - 24) / 720);
      box.style.setProperty('--cs', s.toFixed(4));
      host.style.height = Math.round(430 * s + 24 * s) + 'px';
    }
    function mount() {
      const tpl = $('#tpl-cm');
      const wrap = document.createElement('div'); wrap.className = 'slot';
      wrap.appendChild(tpl.content.cloneNode(true));
      const w = wrap.firstElementChild;
      applyWords(w);
      $$('[data-mb="main"]', w).forEach((e) => { e.textContent = D.mach.slow.mb; });
      $$('[data-mb="small"]', w).forEach((e) => { e.textContent = D.mach.slow.small; });
      const old = el; el = w;
      wrap.style.opacity = '0'; box.appendChild(wrap);
      set(st, true);
      requestAnimationFrame(() => { wrap.style.opacity = '1'; if (old) old.parentElement.style.opacity = '0'; });
      if (old) setTimeout(() => old.parentElement.remove(), 420);
    }
    function set(v, instant) {
      st = v;
      el.dataset.cm = v;
      const pill = $('.cm-pill', el), pls = $('.pls', el);
      const key = v === 'nodl' ? 'dl' : v === 'ask' ? 'none' : v;
      const nOn = $('.pl[data-pl="' + key + '"]', pls), oOn = $('.pl.on', pls);
      if (nOn !== oOn) {
        pill.style.width = pill.offsetWidth + 'px';
        if (instant) pill.style.transition = 'none';
        void pill.offsetWidth;
        if (oOn) oOn.classList.remove('on');
        nOn.classList.add('on');
        pill.style.width = nOn.offsetWidth + 'px';
        if (instant) { void pill.offsetWidth; pill.style.transition = ''; }
      }
      pill.classList.toggle('open', v !== 'ask');
      $('.cm-pop', el).classList.toggle('open', v === 'ask');
      if (v === 'nodl') got = 0.0; else if (v === 'dl') got = 0.36;
      if ((v === 'dl' || v === 'nodl') && !raf) { lastT = null; raf = requestAnimationFrame(run); }
      draw();
    }
    function draw() {
      const mb = parseInt(D.mach.pc.mb, 10);
      el.style.setProperty('--dg', got.toFixed(3));
      const m = $('[data-mbt]', el); if (m) m.textContent = Math.round(mb * got) + ' of ' + mb + ' MB';
    }
    function run(now) {
      if (st !== 'dl' && st !== 'nodl') { raf = null; return; }
      if (lastT != null) got = Math.min(1, got + (now - lastT) / 1000 / 40);
      lastT = now; draw();
      raf = requestAnimationFrame(run);
    }
    const seg = K.seg($('#segC'), D.cmStates.map((x) => ({ v: x[0], t: x[1] })), st, (v) => { S.set('wv.cm', v); set(v); }, { wrap: true });
    host.addEventListener('click', (e) => { const b = e.target.closest('[data-act]'); if (b && (b.dataset.act === 'live' || b.dataset.act === 'acc')) { seg.set('listen'); S.set('wv.cm', 'listen'); set('listen'); } });
    if (window.ResizeObserver) new ResizeObserver(fit).observe(host);
    fit(); mount();
    return { words: mount };
  })();

  K.onTheme((dark) => { stage.classList.toggle('light', !dark); $('#cmStage').classList.toggle('light', !dark); });
  stage.classList.toggle('light', !K.isDark());
  $('#cmStage').classList.toggle('light', !K.isDark());

  plan = makePlan();
  lineV(); lineW();
  swap();
  // the first view: Welcome with the question already in place; Play runs the whole first run from the start
  t = plan.qIn + 1.2; frame(t, true);
  window.__wv = { cfg, get plan() { return plan; }, frame: (x) => { playing = false; t = x; frame(x, true); }, play, get t() { return t; } };
  if (new URLSearchParams(location.search).get('autoplay') === '1') play(0);
})();
