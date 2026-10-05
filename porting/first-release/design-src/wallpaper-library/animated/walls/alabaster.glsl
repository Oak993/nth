// Alabaster: a thin slab of translucent stone with a light behind it that breathes, a quicker swell and a long fall.
// The stone is banded the way onyx and alabaster are, with a few fine veins; where the light is strong the bands glow
// through and the veins show, where it is weak the stone is calm and nearly even. The stone never moves; only the
// light behind it does, so the picture stays still while it breathes. Under Perspective the slab and the light part.
// Palette: 0 the stone where little light passes, 1 the stone's body, 2 the light through it, 3 the veins, 4 the glow's
// core, 5 the second, cooler light.
// Options: x = motion (how far the light drifts and how deep it breathes), y = veins (faint to clear).
const float T = 80.0;

// one breath: a cosine whose phase is bent, so the swell is quicker than the fall; 0 at rest .. 1 full
float breath(float ph) { return 0.5 - 0.5 * cos(ph + 0.45 * sin(ph)); }

// a mix that travels round the hue circle instead of across it, so a warm light over a cool stone passes through rose,
// never through grey or brown
vec3 mixh(vec3 a, vec3 b, float k) {
  float ca = length(a.yz);
  float cb = length(b.yz);
  float ha = atan(a.z, a.y + 1e-6);
  float dh = atan(b.z, b.y + 1e-6) - ha;
  dh -= TAU * floor(dh / TAU + 0.5);
  float h = ha + dh * k;
  float c = mix(ca, cb, k);
  // near-opposite hues have no shortest way round: there it falls back to the straight mix, so nothing ever jumps
  return mix(vec3(mix(a.x, b.x, k), c * cos(h), c * sin(h)), mix(a, b, k), smoothstep(2.3, 2.9, abs(dh)));
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float vk = mix(0.3, 1.3, u_opt.y);
  // the slab: banded layers folded by a slow warp, and veins where the warped field crosses one level
  vec2 s = rot(p + tiltD(1.0), 0.42) * 0.85;
  vec2 w = vec2(fbm3(vec3(s * 0.65, 1.0)), fbm3(vec3(s * 0.65 + 5.2, 2.0)));
  float v = fbm4(vec3(s * 1.25 + w * 1.7, 3.0));
  float ph = s.y + 0.95 * w.y + 0.7 * v;
  float band = 0.5 + 0.5 * sin(ph * 8.0);
  float band2 = 0.5 + 0.5 * sin(ph * 19.0 + 1.3 * w.x);
  float vein = 1.0 - smoothstep(0.0, 0.03 + 0.02 * u_focus, abs(v + 0.06 - 0.12 * w.x));
  float tr = sat(0.56 + 0.22 * (band - 0.5) + 0.1 * (band2 - 0.5) + 0.55 * v);
  // the lights behind it
  vec2 q = p + tiltD(-1.0);
  float b = breath(5.0 * a) * mix(0.55, 1.0, sat(u_opt.x * 1.5));
  vec2 c1 = vec2(-0.38, -0.18) + orbit(a, 0.12 * m) + vec2(0.04 * m * sin(2.0 * a + 1.0), 0.0);
  vec2 d1 = (q - c1) * vec2(0.8, 1.0);
  float I = exp(-dot(d1, d1) / (0.75 + 0.3 * b)) * (0.48 + 0.52 * b);
  vec2 d2 = q - vec2(1.2, 0.7) - orbit(-a, 0.05 * m);
  float I2 = exp(-dot(d2, d2) / 0.6) * (0.6 - 0.2 * b);
  float g = I * (0.45 + 0.75 * tr);
  float g2 = I2 * (0.4 + 0.7 * tr);
  vec3 col = mix(u_pal[0], u_pal[1], tr);
  col = mixh(col, u_pal[5], sat(g2 * 0.75));
  col = mixh(col, u_pal[2], sat(g * mix(1.4, 1.1, u_dark)));
  col = mix(col, u_pal[4], 0.55 * smoothstep(0.45, 1.0, g));
  col = mix(col, u_pal[3], sat(vein * mix(0.25, 0.4, u_dark) * vk * (0.35 + 0.65 * sat(I + I2))));
  col.x += 0.012 * (band2 - 0.5) * vk * (0.3 + I);
  return outLab(col);
}
