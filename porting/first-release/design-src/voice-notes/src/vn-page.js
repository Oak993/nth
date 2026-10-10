// Voice Notes Picks (D-VN): picks (one per question, kept per viewer when the browser allows), the picks line, the page
// theme, and the three stages. Each mock is drawn twice, dark and light side by side; every change crossfades or eases
// (OD-0076). Nothing is sent anywhere.
(function () {
  const K = window.Kit, S = K.store, PAGE = 'voice-notes';
  const D = JSON.parse(document.getElementById('vndata').textContent);
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const THEMES = [['d', 'Dark'], ['l', 'Light']];
  const reduce = () => !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);

  // ------------------------------------------------------------------ picks and the line
  const cards = $$('.dk-q[data-q]');
  const ids = cards.map((c) => c.dataset.q);
  const rec = {}; cards.forEach((c) => { rec[c.dataset.q] = c.dataset.rec; });
  const state = {};
  cards.forEach((c) => {
    let v = S.get(PAGE + '.pick.' + c.dataset.q, '');
    if (!$('[data-pick-v="' + v + '"]', c)) v = '';
    state[c.dataset.q] = v;
  });
  const cnt = $('#cnt'), line = $('#line'), copy = $('#copy');
  function lineText() {
    const picked = ids.filter((i) => state[i]);
    return PAGE + ': ' + (picked.length ? picked.map((i) => i + ' ' + state[i]).join(', ') : 'nothing picked yet');
  }
  function render() {
    cards.forEach((c) => {
      const v = state[c.dataset.q];
      $$('.dk-opt', c).forEach((o) => {
        const on = o.dataset.o === v;
        o.setAttribute('aria-current', String(on));
        $('.dk-pick', o).setAttribute('aria-pressed', String(on));
      });
    });
    const n = ids.filter((i) => state[i]).length;
    const off = ids.filter((i) => state[i] && state[i] !== rec[i]).length;
    cnt.innerHTML = '<b>' + n + ' of ' + ids.length + '</b> picked' + (n && off ? ' · ' + off + ' differ from the recommendation' : '');
    line.textContent = lineText();
    $$('.links a').forEach((a) => { const q = a.getAttribute('href').slice(1); $('b', a).textContent = state[q] || '?'; a.classList.toggle('on', !!state[q]); });
  }
  function set(q, v) { state[q] = v; S.set(PAGE + '.pick.' + q, v); }
  const show = {};
  document.addEventListener('click', (e) => {
    const opt = e.target.closest('.dk-opt');
    if (!opt) return;
    const sec = opt.closest('.dk-q'), q = sec.dataset.q, v = opt.dataset.o;
    if (e.target.closest('.dk-pick')) { set(q, state[q] === v ? '' : v); render(); }
    if (show[q]) show[q](v);
  });
  $('#all').addEventListener('click', () => { ids.forEach((i) => set(i, rec[i])); render(); });
  $('#clear').addEventListener('click', () => { ids.forEach((i) => set(i, '')); render(); });
  copy.addEventListener('click', async () => {
    const t = lineText();
    try { await navigator.clipboard.writeText(t); copy.textContent = 'Copied'; }
    catch (e) { copy.textContent = 'Select the text'; try { const r = document.createRange(); r.selectNodeContents(line); const s = getSelection(); s.removeAllRanges(); s.addRange(r); } catch (e2) { /* nothing to select with */ } }
    setTimeout(() => { copy.textContent = 'Copy'; }, 1600);
  });
  K.seg($('#segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], K.themeMode(), (v) => K.theme(v));
  render();

  // ------------------------------------------------------------------ fitting a fixed-size mock to its cell
  const fits = [];
  function fitOne(f) {
    const w = +f.dataset.w, h = +f.dataset.h;
    const k = Math.min(1, (f.clientWidth || w) / w);
    f.style.setProperty('--w', w); f.style.setProperty('--h', h); f.style.setProperty('--k', k.toFixed(4));
  }
  function fit(w, h, inner, cls) {
    const f = document.createElement('div');
    f.className = 'fit' + (cls ? ' ' + cls : ''); f.dataset.w = w; f.dataset.h = h;
    f.innerHTML = '<div class="in">' + inner + '</div>';
    fits.push(f);
    return f;
  }
  const ro = window.ResizeObserver ? new ResizeObserver((es) => es.forEach((e) => fitOne(e.target))) : null;
  function place(f) { fitOne(f); if (ro) ro.observe(f); }
  window.addEventListener('resize', () => fits.forEach(fitOne));
  function cell(caption, f) {
    const c = document.createElement('div');
    c.className = 'cell';
    c.innerHTML = '<div class="cap">' + caption + '</div>';
    c.appendChild(f);
    return c;
  }

  // ------------------------------------------------------------------ the composer pieces (voice-composer.html)
  const NOTE_ICON = '<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.2"></circle><path d="M8 4.8v3.6M8 10.9v.1"></path></svg>';
  const CLIP = '<span class="att" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="M10.6 4.6 5.7 9.5a1.55 1.55 0 0 0 2.2 2.2l5-5a3 3 0 0 0-4.25-4.25l-5 5a4.4 4.4 0 0 0 6.2 6.2l4.2-4.2"></path></svg></span>';
  const MARK = '<svg class="mk" viewBox="0 0 16 16" aria-hidden="true">' + Array.from({ length: 12 }, (_, i) => { const a = i * Math.PI / 6 + 0.13; return '<path d="M' + (8 + 2 * Math.cos(a)).toFixed(2) + ' ' + (8 + 2 * Math.sin(a)).toFixed(2) + ' L' + (8 + 6.6 * Math.cos(a)).toFixed(2) + ' ' + (8 + 6.6 * Math.sin(a)).toFixed(2) + '"></path>'; }).join('') + '</svg>';
  const CHIP = '<span class="chip">' + MARK + '<span>Opus</span><span class="ef">High</span><svg class="cv" viewBox="0 0 8 6" aria-hidden="true"><path d="M1.2 1.5 4 4.3 6.8 1.5"></path></svg></span>';
  const RING = '<span class="ring" aria-hidden="true"><svg width="18" height="18" viewBox="0 0 18 18"><circle class="t" cx="9" cy="9" r="7"></circle><circle class="a" cx="9" cy="9" r="7" style="stroke-dasharray:31 44" transform="rotate(-90 9 9)"></circle></svg></span>';
  const SEND = '<span class="send" aria-hidden="true"><svg width="12" height="12" viewBox="0 0 12 12"><path d="M6 10.2V1.9M2.6 5.3 6 1.9l3.4 3.4"></path></svg></span>';
  const X = '<span class="px" aria-hidden="true"><svg viewBox="0 0 12 12"><path d="M3.2 3.2 8.8 8.8M8.8 3.2 3.2 8.8"></path></svg></span>';
  const PILL = '<span class="vpill"><span class="drn"></span>' + X + '<span class="plab"></span></span>';
  const DRAFT = 'Add a shortcut for the theme switch and list it in Settings';
  const TYPED = 'so the release notes list every change, and the installer keeps the old settings when it <i>upgrades</i>';

  function noteHTML(n, inline) {
    const [before, link] = n;
    const t = esc(before) + (inline ? '<u>' + esc(link) + '</u>' : '<span class="lnk">' + esc(link) + '</span>');
    return inline ? t : NOTE_ICON + '<span>' + t + '</span>';
  }

  // ================================================================== Q1: the error notes
  (function () {
    const st = { set: 'A', cas: 'nomic', draft: 'empty' };
    const host = $('#stN');
    host.classList.add('one');
    const wins = THEMES.map(([th, name]) => {
      const inner = '<div class="vwin th-' + th + '"><div class="rep"><b>Done.</b> The theme switch now answers to Ctrl+Shift+L, and Settings lists it under Keyboard.</div>'
        + '<div class="cmp"><div class="plate"></div>' + CLIP
        + '<div class="fld"><div class="lay hint">Ask Claude for anything; big jobs go out to a team of heads</div><div class="lay note n0"></div><div class="lay note n1"></div></div>'
        + '<div class="col"><div class="ntc"><div class="t0"></div><div class="t1"></div></div><div class="dtx">' + esc(DRAFT) + '</div></div>'
        + '<div class="tools"><span class="oslot"><span class="vorb"></span></span>' + CHIP + '</div>' + RING + SEND + '</div></div>';
      const f = fit(880, 128, inner);
      host.appendChild(cell('<b>' + name + '</b><span id="capN' + th + '"></span>', f));
      place(f);
      return { th, f, flip: 0 };
    });
    function current() {
      if (st.cas === 'blocked') return D.locked;
      return D.sets[st.set].notes[st.cas];
    }
    function paint() {
      const n = current();
      const dest = D.dest[n[2]];
      wins.forEach((w) => {
        const cmp = $('.cmp', w.f);
        cmp.classList.toggle('tall', st.draft === 'text');
        w.flip = 1 - w.flip;
        const on = $('.n' + w.flip, w.f), off = $('.n' + (1 - w.flip), w.f);
        on.innerHTML = noteHTML(n, false);
        off.classList.remove('on'); $('.hint', w.f).classList.remove('on');
        void on.offsetWidth; on.classList.add('on');
        const t1 = $('.t' + w.flip, w.f), t0 = $('.t' + (1 - w.flip), w.f);
        t1.innerHTML = noteHTML(n, true);
        t0.classList.remove('on'); void t1.offsetWidth; t1.classList.add('on');
        const cap = $('#capN' + w.th);
        cap.textContent = (st.cas === 'blocked' ? 'Locked note' : 'Set ' + st.set) + ' · opens ' + dest.t;
      });
      $$('#wlist .wcase').forEach((c) => {
        const here = c.dataset.case === st.cas;
        c.classList.toggle('cur', here);
        $$('li', c).forEach((li) => li.classList.toggle('cur', here && (li.dataset.set === st.set || li.dataset.set === 'L')));
      });
    }
    const segS = K.seg($('#segNS'), Object.keys(D.sets).map((k) => ({ v: k, t: k + ' ' + D.sets[k].name })), st.set, (v) => { st.set = v; paint(); });
    const segC = K.seg($('#segNC'), D.cases.map((c) => ({ v: c.id, t: c.t })), st.cas, (v) => { st.cas = v; paint(); }, { wrap: true });
    K.seg($('#segND'), [{ v: 'empty', t: 'Empty' }, { v: 'text', t: 'Has text' }], st.draft, (v) => { st.draft = v; paint(); });
    show.Notes = (v) => { st.set = v; segS.set(v); paint(); };
    $('#wlist').addEventListener('click', (e) => {
      const li = e.target.closest('li'); if (!li) return;
      const c = li.closest('.wcase').dataset.case;
      st.cas = c; segC.set(c);
      if (li.dataset.set !== 'L') { st.set = li.dataset.set; segS.set(li.dataset.set); }
      paint();
      host.scrollIntoView({ behavior: reduce() ? 'auto' : 'smooth', block: 'nearest' });
    });
    paint();
  })();

  // ================================================================== Q2: the countdown in the pill
  (function () {
    const st = { look: 'B', speed: '1' };
    const mm = (s) => '0:' + String(Math.max(0, s)).padStart(2, '0');
    // which state words show (V-6.3's hysteresis is the voice's; here a fixed script for the run)
    const words = (left) => (left === 'fin' ? 'Finishing…' : (left > 27 || (left <= 12 && left > 6) ? 'Transcribing…' : 'Listening…'));
    // the label's parts for a look at a time: [{t, cls, num}] (num: tabular, crossfades as figures)
    function parts(look, left, w) {
      if (left === 'fin' || left === null || left > 30) return [{ t: w || words(left), k: 'w' }];
      if (look === 'A') return [{ t: mm(left), k: 'n', num: true }, { t: ' left', k: 's' }];
      if (look === 'B') return [{ t: w || words(left), k: 'w' }, { t: ' ', k: 'g', cls: 'lt' }, { t: mm(left), k: 'n', num: true, cls: 'lt' }];
      if (look === 'C') return [{ t: w || words(left), k: 'w' }];
      return [{ t: 'Stops in ', k: 's' }, { t: mm(left), k: 'n', num: true, cls: left <= 10 ? 'amb' : '' }];
    }
    const cv = document.createElement('canvas').getContext('2d');
    const FONT = '"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,sans-serif';
    function width(ps) {
      let w = 0;
      ps.forEach((p) => {
        cv.font = (p.cls === 'lt' ? '400' : '600') + ' 13.5px ' + FONT;
        // the figures are tabular in the label, so they are measured as zeros: the width holds while they count
        w += cv.measureText(p.num ? p.t.replace(/[0-9]/g, '0') : p.t).width;
      });
      return Math.ceil(w);
    }
    const runs = (ps) => ps.map((p) => '<span class="xg' + (p.num ? ' num' : '') + (p.cls ? ' ' + p.cls : '') + '" data-k="' + p.k + '"><span>' + esc(p.t) + '</span></span>').join('');
    // writes the label: a new arrangement of parts crossfades as a whole (V-12.9 relabel); within one arrangement each
    // part is a crossfading run, so only the run that changes moves (the figures)
    function label(pill, ps, still) {
      const lab = $('.plab', pill);
      const keys = ps.map((p) => p.k).join('|');
      let all = $('.xg.all', lab);
      if (!all) { lab.innerHTML = '<span class="xg all"></span>'; all = $('.xg.all', lab); }
      let ver = all.lastElementChild;
      if (!ver || ver.dataset.keys !== keys) {
        const n = document.createElement('span');
        n.dataset.keys = keys; n.innerHTML = runs(ps);
        if (still || reduce() || !ver) { all.innerHTML = ''; all.appendChild(n); return; }
        n.className = 'pre';
        $$(':scope>span', all).forEach((o) => { o.classList.add('out', 'gone'); setTimeout(() => o.remove(), 340); });
        all.appendChild(n); void n.offsetWidth; n.classList.remove('pre');
        return;
      }
      ps.forEach((p) => {
        const g = $('[data-k="' + p.k + '"]', ver);
        g.className = 'xg' + (p.num ? ' num' : '') + (p.cls ? ' ' + p.cls : '');
        const cur = g.lastElementChild;
        if (cur && cur.textContent === p.t) return;
        if (still || reduce()) { g.style.width = ''; g.innerHTML = '<span>' + esc(p.t) + '</span>'; return; }
        // the leaving words step out of the flow and the run eases from the old width to the new one, so what follows
        // (the time, in B) slides with the pill instead of jumping when the old words are removed
        const from = g.offsetWidth;
        const n = document.createElement('span'); n.className = 'pre'; n.textContent = p.t;
        $$(':scope>span', g).forEach((o) => { o.classList.add('out', 'gone'); setTimeout(() => o.remove(), 340); });
        g.appendChild(n);
        g.style.width = ''; const to = n.offsetWidth;
        if (!p.num && from !== to) {
          g.style.width = from + 'px'; void g.offsetWidth;
          g.classList.add('ease'); g.style.width = to + 'px';
          clearTimeout(g._t); g._t = setTimeout(() => { g.classList.remove('ease'); g.style.width = ''; }, 460);
        }
        void n.offsetWidth; n.classList.remove('pre');
      });
    }
    function setPill(win, look, left, phase, still) {
      const cmp = $('.cmp', win), pill = $('.vpill', win), live = phase !== 'rest';
      cmp.classList.toggle('live', live);
      cmp.classList.toggle('fin', phase === 'fin');
      const ps = parts(look, phase === 'fin' ? 'fin' : left);
      if (live) label(pill, ps, still);
      const W = live ? width(ps) + 88 : 40;
      pill.style.width = W + 'px';
      const drn = $('.drn', pill);
      const counting = phase === 'listen' && left !== null && left <= 30;
      drn.classList.toggle('on', look === 'C' && counting);
      drn.classList.toggle('amb', look === 'C' && counting && left <= 10);
      drn.style.setProperty('--f', look === 'C' && counting ? (left / 30).toFixed(4) : '1');
      // the text field ends 10 px before the pill's left end (V-12.6)
      const tools = $('.tools', win), slot = $('.oslot', win);
      const orbX = tools.offsetLeft + slot.offsetLeft + 16;
      const fld = $('.fld', win);
      fld.style.right = (cmp.offsetWidth - (live ? orbX + 20 - W - 10 : orbX - 16 - 12)) + 'px';
    }
    function composer(th, w, h) {
      return '<div class="vwin th-' + th + '"><div class="cmp"><div class="plate"></div>' + CLIP
        + '<div class="fld"><div class="lay on"><span class="typed">' + TYPED + '</span></div></div>'
        + '<div class="tools"><span class="oslot">' + PILL + '<span class="vorb"></span></span>' + CHIP + '</div>' + RING + SEND + '</div></div>';
    }
    // the live pair
    const host = $('#stC');
    host.classList.add('one');
    const lives = THEMES.map(([th, name]) => {
      const f = fit(880, 80, composer(th));
      host.appendChild(cell('<b>' + name + '</b><span class="lt" id="capC' + th + '">At rest</span>', f));
      place(f);
      return f;
    });
    // the frames: 0:30, 0:10, 0:00, finishing, in each theme
    const FR = [[30, 'listen', '0:30'], [10, 'listen', '0:10'], [0, 'listen', '0:00, the cap'], [null, 'fin', 'Finishing…']];
    const fr = $('#frC');
    const frames = [];
    THEMES.forEach(([th, name]) => FR.forEach(([left, phase, t]) => {
      const f = fit(520, 80, composer(th).replace('class="cmp"', 'class="cmp" style="left:-200px"'), 'still');
      fr.appendChild(cell('<b>' + name + '</b><span>' + esc(t) + '</span>', f));
      place(f);
      frames.push({ f, left, phase });
    }));
    function paintFrames() {
      frames.forEach((x) => setPill(x.f, st.look, x.left, x.phase, false));
    }
    // the run: 0:33 of listening left (4:27), the countdown from 0:30, the cap, Finishing..., the pill folds
    let timer = null, left = 33, phase = 'rest';
    const playL = $('#playCL');
    function paintLive(still) {
      lives.forEach((f, i) => {
        setPill(f, st.look, left, phase, still);
        const c = $('#capC' + THEMES[i][0]);
        c.textContent = phase === 'rest' ? 'At rest: press Play' : phase === 'fin' ? 'At 5:00 the text finishes as if the orb were clicked' : 'Listening, ' + (left > 30 ? (300 - left) + ' s in' : mm(left) + ' left of 5:00');
      });
    }
    function stop() { if (timer) clearTimeout(timer); timer = null; playL.textContent = 'Play'; }
    function step() {
      const sp = +st.speed, tick = 1000 / sp;
      if (phase === 'listen' && left > 0) { left -= 1; paintLive(); timer = setTimeout(step, left === 0 ? 420 / sp : tick); return; }
      if (phase === 'listen') { phase = 'fin'; paintLive(); timer = setTimeout(step, 2200 / sp); return; }
      if (phase === 'fin') { phase = 'rest'; left = 33; paintLive(); stop(); }
    }
    $('#playC').addEventListener('click', () => {
      if (timer) { stop(); phase = 'rest'; left = 33; paintLive(); return; }
      phase = 'listen'; left = 33; paintLive(); playL.textContent = 'Stop';
      timer = setTimeout(step, 1000 / +st.speed);
    });
    const segL = K.seg($('#segCL'), D.count.map((c) => ({ v: c.v, t: c.v + ' ' + c.t })), st.look, (v) => { st.look = v; paintLive(); paintFrames(); }, { wrap: true });
    K.seg($('#segCS'), [{ v: '1', t: '1x' }, { v: '3', t: '3x' }], st.speed, (v) => { st.speed = v; });
    show.Countdown = (v) => { st.look = v; segL.set(v); paintLive(); paintFrames(); };
    const start = () => { paintLive(true); paintFrames(); };
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(start); else start();
    start();
  })();

  // ================================================================== Q3: the CUDA offer in the hardware card
  (function () {
    const st = { look: 'A', state: 'offer', pct: 42 };
    const CK = '<svg class="ck" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.6 6.3 5 8.6l4.4-5"></path></svg>';
    const MX = '<span class="mx" title="Cancel the download"><svg viewBox="0 0 10 10"><path d="M2.5 2.5 7.5 7.5M7.5 2.5 2.5 7.5"></path></svg></span>';
    const PC = {
      desk: { hw: [['Processor', 'AMD Ryzen 7 9800X3D, 8 cores'], ['Memory', '31 GB'], ['Graphics', 'NVIDIA GeForce RTX 4080, 16 GB'], ['GPU backend', 'Vulkan, working']],
        rec: 'Large v3 Turbo, balanced 874 MB', why: 'Your RTX 4080 runs it on the graphics card: text about 1 to 2 seconds after you stop speaking.' },
      laptop: { hw: [['Processor', 'Intel Core i5-1235U, 10 cores'], ['Memory', '16 GB'], ['Graphics', 'Intel Iris Xe, shared memory'], ['GPU backend', 'None working, runs on the processor']],
        rec: 'Small, compact 190 MB', why: 'On the processor alone Small answers in about 3 to 6 seconds; Large v3 Turbo would take 15 seconds or more.' },
    };
    const SIZE = '644 MB';
    function card(look, s, pct) {
      const nv = s !== 'none', P = PC[nv ? 'desk' : 'laptop'];
      const p = Math.round(pct);
      const rows = P.hw.map(([k, v], i) => {
        let extra = '';
        if (nv && look === 'C' && i === 3) {
          extra = s === 'offer' ? '<button class="czbtn sm" data-a="dl" title="NVIDIA CUDA, ' + SIZE + ' from nuget.org and NVIDIA">Get CUDA, ' + SIZE + '</button>'
            : s === 'dl' ? '<span class="dt">CUDA <span class="pct">' + p + '%</span></span>' + MX
              : s === 'done' ? '<span class="dt">CUDA installed, restart Nth to use it</span>'
                : '<span class="dt">CUDA didn’t download</span><button class="czbtn sm" data-a="dl">Try again</button>';
        }
        return '<div class="hwi"><span>' + CK + '</span><span class="hwk">' + k + '</span><span class="hwv"><span>' + esc(v) + '</span>' + extra + '</span></div>';
      }).join('');
      let inBox = '', after = '', foot = '';
      if (nv && look === 'B') {
        const t = s === 'offer' ? 'Your RTX 4080 can also run it on NVIDIA CUDA, a separate ' + SIZE + ' download.'
          : s === 'dl' ? 'Downloading NVIDIA CUDA: <span class="pct">' + p + '%</span>'
            : s === 'done' ? 'NVIDIA CUDA is installed. Restart Nth to use it.'
              : 'NVIDIA CUDA didn’t download. Nothing was installed.';
        const b = s === 'offer' ? '<button class="czbtn" data-a="dl">Download CUDA</button>' : s === 'dl' ? MX : s === 'fail' ? '<button class="czbtn" data-a="dl">Try again</button>' : '';
        inBox = '<div class="cudal"><span class="dt">' + t + '</span>' + b + '</div>' + (s === 'dl' ? '<div class="mbar on" style="margin:0;--p:' + p + '%"><i></i></div>' : '');
      }
      if (nv && look === 'A') {
        const d = s === 'offer' ? 'Optional, for NVIDIA cards. ' + SIZE + ' from nuget.org and NVIDIA'
          : s === 'dl' ? 'Downloading from nuget.org and NVIDIA'
            : s === 'done' ? 'Installed. Restart Nth to use it'
              : 'The download didn’t finish. Nothing was installed';
        const m = s === 'offer' || s === 'fail' ? '<button class="czbtn" data-a="dl">Download</button>'
          : s === 'dl' ? '<span class="pct">' + p + '%</span>' + MX : CK + '<span>Installed</span>';
        after = '<div class="dv"></div><div class="row"><div class="rt"><b>NVIDIA CUDA</b><span class="dt">' + d + '</span></div><span class="mst">' + m + '</span></div>'
          + '<div class="mbar' + (s === 'dl' ? ' on' : '') + '" style="--p:' + (s === 'dl' ? p : 0) + '%;margin-bottom:10px"><i></i></div>';
      }
      if (nv && look === 'D') {
        const t = s === 'offer' ? 'NVIDIA cards can also run voice typing on CUDA, NVIDIA’s own runtime: a separate ' + SIZE + ' download from nuget.org and NVIDIA. <button class="lk" data-a="dl">Download CUDA</button>'
          : s === 'dl' ? 'Downloading NVIDIA CUDA, <span class="pct">' + p + '%</span>. <button class="lk">Cancel</button>'
            : s === 'done' ? 'NVIDIA CUDA is installed. Restart Nth to use it.'
              : 'NVIDIA CUDA didn’t download, and nothing was installed. <button class="lk" data-a="dl">Try again</button>';
        foot = '<div class="hwnote">' + t + '</div>';
      }
      return '<div class="sh">Speech model</div><div class="card">'
        + '<div class="row"><div class="rt"><b>Recommended for this PC</b><span class="dt">Checked by itself when voice typing was set up</span></div><button class="czbtn">Check again</button></div>'
        + '<div class="hwlist">' + rows + '</div>'
        + '<div class="hwrec"><div class="hwrt"><b>' + esc(P.rec) + '</b><span class="dt">' + esc(P.why) + '</span></div>' + inBox + '<div class="hwstate">' + CK + '<span>In use</span></div></div>'
        + after
        + '<div class="hwnote">The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it again any time.</div>' + foot
        + '</div>';
    }
    const host = $('#stU');
    const panes = THEMES.map(([th, name]) => {
      const f = fit(569, 420, '<div class="spane th-' + th + '"><div class="swap"><div class="new"></div></div></div>', 'auto');
      host.appendChild(cell('<b>' + name + '</b><span>Settings &gt; Voice typing &gt; Speech model</span>', f));
      place(f);
      return f;
    });
    function paint() {
      panes.forEach((f) => {
        const sw = $('.swap', f);
        $$('.old', sw).forEach((o) => o.remove());
        const cur = $('.new', sw);
        if (cur) { cur.className = 'old'; setTimeout(() => cur.remove(), 260); }
        const n = document.createElement('div'); n.className = 'new';
        n.innerHTML = card(st.look, st.state, st.pct);
        sw.appendChild(n);
        const h = $('.spane', f).offsetHeight;
        f.dataset.h = h; fitOne(f);
      });
    }
    // the percentage moves in place while downloading, so only the figures and the bar change
    function paintPct() {
      panes.forEach((f) => {
        const n = $('.new', f); if (!n) return;
        $$('.pct', n).forEach((e) => { e.textContent = Math.round(st.pct) + '%'; });
        $$('.mbar', n).forEach((e) => e.style.setProperty('--p', Math.round(st.pct) + '%'));
      });
    }
    const segL = K.seg($('#segUL'), D.cuda.map((c) => ({ v: c.v, t: c.v + ' ' + c.t })), st.look, (v) => { st.look = v; paint(); }, { wrap: true });
    const segS = K.seg($('#segUS'), [{ v: 'offer', t: 'Offer' }, { v: 'dl', t: 'Downloading' }, { v: 'done', t: 'Installed' }, { v: 'fail', t: 'Failed' }, { v: 'none', t: 'Not NVIDIA' }], st.state, (v) => { stopRun(); st.state = v; st.pct = 42; paint(); }, { wrap: true });
    show.CUDA = (v) => { st.look = v; segL.set(v); paint(); };
    let run = null, waits = [];
    function stopRun() { if (run) clearInterval(run); run = null; waits.forEach(clearTimeout); waits = []; }
    $('#playU').addEventListener('click', () => {
      stopRun();
      st.state = 'offer'; st.pct = 0; segS.set('offer'); paint();
      waits.push(setTimeout(() => { $$('.new [data-a="dl"]', host).forEach((b) => b.classList.add('press')); }, 900));
      waits.push(setTimeout(() => {
        st.state = 'dl'; segS.set('dl'); paint();
        const t0 = performance.now();
        run = setInterval(() => {
          st.pct = Math.min(100, (performance.now() - t0) / 60);
          paintPct();
          if (st.pct >= 100) { stopRun(); st.state = 'done'; segS.set('done'); paint(); }
        }, 100);
      }, 1200));
    });
    paint();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => panes.forEach((f) => { f.dataset.h = $('.spane', f).offsetHeight; fitOne(f); }));
  })();
})();
