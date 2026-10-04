// Look Picks: the page itself. Picks (one per question, stored per viewer when the browser allows), the picks line
// "look-picks: Q-CZ-3 B, Q-RIM B, Q-TEXT A, U-06 B", the theme, and each question's controls and stage.
(function () {
  const K = window.Kit, S = K.store, PAGE = 'look-picks';
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
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
  const show = {};   // per question: show an option in its stage when its card is tapped
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

  // ---- Q-CZ-3
  const CZ = window.CZ;
  CZ.build($('#cz'));
  const czA = $('#czA');
  const czSeg = K.seg($('#czO'), ['A', 'B', 'C', 'D'].map((k) => ({ v: k, t: k + ' ' + CZ.names[k] })), 'B', (v) => { CZ.choose(v); czA.classList.toggle('on', v === 'A'); }, { wrap: true });
  show['Q-CZ-3'] = (v) => { czSeg.set(v); CZ.choose(v); czA.classList.toggle('on', v === 'A'); };
  K.seg($('#czShape'), [{ v: 'box', t: 'Box' }, { v: 'ring', t: 'Ring' }], 'box', (v) => CZ.optA({ shape: v }));
  K.seg($('#czTabs'), [{ v: 'all', t: 'All three' }, { v: 'one', t: 'Spectrum only' }], 'all', (v) => CZ.optA({ tabs: v === 'all' }));

  // ---- Q-RIM
  const rim = {};
  window.RIM.build($('#rim'), rim);
  K.seg($('#rimM'), [['Claude', 'Claude'], ['OpenAI', 'OpenAI (pale)'], ['DeepSeek', 'DeepSeek'], ['Gemini', 'Gemini'], ['Grok', 'Grok']].map(([v, t]) => ({ v, t })), 'Claude', (v) => rim.maker && rim.maker(v), { wrap: true });
  K.seg($('#rimZ'), [{ v: '3', t: '3x' }, { v: '6', t: '6x' }, { v: '10', t: '10x' }], '6', (v) => rim.zoom && rim.zoom(+v));
  K.seg($('#rimW'), [{ v: 'left', t: 'Left rim' }, { v: 'top', t: 'Top edge' }, { v: 'right', t: 'Right end' }], 'left', (v) => rim.where && rim.where(v));
  K.seg($('#rimS'), [{ v: '1', t: '1x' }, { v: '0.25', t: '¼x' }, { v: '0', t: 'Pause' }], '1', (v) => { if (!rim.play) return; rim.play(v !== '0'); if (v !== '0') rim.speed(+v); });

  // ---- Q-TEXT
  const tx = {};
  window.TXT.build($('#tx'), tx);
  const dpr = window.devicePixelRatio || 1;
  K.seg($('#txS'), [{ v: '100', t: '100 %' }, { v: '150', t: '150 %' }], dpr >= 1.25 ? '150' : '100', (v) => tx.scale(+v));
  tx.scale(dpr >= 1.25 ? 150 : 100);
  K.seg($('#txK'), [{ v: 'scene', t: 'App scene' }, { v: 'ramp', t: 'Sizes 34 · 15 · 13 · 11' }], 'scene', (v) => tx.kind(v), { wrap: true });
  K.seg($('#txZ'), [{ v: '4', t: '4x' }, { v: '8', t: '8x' }, { v: '16', t: '16x' }], '8', (v) => tx.zoom(+v));

  // ---- U-06
  const u6 = {};
  window.U06.build($('#u6'), u6);
  K.seg($('#u6S'), [{ v: '1', t: '1x' }, { v: '0.5', t: '½x' }, { v: '0.25', t: '¼x' }, { v: '0', t: 'Pause' }], '1', (v) => { if (!u6.play) return; u6.play(v !== '0'); if (v !== '0') u6.speed(+v); });
})();
