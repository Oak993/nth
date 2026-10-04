// ---------- Settings > Voice typing (OD-0091, OD-0092): engine, speech model, hardware check, language ----------
// A sidebar page of its own, beside Customization: the orb's look and what happens after voice input stay in
// Customization (as ruled); what turns speech into text lives here. Everything is simulated: no microphone, no
// hardware access, no download. Model facts: E:/Droppy/work/voice/RESEARCH.md section 1.3 (whisper.cpp ggml files)
// and OpenAI's README relative speeds; the two simulated PCs are the owner's desktop and a CPU-only laptop.

// OpenAI Whisper only (owner): the six models, each with its quantized versions and, tiny to medium, English only.
const VM_FAMILIES = [
  { id: 'whisper', t: 'Whisper', sub: 'OpenAI, 99 languages. Speed as on a graphics card; the processor alone is slower', models: [
    { id: 'tiny', name: 'Tiny', params: '39 M', speed: 5, acc: 1, en: true, vers: [['q5', 'Compact', 32.2], ['q8', 'Balanced', 43.5], ['f16', 'Full', 77.7]] },
    { id: 'base', name: 'Base', params: '74 M', speed: 5, acc: 2, en: true, vers: [['q5', 'Compact', 59.7], ['q8', 'Balanced', 81.8], ['f16', 'Full', 148]] },
    { id: 'small', name: 'Small', params: '244 M', speed: 4, acc: 3, en: true, vers: [['q5', 'Compact', 190], ['q8', 'Balanced', 264], ['f16', 'Full', 488]] },
    { id: 'medium', name: 'Medium', params: '769 M', speed: 2, acc: 4, en: true, tr: true, vers: [['q5', 'Compact', 539], ['q8', 'Balanced', 823], ['f16', 'Full', 1530]] },
    { id: 'turbo', name: 'Large v3 Turbo', params: '809 M', speed: 4, acc: 5, vers: [['q5', 'Compact', 574], ['q8', 'Balanced', 874], ['f16', 'Full', 1620]] },
    { id: 'large', name: 'Large v3', params: '1.55 B', speed: 1, acc: 5, tr: true, vers: [['q5', 'Compact', 1080], ['f16', 'Full', 3100]] },
  ] },
];
const VM_ALL = [].concat(...VM_FAMILIES.map((f) => f.models));
const VM_RATE = 90;   // simulated download speed, MB per second
const VM_PCS = {
  desk: { t: 'Desktop with RTX 4080', hw: [['Processor', 'AMD Ryzen 7 9800X3D, 8 cores'], ['Memory', '31 GB'], ['Graphics', 'NVIDIA GeForce RTX 4080, 16 GB'], ['GPU backend', 'Vulkan, working']],
    rec: 'turbo|q5|m', why: 'Your RTX 4080 runs it on the graphics card: text about 1 to 2 seconds after you stop speaking.' },
  laptop: { t: 'Laptop, no GPU', hw: [['Processor', 'Intel Core i5-1235U, 10 cores'], ['Memory', '16 GB'], ['Graphics', 'Intel Iris Xe, shared memory'], ['GPU backend', 'None working, runs on the processor']],
    rec: 'small|q5|m', why: 'On the processor alone Small answers in about 3 to 6 seconds; Large v3 Turbo would take 15 seconds or more.' },
};
const VM_LANGS = ['Auto-detect', 'English', 'Hindi', 'Urdu', 'Arabic', 'Bengali', 'Chinese', 'French', 'German', 'Indonesian', 'Italian', 'Japanese', 'Korean', 'Persian', 'Portuguese', 'Punjabi', 'Russian', 'Spanish', 'Tamil', 'Turkish', 'Vietnamese'];
const VM_SIDE_ICON = 'M1.8 7h.01M4.2 5.2v3.6M6.6 3v8M9 4.6v4.8M11.4 6.1v1.8';
const vmKey = (m, v, en) => m + '|' + v + '|' + (en ? 'en' : 'm');
const vmParse = (k) => { const [m, v, e] = k.split('|'); return { m, v, en: e === 'en' }; };
const vmModel = (id) => VM_ALL.find((x) => x.id === id);
const vmMB = (k) => { const p = vmParse(k), M = vmModel(p.m), V = M.vers.find((x) => x[0] === p.v) || M.vers[0]; return V[2]; };
const vmSize = (mb) => (mb >= 1000 ? fmt(mb / 1000, 2) + ' GB' : Math.round(mb) + ' MB');
const vmName = (k) => { const p = vmParse(k), M = vmModel(p.m), V = M.vers.find((x) => x[0] === p.v); return M.name + (p.en ? ' (English only)' : '') + ', ' + V[1].toLowerCase() + ' ' + vmSize(V[2]); };

