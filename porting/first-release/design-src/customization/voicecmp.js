// ---------- the voice orb in the stage composer, after the voice design (OD-0091, sixth amendment) ----------
// E:/Droppy/work/voice/design/voice-composer.html and .js: the listening styles (Pill in the composer, the default;
// Cross beside the orb; Floating pill above the composer), the orb at 32 px (default) or 26 px, the attachment button
// inside the composer at its left (default) or outside in the gutter. Its CSS is carried over by gen.py from that page
// (the attachment, the glass pill, the cross); this file carries its geometry, its state targets and springs, and runs
// the exact orbs of voice-orbs.js (window.__czOrbs) as it does: a 72 px canvas, the ball at the orb's size.

const VC_K = {   // voice-composer.js K, the numbers the voice design settled
  hoverSpeed: 2.0, voiceSpeed: 2.2, procSpeed: 4.6, hoverAct: 0.3, hoverScale: 1.04, listenScale: 1.16, voiceScale: 0.04, procScale: 1.1, pressScale: 0.94,
  hoverGlow: 0.55, listenGlow: 0.45, voiceGlow: 0.4, procGlow: 0.75, actUp: 0.22, actDown: 0.65, vAttack: 0.12, vRelease: 0.45,
  popResp: 0.3, popDamp: 0.62, settleResp: 0.32, settleDamp: 0.78, hoverResp: 0.22, hoverDamp: 0.8,
};
const VC_STYLES = [{ id: 'pill', t: 'Pill in the composer', pill: 'inline' }, { id: 'cross', t: 'Cross beside the orb', pill: null }, { id: 'float', t: 'Floating pill', pill: 'float' }];
const VC_PILL = { inline: { H: 40, top: 6, font: 13.5 }, float: { H: 44, top: -52, font: 15 } };
const VC_LABELS = { listening: 'Listening\u2026', transcribing: 'Transcribing\u2026', finishing: 'Finishing\u2026' };
const VC_CV = 72;
const VC_W = { in: 472, out: 428 };   // the composer gives the outside button 44 px where it fills the column
const vcFollow = (cur, target, dt, up, down) => cur + (target - cur) * (1 - Math.exp(-dt / Math.max(1e-3, target > cur ? up : down)));
function vcSpring(o, target, dt, resp, zeta) {   // SwiftUI spring(response:dampingFraction:), as voice-composer.js
  const w = (2 * Math.PI) / resp, k = w * w, c = 2 * zeta * w, n = Math.max(1, Math.ceil(dt / 0.004)), h = dt / n;
  for (let i = 0; i < n; i++) { o.v += (-k * (o.p - target) - c * o.v) * h; o.p += o.v * h; }
  if (Math.abs(o.p - target) < 1e-4 && Math.abs(o.v) < 1e-3) { o.p = target; o.v = 0; }
}
// the simulated speaker: a breath of silence (Listening...), a phrase, a pause, a second phrase
const VC_SPEECH = [[0.75, 2.25], [2.95, 4.05]];
function vcLevel(t) {
  for (const [a, b] of VC_SPEECH) {
    if (t < a - 0.05 || t > b + 0.12) continue;
    const env = Math.min(1, Math.max(0, (t - a) / 0.08)) * Math.min(1, Math.max(0, (b + 0.12 - t) / 0.16));
    const syl = Math.max(0, Math.sin(t * 13.7) * 0.6 + Math.sin(t * 7.3 + 1) * 0.4);
    return Math.min(1, env * (0.18 + 0.8 * syl));
  }
  return 0.01;
}

