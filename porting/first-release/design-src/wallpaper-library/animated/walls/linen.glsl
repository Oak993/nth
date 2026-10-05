// Linen: a woven cloth breathing in a faint draught. The weave is real: warp and weft threads passing over and under
// each other, each thread a little thicker or thinner along its length (the slubs that make linen linen), so the cloth
// carries fine streaks along both directions. The threads follow the folds, the folds rise and settle, and the light
// from the top left grazes the weave, showing it most on the lit slopes.
// Palette: 0 fold shadow, 1 cloth, 2 lit cloth, 3 the second tint, 4 sheen, 5 the light.
// Options: x = motion (how far the cloth lifts), y = weave (fine to coarse).
const float T = 72.0;

float cloth(vec2 q, float a, float m) {
  float u = q.x + 0.3 * sin(q.y * 0.9 + 0.5 * m * sin(a)) + 0.12 * m * sin(q.y * 1.7 - a + 0.5);
  return 0.5 * sin(u * 1.7 + 0.6 * sin(q.y * 0.7 + a)) + 0.22 * sin(u * 3.1 - q.y * 0.5 + 1.1 + 0.5 * m * sin(a + 2.0));
}

float h1(float x) { return hash33(vec3(x, 7.0, 3.0)).x * 0.5 + 0.5; }

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float P = mix(0.0075, 0.018, u_opt.y);
  vec2 q = rot(p, -0.35);
  float e = 0.004;
  float h0 = cloth(q, a, m);
  vec2 gr = vec2(cloth(q + vec2(e, 0.0), a, m) - h0, cloth(q + vec2(0.0, e), a, m) - h0) / e;
  vec3 n = normalize(vec3(-gr * 0.42, 1.0));
  vec3 L = normalize(vec3(-0.4, 0.5, 0.77));
  float s = sat(dot(n, L) * 1.3 - 0.05);
  float sheen = pow(max(dot(n, normalize(L + vec3(0.0, 0.0, 1.0))), 0.0), 12.0);
  vec3 col = mix(u_pal[0], u_pal[1], smoothstep(0.45, 0.85, s));
  col = mix(col, u_pal[2], smoothstep(0.82, 1.0, s));
  col = mix(col, mix(col, u_pal[3], 0.75), smoothstep(-1.4, 1.4, p.x * 0.8 + p.y * 0.4 + 0.3 * sin(a)));

  // the weave, in cloth coordinates that ride on the folds
  vec2 w = rot(p, 0.12) + gr * 0.012;
  float wu = w.x / P + 0.18 * gnoise(vec3(w * 9.0, 4.0));
  float wv = w.y / P + 0.18 * gnoise(vec3(w * 9.0, 8.0));
  float cu = floor(wu);
  float cv = floor(wv);
  float fu = wu - cu - 0.5;
  float fv = wv - cv - 0.5;
  float warpTop = step(0.5, mod(cu + cv, 2.0));
  float slubU = 0.75 + 0.5 * h1(cu) + 0.35 * gnoise(vec3(cu * 0.37, wv * 0.045, 1.0));
  float slubV = 0.75 + 0.5 * h1(cv + 91.0) + 0.35 * gnoise(vec3(wu * 0.045, cv * 0.37, 2.0));
  float tu = cos(3.14159 * fu * 0.95);
  float tv = cos(3.14159 * fv * 0.95);
  float weave = mix(tv * slubV, tu * slubU, warpTop) * (0.55 + 0.45 * mix(abs(tu), abs(tv), warpTop));
  float gap = smoothstep(0.42, 0.5, max(abs(fu), abs(fv)));
  float fine = sat(P / u_px / 3.5 - 0.6);
  float grazing = 0.55 + 0.9 * sat(dot(gr, vec2(0.6, -0.55)) * 0.6 + 0.4);
  col.x += (0.022 * (weave - 0.55) - 0.006 * gap) * fine * grazing;

  // the slubs at the scale a tile can show: fine streaks along both thread directions
  float st = gnoise(vec3(w.x * 46.0, w.y * 2.2, 3.0)) + gnoise(vec3(w.x * 2.2, w.y * 46.0, 5.0));
  st += 0.6 * (gnoise(vec3(w.x * 110.0, w.y * 4.0, 6.0)) + gnoise(vec3(w.x * 4.0, w.y * 110.0, 7.0))) * sat(0.02 / u_px / 3.0 - 0.5);
  col.x += 0.012 * st * (0.6 + 0.4 * s);
  col = mix(col, u_pal[4], sheen * 0.28);
  col = mix(col, u_pal[5], 0.1 * smoothstep(1.6, 0.0, length(p - vec2(-0.9, 0.7))));
  return outLab(col);
}