Object.assign(CzComponent.prototype, {
  vInit() {
    if (this.vReady) return;
    this.vReady = true;
    // as on a first install: the check ran by itself on the owner's desktop and picked and installed Large v3 Turbo
    Object.assign(this.state, {
      page: this.state.page || 'cz', pc: 'desk', engine: 'local', lang: 'Auto-detect', langMenu: false, keyOpen: false,
      hw: { phase: 'done', pc: 'desk', shown: 4, first: true, decided: true },
      installed: ['turbo|q5|m'], selected: 'turbo|q5|m', dl: {}, pending: null, vsel: {}, venOn: {}, vopen: null,
    });
    this.hwT = 0; this.dlT = 0;
  },

  setPage(p) {
    if (p === this.state.page) return;
    if (p !== 'cz') { this.varAt[this.state.variant] = performance.now(); this.left(this.state.variant); this.entering('V'); }
    else { this.left('V'); this.entering(this.state.variant); }
    this.setState({ page: p, menu: null, langMenu: false });
  },

  // the simulated check: each line resolves in turn, then the recommendation folds open
  hwRun() {
    const H = this.state.hw;
    if (H.phase === 'running') return;
    this.hwT = 0;
    this.setState({ hw: { phase: 'running', pc: this.state.pc, shown: 0, first: false, decided: false } });
  },
  setPc(pc) {
    if (pc === this.state.pc) return;
    this.setState({ pc, hw: { phase: 'idle', pc, shown: 0, first: false, decided: false } });
  },
  vDownload(k, select) {
    const S = this.state;
    if (S.installed.indexOf(k) >= 0) { if (select) this.setState({ selected: k }); return; }
    const dl = Object.assign({}, S.dl);
    if (!(k in dl)) dl[k] = 0;
    this.setState({ dl, pending: select ? k : S.pending });
  },
  vCancel(k) {
    const dl = Object.assign({}, this.state.dl); delete dl[k];
    this.setState({ dl, pending: this.state.pending === k ? null : this.state.pending });
  },
  vTick(dt) {
    const S = this.state;
    if (!S.hw) return;
    if (S.hw.phase === 'running') {
      this.hwT += dt;
      const shown = Math.min(4, Math.floor(this.hwT / 0.62));
      if (this.hwT > 4 * 0.62 + 0.35) this.setState({ hw: Object.assign({}, S.hw, { phase: 'done', shown: 4 }) });
      else if (shown !== S.hw.shown) this.setState({ hw: Object.assign({}, S.hw, { shown }) });
    }
    const keys = Object.keys(S.dl);
    if (!keys.length) return;
    this.dlT += dt;
    const dl = Object.assign({}, S.dl);
    let inst = S.installed, sel = S.selected, pend = S.pending, done = false;
    for (const k of keys) {
      dl[k] = Math.min(1, dl[k] + dt * VM_RATE / vmMB(k));
      if (dl[k] >= 1) { delete dl[k]; inst = inst.concat([k]); done = true; if (pend === k) { sel = k; pend = null; } }
    }
    this.vDl = dl;
    if (done || this.dlT > 0.1) { this.dlT = 0; this.setState({ dl, installed: inst, selected: sel, pending: pend }); }
    else this.state.dl = dl;
  },

  voicePcSeg() { return this.seg('Simulated PC', [{ id: 'desk', t: 'Desktop, RTX 4080' }, { id: 'laptop', t: 'Laptop, no GPU' }], this.state.pc, (id) => this.setPc(id)); },

  voiceVals() {
    this.vInit();
    const S = this.state, local = S.engine === 'local', H = S.hw, pc = VM_PCS[H.pc];
    const rec = pc.rec;
    // the hardware card
    const running = H.phase === 'running', done = H.phase === 'done';
    const hw = {
      d: this.dx('v-hwd', running ? 'Checking this computer' : done ? (H.first ? 'Checked by itself when voice typing was set up' : 'Checked just now') : 'Not checked on this computer yet'),
      btnc: 'czbtn hwgo' + (running ? ' busy' : ''), lab: this.xf('v-hwlab', running ? 'Checking' : done ? 'Check again' : 'Check my hardware'), dis: running ? 'true' : 'false',
      run: () => this.hwRun(),
      listFc: 'czf' + (H.phase === 'idle' ? ' off' : ''),
      items: VM_PCS[H.pc].hw.map((x, i) => ({
        k: x[0], v: this.xf('v-hw' + i, i < H.shown ? x[1] : ''),
        cls: 'hwi' + (i < H.shown ? ' ok' : running && i === H.shown ? ' busy' : '') + (running || i < H.shown ? ' on' : ''),
      })),
      recFc: 'czf' + (done ? '' : ' off'),
      recName: this.xf('v-recn', vmName(rec)), why: this.xf('v-recw', pc.why),
      useFc: 'czf' + (done && S.selected !== rec && S.pending !== rec && !H.decided ? '' : ' off'),
      stateT: this.xf('v-recs', S.selected === rec ? 'In use' : S.pending === rec ? 'Downloading, then in use' : 'You kept ' + vmModel(vmParse(S.selected).m).name),
      stateFc: 'czf' + (done && (S.selected === rec || S.pending === rec || H.decided) ? '' : ' off'),
      use: () => { this.vDownload(rec, true); this.setState({ hw: Object.assign({}, this.state.hw, { decided: true }) }); },
      keep: () => this.setState({ hw: Object.assign({}, this.state.hw, { decided: true }) }),
      keepT: 'Keep ' + vmModel(vmParse(S.selected).m).name,
    };
    // the model list
    let total = 0;
    S.installed.forEach((k) => { total += vmMB(k); });
    const groups = VM_FAMILIES.map((f) => ({
      t: f.t, sub: f.sub,
      rows: f.models.map((M, i) => {
        const v = S.vsel[M.id] || M.vers[0][0], en = !!(M.en && S.venOn[M.id]);
        const k = vmKey(M.id, v, en), V = M.vers.find((x) => x[0] === v);
        const inst = S.installed.indexOf(k) >= 0, use = S.selected === k, prog = k in S.dl, pct = prog ? S.dl[k] : 0;
        const st = use ? 'in' : prog ? 'prog' : inst ? 'use' : 'dl';
        const open = S.vopen === M.id;
        const dots = (n) => [1, 2, 3, 4, 5].map((j) => (j <= n ? 'on' : ''));
        return {
          dvc: 'dv' + (i === 0 ? ' none' : ''), name: M.name, recc: 'vbadge' + (rec.split('|')[0] === M.id ? ' on' : ''),
          meta: this.xf('v-meta-' + M.id, V[1] + ', ' + vmSize(V[2]) + (en ? ', English only' : '')),
          aria: M.name + ': speed ' + M.speed + ' of 5, accuracy ' + M.acc + ' of 5',
          sp: dots(M.speed), ac: dots(M.acc), open: open ? 'true' : 'false', verc: 'mver' + (open ? ' open' : ''),
          togV: () => this.setState({ vopen: open ? null : M.id }),
          verFc: 'czf' + (open ? '' : ' off'),
          verSeg: this.seg('Version of ' + M.name, M.vers.map((x) => ({ id: x[0], t: x[1] + ' \u00b7 ' + vmSize(x[2]) })), v, (id) => this.setState({ vsel: Object.assign({}, S.vsel, { [M.id]: id }) }), { sm: true }),
          hasEn: !!M.en, enOn: en ? 'true' : 'false', enc: 'tog' + (en ? ' on' : ''), enTog: () => this.setState({ venOn: Object.assign({}, S.venOn, { [M.id]: !en }) }),
          note: M.tr ? 'Can also translate speech into English.' : M.id === 'turbo' ? 'About as accurate as Large v3, far quicker; it does not translate.' : M.en ? 'English only is a little more accurate for English and knows no other language.' : '',
          s: ['in', 'prog', 'use', 'dl'].reduce((o, x) => { o[x] = 'mso' + (st === x ? ' on' : ''); return o; }, {}),
          pct: fmt(pct * 100, 1), pctT: Math.floor(pct * 100) + '%', barc: 'mbar' + (prog ? ' on' : ''),
          dl: () => this.vDownload(k, false), sel: () => this.vDownload(k, true), cancel: () => this.vCancel(k),
          rowc: 'row mr' + (use ? ' cur' : ''),
        };
      }),
    }));
    const langItems = VM_LANGS.map((L) => ({ t: L, on: S.lang === L ? 'true' : 'false', chk: 'pcheck' + (S.lang === L ? ' on' : ''), pick: () => { this.setState({ lang: L, langMenu: false }); } }));
    const langD = S.lang === 'Auto-detect' ? 'Whisper works out the language; pick one if Hindi comes out as Urdu' : S.lang === 'Hindi' ? 'Written in Devanagari' : S.lang === 'Urdu' ? 'Written in Urdu script, right to left' : 'Every dictation is read as ' + S.lang;
    return {
      pageV: 'czpage' + (S.page === 'voice' ? ' on' : ''), scrollV: 'czscroll' + (S.sc.V ? ' sc' : ''), onScrollV: this.scrollRef('V'),
      v: {
        engSeg: this.seg('Engine', [{ id: 'local', t: 'On this PC' }, { id: 'cloud', t: 'OpenAI cloud' }], S.engine, (id) => this.setState({ engine: id }), { sm: true }),
        engD: this.dx('v-eng', local ? 'Your voice never leaves this computer' : 'Sent to OpenAI with your own key, about half a US cent a minute'),
        cloudFc: 'czf' + (local ? ' off' : ''), localFc: 'czf' + (local ? '' : ' off'),
        keyFc: 'czf' + (S.keyOpen && !local ? '' : ' off'), keyLab: this.xf('v-keylab', S.keyOpen ? 'Cancel' : 'Add key'), keyTog: () => this.setState({ keyOpen: !S.keyOpen }),
        langLab: this.xf('v-lang', S.lang), langW: fmt(this.tw(S.lang, 13, 400, UIF) + 1, 2), langD: this.dx('v-langd', langD),
        langCls: 'czms czlang' + (S.langMenu ? ' open' : ''), langOpen: S.langMenu ? 'true' : 'false', langPop: 'czpop' + (S.langMenu ? ' open' : ''),
        langTog: () => { this.menuFocus = !S.langMenu; this.setState({ langMenu: !S.langMenu }); },
        langKey: (ev) => {
          if (ev.key === 'Escape' && this.state.langMenu) { ev.preventDefault(); this.setState({ langMenu: false }); const c = ev.currentTarget.querySelector('.czmc'); if (c) c.focus(); return; }
          if (!this.state.langMenu) return;
          const items = Array.from(ev.currentTarget.querySelectorAll('.pitem')), i = items.indexOf(document.activeElement);
          const go = { ArrowDown: i + 1, ArrowUp: i - 1, Home: 0, End: items.length - 1 }[ev.key];
          if (go === undefined) return;
          ev.preventDefault(); const t = items[(go + items.length) % items.length]; if (t) t.focus();
        },
        langs: langItems,
        toCz: () => this.setPage('cz'),
        hw, groups, total: this.xf('v-total', 'Installed: ' + vmSize(total) + ', in Marrow Code\u2019s models folder'),
        priv: this.xf('v-priv', local ? 'Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.' : 'Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.'),
      },
      pcSeg: this.voicePcSeg(),
    };
  },
});

