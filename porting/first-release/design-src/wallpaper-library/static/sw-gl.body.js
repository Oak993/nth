(function () {
  const MAXN = 16;
  const VERT = `#version 300 es
void main(){ vec2 a = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); gl_Position = vec4(a*2. - 1., 0., 1.); }`;
/*@@GLASS@@*/

  function compile(gl, type, src, tag) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error((tag || 'shader') + ': ' + gl.getShaderInfoLog(s));
    return s;
  }
  function program(gl, fs, tag) {
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, VERT, tag));
    gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, fs, tag));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error('link ' + (tag || '') + ': ' + gl.getProgramInfoLog(p));
    const u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) { const info = gl.getActiveUniform(p, i); u[info.name.replace(/\[0\]$/, '')] = gl.getUniformLocation(p, info.name); }
    return { p, u };
  }
  const smooth = (x) => x * x * (3 - 2 * x);
  const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
  // The premium options (OPTIONS-FORMAT.md). AMP: the largest Perspective displacement, in height units (0.015 is about 6 px
  // at 800 px high), reached at depth 1. FOCUS_SIG: the gaussian sigma of Focus 1, as a share of the picture height. WASH: the
  // strength of the brightness wash the engine adds for a change of the light direction.
  const AMP = 0.015, FOCUS_SIG = 0.022, WASH = 0.028;
  // The engine's side of one wallpaper (a thumbnail, the timer and the viewer all use it): its option data with the defaults
  // filled in, as the uniforms. over replaces fields.
  function wallX(w, over) {
    const light = (w.light && w.light.def) || [-0.35, 0.9];
    return Object.assign({ tilt: [0, 0], tg: w.persp && w.persp.own ? 0 : 1, light, light0: light, lw: w.light && w.light.own ? 0 : WASH, focus: 0, blur: 0, dream: 0 }, over || {});
  }
  // Dynamic: the palette (6 OKLab colours, 18 floats), u_dark and the light direction at a clock hour, for dark amount dk (0 light
  // .. 1 dark) and Dynamic strength k (0 off .. 1 on). Off, it returns the inputs untouched.
  function dynState(w, pal, dk, clock, k, out) {
    const light = (w.light && w.light.def) || [-0.35, 0.9];
    if (!w.dyn || !(k > 0.0005)) return { pal, dark: dk, light };
    const d = SWC.dynDelta(w.dyn, clock, dk, k, light);
    return { pal: SWC.shiftPal(pal, d, out), dark: clamp(dk + d.dn, 0, 1), light: [light[0] + d.dlx, light[1] + d.dly] };
  }
  function setWallUniforms(gl, U, w, h, dpr, pal, dark, grain, opt, X) {
    gl.uniform2f(U.u_res, w, h); gl.uniform1f(U.u_dpr, dpr);
    gl.uniform3fv(U.u_pal, pal); gl.uniform1f(U.u_dark, dark);
    gl.uniform1f(U.u_grain, X.blur > 0 ? 0 : grain); gl.uniform1f(U.u_opt, opt);   // with the built-in blur the grain is laid on after it
    gl.uniform2f(U.u_tilt, X.tilt[0], X.tilt[1]); gl.uniform1f(U.u_tg, X.tg);
    gl.uniform2f(U.u_light, X.light[0], X.light[1]); gl.uniform2f(U.u_light0, X.light0[0], X.light0[1]);
    gl.uniform1f(U.u_lw, X.lw); gl.uniform1f(U.u_focus, X.focus);
  }
  // Focus, the built-in: a two-pass gaussian over the rendered wallpaper (13 taps at half-sigma spacing, bilinear), then the
  // film grain and the dither laid on after it so the grain stays sharp. 26 reads a pixel, and only while Focus > 0.
  const BLUR_FS = `#version 300 es
precision highp float;
uniform sampler2D u_src;
uniform vec2 u_px;
uniform vec2 u_dir;
uniform float u_sig;
uniform float u_grain;
uniform float u_last;
uniform float u_dream;
out vec4 o;
float h21(vec2 p){ vec3 q = fract(vec3(p.xyx)*.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y)*q.z); }
void main(){
  vec2 uv = gl_FragCoord.xy/u_px;
  vec3 s = vec3(0.); float ws = 0.;
  for (int i = -6; i <= 6; i++) {
    float k = float(i)*.5, w = exp(-.5*k*k);
    s += w*texture(u_src, uv + u_dir*(k*u_sig)/u_px).rgb; ws += w;
  }
  vec3 c = s/ws;
  if (u_last > .5) {
    c = mix(c, 1. - (1. - c)*(1. - c), u_dream*.16);   // dreamy: a soft screen glow, so a smooth wallpaper changes too
    vec2 fc = gl_FragCoord.xy;
    c += (h21(fc*1.13 + 3.7) + h21(fc*.97 + 19.1) - 1.)*u_grain*.034;
    c += (h21(fc + 41.) + h21(fc + 87.3) - 1.)/255.;
  }
  o = vec4(c, 1.);
}`;
  // the two passes: src -> mid -> dst (dstFbo null is the screen)
  function blurPasses(gl, B, srcTex, midTex, midFbo, dstFbo, W, H, sig, dream, grain, last) {
    gl.useProgram(B.p); gl.uniform2f(B.u.u_px, W, H); gl.uniform1f(B.u.u_sig, sig); gl.uniform1f(B.u.u_dream, dream); gl.uniform1f(B.u.u_grain, grain); gl.uniform1i(B.u.u_src, 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, midFbo); gl.viewport(0, 0, W, H);
    gl.bindTexture(gl.TEXTURE_2D, srcTex); gl.uniform2f(B.u.u_dir, 1, 0); gl.uniform1f(B.u.u_last, 0);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.bindFramebuffer(gl.FRAMEBUFFER, dstFbo); gl.viewport(0, 0, W, H);
    gl.bindTexture(gl.TEXTURE_2D, midTex); gl.uniform2f(B.u.u_dir, 0, 1); gl.uniform1f(B.u.u_last, last ? 1 : 0);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }
  function texSetup(gl, tex) {
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }

  // ---------------------------------------------------------------- the shared renderer: thumbnails and timing
  class Renderer {
    constructor() {
      this.cv = document.createElement('canvas'); this.cv.width = 4; this.cv.height = 4;
      let gl = null;
      try { gl = this.cv.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, preserveDrawingBuffer: true, powerPreference: 'high-performance' }); } catch (e) { gl = null; }
      this.gl = gl; this.ok = !!gl; this.progs = {}; this.err = {};
      if (!gl) return;
      this.vao = gl.createVertexArray();
      const dbg = gl.getExtension('WEBGL_debug_renderer_info');
      this.renderer = dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : 'WebGL2';
      this.cv.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.ok = false; });
    }
    prog(w) {
      if (this.progs[w.id]) return this.progs[w.id];
      try { return (this.progs[w.id] = program(this.gl, w.fs, w.id)); } catch (e) { this.err[w.id] = String(e.message || e); return null; }
    }
    // two RGBA8 targets of the given size for Focus (the sharp wallpaper, then the half-blurred one)
    scratch(W, H) {
      const gl = this.gl;
      if (!this.blurP) this.blurP = program(gl, BLUR_FS, 'blur');
      if (!this.sc) this.sc = [0, 1].map(() => ({ tex: gl.createTexture(), fbo: gl.createFramebuffer(), w: 0, h: 0 }));
      this.sc.forEach((t) => {
        if (t.w === W && t.h === H) return;
        texSetup(gl, t.tex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, W, H, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        gl.bindFramebuffer(gl.FRAMEBUFFER, t.fbo); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, t.tex, 0);
        t.w = W; t.h = H;
      });
      return this.sc;
    }
    // draws one wallpaper into a 2D canvas of the given device size. x: the option state (wallX); x.blur > 0 is the Focus sigma in px
    draw(w, target, W, H, dpr, pal, dark, grain, opt, x) {
      if (!this.ok) return false;
      const gl = this.gl, P = this.prog(w); if (!P) return false;
      x = x || wallX(w);
      if (this.cv.width !== W || this.cv.height !== H) { this.cv.width = W; this.cv.height = H; }
      gl.bindVertexArray(this.vao);
      if (x.blur > 0) {
        const sc = this.scratch(W, H);
        gl.bindFramebuffer(gl.FRAMEBUFFER, sc[0].fbo); gl.viewport(0, 0, W, H);
        gl.useProgram(P.p); setWallUniforms(gl, P.u, W, H, dpr, pal, dark, grain, opt, x);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        blurPasses(gl, this.blurP, sc[0].tex, sc[1].tex, sc[1].fbo, null, W, H, x.blur, x.dream || 0, grain, true);
      } else {
        gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, W, H);
        gl.useProgram(P.p);
        setWallUniforms(gl, P.u, W, H, dpr, pal, dark, grain, opt, x);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
      }
      if (target.width !== W || target.height !== H) { target.width = W; target.height = H; }
      const ctx = target.getContext('2d'); ctx.drawImage(this.cv, 0, 0);
      return true;
    }
    // the cost of the one render the app does: W x H into an 8-bit target, finished (readPixels waits for the GPU).
    // The first draw after a compile is discarded; the result is the median of n. x: the option state to time it under
    // (Dynamic, Match accent and a tilt only change uniforms; Focus adds the cheap blur passes: all are timed in).
    time(w, W, H, pal, dark, n, x) {
      if (!this.ok) return null;
      const gl = this.gl, P = this.prog(w); if (!P) return null;
      x = x || wallX(w);
      if (!this.fbo) { this.fbo = gl.createFramebuffer(); this.ftex = gl.createTexture(); this.fsz = [0, 0]; }
      if (this.fsz[0] !== W || this.fsz[1] !== H) {
        gl.bindTexture(gl.TEXTURE_2D, this.ftex);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, W, H, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, this.ftex, 0);
        this.fsz = [W, H];
      }
      const sc = x.blur > 0 ? this.scratch(W, H) : null;
      const once = () => {
        gl.bindFramebuffer(gl.FRAMEBUFFER, sc ? sc[0].fbo : this.fbo); gl.viewport(0, 0, W, H);
        gl.useProgram(P.p); gl.bindVertexArray(this.vao);
        setWallUniforms(gl, P.u, W, H, 2, pal, dark, w.grain, w.opt.def, x);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        if (sc) blurPasses(gl, this.blurP, sc[0].tex, sc[1].tex, sc[1].fbo, this.fbo, W, H, x.blur, x.dream || 0, w.grain, true);
      };
      const px = new Uint8Array(4), ts = [];
      for (let i = 0; i < 3; i++) { once(); gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px); }
      for (let i = 0; i < (n || 11); i++) {
        const t0 = performance.now();
        once(); gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
        ts.push(performance.now() - t0);
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      ts.sort((a, b) => a - b);
      return { med: ts[ts.length >> 1], min: ts[0], max: ts[ts.length - 1] };
    }
  }

  // ---------------------------------------------------------------- the large view: the wallpaper, then the glass
  // Every input is a target the view eases toward, so each change (theme, pack, accent, option, grain, focus, the clock, the
  // switches, the pointer) is a crossfade or an ease, never a cut (OD-0076). The wallpaper redraws only while something moves.
  class Viewer {
    constructor(canvas, wall) {
      this.canvas = canvas; this.wall = wall; this.ok = false;
      this.pal = null; this.palT = null; this.palK = 1; this.dark = 1; this.darkT = 1; this.grain = wall.grain; this.grainT = wall.grain;
      this.opt = wall.opt.def; this.optT = this.opt; this.glass = 1; this.glassT = 1;
      this.focus = 0; this.focusT = 0;                       // Focus 0 .. 1
      this.dyn = 0; this.dynT = 0; this.clock = 12; this.clockT = 12;   // Dynamic: 0 off .. 1 on, and the simulated hour
      this.persp = 0; this.perspT = 0; this.tilt = [0, 0]; this.tiltT = [0, 0]; this.tiltR = [0, 0];   // Perspective: 0 off .. 1 on; the pointer, -1 .. 1
      this.keys = wall.dyn || null; this.palOut = new Float32Array(18);
      this.X = wallX(wall); this.u = null;
      this.wallDirty = true; this.dirty = true; this.shapes = []; this.nShapes = 0;
      this.W = 900; this.H = 560; this.k = 1; this.px = [2, 2]; this.dpr = 1; this.n = 0; this.lastWall = 0; this.blurred = false;
      let gl = null;
      try { gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, powerPreference: 'high-performance' }); } catch (e) { gl = null; }
      if (!gl) return;
      this.gl = gl;
      try { this.pw = program(gl, wall.fs, wall.id); this.pg = program(gl, GLASS, 'glass'); this.pb = program(gl, BLUR_FS, 'blur'); } catch (e) { this.err = String(e.message || e); return; }
      this.floatOk = !!(gl.getExtension('EXT_color_buffer_float') || gl.getExtension('EXT_color_buffer_half_float'));
      this.vao = gl.createVertexArray();
      this.T = [0, 1, 2].map(() => ({ tex: gl.createTexture(), fbo: gl.createFramebuffer() }));   // the wallpaper, the half-blurred one, the blurred one
      canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.ok = false; });
      this.ok = true;
      this.allocate();
    }
    allocate() {
      const gl = this.gl; if (!gl) return;
      const [w, h] = this.px;
      const tryFmt = (t, ifmt, type) => {
        gl.bindTexture(gl.TEXTURE_2D, t.tex);
        gl.texImage2D(gl.TEXTURE_2D, 0, ifmt, w, h, 0, gl.RGBA, type, null);
        texSetup(gl, t.tex);
        gl.bindFramebuffer(gl.FRAMEBUFFER, t.fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, t.tex, 0);
        return gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
      };
      this.half = this.floatOk && tryFmt(this.T[0], gl.RGBA16F, gl.HALF_FLOAT);
      if (!this.half) tryFmt(this.T[0], gl.RGBA8, gl.UNSIGNED_BYTE);
      this.T.slice(1).forEach((t) => { if (this.half) tryFmt(t, gl.RGBA16F, gl.HALF_FLOAT); else tryFmt(t, gl.RGBA8, gl.UNSIGNED_BYTE); });
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      this.wallDirty = this.dirty = true;
    }
    resize(cssW, cssH, dpr, W, H) {
      const w = Math.max(2, Math.round(cssW * dpr)), h = Math.max(2, Math.round(cssH * dpr));
      this.W = W; this.H = H; this.k = w / W; this.dpr = dpr;
      if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; this.px = [w, h]; this.allocate(); }
      this.wallDirty = this.dirty = true;
    }
    setShapes(list) {
      this.shapes = list.slice(0, MAXN); this.nShapes = this.shapes.length;
      const box = new Float32Array(MAXN * 4), prm = new Float32Array(MAXN * 4), acc = new Float32Array(MAXN * 4);
      this.shapes.forEach((s, i) => {
        box.set([s.x + s.w / 2, s.y + s.h / 2, s.w / 2, s.h / 2], i * 4);
        prm.set([Math.min(s.r, s.w / 2, s.h / 2), s.tint, s.blur == null ? 1 : s.blur, s.scale == null ? 1 : s.scale], i * 4);
        acc.set([s.acc || 0, 0, 0, 0], i * 4);
      });
      this.boxA = box; this.prmA = prm; this.accA = acc; this.dirty = true;
    }
    // targets; the view eases toward them, so every change is a crossfade, never a cut
    target(o) {
      // a new palette (theme, pack or accent) is a 0.55 s crossfade from wherever the colours are now; a new one while a fade
      // is still running (an accent drag) starts from there and eases out in 0.3 s, so it never stalls
      if (o.pal) {
        const np = Float32Array.from(o.pal);
        if (!this.pal) this.pal = Float32Array.from(np);
        if (!this.palT || np.some((v, i) => Math.abs(v - this.palT[i]) > 1e-6)) {
          this.palD = this.palK < 1 ? 0.3 : 0.55; this.palE = this.palK < 1;
          this.palFrom = Float32Array.from(this.pal); this.palT = np; this.palK = 0;
        }
      }
      if (o.dark != null) this.darkT = o.dark;
      if (o.grain != null) this.grainT = o.grain;
      if (o.opt != null) this.optT = o.opt;
      if (o.glass != null) this.glassT = o.glass;
      if (o.focus != null) this.focusT = o.focus;
      if (o.dyn != null) this.dynT = o.dyn;
      if (o.clock != null) this.clockT = o.clock;
      if (o.persp != null) this.perspT = o.persp;
      if (o.tilt) this.tiltT = [clamp(o.tilt[0], -1, 1), clamp(o.tilt[1], -1, 1)];
    }
    snap() {
      if (this.palT) { this.pal = Float32Array.from(this.palT); this.palK = 1; }
      ['dark', 'grain', 'opt', 'glass', 'focus', 'dyn', 'clock', 'persp'].forEach((k) => { this[k] = this[k + 'T']; });
      this.tilt = this.tiltT.slice(); this.wallDirty = this.dirty = true;
    }
    tick(dt) {
      let ch = this.dirty, busy = false, wch = this.wallDirty;
      const a = 1 - Math.exp(-dt / .16), a2 = 1 - Math.exp(-dt / .09);
      const ease = (k, kt, f, eps) => { if (this[k] === this[kt]) return false; this[k] += (this[kt] - this[k]) * f; if (Math.abs(this[k] - this[kt]) < (eps || .002)) this[k] = this[kt]; busy = busy || this[k] !== this[kt]; return true; };
      if (this.pal && this.palT && this.palK < 1) {
        this.palK = Math.min(1, this.palK + dt / this.palD);
        const k = this.palE ? 1 - Math.pow(1 - this.palK, 3) : smooth(this.palK);
        for (let i = 0; i < 18; i++) this.pal[i] = this.palFrom[i] + (this.palT[i] - this.palFrom[i]) * k;
        wch = true; busy = busy || this.palK < 1;
      }
      if (ease('dark', 'darkT', a)) wch = true;
      if (ease('grain', 'grainT', a2)) wch = true;
      if (ease('opt', 'optT', a2)) wch = true;
      if (ease('focus', 'focusT', a2)) wch = true;
      if (ease('dyn', 'dynT', a)) wch = true;
      if (ease('clock', 'clockT', a, .004)) wch = true;
      if (ease('persp', 'perspT', a)) wch = true;
      if (ease('glass', 'glassT', a)) ch = true;
      // the pointer: a soft spring toward it (0.18 s); the wallpaper redraws only when the picture has moved by 1/16 px
      const at = 1 - Math.exp(-dt / .18);
      for (let i = 0; i < 2; i++) {
        if (this.tilt[i] !== this.tiltT[i]) { this.tilt[i] += (this.tiltT[i] - this.tilt[i]) * at; if (Math.abs(this.tilt[i] - this.tiltT[i]) < .0015) this.tilt[i] = this.tiltT[i]; busy = busy || this.tilt[i] !== this.tiltT[i]; }
      }
      const dep = this.wall.persp ? this.wall.persp.depth : 0, px = AMP * dep * this.H / 2 * this.k;
      if (this.persp > 0 && Math.hypot(this.tilt[0] - this.tiltR[0], this.tilt[1] - this.tiltR[1]) * px * this.persp > 1 / 16) wch = true;
      this.wallDirty = wch; this.busy = busy;
      return ch || wch;
    }
    // the uniforms the wallpaper is drawn with now: the pack palette shifted by the clock (Dynamic), the tilt, the focus
    state() {
      const w = this.wall, dk = smooth(this.dark), x = wallX(w);
      const D = dynState(w, this.pal, dk, this.clock, this.dyn, this.palOut);
      const pal = D.pal, dark = D.dark; x.light = D.light;
      const dep = w.persp ? w.persp.depth : 0, own = w.persp && w.persp.own, f = AMP * dep * this.persp;
      let tx = this.tilt[0], ty = this.tilt[1]; const l = Math.hypot(tx, ty); if (l > 1) { tx /= l; ty /= l; }
      x.tilt = [tx * f, -ty * f]; x.tg = own ? 0 : 1; this.tiltR = this.tilt.slice();
      const fo = smooth(clamp(this.focus, 0, 1)), ownF = w.focus && w.focus.own;
      x.focus = ownF ? fo : 0;
      x.blur = !ownF && fo > 0.004 ? fo * FOCUS_SIG * this.px[1] : 0; x.dream = x.blur > 0 ? fo : 0;
      return { pal, dark, x };
    }
    render() {
      const gl = this.gl; if (!this.ok || !gl || !this.pal) return 0;
      const [w, h] = this.px;
      gl.bindVertexArray(this.vao);
      if (this.wallDirty) {
        const t0 = performance.now(), S = this.state();
        gl.bindFramebuffer(gl.FRAMEBUFFER, this.T[0].fbo); gl.viewport(0, 0, w, h);
        gl.useProgram(this.pw.p);
        setWallUniforms(gl, this.pw.u, w, h, this.dpr, S.pal, S.dark, this.grain, this.opt, S.x);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        this.blurred = S.x.blur > 0;
        if (this.blurred) blurPasses(gl, this.pb, this.T[0].tex, this.T[1].tex, this.T[1].fbo, this.T[2].fbo, w, h, S.x.blur, S.x.dream, this.grain, true);
        this.u = S.x; this.lastDark = S.dark; this.lastPal = Array.from(S.pal);
        this.lastWall = performance.now() - t0;
        this.wallDirty = false;
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, w, h);
      gl.useProgram(this.pg.p);
      const G = this.pg.u;
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.T[this.blurred ? 2 : 0].tex); gl.uniform1i(G.u_wall, 0);
      gl.uniform2f(G.u_px, w, h); gl.uniform1f(G.u_k, this.k); gl.uniform1i(G.u_n, this.nShapes);
      if (this.boxA) { gl.uniform4fv(G.u_box, this.boxA); gl.uniform4fv(G.u_prm, this.prmA); gl.uniform4fv(G.u_acc, this.accA); }
      gl.uniform1f(G.u_dark, smooth(this.dark)); gl.uniform1f(G.u_glass, smooth(this.glass));
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      this.dirty = false; this.n++;
      return 1;
    }
    dispose() {
      try { const x = this.gl && this.gl.getExtension('WEBGL_lose_context'); if (x) x.loseContext(); } catch (e) { /* already gone */ }
      this.ok = false;
    }
  }
  window.SWG = { Renderer, Viewer, MAXN, AMP, FOCUS_SIG, WASH, wallX, dynState };
})();
