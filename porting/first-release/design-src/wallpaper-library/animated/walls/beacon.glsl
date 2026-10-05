// Beacon: a lighthouse on a dark headland at dusk, its lamp turning in a sea haze. The two beams sweep round once a
// loop: each is a soft shaft of light laid across the haze, long and faint when it points away along the coast,
// shortening and brightening as it swings toward us, until for a moment the lamp flares. Banks of haze drift slowly
// through the beams over a calm, darkening sea.
// Palette: 0 the high sky, 1 the haze low in the sky, 2 the sea, 3 the beam's light, 4 the headland, 5 the lamp.
// Options: x = motion (how much the haze drifts), y = haze (clear to thick).
const float T = 40.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float hz = mix(0.55, 1.4, u_opt.y);
  float yh = -0.28;
  vec2 lamp0 = vec2(1.0, -0.0);
  vec2 lamp = lamp0 - tiltD(0.6);
  // the sky and the sea
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(yh, 1.1, p.y));
  float dz = max(yh - p.y, 0.0);
  vec3 sea = mix(u_pal[1], u_pal[2], smoothstep(0.0, 0.35, dz));
  sea.x += 0.015 * lnoise(vec2(p.x * 8.0, dz * 40.0 / (dz + 0.1)), a, 0.3 * m);
  vec3 col = mix(sea, sky, smoothstep(-edgePx(), edgePx(), p.y - yh));
  vec3 lin = lab2lin(col);
  // the haze, drifting in banks
  float hn = lnoise(p * vec2(0.9, 2.2) + vec2(0.0, 1.0), a, 0.35 * m) * 0.6 + lnoise(p * vec2(2.1, 4.0), a, 0.25 * m) * 0.4;
  float haze = sat((0.55 + 0.6 * hn) * exp(-abs(p.y - yh - 0.15) * 1.6)) * hz;
  // the beams: each turns round a vertical axis; on the screen it reaches out to one side, foreshortened as it turns
  vec3 bl = lab2lin(u_pal[3]);
  float glowSum = 0.0;
  for (int i = 0; i < 2; i++) {
    float th = a + float(i) * 3.14159265;
    float cx = cos(th);
    float tw = sin(th);
    vec2 d = p - lamp;
    float s = d.x * sign(cx);
    float reach = 0.9 + 2.6 * abs(cx);
    float along = s / reach;
    float wd = 0.012 + 0.07 * max(s, 0.0) + 0.05 * (1.0 - abs(cx));
    float off = d.y + 0.035 * s;
    float beam = exp(-off * off / (wd * wd)) * smoothstep(-0.01, 0.04, s) * exp(-max(along, 0.0) * 1.4);
    // brighter as it turns toward us, the way light scattered forward in haze is
    float fwd = 0.55 + 0.45 * sat(tw);
    // the two lamps of the lens are not matched: one beam is a little weaker
    float bi = 1.0 - 0.4 * float(i);
    // the beam lights the haze it passes through, so it carries the haze's own texture
    glowSum += beam * fwd * (0.45 + 0.9 * haze) * (0.75 + 0.5 * hn) * bi;
    // the flare when the beam points straight at us
    float fl = pow(sat(tw), 24.0);
    float r = length(d * vec2(0.6, 1.0));
    glowSum += fl * (exp(-r * r / 0.02) * 1.2 + exp(-r / 0.35) * 0.35) * bi;
  }
  // the haze round the lamp is always lit: a soft glow, and a faint band along the height of the beams
  vec2 dl = (p - lamp) * vec2(0.45, 1.0);
  float halo = exp(-length(dl) / 0.28) * 0.45 + exp(-dot(dl, dl) / 0.004) * 0.6;
  float band = exp(-pow((p.y - lamp.y + 0.01 * (p.x - lamp.x)) / 0.12, 2.0)) * exp(-abs(p.x - lamp.x) * 0.55) * 0.22;
  glowSum += (halo + band * (0.6 + 0.8 * hn)) * (0.5 + 0.7 * haze);
  // dark: light added to the night; light: the warm beam laid over a pale evening, so it keeps its colour
  vec3 lit = lin + bl * glowSum * 0.7;
  vec3 lay = mix(lin, bl, sat(glowSum * 0.85));
  lin = mix(lay, lit, u_dark);
  lin = mix(lin, lab2lin(mix(u_pal[1], u_pal[3], 0.2)), haze * 0.25);
  // the reflection of the lamp's glow on the sea
  float refl = exp(-pow((p.x - lamp.x) / (0.05 + 0.4 * dz), 2.0)) * smoothstep(0.6, 0.0, dz) * step(p.y, yh);
  lin += bl * refl * 0.08 * (0.5 + 0.5 * lnoise(vec2(p.x * 20.0, dz * 60.0), a * 2.0, 0.5));
  // the headland and the tower
  vec2 q = p + tiltD(0.6);
  float land = 0.0;
  {
    float x0 = q.x;
    float top = yh + 0.2 * smoothstep(0.55, 1.05, x0) + 0.012 * sin(x0 * 11.0 + 1.0) + 0.006 * sin(x0 * 29.0);
    float hl = -1.4 + (top + 1.4) * smoothstep(0.36, 0.6, x0);
    float x1 = x0 + 0.004;
    float top1 = yh + 0.2 * smoothstep(0.55, 1.05, x1) + 0.012 * sin(x1 * 11.0 + 1.0) + 0.006 * sin(x1 * 29.0);
    float hl1 = -1.4 + (top1 + 1.4) * smoothstep(0.36, 0.6, x1);
    float sl = (hl1 - hl) / 0.004;
    land = smoothstep(edgePx(), -edgePx(), (q.y - hl) / sqrt(1.0 + sl * sl));
  }
  float hl = yh + 0.2 * smoothstep(0.55, 1.05, q.x);
  float tw2 = 0.02 + 0.012 * (lamp0.y - q.y) / 0.2;
  float tower = smoothstep(tw2 + edgePx(), tw2 - edgePx(), abs(q.x - lamp0.x)) * smoothstep(lamp0.y - 0.01, lamp0.y - 0.02, q.y) * step(hl - 0.03, q.y);
  vec3 lc = lab2lin(u_pal[4]);
  lin = mix(lin, lc, sat(max(land, tower)));
  // the lamp itself
  float lr = length(p - lamp);
  lin += lab2lin(u_pal[5]) * (smoothstep(0.016, 0.008, lr) + exp(-lr * lr / 0.003) * 0.4);
  return lin2srgb(shoulder(lin));
}