// the page joins the board: the sidebar, the chrome title, the composer clock and the menus follow it
(function () {
  const P = CzComponent.prototype, rv = P.renderVals, tk = P.tick, du = P.componentDidUpdate, dm = P.componentDidMount, sv = P.setVariant;
  P.setVariant = function (v) { if (this.state.page !== 'cz') this.setPage('cz'); sv.call(this, v); };
  P.renderVals = function () {
    this.vInit();
    const o = rv.call(this), S = this.state, voice = S.page === 'voice';
    o.showV = this.shown('V');
    if (o.showV) Object.assign(o, this.voiceVals());
    else Object.assign(o, { pageV: 'czpage', scrollV: 'czscroll', onScrollV: this.scrollRef('V'), v: {}, pcSeg: this.voicePcSeg() });
    o.side = CZ_SIDE.map(([name, d]) => ({ name, d, cls: 'witem' + (name === 'Customization' && !voice ? ' sel' : ''), go: name === 'Customization' ? () => this.setPage('cz') : null, nav: name === 'Customization' }));
    o.side.splice(4, 0, { name: 'Voice typing', d: VM_SIDE_ICON, cls: 'witem' + (voice ? ' sel' : ''), go: () => this.setPage('voice'), nav: true });
    o.side.forEach((x) => { x.cls += x.nav ? ' nav' : ''; x.tab = x.nav ? '0' : null; x.role = x.nav ? 'button' : null; x.key = x.nav ? (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); x.go(); } } : null; });
    if (voice) { o.pageA = o.pageB = o.pageC = 'czpage'; }
    o.scrollB = o.scrollB + ' pb';
    o.ctitle = this.xf('ctitle', voice ? 'Voice typing' : 'Customization');
    o.titleCls = 'cztitle' + (S.sc[voice ? 'V' : S.variant] ? ' on' : '');
    o.accCls = 'czacc' + (S.variant === 'B' && !voice ? ' on' : '');
    o.voiceLink = this.xf('v-link', vmModel(vmParse(S.selected).m).name + ' \u00b7 ' + S.lang + ' \u00b7 ' + (S.engine === 'local' ? 'On this PC' : 'OpenAI cloud'));
    o.toVoice = () => this.setPage('voice');
    o.vnote = 'czvn' + (voice ? ' on' : '');
    o.notes.forEach((n) => { if (voice) n.cls = 'czvn'; });
    return o;
  };
  P.tick = function (now) {
    const dt = this.lastVT == null ? 0 : Math.min(0.05, Math.max(0, (now - this.lastVT) / 1000));
    this.lastVT = now;
    this.vTick(dt);
    tk.call(this, now);
  };
  P.componentDidUpdate = function () {
    du.call(this);
    if (this.menuFocus && this.state.langMenu) {
      this.menuFocus = false;
      const pop = document.querySelector('.czlang.open .czpop'), it = pop && pop.querySelector('.pitem[aria-checked="true"]');
      if (it) { const l = it.parentNode; l.scrollTop = Math.max(0, it.offsetTop - (l.clientHeight - it.offsetHeight) / 2); it.focus({ preventScroll: true }); }
    }
  };
  P.componentDidMount = function () {
    dm.call(this);
    document.addEventListener('pointerdown', (ev) => { if (this.state.langMenu && !(ev.target.closest && ev.target.closest('.czlang'))) this.setState({ langMenu: false }); }, true);
  };
})();