(function () {
  const P = CzComponent.prototype;

  P.vcInit = function () {
    if (this.vc) return;
    let saved = {};
    try { saved = JSON.parse(window.localStorage.getItem(CZ_SAVE + '-voice') || '{}') || {}; } catch (e) { saved = {}; }
    Object.assign(this.state, {
      lstyle: VC_STYLES.some((x) => x.id === saved.lstyle) ? saved.lstyle : 'pill',
      orbSize: saved.orbSize === 26 ? 26 : 32, attach: saved.attach === 'out' ? 'out' : 'in', plab: 'listening',
    });
    CW = VC_W[this.state.attach];
    this.vc = { D: { p: this.state.orbSize, v: 0 }, scale: { p: 1, v: 0 }, flt: { p: 0, v: 0 }, act: 0, speed: 1, glow: 0, lift: 0, v: 0, raw: 0, speechAt: -9, layUntil: 0, pressed: false };
    this.vt = 0; this.vlev = 0;
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && (this.state.vmode === 'listen' || this.state.vmode === 'process')) this.voiceCancel(); });
  };
  P.vcSave = function () {
    try { window.localStorage.setItem(CZ_SAVE + '-voice', JSON.stringify({ lstyle: this.state.lstyle, orbSize: this.state.orbSize, attach: this.state.attach })); } catch (e) {}
  };
  P.vcDirty = function (s) { this.vc.layUntil = Math.max(this.vc.layUntil, performance.now() + (s || 0.7) * 1000); };
  P.setVc = function (patch, demo) {
    if (patch.attach && patch.attach !== this.state.attach) { CW = VC_W[patch.attach]; this.vcDirty(0.9); }
    if (demo && (this.state.vmode === 'listen' || this.state.vmode === 'process')) this.state.vmode = 'idle';
    this.setState(patch); this.vcSave(); this.vcDirty();
    // a new listening style is shown at once: the preview listens for a moment
    if (demo) { this.vt = 0; this.vc.speechAt = -9; this.setState({ vmode: 'listen', vN: 0, plab: 'listening' }); this.setMode('rest'); }
  };

  P.voiceTap = function () {
    const v = this.state.vmode;
    if (v === 'listen') { this.vt = 0; this.setState({ vmode: 'process', plab: 'finishing' }); }
    else if (v === 'idle' || v === 'shown') { this.vt = 0; this.vc.speechAt = -9; this.setState({ vmode: 'listen', vN: 0, plab: 'listening' }); }
    this.vcDirty();
  };
  P.voiceCancel = function () { this.vt = 0; this.setState({ vmode: 'idle', vN: 0 }); this.vcDirty(); };

  P.voiceTick = function (dt) {
    this.vcInit();
    const S = this.state, v = S.vmode;
    if (v === 'idle') { this.vlev = 0; return; }
    this.vt += dt;
    if (v === 'listen') {
      this.vlev = vcLevel(this.vt);
      if (this.vlev > 0.16) this.vc.speechAt = this.vt;
      const lab = this.vt - this.vc.speechAt < 0.6 ? 'transcribing' : 'listening';
      if (lab !== S.plab) { this.setState({ plab: lab }); this.vcDirty(); }
      if (this.vt > 4.6) { this.vt = 0; this.setState({ vmode: 'process', plab: 'finishing' }); this.vcDirty(); }
    } else if (v === 'process') {
      this.vlev = 0;
      if (this.vt > 1.1) { this.vt = 0; this.setState({ vmode: 'insert', vN: 0 }); this.vcDirty(); }
    } else if (v === 'insert') {
      const n = Math.min(CZ_VTEXT.length, Math.floor(this.vt * 90));
      if (n !== S.vN) this.setState({ vN: n });
      if (n >= CZ_VTEXT.length) { this.vt = 0; this.setState({ vmode: S.afterVoice === 'send' ? 'sending' : 'shown' }); }
    } else if (v === 'sending') {
      if (this.vt > 0.55) { this.vt = 0; this.setState({ vmode: 'sent' }); }
    } else if (v === 'sent') {
      if (this.vt > 2.6) { this.vt = 0; this.setState({ vmode: 'idle' }); }
    }
  };

  // one orb on one canvas: the exact orb (voice-orbs.js) when it has loaded, else the marked placeholder
  P.orbFor = function (el, kind, size, ball) {
    const k = CZ_ORBS.find((o) => o.id === kind);
    const reg = this.orbReg && this.orbReg.find((o) => o.id === k.rid);
    const want = (reg ? 'r:' : 'p:') + kind;
    if (el.__ok !== want) {
      if (el.__orb) el.__orb.dispose();
      el.__orb = reg ? reg.make(el, { size, ball, dark: this.dark(), colors: 'preset', context: 'shared' }) : czPlaceholderOrb(el, kind);
      el.__ok = want; el.__sz = reg ? size + '|' + ball : '';
    }
    const sz = size + '|' + ball;
    if (el.__sz !== sz) { el.__sz = sz; if (reg) el.__orb.resize(size, ball); else el.__orb.resize(size); }
    if (el.__dk !== this.dark()) { el.__dk = this.dark(); el.__orb.setDark(this.dark()); }
    return el.__orb;
  };
  P.orbRef = function (vid, key) {
    const k = vid + '|' + key;
    if (!this.orbRefs[k]) this.orbRefs[k] = (el) => {
      const E = this.orbEls[vid] || (this.orbEls[vid] = {});
      if (E[key] && E[key] !== el && E[key].__orb) { E[key].__orb.dispose(); E[key].__orb = null; E[key].__ok = null; }
      E[key] = el;
    };
    return this.orbRefs[k];
  };

  P.orbTick = function (now, dt, vis) {
    this.vcInit();
    const S = this.state, V = this.vc, K = VC_K, reduce = !!(this.mq && this.mq.matches), t = now / 1000;
    const mode = S.vmode === 'listen' ? 'listening' : S.vmode === 'process' ? 'processing' : 'rest';
    V.raw = mode === 'listening' ? this.vlev : 0;
    V.v = vcFollow(V.v, V.raw, dt, K.vAttack, K.vRelease);
    const hov = S.orbHov && mode === 'rest';
    let tSpeed = 1, tAct = 0, tScale = 1, tGlow = 0, tLift = 0, resp = K.settleResp, damp = K.settleDamp;
    if (mode === 'listening') { tSpeed = 1 + K.voiceSpeed * V.v; tAct = 1; tScale = K.listenScale + K.voiceScale * V.v; tGlow = K.listenGlow + K.voiceGlow * V.v; tLift = 1; resp = K.popResp; damp = K.popDamp; }
    else if (mode === 'processing') { tSpeed = K.procSpeed; tAct = 1; tScale = K.procScale; tGlow = K.procGlow; tLift = 0.8; resp = K.popResp; damp = 0.75; }
    else if (hov) { tSpeed = K.hoverSpeed; tAct = K.hoverAct; tScale = K.hoverScale; tGlow = K.hoverGlow; tLift = 0.15; resp = K.hoverResp; damp = K.hoverDamp; }
    if (V.pressed) { tScale = K.pressScale * (mode === 'listening' ? K.listenScale : 1); resp = 0.18; damp = 0.8; }
    V.act = vcFollow(V.act, tAct, dt, K.actUp / 3, K.actDown / 3);
    V.speed = vcFollow(V.speed, tSpeed, dt, mode === 'listening' ? 0.05 : 0.2, mode === 'listening' ? 0.05 : 0.5);
    V.glow = vcFollow(V.glow, tGlow, dt, 0.12, 0.25);
    V.lift = vcFollow(V.lift, tLift, dt, 0.12, 0.3);
    vcSpring(V.scale, tScale, dt, resp, damp);
    const dWas = V.D.p;
    vcSpring(V.D, S.orbSize, dt, 0.34, 0.86);
    const live = mode !== 'rest', style = VC_STYLES.find((x) => x.id === S.lstyle) || VC_STYLES[0];
    vcSpring(V.flt, style.pill === 'float' && live ? 1 : 0, dt, live ? 0.36 : 0.34, live ? 0.8 : 0.86);
    if (dWas !== V.D.p || V.flt.v !== 0 || V.flt.p !== 0) this.vcDirty(0.1);
    const v4 = V.v;
    const state = { act: V.act, speed: V.speed, scale: V.scale.p, glow: V.glow, lift: V.lift, bands: mode === 'listening' ? { all: v4, low: v4 * 0.85, mid: v4, high: v4 * 0.55 } : null, reduceMotion: reduce, charge: V.act };
    for (const id in vis) {
      const vid = id.slice(1), E = this.orbEls[vid];
      if (!E) continue;
      for (const k of CZ_ORBS) {
        const c = E['c-' + k.id], tl = E['t-' + k.id];
        if (c && c.isConnected && (k.id === S.orb || now - (this.orbAt || -1e9) < 700)) this.orbFor(c, k.id, VC_CV, V.D.p).frame(t, state);
        if (tl && tl.isConnected) this.orbFor(tl, k.id, 52, 30).frame(t, { act: 0, speed: 1, scale: 1, glow: k.id === S.orb ? 0.25 : 0, reduceMotion: reduce, charge: 0 });
      }
      if (now < V.layUntil) this.vcLayout(vid, live, style);
    }
  };

  // voice-composer.js's field and pill geometry, in this composer's coordinates (its width is CW)
  P.vcLayout = function (vid, live, style) {
    const E = this.orbEls[vid], V = this.vc, S = this.state;
    if (!E || !E.slot || !E.slot.isConnected) return;
    const tools = E.slot.parentNode, Wc = CW, D = V.D.p;
    const tl0 = tools.offsetLeft + E.slot.offsetLeft, cx = tl0 + D / 2;
    E.slot.style.width = E.slot.style.height = D.toFixed(2) + 'px';
    const ty = (V.flt.p * (VC_PILL.float.top + VC_PILL.float.H / 2 - 26)).toFixed(2);
    if (E.move && E.move.__ty !== ty) { E.move.__ty = ty; E.move.style.transform = Math.abs(+ty) < 0.01 ? '' : 'translateY(' + ty + 'px)'; }
    let right = Wc - (tl0 - 12);
    const vp = E.pill;
    if (vp && style.pill) {
      const Pp = VC_PILL[style.pill], lw = this.tw(VC_LABELS[S.plab] || '', Pp.font, 600, UIF) + (this.vcNameW ? this.vcNameW(Pp.font) : 0);   // + the language name (OD-0097 B)
      const Wp = Pp.H + 8 + lw + 8 + 24 + 8, w = live ? Wp : Pp.H;
      vp.style.top = Pp.top + 'px'; vp.style.height = Pp.H + 'px';
      vp.style.left = (style.pill === 'float' ? cx - Pp.H / 2 : cx + Pp.H / 2 - w).toFixed(1) + 'px'; vp.style.width = w.toFixed(1) + 'px';
      vp.style.setProperty('--fs', Pp.font + 'px');
      if (live && style.pill === 'inline') right = Wc - (cx + Pp.H / 2 - Wp - 10);
    } else if (live) right = Wc - (cx - (D * VC_K.listenScale / 2 + 7) - 24 - 10);
    if (E.field) { E.field.style.left = (S.attach === 'out' ? 14 : 48) + 'px'; E.field.style.right = Math.round(right) + 'px'; }
    E.slot.style.setProperty('--xo', (-(D * VC_K.listenScale / 2 + 7)).toFixed(1) + 'px');
    if (vp) vp.style.setProperty('--oR', (D / 2).toFixed(2) + 'px');   // the pill's mask around the orb (OD-0097 amendment)
  };

  P.orbVals = function (vid, o) {
    this.vcInit();
    const S = this.state, v = S.vmode, live = v === 'listen' || v === 'process', style = VC_STYLES.find((x) => x.id === S.lstyle) || VC_STYLES[0];
    o.cmpCls += live ? ' live' : '';
    // the floating pill takes the files line's place while it is open: in a composer this narrow its right-opening
    // pill would cross the centred line (voice-composer.html opens it to the right of the line at 820 px)
    o.pvExtra = (S.attach === 'out' ? ' v-out' : '') + (style.pill ? ' v-pill' : '') + (live && style.pill === 'float' ? ' fpo' : '');
    o.orbAria = v === 'listen' ? 'Stop and finish the text' : v === 'process' ? 'Finishing the text' : 'Start voice input (' + CZ_ORBS.find((k) => k.id === S.orb).name + ')';
    o.orbPressed = v === 'listen' ? 'true' : 'false';
    o.orbTap = () => { this.vc.pressed = false; this.voiceTap(); };
    o.orbIn = () => { if (!this.state.orbHov) this.setState({ orbHov: true }); };
    o.orbOut = () => { this.vc.pressed = false; if (this.state.orbHov) this.setState({ orbHov: false }); };
    o.orbDown = () => { if (this.state.vmode !== 'process') this.vc.pressed = true; };
    o.orbUp = () => { this.vc.pressed = false; };
    o.orbKey = (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); this.voiceTap(); } };
    o.cancel = () => this.voiceCancel();
    o.xTab = live && !style.pill ? '0' : '-1';
    o.pxTab = live && style.pill ? '0' : '-1';
    o.pillCls = 'vpill' + (live && style.pill ? ' open' : '') + (style.pill === 'float' ? ' float' : '');
    o.plabs = ['listening', 'transcribing', 'finishing'].map((k) => ({ t: VC_LABELS[k], cls: 'pl' + (S.plab === k ? ' on' : '') }));
    o.slotRef = this.orbRef(vid, 'slot'); o.moveRef = this.orbRef(vid, 'move'); o.pillRef = this.orbRef(vid, 'pill'); o.fieldRef = this.orbRef(vid, 'field');
    o.holdRef = this.orbRef(vid, 'hold');
    o.orbs = CZ_ORBS.map((k) => ({ cls: 'orbcv' + (k.id === S.orb ? ' on' : ''), ref: this.orbRef(vid, 'c-' + k.id) }));
    return o;
  };

  // the voice section's rows: the orb, how listening shows, its size, where the attachment goes, after voice input
  const orbRows0 = P.orbRows;
  P.orbRows = function (vid) {
    this.vcInit();
    const S = this.state, rows = orbRows0.call(this, vid);   // [orb, after voice input, hint]
    const lst = this.seg('Listening style', VC_STYLES.map((x) => ({ id: x.id, t: x.t })), S.lstyle, (id) => this.setVc({ lstyle: id }, true), { sm: true });
    const listen = this.row('Seg', {
      t: 'Listening style', stack: true,
      d: this.dx('d-lstyle', S.lstyle === 'pill' ? 'The orb opens into a glass pill that says what it hears; the cross at its far end cancels' : S.lstyle === 'cross' ? 'A cross slides out beside the orb to cancel; the text field shows the words' : 'The orb lifts into a glass pill above the composer, the text keeps its full width'),
      segs: [{ cls: 'czsgw on', g: lst }],
    });
    const size = this.segRow('vc.size', 'Orb size', S.orbSize === 32 ? '32 px, the default' : '26 px, a quieter orb', [{ on: true, g: this.seg('Orb size', [{ id: 32, t: '32 px' }, { id: 26, t: '26 px' }], S.orbSize, (id) => this.setVc({ orbSize: id }), { sm: true }) }]);
    const att = this.segRow('vc.attach', 'Attachment button', S.attach === 'in' ? 'At the composer\u2019s left, inside it' : 'In the margin beside the composer', [{ on: true, g: this.seg('Attachment button', [{ id: 'in', t: 'Inside the composer' }, { id: 'out', t: 'Outside' }], S.attach, (id) => this.setVc({ attach: id }), { sm: true }) }]);
    const hint = this.row('Note', { t: 'Click the orb in the preview to try it: it listens until you click it again (the cross or Esc cancels), then the words arrive.' });
    return [rows[0], listen, size, att, rows[1], hint];
  };

  // the preview's composer width follows the attachment button (eased: --cl, --cw and --cwk are registered)
  const rv = P.renderVals;
  P.renderVals = function () {
    this.vcInit();
    const o = rv.call(this), out = this.state.attach === 'out';
    // the beam's sheet for the other width is made ahead, so switching the attachment never waits on a new sheet
    const w0 = CW, alt = out ? VC_W.in : VC_W.out;
    CW = alt; const cfg = this.beamCfg(this.state.beam), key = bbKey(cfg); CW = w0;
    if (!this.bbCss[key]) this.bbCss[key] = bbSheet(cfg);
    if (o.designCss.indexOf('.bbk-' + key + ' ') < 0) o.designCss += ' ' + this.bbCss[key];
    o.pvVars = '--wdc: ' + o.wdc + '; --cl: ' + (out ? 56 : 12) + 'px; --cw: ' + CW + 'px; --cwk: ' + fmt(CW / 100, 2);
    if (o.pvB) o.pvB.cls += (o.pvB.inst[0] && o.pvB.inst[0].pvExtra) || '';
    if (o.pvA) o.pvA.cls += (o.pvA.inst[0] && o.pvA.inst[0].pvExtra) || '';
    return o;
  };
  const dm = P.componentDidMount;
  P.componentDidMount = function () { dm.call(this); this.vcDirty(1.5); if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => this.vcDirty(0.5)); };
  // the geometry is measured again when something it depends on changes (the model's name moves the tools)
  const du = P.componentDidUpdate;
  P.componentDidUpdate = function () {
    du.call(this);
    const S = this.state, sig = [S.maker, S.vmode, S.plab, S.lstyle, S.attach, S.orbSize, S.page, S.dark].join('|');
    if (sig !== this.vcSig) { this.vcSig = sig; this.vcDirty(0.6); }
  };
})();

// a beam stylesheet is drawn for one composer width: the width joins its key, so a change cross-fades (beam.js slots)
const vcBbKey = bbKey;
bbKey = (c) => vcBbKey(c) + '-w' + c.W;
