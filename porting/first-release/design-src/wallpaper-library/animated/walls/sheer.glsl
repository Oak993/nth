// Sheer: a sheer voile curtain drawn across a bright window, moving in a faint draught. It hangs still at the top and
// sways more toward the hem. Where the cloth turns edge-on in a fold the light passes through more of it, so the folds
// read as soft denser stripes; where two panels overlap the cloth is deeper still. The window's frame shows faintly
// through it, out of focus. Perspective parts the curtain from the window behind it.
// Palette: 0 the room beyond the window's light, 1 the cloth in shade, 2 the window light through the cloth, 3 the
// cloth's own colour where it is dense, 4 the window frame seen through, 5 the brightest light.
// Options: x = motion (how strongly the draught moves the cloth), y = folds (few to many).
const float T = 72.0;

// one panel: how much cloth the view crosses, low where it faces the window, high in the folds
float panel(vec2 q, float a, float m, float k, float seed) {
  float hang = smoothstep(1.15, -1.1, q.y);
  float sway = hang * m * (0.55 * sin(a + q.x * 0.8 + seed) + 0.25 * sin(2.0 * a - q.x * 1.3 + seed * 2.0));
  float ph = q.x * k + 1.1 * sin(q.x * 1.4 + seed) + 0.7 * sin(q.x * 0.55 + 2.0 * seed) + sway + 0.4 * hang * sin(q.x * 2.3 - a + seed);
  // where the cloth turns edge-on to us the light crosses much more of it: a fine denser line at each turn, the front
  // turns a little stronger than the back ones; between them the cloth faces us, slightly shaded as it rounds
  float s = sin(ph);
  float c = cos(ph);
  float line = exp(-s * s / (0.05 + 0.03 * u_focus));
  float soft = exp(-s * s / 0.4);
  return 0.2 + line * (0.55 + 0.25 * sign(c)) + 0.12 * soft + 0.14 * c;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float k = mix(4.0, 9.0, u_opt.y);
  // the window behind: a bright soft field with the shadow of its frame
  vec2 wq = p + tiltD(-1.0);
  vec2 lc = vec2(0.35, 0.3) + u_sun;
  float light = exp(-dot((wq - lc) * vec2(0.55, 0.75), (wq - lc) * vec2(0.55, 0.75)));
  float frame = max(gauss(wq.x - 1.22, 0.08), gauss(wq.y - 0.62, 0.07) * smoothstep(0.2, 0.9, wq.x));
  vec3 back = mix(u_pal[0], u_pal[2], light);
  back = mix(back, u_pal[5], 0.4 * light * light);
  back = mix(back, u_pal[4], frame * mix(0.25, 0.14, u_dark) * (0.4 + 0.6 * light));
  // the cloth in front: two panels, the second overlapping the left part
  vec2 q = p + tiltD(1.0);
  float d1 = panel(q, a, m, k, 0.0);
  float d2 = panel(q * vec2(1.0, 1.0) + vec2(0.37, 0.0), a + 1.9, m * 0.85, k * 1.15, 3.7);
  float over = smoothstep(0.12, -0.35, q.x + 0.5 + 0.06 * sin(q.y * 1.7 + a));
  float dens = d1 + over * (0.35 + 0.8 * d2);
  // a faint thread grain along the hang
  float grain = gnoise(vec3(q.x * 140.0, q.y * 3.0, 1.0)) * sat(0.007 / u_px - 0.4);
  float veil = sat(0.2 + 0.5 * dens + 0.03 * grain);
  vec3 cloth = mix(u_pal[1], u_pal[3], sat(dens * 0.8));
  cloth = mix(cloth, u_pal[2], 0.35 * light);
  vec3 col = mix(back, cloth, veil);
  // light caught in the cloth where it faces the window
  col = mix(col, u_pal[5], 0.12 * light * (1.0 - sat(dens)));
  return outLab(col);
}
