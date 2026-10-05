// Glimmer: the lights of an evening seen far out of focus, through a lens wide open. Each light is a soft disc with
// the faint bright rim real bokeh has; near the edges of the frame the lens clips them into cat's eyes, as real lenses
// do. They lie at a few depths, the near ones larger and softer, gathered low and to the sides, and they drift and
// brighten and dim very slowly, as if the camera breathed. The middle stays nearly clear.
// Palette: 0 the sky high, 1 the evening low, 2 the first lights, 3 the second lights, 4 the warm lights, 5 the haze.
// Options: x = motion (how far the lights drift), y = lights (few to many).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float many = u_opt.y;
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.1, 1.0, p.y));
  col = mix(col, u_pal[5], 0.3 * exp(-(p.y + 0.55) * (p.y + 0.55) / 0.35));
  for (int i = 0; i < 14; i++) {
    float f = float(i);
    vec3 h = hash33(vec3(f * 7.31 + 0.5, f * 3.17 + 2.3, 9.17));
    float z = 0.5 + 0.5 * h.x;
    float show = smoothstep(f - 0.5, f + 0.5, mix(7.0, 14.0, many));
    vec2 q = p + tiltD(mix(-1.0, 1.0, z));
    // spread across the width, gathered low; the middle is left nearly clear
    float yy = fract(f * 0.4142 + 0.3 + 0.2 * h.z);
    vec2 c = vec2(fract(f * 0.618034 + 0.11) * 3.6 - 1.8, -0.9 + 1.35 * yy * yy);
    float k = (h.y > 0.0) ? 1.0 : -1.0;
    c += orbit(k * a + f * 2.1, (0.04 + 0.05 * z) * m);
    float R = mix(0.08, 0.26, z * z);
    float soft = R * mix(0.1, 0.22, z) + 0.004 + 0.01 * u_focus;
    // the lens clips discs toward the frame's edge: the disc meets a second one shifted toward the centre
    vec2 d1 = q - c;
    vec2 d2 = q - c * (1.0 - 0.1 * R);
    float dd = max(length(d1), length(d2));
    float disc = smoothstep(R + soft, R - soft, dd);
    float rim = smoothstep(R * 0.55, R, dd) * disc;
    float shine = (0.62 + 0.38 * sin(a * (1.0 + floor(h.x * 1.99 + 1.0)) + f * 2.7)) * mix(0.85, 0.55, z);
    float side = 0.35 + 0.65 * smoothstep(0.25, 1.0, abs(c.x) * 0.75 + max(-c.y - 0.2, 0.0));
    vec3 lc = (h.y > 0.3) ? u_pal[4] : ((h.y > -0.35) ? u_pal[3] : u_pal[2]);
    float al = sat(disc * shine * side * (0.85 + 0.25 * rim)) * show;
    col = mix(col, lc, al);
    col.x += mix(0.02, 0.07, u_dark) * al;
  }
  return outLab(col);
}
