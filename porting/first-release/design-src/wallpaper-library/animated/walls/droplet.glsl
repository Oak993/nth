// Droplet: still water seen from above, holding the reflection of a soft sky and a pale window of light. Now and then
// a single drop falls, first on one side, then on the other, and its rings spread slowly outward, bending the
// reflection as they pass, and fade long before the next drop. Between the drops the water barely stirs.
// Palette: 0 the water's depth, 1 the reflected sky far, 2 the reflected sky near, 3 the reflected window of light,
// 4 the bright edge of a ring, 5 the window's core.
// Options: x = motion (how strongly the water stirs), y = rings (few and faint to many and clear).
const float T = 60.0;

// the rings of one drop: returns the slope of the water along the radius; age 0..1 runs once per loop
vec2 drop(vec2 q, vec2 c, float age, float rk) {
  vec2 d = q - c;
  float r = length(d) + 1e-4;
  float front = 0.06 + 1.55 * age;
  float x = r - front;
  float wid = 0.08 + 0.36 * age;
  float env = exp(-(x * x) / (wid * wid)) * smoothstep(0.0, 0.04, age) * pow(1.0 - age, 1.4);
  float kk = mix(42.0, 22.0, sqrt(age)) * mix(0.85, 1.15, rk);
  float s = env * cos(x * kk) / sqrt(r + 0.25) * smoothstep(front + 0.02, front - 0.4, r - 0.3 * wid);
  return d / r * s;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float rk = u_opt.y;
  // the slope of the surface: the two drops, half a loop apart, and a faint stir
  float age1 = fract(a / TAU + 0.12);
  float age2 = fract(a / TAU + 0.62);
  vec2 g = drop(p, vec2(-0.78, 0.26), age1, rk) + drop(p, vec2(0.86, -0.38), age2, rk);
  g *= mix(0.012, 0.03, rk);
  g += 0.006 * m * vec2(lnoise(p * 1.4, a, 0.3), lnoise(p * 1.4 + 7.0, a, 0.3));
  // the reflection, displaced by the slope: a soft sky with a few high clouds, still, as the water is
  vec2 rp = p + g * 8.0;
  vec3 col = mix(u_pal[2], u_pal[1], smoothstep(-1.3, 1.2, rp.y - 0.35 * rp.x));
  vec2 sp = rp - vec2(-0.6, 0.55) - u_sun;
  float sunny = exp(-dot(sp, sp) / 0.9);
  col = mix(col, u_pal[3], 0.45 * sunny);
  float cl = fbm3(vec3(rp * vec2(0.8, 1.5) + vec2(0.3, 0.0), 3.0)) + 0.25 * lnoise(rp * 0.6, a, 0.12);
  float cloud = smoothstep(0.02, 0.45, cl);
  col = mix(col, mix(u_pal[3], u_pal[5], sunny), cloud * (0.35 + 0.3 * sunny));
  // the water's own depth darkens the reflection toward the lower left, and the rings catch light on their crests
  col = mix(col, u_pal[0], 0.28 * smoothstep(0.6, 2.4, length(p - vec2(0.4, 0.6))));
  vec2 ld = normalize(vec2(-0.3, 1.0));
  float crest = sat(dot(g, ld) * 110.0);
  col = mix(col, u_pal[4], crest * 0.45);
  col.x -= sat(-dot(g, ld) * 110.0) * 0.035;
  return outLab(col);
}
