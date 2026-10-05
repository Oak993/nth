// Iris: an iridescent film turning slowly. Its thickness field swirls and drains (thicker toward the bottom), and the
// thickness sets the colour the way interference does: a cycle through the pack's three film colours, with a darker
// trough between bands, so it reads as a soap film or a holographic foil rather than a gradient.
// Palette: 0 backdrop, 1 backdrop glow, 2 first film colour, 3 second, 4 third, 5 highlight.
// Options: x = motion (how far the film swirls), y = bands (broad to fine).
const float T = 50.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float th = mix(0.6, 1.6, u_opt.y);
  vec2 c = vec2(0.25, 0.1);
  vec2 d = p - c;
  float r = length(d);
  vec2 q = rot(d, 1.1 * m * sin(a) * exp(-r * 0.9) + 0.3 * m * sin(a + r * 1.6));
  float n = gnoise(vec3(q * 0.7, 0.0) + vec3(orbit(a, 0.16 * m), 0.18 * m * sin(a + 2.0))) + 0.3 * gnoise(vec3(q * 1.5 + 2.0, 1.0) + vec3(orbit(-a, 0.12 * m), 0.0));
  float dd = (1.2 * n + 1.1 * sat(0.6 - q.y * 0.4) + 0.2 * sin(q.x * 2.0 + n * 3.0)) * th * 1.15;
  float ph = dd * TAU;
  float w0 = pow(0.5 + 0.5 * cos(ph), 2.0);
  float w1 = pow(0.5 + 0.5 * cos(ph - 2.0943951), 2.0);
  float w2 = pow(0.5 + 0.5 * cos(ph - 4.1887902), 2.0);
  vec3 fc = (u_pal[2] * w0 + u_pal[3] * w1 + u_pal[4] * w2) / (w0 + w1 + w2);
  float trough = 0.5 + 0.5 * cos(ph * 1.5 + 1.0);
  float view = dot(normalize(d + 1e-4), normalize(vec2(-0.6, 0.8)));
  fc = hueShift(fc, 0.35 * view * (0.6 + 0.4 * sin(a)));
  float lightAmt = 0.5 + 0.5 * smoothstep(1.9, 0.1, length(p - vec2(-0.3, 0.4)));
  vec3 bgLin = lab2lin(mix(u_pal[0], u_pal[1], smoothstep(1.5, 0.0, r)));
  vec3 hl = lab2lin(u_pal[5]) * (gauss(length(p - vec2(-0.6, 0.52)), 0.32) * 0.2 + gauss(length(p - vec2(-0.64, 0.56)), 0.05) * 0.3);
  vec3 dkc = vec3(fc.x * (0.55 + 0.4 * trough), fc.yz * (0.45 + 0.55 * trough));
  vec3 dk = bgLin * 0.6 + lab2lin(dkc) * lightAmt * 0.85 + hl;
  vec3 ltc = vec3(mix(0.8, fc.x, 0.6) * (0.92 + 0.08 * trough), fc.yz * (0.7 + 0.3 * trough));
  vec3 lt = mix(bgLin, lab2lin(ltc), 0.85) + hl * 0.3;
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
