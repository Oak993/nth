@@NOTICE@@
(function () {
  'use strict';
  const MAXN = 16;
  const LIB = window.WL_DATA;
  const VERT = '#version 300 es\nvoid main(){ vec2 a = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); gl_Position = vec4(a*2. - 1., 0., 1.); }';

  // The WebGL host around a wallpaper: the uniforms, the wallpaper's own constants and wall(), then main(), which maps
  // the pixel to p, calls wall(p, t) and adds the dither. Only common + wall are the wallpaper; the app's SkSL host is
  // the equivalent of this main().
  const HEAD = '#version 300 es\nprecision highp float;\nuniform vec2 u_res;\nuniform float u_t;\nuniform vec3 u_pal[6];\nuniform float u_dark;\nuniform vec2 u_opt;\nuniform float u_px;\nuniform float u_dither;\nout vec4 o_col;\n';
  const MAIN = '\nfloat wl_ign(vec2 v){ return fract(52.9829189*fract(dot(v, vec2(.06711056, .00583715)))); }\n'
    + 'void main(){\n  vec2 fc = gl_FragCoord.xy;\n  vec2 p = (fc - .5*u_res)*(2./u_res.y);\n  vec3 c = wall(p, u_t);\n'
    + '  c += u_dither*(wl_ign(fc) + wl_ign(fc + vec2(37., 17.)) - 1.)/255.;\n  o_col = vec4(c, 1.);\n}\n';

  const GLASS = `@@GLASS@@`;

  const FLAT = '#version 300 es\nprecision highp float;\nout vec4 o_col;\nvoid main(){ o_col = vec4(.5, .5, .5, 1.); }\n';

  // ---- colour on the CPU: hex to OKLab, for the pack palettes the host mixes and crossfades
  function hex2lab(h) {
    const n = parseInt(h.slice(1), 16);
    const f = (v) => { v /= 255; return v <= 0.04045 ? v/12.92 : Math.pow((v + 0.055)/1.055, 2.4); };
    const r = f((n >> 16) & 255), g = f((n >> 8) & 255), b = f(n & 255);
    const l = Math.cbrt(0.4122214708*r + 0.5363325363*g + 0.0514459929*b);
    const m = Math.cbrt(0.2119034982*r + 0.6806995451*g + 0.1073969566*b);
    const s = Math.cbrt(0.0883024619*r + 0.2817188376*g + 0.6299787005*b);
    return [0.2104542553*l + 0.7936177850*m - 0.0040720468*s, 1.9779984951*l - 2.4285922050*m + 0.4505937099*s, 0.0259040371*l + 0.7827717662*m - 0.8086757660*s];
  }
  // the palette a pack shows at a given darkness (0 light .. 1 dark), mixed in OKLab
  function packLab(pack, dark) {
    const out = new Float32Array(18);
    for (let i = 0; i < 6; i++) {
      const d = pack._d || (pack._d = pack.dark.map(hex2lab)), l = pack._l || (pack._l = pack.light.map(hex2lab));
      for (let j = 0; j < 3; j++) out[i*3 + j] = l[i][j] + (d[i][j] - l[i][j])*dark;
    }
    return out;
  }

  function compile(gl, type, src) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src); gl.compileShader(s);
    return s;
  }
  function uniforms(gl, p) {
    const u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) { const info = gl.getActiveUniform(p, i); u[info.name.replace(/\[0\]$/, '')] = gl.getUniformLocation(p, info.name); }
    return u;
  }

  class Engine {
    constructor() {
      this.cv = document.createElement('canvas');
      this.cv.width = 2; this.cv.height = 2;
      let gl = null;
      try { gl = this.cv.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, premultipliedAlpha: false, preserveDrawingBuffer: false, powerPreference: 'high-performance' }); } catch (e) { gl = null; }
      this.gl = gl; this.ok = !!gl; this.progs = {}; this.err = '';
      if (!gl) return;
      this.par = gl.getExtension('KHR_parallel_shader_compile');
      const dbg = gl.getExtension('WEBGL_debug_renderer_info');
      this.renderer = dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : 'WebGL2';
      this.floatOk = !!gl.getExtension('EXT_color_buffer_float');
      this.vao = gl.createVertexArray();
      this.vs = compile(gl, gl.VERTEX_SHADER, VERT);
      this.glass = this.link(GLASS);
      this.flat = this.link(FLAT);
      this.fbos = {};
      this.cv.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.ok = false; });
    }
    link(fsrc) {
      const gl = this.gl;
      const p = gl.createProgram();
      const fs = compile(gl, gl.FRAGMENT_SHADER, fsrc);
      gl.attachShader(p, this.vs); gl.attachShader(p, fs); gl.linkProgram(p);
      return { p, fs, u: null, ready: false, bad: false };
    }
    // a program is ready once linked; with the parallel-compile extension the page never blocks waiting for one
    ready(P) {
      if (!P || P.bad) return false;
      if (P.ready) return true;
      const gl = this.gl;
      if (this.par && !gl.getProgramParameter(P.p, this.par.COMPLETION_STATUS_KHR)) return false;
      if (!gl.getProgramParameter(P.p, gl.LINK_STATUS)) {
        P.bad = true;
        this.err = (gl.getShaderInfoLog(P.fs) || '') + (gl.getProgramInfoLog(P.p) || '');
        console.error('wallpaper shader: ' + this.err.slice(0, 600));
        return false;
      }
      P.u = uniforms(gl, P.p); P.ready = true;
      return true;
    }
    source(w) { return HEAD + LIB.common + '\n' + w.glsl + MAIN; }
    prog(id) {
      if (!this.ok) return null;
      let P = this.progs[id];
      if (!P) { const w = LIB.byId[id]; P = this.progs[id] = this.link(this.source(w)); }
      return this.ready(P) ? P : null;
    }
    warm(id) { if (this.ok && !this.progs[id]) this.progs[id] = this.link(this.source(LIB.byId[id])); }
    // the shared drawing buffer only ever grows, so drawing a tile never reallocates it
    fit(w, h) {
      if (this.cv.width < w || this.cv.height < h) {
        this.cv.width = Math.max(this.cv.width, Math.ceil(w/64)*64);
        this.cv.height = Math.max(this.cv.height, Math.ceil(h/64)*64);
      }
    }
    fbo(key, w, h, half) {
      const gl = this.gl;
      let F = this.fbos[key];
      if (F && F.w === w && F.h === h) return F;
      if (!F) { F = this.fbos[key] = { tex: gl.createTexture(), fb: gl.createFramebuffer() }; }
      F.w = w; F.h = h;
      gl.bindTexture(gl.TEXTURE_2D, F.tex);
      const useHalf = half && this.floatOk;
      gl.texImage2D(gl.TEXTURE_2D, 0, useHalf ? gl.RGBA16F : gl.RGBA8, w, h, 0, gl.RGBA, useHalf ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      gl.bindFramebuffer(gl.FRAMEBUFFER, F.fb);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, F.tex, 0);
      F.half = useHalf;
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      return F;
    }
    // draws wallpaper `id` at loop time t into the bound target; s = { pal: Float32Array(18), dark, opt: [x, y] }
    wall(P, w, h, t, s, dither) {
      const gl = this.gl, U = P.u;
      gl.useProgram(P.p);
      gl.uniform2f(U.u_res, w, h); gl.uniform1f(U.u_t, t);
      gl.uniform3fv(U.u_pal, s.pal); gl.uniform1f(U.u_dark, s.dark);
      if (U.u_opt) gl.uniform2f(U.u_opt, s.opt[0], s.opt[1]);
      if (U.u_px) gl.uniform1f(U.u_px, 2/h);
      gl.uniform1f(U.u_dither, dither);
      gl.bindVertexArray(this.vao);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    }
    // a tile: draws into the shared buffer and copies the picture into the tile's own 2D canvas
    tile(id, ctx, w, h, t, s) {
      const P = this.prog(id); if (!P) return false;
      const gl = this.gl;
      this.fit(w, h);
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, w, h);
      this.wall(P, w, h, t, s, 1);
      ctx.drawImage(this.cv, 0, this.cv.height - h, w, h, 0, 0, w, h);
      return true;
    }
    // the large view behind the glass: pass 1 draws the wallpaper into a texture, pass 2 refracts it through the mock
    // window's glass shapes (g = { k, n, box, prm, acc, glass }) and writes the result
    glassView(id, ctx, w, h, t, s, g) {
      const P = this.prog(id); if (!P) return false;
      const gl = this.gl;
      this.fit(w, h);
      const F = this.fbo('view', w, h, true);
      gl.bindFramebuffer(gl.FRAMEBUFFER, F.fb);
      gl.viewport(0, 0, w, h);
      this.wall(P, w, h, t, s, F.half ? 0 : 1);
      const G = this.glass;
      if (!this.ready(G)) return false;
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, w, h);
      gl.useProgram(G.p);
      const U = G.u;
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, F.tex); gl.uniform1i(U.u_wall, 0);
      gl.uniform2f(U.u_px, w, h); gl.uniform1f(U.u_k, g.k); gl.uniform1i(U.u_n, g.n);
      gl.uniform4fv(U.u_box, g.box); gl.uniform4fv(U.u_prm, g.prm); gl.uniform4fv(U.u_acc, g.acc);
      gl.uniform1f(U.u_dark, s.dark); gl.uniform1f(U.u_glass, g.glass);
      gl.bindVertexArray(this.vao);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      ctx.drawImage(this.cv, 0, this.cv.height - h, w, h, 0, 0, w, h);
      return true;
    }
    // reads back the picture at time t, at w x h, in 8 bits with the dither, exactly as the screen would get it
    read(P, w, h, t, s) {
      const gl = this.gl;
      const F = this.fbo('read' + w + 'x' + h, w, h, false);
      gl.bindFramebuffer(gl.FRAMEBUFFER, F.fb);
      gl.viewport(0, 0, w, h);
      this.wall(P, w, h, t, s, 1);
      const px = new Uint8Array(w*h*4);
      gl.readPixels(0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, px);
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      return px;
    }
    // the loop proof: frame(0) against frame(T), and frame(0) against frame(T/2) to show that it really moves
    proof(id, s, w, h) {
      const P = this.prog(id); if (!P) return null;
      const T = LIB.byId[id].period;
      const a = this.read(P, w, h, 0, s), b = this.read(P, w, h, T, s), c = this.read(P, w, h, T/2, s);
      let dT = 0, dH = 0, sumH = 0;
      for (let i = 0; i < a.length; i += 4) {
        for (let j = 0; j < 3; j++) {
          const x = Math.abs(a[i + j] - b[i + j]); if (x > dT) dT = x;
          const y = Math.abs(a[i + j] - c[i + j]); if (y > dH) dH = y; sumH += y;
        }
      }
      return { dT, dH, meanH: sumH/(a.length*0.75) };
    }
    // the frame cost at 1280 x 800: frames drawn back to back into an 8-bit target, the GPU made to finish at the end,
    // the total divided by the count; the best of three runs
    bench(id, s, frames) {
      const P = this.prog(id); if (!P) return null;
      const gl = this.gl, N = frames || 48, W = 1280, H = 800;
      const F = this.fbo('bench', W, H, false);
      const px = new Uint8Array(4);
      gl.bindFramebuffer(gl.FRAMEBUFFER, F.fb);
      gl.viewport(0, 0, W, H);
      const T = LIB.byId[id].period;
      for (let i = 0; i < 6; i++) this.wall(P, W, H, i*0.37 % T, s, 1);
      gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
      let best = 1e9;
      for (let r = 0; r < 3; r++) {
        const t0 = performance.now();
        for (let i = 0; i < N; i++) this.wall(P, W, H, (i*0.016 + r) % T, s, 1);
        gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
        best = Math.min(best, (performance.now() - t0)/N);
      }
      // the same loop with a flat shader: what the timing costs by itself, reported beside it
      let base = 1e9;
      if (this.ready(this.flat)) {
        gl.useProgram(this.flat.p);
        for (let r = 0; r < 2; r++) {
          const t0 = performance.now();
          for (let i = 0; i < N; i++) gl.drawArrays(gl.TRIANGLES, 0, 3);
          gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
          base = Math.min(base, (performance.now() - t0)/N);
        }
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      return { ms: best, base: base < 1e9 ? base : 0 };
    }
    // a PNG of one frame, for the checks
    png(id, s, w, h, t) {
      const P = this.prog(id); if (!P) return null;
      const px = this.read(P, w, h, t, s);
      const c = document.createElement('canvas'); c.width = w; c.height = h;
      const x = c.getContext('2d'); const im = x.createImageData(w, h);
      for (let y = 0; y < h; y++) im.data.set(px.subarray((h - 1 - y)*w*4, (h - y)*w*4), y*w*4);
      x.putImageData(im, 0, 0);
      return c;
    }
  }

  window.WLGL = { Engine, packLab, hex2lab, MAXN };
})();
