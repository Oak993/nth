// Milky Way: the core of the galaxy rising over a dark ridge on a moonless night. The band of light climbs from the
// horizon, brightest and warmest in the bulge low down, split by dark rifts of dust; stars crowd toward it. Near the
// horizon the air itself glows faintly green and red in slow ripples, and now and then a meteor draws a quick line.
// Palette: 0 the sky high, 1 the sky low, 2 the band's light, 3 the bulge, 4 the airglow, 5 the stars.
// Options: x = motion (how much the airglow ripples and the stars twinkle), y = brightness of the band.
// Perspective: the sky is far (tiltD -1), the ridge near (tiltD 1).
const float T = 75.0;

float starField(vec2 q, float sc, float thr, float a, float seed) {
  vec2 g = q * sc;
  vec2 id = floor(g);
  vec3 h = hash33(vec3(id, seed)) * 0.5 + 0.5;
  vec2 c = id + 0.5 + (h.xy - 0.5) * 0.75;
  float d = length(g - c) / sc;
  float r = 0.8 * u_px;
  float s = exp(-(d * d) / (r * r)) * step(thr, h.z) * (h.z - thr) / (1.0 - thr);
  float vis = sat((1.0 / sc) / u_px / 7.0 - 0.4);
  return s * vis * (0.75 + 0.25 * sin(a * 3.0 + h.x * TAU));
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float br = mix(0.55, 1.4, u_opt.y);
  vec2 q = p + tiltD(-1.0);
  vec3 col = lab2lin(mix(u_pal[1], u_pal[0], smoothstep(-0.7, 0.9, q.y)));
  // the band: a great circle climbing from the lower left
  vec2 o = vec2(-0.75, -0.62);
  vec2 dir = normalize(vec2(0.62, 1.0));
  vec2 r = q - o;
  float along = dot(r, dir);
  float across = dot(r, vec2(-dir.y, dir.x));
  float wid = 0.24 + 0.1 * smoothstep(0.8, 0.0, along);
  float band = exp(-across * across / (wid * wid)) * smoothstep(-0.25, 0.15, along) * (0.55 + 0.45 * smoothstep(2.6, 0.2, along));
  float bulge = exp(-dot(r - dir * 0.18, r - dir * 0.18) * 5.0);
  // clouds of stars and the dark rifts of dust along it
  vec3 sq = vec3(along * 2.2, across * 3.2, 1.0);
  float mot = fbm4(sq * 1.3 + vec3(0.0, 0.0, 0.0));
  float lane = 1.0 - abs(fbm3(vec3(along * 2.0, across * 4.5 + 0.6 * mot, 4.0)) * 2.6);
  float rift = smoothstep(0.45, 0.92, lane) * exp(-pow(across / wid - 0.1, 2.0) * 1.5);
  float grain = 0.6 + 0.8 * (0.5 + 0.5 * gnoise(vec3(along * 16.0, across * 16.0, 3.0))) * (0.5 + 0.5 * gnoise(vec3(along * 5.0, across * 5.0, 6.0)));
  float glow = band * (0.45 + 0.6 * mot) * grain * (1.0 - 0.8 * rift);
  vec3 bc = mix(lab2lin(u_pal[2]), lab2lin(u_pal[3]), bulge);
  col += bc * (glow * 0.34 + bulge * (1.0 - 0.75 * rift) * 0.35) * br;
  // stars, more of them near the band
  float stars = starField(q, 34.0, 0.9 - 0.06 * band, a, 2.0) + 0.7 * starField(q, 71.0, 0.86 - 0.1 * band, a, 5.0);
  stars += 0.5 * starField(q + 0.37, 140.0, 0.8 - 0.15 * band, a, 9.0) * band;
  col += lab2lin(u_pal[5]) * stars;
  // airglow: faint banded light low in the sky, rippling slowly
  float wave = sin(q.x * 5.0 + q.y * 9.0 - 2.0 * a + 0.8 * sin(q.x * 1.3 + a)) * 0.5 + 0.5;
  float ag = exp(-pow((q.y + 0.4) / 0.24, 2.0)) * (0.45 + 0.55 * mix(0.5, wave, sat(0.5 * m + 0.2))) * (0.75 + 0.25 * sin(q.x * 1.7 - a));
  col += lab2lin(u_pal[4]) * ag * 0.26;
  // a meteor, twice per loop, each a short bright streak that fades
  for (int k = 0; k < 2; k++) {
    float fk = float(k);
    float ph = fract(a / TAU * 2.0 + fk * 0.5 + 0.13);
    float life = smoothstep(0.0, 0.02, ph) * smoothstep(0.08, 0.02, ph);
    vec2 s0 = vec2(0.9 - 1.4 * fk, 0.75 - 0.15 * fk);
    vec2 md = normalize(vec2(-0.7 + 1.0 * fk, -0.45));
    vec2 mp = s0 + md * ph * 6.0;
    vec2 dv = q - mp;
    float al = dot(dv, md);
    float ac = dot(dv, vec2(-md.y, md.x));
    float streak = exp(-ac * ac / (u_px * u_px * 1.6 + 1e-6)) * smoothstep(-0.25, 0.0, al) * smoothstep(0.01, -0.01, al);
    col += lab2lin(u_pal[5]) * streak * life * 0.9 * (0.5 + 0.5 * m);
  }
  // the ridge, black against the glow, with the faintest rim of light
  vec2 qr = p + tiltD(1.0);
  float ridge = -0.7 + 0.1 * sin(qr.x * 1.3 + 2.0) + 0.05 * sin(qr.x * 3.7 + 0.4) + 0.02 * sin(qr.x * 9.1) + 0.008 * sin(qr.x * 23.0 + 1.0);
  float rc = smoothstep(edgePx() * 1.3, -edgePx() * 1.3, qr.y - ridge);
  vec3 rl = lab2lin(setL(u_pal[0], u_pal[0].x * mix(0.45, 0.75, 1.0 - u_dark)));
  col = mix(col, rl, rc);
  // light mode: the same sky at the blue hour, so the band is a pale veil
  vec3 lt = mix(lab2lin(mix(u_pal[1], u_pal[0], smoothstep(-0.7, 0.9, q.y))), lab2lin(u_pal[4]), exp(-pow((q.y + 0.6) / 0.35, 2.0)) * 0.6 * (0.55 + 0.9 * ag));
  lt = mix(lt, bc, sat(glow * 0.9 + bulge * 0.4) * 0.5 * br);
  lt = mix(lt, rl, rc);
  return lin2srgb(shoulder(mix(lt, col, u_dark)));
}
