// Bloom: a flower made of light. Two rings of long translucent petals, each with a luminous edge and a clear heart,
// sway against each other and breathe; where petals cross, their light adds (dark) or they filter each other like
// tinted glass (light).
// Palette: 0 background, 1 background glow, 2 petal base, 3 petal tip, 4 inner ring, 5 the flower's core.
// Options: x = motion (how far the petals sway), y = softness (how feathered their edges are).
const float T = 72.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float soft = mix(0.5, 1.8, u_opt.y);
  vec2 c = vec2(0.3, -0.06) + orbit(a, 0.02 * m);
  vec2 d = p - c;
  float r = length(d);
  vec3 bg = mix(u_pal[1], u_pal[0], smoothstep(0.1, 1.7, r));
  vec3 bgLin = lab2lin(bg);
  vec3 add = vec3(0.0);
  vec3 filt = vec3(1.0);
  float sect = TAU / 6.0;
  for (int j = 0; j < 2; j++) {
    float fj = float(j);
    float spin = (fj * 2.0 - 1.0) * 0.16 * m * sin(a + fj * 1.7) + fj * sect * 0.5 + 0.25;
    float len = (1.3 - 0.5 * fj) * (1.0 + 0.04 * m * sin(2.0 * a + fj * 2.0));
    float wid = 0.42 - 0.12 * fj;
    vec2 dj = d + tiltD(0.7 - 1.4 * fj);
    float ang = atan(dj.y, dj.x);
    float k0 = floor((ang - spin) / sect + 0.5);
    for (int k = 0; k < 3; k++) {
      float pa = (k0 + float(k) - 1.0) * sect + spin;
      vec2 q = rot(dj, -pa);
      float u = q.x / len;
      float bend = 0.1 * sin(pa * 2.0 + a) * m;
      float v0 = q.y - bend * u * u * len;
      float prof = pow(sat(sin(3.14159265 * pow(sat(u), 0.8))), 0.6);
      float w = wid * prof + 1e-3;
      float v = abs(v0) / w;
      float fe = 0.06 + 0.16 * soft;
      float body = smoothstep(1.0, 1.0 - fe * 2.0, v) * step(0.0, u) * smoothstep(1.0, 0.94, u);
      float edge = body * smoothstep(0.55, 0.98, v) + body * smoothstep(0.75, 0.98, u) * 0.6;
      float vein = body * gauss(v0 / w, 0.1) * smoothstep(0.2, 0.7, u) * 0.0;
      vec3 col = mix(u_pal[2], mix(u_pal[3], u_pal[4], fj), smoothstep(0.05, 0.95, u));
      col = hueShift(col, 0.14 * sin(pa * 2.0 + fj * 1.3 + 0.4 * sin(a)));
      vec3 cl = lab2lin(col);
      float amt = body * (0.14 + 0.26 * u) + edge * 0.55 + vein;
      add += cl * amt;
      filt *= mix(vec3(1.0), cl, sat(body * (0.2 + 0.35 * u) + edge * 0.35 + vein * 0.5));
    }
  }
  vec3 core = lab2lin(u_pal[5]) * (exp(-(r * r) / 0.006) * 0.9 + exp(-(r * r) / 0.06) * 0.3);
  vec3 dk = bgLin + add + core;
  vec3 lt = bgLin * filt + core * 0.3;
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
