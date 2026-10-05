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
  function setWallUniforms(gl, U, w, h, dpr, pal, dark, grain, opt) {
    gl.uniform2f(U.u_res, w, h); gl.uniform1f(U.u_dpr, dpr);
    gl.uniform3fv(U.u_pal, pal); gl.uniform1f(U.u_dark, dark);
    gl.uniform1f(U.u_grain, grain); gl.uniform1f(U.u_opt, opt);
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
    // draws one wallpaper into a 2D canvas of the given device size
    draw(w, target, W, H, dpr, pal, dark, grain, opt) {
      if (!this.ok) return false;
      const gl = this.gl, P = this.prog(w); if (!P) return false;
      if (this.cv.width !== W || this.cv.height !== H) { this.cv.width = W; this.cv.height = H; }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, W, H);
      gl.useProgram(P.p); gl.bindVertexArray(this.vao);
      setWallUniforms(gl, P.u, W, H, dpr, pal, dark, grain, opt);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      if (target.width !== W || target.height !== H) { target.width = W; target.height = H; }
      const ctx = target.getContext('2d'); ctx.drawImage(this.cv, 0, 0);
      return true;
    }
    // the cost of the one render the app does: W x H into an 8-bit target, finished (readPixels waits for the GPU).
    // The first draw after a compile is discarded; the result is the median of n.
    time(w, W, H, pal, dark, n) {
      if (!this.ok) return null;
      const gl = this.gl, P = this.prog(w); if (!P) return null;
      if (!this.fbo) { this.fbo = gl.createFramebuffer(); this.ftex = gl.createTexture(); this.fsz = [0, 0]; }
      if (this.fsz[0] !== W || this.fsz[1] !== H) {
        gl.bindTexture(gl.TEXTURE_2D, this.ftex);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, W, H, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, this.ftex, 0);
        this.fsz = [W, H];
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbo); gl.viewport(0, 0, W, H);
      gl.useProgram(P.p); gl.bindVertexArray(this.vao);
      setWallUniforms(gl, P.u, W, H, 2, pal, dark, w.grain, w.opt.def);
      const px = new Uint8Array(4), ts = [];
      for (let i = 0; i < 3; i++) { gl.drawArrays(gl.TRIANGLES, 0, 3); gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px); }
      for (let i = 0; i < (n || 11); i++) {
        const t0 = performance.now();
        gl.drawArrays(gl.TRIANGLES, 0, 3); gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
        ts.push(performance.now() - t0);
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      ts.sort((a, b) => a - b);
      return { med: ts[ts.length >> 1], min: ts[0], max: ts[ts.length - 1] };
    }
  }

  // ---------------------------------------------------------------- the large view: the wallpaper, then the glass
  class Viewer {
    constructor(canvas, wall) {
      this.canvas = canvas; this.wall = wall; this.ok = false;
      this.pal = null; this.palT = null; this.palK = 1; this.dark = 1; this.darkT = 1; this.grain = wall.grain; this.grainT = wall.grain;
      this.opt = wall.opt.def; this.optT = this.opt; this.glass = 1; this.glassT = 1;
      this.wallDirty = true; this.dirty = true; this.shapes = []; this.nShapes = 0;
      this.W = 900; this.H = 560; this.k = 1; this.px = [2, 2]; this.dpr = 1; this.n = 0; this.lastWall = 0;
      let gl = null;
      try { gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, powerPreference: 'high-performance' }); } catch (e) { gl = null; }
      if (!gl) return;
      this.gl = gl;
      try { this.pw = program(gl, wall.fs, wall.id); this.pg = program(gl, GLASS, 'glass'); } catch (e) { this.err = String(e.message || e); return; }
      this.floatOk = !!(gl.getExtension('EXT_color_buffer_float') || gl.getExtension('EXT_color_buffer_half_float'));
      this.vao = gl.createVertexArray(); this.tex = gl.createTexture(); this.fbo = gl.createFramebuffer();
      canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.ok = false; });
      this.ok = true;
      this.allocate();
    }
    allocate() {
      const gl = this.gl; if (!gl) return;
      const [w, h] = this.px;
      gl.bindTexture(gl.TEXTURE_2D, this.tex);
      const tryFmt = (ifmt, type) => {
        gl.texImage2D(gl.TEXTURE_2D, 0, ifmt, w, h, 0, gl.RGBA, type, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, this.tex, 0);
        return gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
      };
      this.half = this.floatOk && tryFmt(gl.RGBA16F, gl.HALF_FLOAT);
      if (!this.half) tryFmt(gl.RGBA8, gl.UNSIGNED_BYTE);
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
      // a new palette (theme or pack) is a 0.55 s crossfade from wherever the colours are now
      if (o.pal) {
        const np = Float32Array.from(o.pal);
        if (!this.pal) this.pal = Float32Array.from(np);
        if (!this.palT || np.some((v, i) => Math.abs(v - this.palT[i]) > 1e-6)) { this.palFrom = Float32Array.from(this.pal); this.palT = np; this.palK = 0; }
      }
      if (o.dark != null) this.darkT = o.dark;
      if (o.grain != null) this.grainT = o.grain;
      if (o.opt != null) this.optT = o.opt;
      if (o.glass != null) this.glassT = o.glass;
    }
    snap() { if (this.palT) { this.pal = Float32Array.from(this.palT); this.palK = 1; } this.dark = this.darkT; this.grain = this.grainT; this.opt = this.optT; this.glass = this.glassT; this.wallDirty = this.dirty = true; }
    tick(dt) {
      let ch = this.dirty, busy = false, wch = this.wallDirty;
      const a = 1 - Math.exp(-dt / .16), a2 = 1 - Math.exp(-dt / .09);
      const ease = (k, kt, f) => { if (this[k] === this[kt]) return false; this[k] += (this[kt] - this[k]) * f; if (Math.abs(this[k] - this[kt]) < .002) this[k] = this[kt]; busy = busy || this[k] !== this[kt]; return true; };
      if (this.pal && this.palT && this.palK < 1) {
        this.palK = Math.min(1, this.palK + dt / .55);
        const k = smooth(this.palK);
        for (let i = 0; i < 18; i++) this.pal[i] = this.palFrom[i] + (this.palT[i] - this.palFrom[i]) * k;
        wch = true; busy = busy || this.palK < 1;
      }
      if (ease('dark', 'darkT', a)) wch = true;
      if (ease('grain', 'grainT', a2)) wch = true;
      if (ease('opt', 'optT', a2)) wch = true;
      if (ease('glass', 'glassT', a)) ch = true;
      this.wallDirty = wch; this.busy = busy;
      return ch || wch;
    }
    render() {
      const gl = this.gl; if (!this.ok || !gl || !this.pal) return 0;
      const [w, h] = this.px;
      gl.bindVertexArray(this.vao);
      if (this.wallDirty) {
        const t0 = performance.now();
        gl.bindFramebuffer(gl.FRAMEBUFFER, this.fbo); gl.viewport(0, 0, w, h);
        gl.useProgram(this.pw.p);
        setWallUniforms(gl, this.pw.u, w, h, this.dpr, this.pal, smooth(this.dark), this.grain, this.opt);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        this.lastWall = performance.now() - t0;
        this.wallDirty = false;
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, w, h);
      gl.useProgram(this.pg.p);
      const G = this.pg.u;
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.tex); gl.uniform1i(G.u_wall, 0);
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
  window.SWG = { Renderer, Viewer, MAXN };
})();
