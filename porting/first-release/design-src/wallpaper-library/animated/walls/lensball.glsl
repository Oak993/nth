// Lensball: a sphere of clear crystal resting on a dark, glossy ledge at dusk, the sky behind it far out of focus. The
// crystal bends the whole scene into itself, sharp, turned upside down and squeezed toward its rim, so the soft
// colours behind become a small clear picture inside it: sky, glow and the line of distant hills. A thin bright rim and
// a darker ring where the light leaves through the edge outline it; the ledge mirrors it, and the light it gathers
// falls as a bright spot beside its shadow. Clouds drift across the sky, and inside the crystal they drift backwards.
// Palette: 0 the sky high up, 1 the sky low down, 2 the glow on the horizon, 3 the hills, 4 the ledge, 5 the sun.
// Options: x = motion (how fast the clouds drift), y = focus (how soft the background is).
// Dynamic: u_sun moves the sun along the horizon (and below it at night). Perspective: the far scene is tiltD -1, the
// crystal and its ledge tiltD 0.7.
const float T = 60.0;

// the land and sky behind the crystal; blur 0 = sharp, 1 = far out of focus
vec3 upper(vec2 q, float a, float m, float blur) {
  float hy = -0.3;
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(hy - 0.05, 1.1, q.y));
  vec2 sp = vec2(-0.55, hy + 0.1) + u_sun * vec2(0.8, 0.6);
  float sd = length((q - sp) * vec2(0.8, 1.0));
  sky = mix(sky, u_pal[2], exp(-sd * sd / mix(0.08, 0.16, blur)) * 0.9);
  vec3 lin = lab2lin(sky);
  // clouds: long soft bands lit from below by the glow
  float cl = fbm3(vec3(q.x * 1.2 + 0.25 * m * sin(a), q.y * 4.0, 1.0) + vec3(orbit(a, 0.18 * m), 0.0));
  float band = smoothstep(0.05, 0.4, cl + 0.25 * sin(q.y * 9.0)) * smoothstep(hy + 0.05, hy + 0.35, q.y) * smoothstep(1.0, 0.3, q.y);
  lin = mix(lin, lab2lin(mix(u_pal[2], u_pal[0], 0.35)), band * mix(0.4, 0.22, blur));
  // the sun, low, and the hills on the horizon
  float sun = exp(-sd * sd / mix(0.0012, 0.02, blur));
  lin += lab2lin(u_pal[5]) * sun * mix(1.4, 0.5, blur);
  float hill = hy + 0.05 * sin(q.x * 3.1 + 1.0) + 0.03 * sin(q.x * 7.3 + 2.0) + 0.012 * sin(q.x * 17.0);
  float hs = mix(0.008, 0.05, blur);
  vec3 hc = lab2lin(mix(u_pal[3], u_pal[2], 0.15 * smoothstep(hill - 0.12, hill, q.y)));
  lin = mix(lin, hc, smoothstep(hill + hs, hill - hs, q.y));
  // the lights of a far town at the water's edge: large soft discs out of focus, sharp points in focus
  for (int i = 0; i < 9; i++) {
    float f = float(i);
    vec3 h = hash33(vec3(f * 3.7, 1.3, 7.1)) * 0.5 + 0.5;
    vec2 lp = vec2(-1.75 + f * 0.43 + 0.2 * h.x, hy - 0.06 + 0.035 * h.y);
    float br = mix(0.004, mix(0.035, 0.07, h.x), blur);
    float ld = length(q - lp);
    float disc = smoothstep(br, br * 0.8, ld) * (0.7 + 0.3 * smoothstep(br * 0.4, br * 0.95, ld));
    float pt = exp(-ld * ld / (br * br * 0.4));
    float k = mix(pt, disc * mix(0.35, 0.55, h.z), smoothstep(0.2, 0.6, blur));
    lin += lab2lin(mix(u_pal[2], u_pal[5], 0.45)) * k * (0.8 + 0.2 * sin(a * 2.0 + h.z * TAU)) * step(0.3, h.y + 0.25 * f - floor(h.y + 0.25 * f) + 0.0) ;
  }
  return lin;
}

