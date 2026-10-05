// Afterglow: warm light leaking into the frame, as at the edge of a roll of film: wide soft flares that slide along the
// edges, swell and fade, with a hot core and a faint streak, over a quiet dark (or paper-pale) ground.
// Palette: 0 ground, 1 ground glow, 2 warm leak, 3 hot leak, 4 cool counter-light, 5 the hot core.
// Options: x = motion (how far the leaks travel), y = intensity.
const float T = 36.0;

vec2 leak(vec2 p, vec2 c, vec2 s, float b) {
  vec2 d = (p - c) / s;
  float q = dot(d, d);
  return vec2(exp(-q), exp(-q * 5.0)) * b;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float k = mix(0.45, 1.35, u_opt.y);
  vec3 ground = mix(u_pal[0], u_pal[1], smoothstep(-1.4, 1.4, p.x * 0.5 - p.y * 0.7));
  vec3 lin = lab2lin(ground);
  vec2 l1 = leak(p, vec2(-1.75, 0.25 + 0.45 * m * sin(a)), vec2(0.75, 1.3), 0.6 + 0.4 * cos(a + 0.5));
  vec2 l2 = leak(p, vec2(1.0 + 0.35 * m * cos(a + 1.0), 1.25), vec2(1.2, 0.55), 0.5 + 0.4 * cos(a + 2.6));
  vec2 l3 = leak(p, vec2(0.3 + 0.55 * m * sin(a + 2.0), -1.3), vec2(1.1, 0.5), 0.45 + 0.35 * cos(a + 4.2));
  vec2 l4 = leak(p, vec2(1.75, -0.45 + 0.3 * m * cos(a + 3.0)), vec2(0.5, 0.9), 0.25 + 0.2 * cos(a + 1.3));
  float sx = -1.05 + 0.3 * m * sin(a + 0.7);
  float streak = gauss(p.x - sx - 0.12 * p.y, 0.09) * (0.15 + 0.1 * cos(a * 2.0)) * smoothstep(1.3, -0.2, abs(p.y - 0.1));
  vec3 cw = lab2lin(u_pal[2]);
  vec3 ch = lab2lin(u_pal[3]);
  vec3 cc = lab2lin(u_pal[4]);
  vec3 c5 = lab2lin(u_pal[5]);
  vec3 L = cw * (l1.x + l3.x * 0.8) + ch * (l1.x * l1.x + l2.x) * 0.8 + cc * l4.x + c5 * (l1.y + l2.y * 0.7 + l3.y * 0.5) + cw * streak;
  L *= k;
  // dark: light added to film, its faint tails falling off faster than its core, so the ground stays clean between leaks
  vec3 dk = 1.0 - (1.0 - lin) * exp(-(L * 0.9 + L * L * 1.1));
  float wsum = l1.x + l2.x + l3.x + l4.x + streak;
  // light mode: the leaks are coloured light laid over paper, so their colour shows as a wash, deepest at the edge of
  // the frame, with a lighter hot core: visibly colourful, still soft
  float al = 1.0 - exp(-wsum * k * 4.2);
  vec3 Lc = cw * (l1.x + l3.x * 0.8 + streak) + ch * (l1.x * l1.x + l2.x) * 0.8 + cc * l4.x;
  vec3 avg = Lc / max(l1.x * (1.8 + l1.x * 0.8) + l2.x * 0.8 + l3.x * 0.8 + l4.x + streak, 1e-3);
  vec3 lt = mix(lin, avg, al * 0.94);
  lt = mix(lt, c5, sat((l1.y + l2.y * 0.7 + l3.y * 0.5) * k * 0.55));
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
