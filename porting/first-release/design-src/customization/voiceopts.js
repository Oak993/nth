// ---------- the voice options (OD-0097): the pill's words, the composer light while you speak, the spoken language ----------
// The owner shipped L1, B1 and A from the Voice Options decision page (E:/Droppy/work/design/decisions-2026-10-03/
// voice-options/: voice-options.js LSETS, the beam follower, askLang / answerLang; voice-options.html Q1-Q3) and made
// every alternative a choice here, previewed in the stage. Row places, titles and details are engineering decisions
// (build-specs/customization/SPEC.md CZ-9.8 to CZ-9.11). The amendment: nothing but the orb, the label and the cancel
// control in the pill (no separator dot, nothing beside the orb).

const VO_WORDS = {
  L1: ['Listening\u2026', 'Transcribing\u2026', 'Finishing\u2026'],
  L2: ['Listening\u2026', 'Writing\u2026', 'Finishing\u2026'],
  L3: ['Hearing you\u2026', 'Writing\u2026', 'Tidying up\u2026'],
  L4: ['Speak now', 'Transcribing\u2026', 'Almost done\u2026'],
  L5: ['Listening\u2026', 'Typing\u2026', 'Wrapping up\u2026'],
  L6: ['Listening', 'Transcribing', 'Finishing'],
};
const VO_WORD_D = {
  L1: 'What the app is doing, plainly',
  L2: 'Writing says what you see: the words appearing in the composer',
  L3: 'Warmer and personal',
  L4: 'An invitation in silence, then what the app is doing',
  L5: 'Names the composer\u2019s own state: it is typing',
  L6: 'Without the ellipses: quieter, and the pill stays narrower',
};
const VO_WORD_DIM = 'Cross beside the orb has no pill, so no words are shown';
const VO_LIGHTS = [{ id: 'away', t: 'Steps aside' }, { id: 'dim', t: 'Dims to 35 %' }, { id: 'stay', t: 'Stays on' }];
const VO_LIGHT_LV = { away: 0, dim: 0.35, stay: 1 };
const VO_LIGHT_D = {
  away: 'The light fades out as you start speaking and returns when the text is done: one light on the composer at a time',
  dim: 'The light keeps running at 35 % under the voice glow',
  stay: 'The light keeps running at full strength under the voice glow',
};
const VO_LIGHT_DIM = 'No effect while the composer light is Off';
const VO_LANGS = [{ id: 'auto', t: 'Auto-detect, silent' }, { id: 'show', t: 'Auto-detect, named in the pill' }, { id: 'windows', t: 'Windows display language' }, { id: 'ask', t: 'Asked on first use' }];
const VO_WIN = 'English';   // the simulated PC's Windows display language
const VO_LANG_D = {
  auto: 'Whisper detects the language each time; nothing is shown',
  show: 'Whisper detects the language, and the pill names it once it is heard, so a wrong guess shows at once',
  windows: VO_WIN + ', Windows\u2019 display language, from the start; Auto-detect stays one click away on the Voice typing page',
  ask: 'The first click on the orb asks which language you will speak; the answer is kept on the Voice typing page',
};
const VO_ANSWERS = [['English', 'English'], ['Hindi', 'Hindi'], ['Urdu', 'Urdu'], ['Auto', 'Auto-detect']];
const VO_NAME = '\u2002English';   // the language as the pill names it under B: an en space, no separator dot

