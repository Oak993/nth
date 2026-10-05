// Billow: a cloud of ink unfurling in clear water, seen close, as in a macro photograph. Two inks rise together in one
// plume that curls over to the right; its surface is sculpted into rounded billows, each lit from the upper left so it
// reads as a solid, soft form, with fine tendrils fraying off its edges. In dark mode the ink glows against black
// water; in light mode it is dense pigment in a white tank, deepest in its folds.
// Palette: 0 the water, 1 the water's light, 2 first ink, 3 second ink, 4 the ink's lit edges, 5 the light.
// Options: x = motion (how far the ink unfurls), y = density (thin veils to dense plumes).
// Perspective: the water is far (tiltD -1), the plume near (tiltD 0.7).
const float T = 60.0;

// the plume's envelope: a soft tube along a curve rising from the lower left and curling over to the right
float envelope(vec2 q) {
  float s = sat((q.x + 1.6) / 3.0);
  float cy = -1.05 + 1.5 * s - 0.9 * s * s * s + 0.08 * sin(s * 7.0);
  float r = 0.25 + 0.5 * s * (1.0 - 0.4 * s);
  float d = (q.y - cy) / r;
  return exp(-d * d * 0.9) * smoothstep(0.0, 0.15, s) * smoothstep(1.0, 0.75, s);
}

float fbm2(vec3 p) { return 0.5 * gnoise(p) + 0.25 * gnoise(p * 2.03 + vec3(1.7, 9.2, 3.1)); }

// the ink density; the gradient samples use two octaves only (the third is too fine to change the shading)
float dens(vec2 q, vec2 w, float a, float m, float dk) {
  vec2 x = q + w;
  vec3 X = vec3(x * 2.6, 0.0) + vec3(orbit(a, 0.18 * m), 0.2 * m * sin(a + 1.0));
  float f = dk > 0.5 ? fbm3(X) : fbm2(X);
  float env = envelope(q + w * 0.8);
  return env + 0.95 * f * (0.3 + 0.7 * smoothstep(0.02, 0.45, env));
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float dn = mix(0.65, 1.35, u_opt.y);
  vec2 pw = p + tiltD(-1.0);
  vec3 water = lab2lin(mix(u_pal[0], u_pal[1], exp(-dot(pw - vec2(-0.7, 0.7), pw - vec2(-0.7, 0.7)) * 0.4)));
  vec2 q = p + tiltD(0.7);
  // the large flow that sculpts the billows, shared by the density and its gradient
  vec3 o = vec3(orbit(a, 0.22 * m), 0.25 * m * sin(a));
  vec2 w = 0.6 * vec2(fbm2(vec3(q * 0.9, 1.0) + o), fbm2(vec3(q * 0.9 + 5.3, 2.0) - o.yxz)) * 1.15;
  float e = 0.012;
  float d0 = dens(q, w, a, m, 1.0);
  float d1 = dens(q, w, a, m, 0.0);
  float dx = dens(q + vec2(e, 0.0), w, a, m, 0.0);
  float dy = dens(q + vec2(0.0, e), w, a, m, 0.0);
  vec2 g = vec2(dx - d1, dy - d1) / e;
  float thr = mix(0.75, 0.45, sat((dn - 0.65) / 0.7));
  float body = smoothstep(thr - 0.05, thr + 0.35, d0);
  // tendrils: thin filaments where the density crosses the threshold
  float rg = 1.0 - abs(gnoise(vec3((q + w) * 6.0, 3.0) + o * 0.5));
  float fil = pow(rg, 8.0) * smoothstep(thr - 0.3, thr - 0.02, d0) * (1.0 - body) * 0.35;
  // shading of the billows: a normal from the density gradient, lit from the upper left
  vec3 n = normalize(vec3(-g * 0.08, 1.0));
  vec3 L = normalize(vec3(-0.55, 0.6, 0.6));
  float lit = sat(dot(n, L) * 0.9 + 0.2);
  float rimL = pow(1.0 - n.z, 1.5);
  // which ink
  float mixI = smoothstep(-0.7, 0.7, w.x * 3.0 + 0.5 * (q.x - 0.2));
  vec3 ink = mix(lab2lin(u_pal[2]), lab2lin(u_pal[3]), mixI);
  vec3 edgeC = lab2lin(u_pal[4]);
  vec3 lightC = lab2lin(u_pal[5]);
  // dark: the ink glows, brightest on the lit sides of each billow and along its edges
  vec3 dk = water + ink * (body * (0.25 + 0.95 * lit) + fil * 0.8) + edgeC * (rimL * body * 0.35 + fil * 0.3) + lightC * pow(lit, 6.0) * body * 0.12;
  // light: pigment absorbing in clear water, deeper where the ink is thick and in the shaded folds
  vec3 ab = -log(max(ink, vec3(0.02))) * 0.6;
  vec3 lt = water * exp(-ab * (body * (1.1 + 0.9 * (1.0 - lit)) + fil * 0.5) * dn);
  lt += lightC * pow(lit, 8.0) * body * 0.06;
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