// the whole scene: below the shore a still lake mirrors the land and sky, a little darker, with a long glitter of sun
vec3 scene(vec2 q, float a, float m, float blur) {
  float sy = -0.42;
  float ws = mix(0.003, 0.03, blur);
  float w = smoothstep(sy + ws, sy - ws, q.y);
  vec2 qq = mix(q, vec2(q.x + 0.006 * sin(q.y * 60.0 + 2.0 * a) * (sy - q.y), 2.0 * sy - q.y), w);
  vec3 lin = upper(qq, a, m, blur);
  float glit = exp(-pow((q.x + 0.55 - u_sun.x * 0.8) / mix(0.05, 0.1, blur), 2.0)) * (0.6 + 0.4 * sin(q.y * 140.0 - 3.0 * a));
  return mix(lin, lin * 0.62 + lab2lin(u_pal[5]) * glit * 0.12 * exp((q.y - sy) * 3.0), w);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.5, u_opt.x);
  float bl = mix(0.55, 1.0, u_opt.y);
  vec2 C = vec2(0.62, -0.08);
  float R = 0.52;
  float ledge = C.y - R;
  // behind: the scene out of focus, and the ledge in front of it, glossy and dark
  vec2 pf = p + tiltD(-1.0);
  p += tiltD(0.7);
  vec3 col = scene(pf, a, m, bl);
  float onLedge = smoothstep(ledge + 0.006, ledge - 0.006, p.y);
  float lip = exp(-pow((p.y - ledge + 0.008) / 0.006, 2.0));
  vec2 mp = vec2(p.x, 2.0 * ledge - p.y);
  // the ledge only below its edge (a branch by region, so the cost is paid only there)
  if (onLedge > 0.0) {
  vec3 refl = scene(vec2(mp.x, mp.y), a, m, 1.0) * 0.11;
  vec3 led = lab2lin(mix(u_pal[4], setL(u_pal[4], u_pal[4].x * 0.7), smoothstep(ledge, -1.0, p.y))) + refl;
  // the crystal's shadow and the light it focuses on the ledge
  vec2 sp = (p - vec2(C.x + 0.38, ledge - 0.05)) / vec2(0.55, 0.06);
  led *= 1.0 - 0.55 * exp(-dot(sp, sp));
  vec2 fp = (p - vec2(C.x + 0.3, ledge - 0.035)) / vec2(0.09, 0.016);
  led += lab2lin(mix(u_pal[2], u_pal[5], 0.5)) * exp(-dot(fp, fp)) * 0.9;
  // the crystal mirrored in the ledge
  vec2 dm = mp - C;
  float rm = length(dm) / R;
  vec3 mir = scene(C - dm * 2.3 / max(0.35, 1.0 - 0.45 * rm * rm), a, m, 0.0);
  led = mix(led, led * 0.5 + mir * 0.3, smoothstep(1.0, 0.96, rm) * smoothstep(ledge - 0.5, ledge, p.y));
  col = mix(col, led, onLedge);
  }
  col += lab2lin(mix(u_pal[2], u_pal[5], 0.5)) * lip * 0.12 * exp(-pow((p.x + 0.55) / 1.2, 2.0));
  // the crystal: the scene through it, upside down and squeezed toward the rim, with a fine spectral fringe
  vec2 d = p - C;
  float r = length(d) / R;
  float cov = smoothstep(1.0 + 1.5 * edgePx() / R, 1.0 - 1.5 * edgePx() / R, r);
  float k = 2.3 / max(0.35, 1.0 - 0.45 * r * r);
  vec2 qn = d / R;
  if (cov > 0.0) {
  vec3 inn;
  inn.r = scene(C - qn * R * k * 0.985, a, m, 0.0).r;
  inn.g = scene(C - qn * R * k, a, m, 0.0).g;
  inn.b = scene(C - qn * R * k * 1.018, a, m, 0.0).b;
  float rim = smoothstep(0.82, 0.97, r);
  inn *= 1.0 - 0.75 * rim * (1.0 - smoothstep(0.97, 1.0, r));
  inn += lab2lin(u_pal[5]) * exp(-pow((r - 0.985) / 0.01, 2.0)) * 0.35;
  // the window of the sky caught on the top, and a hot glint
  vec2 gq = (qn - vec2(-0.32, 0.55)) / vec2(0.32, 0.14);
  inn += lab2lin(mix(u_pal[0], u_pal[5], 0.4)) * exp(-pow(dot(gq, gq), 1.5)) * 0.16;
  col = mix(col, inn, cov);
  }
  return lin2srgb(shoulder(col));
}