(function () {
  const P = CzComponent.prototype;
  const setWords = (k) => { const w = VO_WORDS[k] || VO_WORDS.L1; VC_LABELS.listening = w[0]; VC_LABELS.transcribing = w[1]; VC_LABELS.finishing = w[2]; };

  const init0 = P.vcInit;
  P.vcInit = function () {
    if (this.vc) return;
    init0.call(this);
    let saved = {};
    try { saved = JSON.parse(window.localStorage.getItem(CZ_SAVE + '-voice') || '{}') || {}; } catch (e) { saved = {}; }
    Object.assign(this.state, {
      words: VO_WORDS[saved.words] ? saved.words : 'L1',
      lightVoice: VO_LIGHT_LV[saved.lightVoice] != null ? saved.lightVoice : 'away',
      vlang: VO_LANGS.some((x) => x.id === saved.vlang) ? saved.vlang : 'auto',
      voMenu: null, langOn: false, relabel: false, askOpen: false, asked: false, askPick: null,
    });
    setWords(this.state.words);
    this.vc.lv = 1; this.vc.askAuto = 0; this.vc.askT = 0;
    document.addEventListener('pointerdown', (ev) => {
      const t = ev.target;
      if (this.state.voMenu && !(t.closest && t.closest('.czvom.open'))) this.setState({ voMenu: null });
      if (this.state.askOpen && !(t.closest && (t.closest('.vask') || t.closest('.orbbtn')))) this.voAskClose();
    }, true);
    document.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape' || e.defaultPrevented || !this.state.askOpen) return;
      e.preventDefault(); this.voAskClose(true);
    });
  };
  const save0 = P.vcSave;
  P.vcSave = function () {
    save0.call(this);
    try {
      const k = CZ_SAVE + '-voice', o = JSON.parse(window.localStorage.getItem(k) || '{}') || {};
      Object.assign(o, { words: this.state.words, lightVoice: this.state.lightVoice, vlang: this.state.vlang });
      window.localStorage.setItem(k, JSON.stringify(o));
    } catch (e) {}
  };

  // a pick: store it, then play the demo from the start (the words after their relabel, as the decision page does)
  P.setVo = function (patch) {
    this.vcInit();
    const S = this.state;
    if (patch.words && patch.words !== S.words) {
      const k = patch.words;
      this.setState({ words: k, relabel: true });
      clearTimeout(this.relabelT);
      this.relabelT = setTimeout(() => { setWords(k); this.setState({ relabel: false }); this.vcDirty(); }, 160);
    } else if (patch.words) { /* the same set: nothing to relabel */ }
    if (patch.lightVoice) this.setState({ lightVoice: patch.lightVoice });
    if (patch.vlang) this.setState({ vlang: patch.vlang, asked: false, langOn: false });   // re-armed: D asks again
    this.vcSave();
    clearTimeout(this.replayT);
    this.replayT = setTimeout(() => this.voReplay(), patch.words ? 200 : 0);
  };
  P.voReplay = function () {
    if (this.state.askOpen) this.voAskClose();
    this.state.vmode = 'idle';
    this.setMode('rest');
    this.voStart(true);
  };
  P.voStart = function (scripted) {
    if (this.state.vlang === 'ask' && !this.state.asked) { this.voAskOpen(scripted); return; }
    this.vt = 0; this.vc.speechAt = -9;
    this.setState({ vmode: 'listen', vN: 0, plab: 'listening', langOn: false });
    this.vcDirty();
  };
  // a listening style pick starts the demo too: with D still to ask, the question comes first
  const setVc0 = P.setVc;
  P.setVc = function (patch, demo) {
    if (demo && this.state.vlang === 'ask' && !this.state.asked) { setVc0.call(this, patch, false); this.voReplay(); return; }
    setVc0.call(this, patch, demo);
  };
  const tap0 = P.voiceTap;
  P.voiceTap = function () {
    this.vcInit();
    const S = this.state;
    if (S.askOpen) { this.voAskClose(); return; }   // a second click on the orb closes the question
    if ((S.vmode === 'idle' || S.vmode === 'shown') && S.vlang === 'ask' && !S.asked) { this.voAskOpen(false); return; }
    tap0.call(this);
  };
  const cancel0 = P.voiceCancel;
  P.voiceCancel = function () { if (this.state.askOpen) this.voAskClose(); cancel0.call(this); };

  // ---- the language question (D): the page's popover with the Mac's arrow tip, anchored on the orb
  P.voAskOpen = function (scripted) {
    this.vc.askAuto = scripted ? 1.6 : 0; this.vc.askT = 0;
    this.askFocus = !scripted;
    this.setState({ askOpen: true, askPick: null });
  };
  P.voAskClose = function (refocus) {
    if (!this.state.askOpen) return;
    this.vc.askAuto = 0;
    this.setState({ askOpen: false });
    if (refocus) { const o = document.querySelector('.czpage.on .orbbtn'); if (o) o.focus({ preventScroll: true }); }
  };
  P.voAnswer = function (id) {
    if (!this.state.askOpen || this.state.askPick) return;
    this.vc.askAuto = 0;
    this.setState({ askPick: id, asked: true });   // the preview's answer is never stored (CZ-4.7.9)
    this.voStart(false);
    setTimeout(() => this.setState({ askOpen: false }), 220);
  };
  P.voAskPlace = function () {
    const el = this.askEl, E = this.orbEls[this.state.variant];
    if (!el || !el.isConnected || !E || !E.slot || !E.slot.isConnected) return;
    const win = el.parentNode, wr = win.getBoundingClientRect(), sr = E.slot.getBoundingClientRect();
    const cmp = E.slot.closest('.cmp'), cr = cmp ? cmp.getBoundingClientRect() : sr;
    const bar = win.querySelector('.czchrome'), barB = bar ? bar.getBoundingClientRect().bottom - wr.top : 52;
    const W = el.offsetWidth, H = el.offsetHeight, cx = sr.left + sr.width / 2 - wr.left;
    const left = Math.round(Math.min(cr.right - wr.left - W, Math.max(cr.left - wr.left, cx - W + 34)));
    const above = sr.top - wr.top - 13 - H, below = above < barB + 4;   // no room under the window's top bar: below
    const top = Math.round(below ? sr.bottom - wr.top + 13 : above);
    const key = left + ',' + top + ',' + below;
    if (el.__k === key) return;
    el.__k = key;
    el.style.left = left + 'px'; el.style.top = top + 'px';
    el.style.setProperty('--tip', Math.round(cx - left) + 'px');
    if (below) el.setAttribute('data-below', ''); else el.removeAttribute('data-below');
  };

  // ---- the frame: the language name (B), the question's self-answer, the light's level and the voice glow
  const tick0 = P.voiceTick;
  P.voiceTick = function (dt) {
    tick0.call(this, dt);
    const S = this.state, V = this.vc;
    if (S.askOpen && V.askAuto && !S.askPick) { V.askT += dt; if (V.askT >= V.askAuto) this.voAnswer('English'); }
    const live = S.vmode === 'listen' || S.vmode === 'process';
    const on = live && S.vlang === 'show' && (S.langOn || S.plab === 'transcribing' || S.plab === 'finishing');
    if (on !== S.langOn) { this.setState({ langOn: on }); this.vcDirty(); }
  };
  P.useGlow = function (fn) { this.glowMount = typeof fn === 'function' ? fn : null; };
  const ot0 = P.orbTick;
  P.orbTick = function (now, dt, vis) {
    ot0.call(this, now, dt, vis);
    const S = this.state, V = this.vc, reduce = !!(this.mq && this.mq.matches);
    if (!this.glowMount && typeof window.__czGlow === 'function') this.glowMount = window.__czGlow;
    const voiceOn = S.vmode === 'listen' || S.vmode === 'process' || S.vmode === 'insert';
    const tgt = voiceOn ? VO_LIGHT_LV[S.lightVoice] : 1;
    V.lv = reduce ? tgt : vcFollow(V.lv, tgt, dt, 0.2, 0.17);
    const o = V.lv < 0.003 ? 0 : V.lv > 0.997 ? 1 : V.lv;
    const gk = [S.vmode === 'listen' || S.vmode === 'process', S.vmode === 'process', this.dark(), reduce].join();
    for (const id in vis) {
      const E = this.orbEls[id.slice(1)];
      if (!E) continue;
      for (const L of [E.light, E.lightO]) if (L && L.isConnected && L.__o !== o) { L.__o = o; L.style.opacity = o === 1 ? '' : o.toFixed(3); }
      const host = E.glow;   // the template's data-keep host: the runtime never patches what the package puts in it
      if (host && host.isConnected && this.glowMount) {
        if (!host.__g) { host.__g = document.createElement('div'); host.__g.className = 'vglow'; host.appendChild(host.__g); }
        const g = host.__g;
        if (!g.__vg) {
          g.__vg = this.glowMount(g, { type: 'default', scale: 0.7, glowWidth: 1.6, lobeSpacing: 2.1, rangeWidth: 1.9, processingTravel: 2.05, borderRadius: 22,
            theme: this.dark() ? 'dark' : 'light', level: () => (this.state.vmode === 'listen' ? this.vlev : 0), active: false, processing: false, reducedMotion: reduce });
          g.__gk = '';
        }
        if (g.__gk !== gk) {
          g.__gk = gk;
          g.__vg.update({ active: S.vmode === 'listen' || S.vmode === 'process', processing: S.vmode === 'process', theme: this.dark() ? 'dark' : 'light', reducedMotion: reduce });
        }
      }
    }
    if (S.askOpen) this.voAskPlace();
  };
  // the language name's width joins the label's (voicecmp.js vcLayout)
  P.vcNameW = function (font) { return this.state.langOn ? this.tw(VO_NAME, font, 400, UIF) : 0; };

  // ---- the rows
  P.voMenuRow = function (key, t, list, cur, pick, o) {
    const S = this.state, open = S.voMenu === key && !o.dim, it = list.find((x) => x.id === cur) || list[0];
    let widest = 0;
    for (const x of list) widest = Math.max(widest, this.tw(x.t, 13, 400, UIF));
    const chip = () => document.querySelector('.czpage.on .czvom.vo-' + key + ' .czmc');
    return this.row('Menu', {
      t, stack: !!o.stack, dim: !!o.dim, d: this.dx('d-vo-' + key, o.d),
      mcls: 'czms czvom vo-' + key + (o.stack ? ' left' : '') + (open ? ' open' : ''), open: open ? 'true' : 'false',
      popc: 'czpop' + (open ? ' open' : ''), popw: Math.max(220, Math.ceil(widest) + 48),
      lab: this.xf('vo-lab-' + key, it.t), w: fmt(this.tw(it.t, 13, 400, UIF) + 1, 2), tab: o.dim ? '-1' : '0',
      fopen: open,
      toggle: () => { if (o.dim) return; this.voMenuFocus = !open; this.setState({ voMenu: open ? null : key }); },
      key: (ev) => {
        if (ev.key === 'Escape' && this.state.voMenu) { ev.preventDefault(); this.setState({ voMenu: null }); const c = chip(); if (c) c.focus(); return; }
        if (this.state.voMenu !== key) return;
        const items = Array.from(ev.currentTarget.querySelectorAll('.pitem')), i = items.indexOf(document.activeElement);
        const go = { ArrowDown: i + 1, ArrowUp: i - 1, Home: 0, End: items.length - 1 }[ev.key];
        if (go === undefined) return;
        ev.preventDefault(); const n = items[(go + items.length) % items.length]; if (n) n.focus();
      },
      items: list.map((x) => ({
        t: x.t, on: x.id === cur ? 'true' : 'false', chk: 'pcheck' + (x.id === cur ? ' on' : ''),
        pick: () => { this.setState({ voMenu: null }); pick(x.id); const c = chip(); if (c) c.focus({ preventScroll: true }); },
      })),
    });
  };
  const rows0 = P.orbRows;
  P.orbRows = function (vid) {
    this.vcInit();
    const S = this.state, r = rows0.call(this, vid);   // [orb, listening style, size, attachment, after voice input, note]
    const cross = S.lstyle === 'cross', off = S.light === 'off';
    const words = this.voMenuRow('words', 'Pill words', Object.keys(VO_WORDS).map((k) => ({ id: k, t: VO_WORDS[k].join(' \u00b7 ') })), S.words,
      (id) => this.setVo({ words: id }), { stack: true, dim: cross, d: cross ? VO_WORD_DIM : VO_WORD_D[S.words] });
    const g = this.seg('Composer light while you speak', VO_LIGHTS, S.lightVoice, (id) => this.setVo({ lightVoice: id }), { sm: true });
    if (off) g.opts.forEach((x) => { x.tab = '-1'; });
    const light = this.row('Seg', { t: 'Composer light while you speak', stack: true, dim: off, d: this.dx('d-vo-light', off ? VO_LIGHT_DIM : VO_LIGHT_D[S.lightVoice]), segs: [{ cls: 'czsgw on', g }] });
    const lang = this.voMenuRow('vlang', 'Spoken language', VO_LANGS, S.vlang, (id) => this.setVo({ vlang: id }),
      { d: S.lang && S.lang !== 'Auto-detect' ? S.lang + ' is set on the Voice typing page and comes first; picking here clears it' : VO_LANG_D[S.vlang] });
    return [r[0], r[1], words, r[2], r[3], light, lang, r[4], r[5]];
  };
  const vs0 = P.voiceSec;
  P.voiceSec = function (vid) { const s = vs0.call(this, vid); if (this.state.voMenu) s.fc += ' open'; return s; };
  // the Speech section's Language row follows the language default while nothing is picked on Voice typing
  P.voLangSummary = function () {
    const S = this.state;
    if (S.lang !== 'Auto-detect') return S.lang;
    return { auto: 'Auto-detect: Whisper works out the language', show: 'Auto-detect, named in the pill while you speak', windows: VO_WIN + ' (from Windows)', ask: 'Asked on first use' }[S.vlang || 'auto'];
  };

  const ov0 = P.orbVals;
  P.orbVals = function (vid, o) {
    o = ov0.call(this, vid, o);
    const S = this.state;
    o.pillCls += (S.langOn ? ' lang' : '') + (S.relabel ? ' relabel' : '');
    o.lightRef = this.orbRef(vid, 'light'); o.lightORef = this.orbRef(vid, 'lightO'); o.glowRef = this.orbRef(vid, 'glow');
    return o;
  };
  const rv0 = P.renderVals;
  P.renderVals = function () {
    this.vcInit();
    const o = rv0.call(this), S = this.state;
    o.vask = {
      cls: 'vask' + (S.askOpen ? ' open' : ''),
      items: VO_ANSWERS.map(([id, t]) => ({ t, cls: S.askPick === id ? 'on' : '', pick: () => this.voAnswer(id) })),
      key: (ev) => {
        const b = Array.from(ev.currentTarget.querySelectorAll('button')), i = b.indexOf(document.activeElement);
        const d = ev.key === 'ArrowRight' || ev.key === 'ArrowDown' ? 1 : ev.key === 'ArrowLeft' || ev.key === 'ArrowUp' ? -1 : 0;
        if (!d || i < 0) return;
        ev.preventDefault(); b[(i + d + b.length) % b.length].focus();
      },
    };
    o.vaskRef = this.vaskRef || (this.vaskRef = (el) => { this.askEl = el; });
    return o;
  };
  const du0 = P.componentDidUpdate;
  P.componentDidUpdate = function () {
    du0.call(this);
    if (this.voMenuFocus && this.state.voMenu) {
      this.voMenuFocus = false;
      const it = document.querySelector('.czpage.on .czvom.open .pitem[aria-checked="true"]');
      if (it) it.focus({ preventScroll: true });
    }
    if (this.askFocus && this.state.askOpen && this.askEl) {
      this.askFocus = false;
      this.voAskPlace();
      const b = this.askEl.querySelector('button'); if (b) setTimeout(() => b.focus({ preventScroll: true }), 30);
    }
  };
})();
