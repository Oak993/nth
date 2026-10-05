// Nebula: glowing gas folded by slow currents, with dark dust lanes across it and a few quiet stars. In light mode the
// same clouds read as pigment drifting in milk.
// Palette: 0 deep space, 1 space glow, 2 gas, 3 second gas, 4 warm dust, 5 the bright heart.
// Options: x = motion (how far the gas churns), y = density (how much of the sky it fills).
const float T = 90.0;

float stars(vec2 p, float a) {
  float sc = 26.0;
  vec2 q = p * sc;
  vec2 id = floor(q);
  vec3 h = hash33(vec3(id, 1.9)) * 0.5 + 0.5;
  vec2 c = id + 0.5 + (h.xy - 0.5) * 0.7;
  float d = length(q - c) / sc;
  float r = 0.8 * u_px;
  float s = exp(-(d * d) / (r * r)) * step(0.87, h.z) * (h.z - 0.87) * 7.0;
  float vis = sat((1.0 / sc) / u_px / 9.0 - 0.5);
  return s * (0.75 + 0.25 * sin(a * 2.0 + h.y * TAU)) * vis;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float den = mix(0.65, 1.4, u_opt.y);
  vec2 pg = p + tiltD(-0.6);
  vec3 q = vec3(pg * 0.72, 0.0);
  vec3 o1 = vec3(orbit(a, 0.2 * m), 0.25 * m * sin(a + 1.0));
  vec2 w = vec2(fbm3(q + o1), fbm3(q + vec3(5.2, 1.3, 2.0) - o1.yxz));
  vec3 q2 = vec3(pg * 0.72 + w * 0.95, 0.5) + vec3(orbit(-a, 0.14 * m), 0.18 * m * cos(a));
  float dn = fbm4(q2 * 1.4);
  float lane = fbm3(q2 * 2.7 + vec3(7.0, 2.0, 1.0));
  float e = smoothstep(-0.2, 0.55, dn * den + 0.12 * (den - 1.0) - 0.06 * length(p));
  vec3 cl = mix(u_pal[2], u_pal[3], smoothstep(-0.35, 0.35, w.x - w.y));
  cl = mix(cl, u_pal[4], smoothstep(0.12, 0.5, dn) * 0.45);
  vec3 cLin = lab2lin(cl);
  float dust = smoothstep(-0.02, 0.32, lane) * e;
  vec3 bgLin = lab2lin(mix(u_pal[0], u_pal[1], smoothstep(-0.3, 0.6, w.y + 0.2 * e)));
  vec3 em = cLin * e * (1.0 - 0.6 * dust) * 1.05 + lab2lin(u_pal[5]) * pow(e, 3.0) * (1.0 - dust) * 0.4;
  vec3 dk = bgLin + em + vec3(stars(p + tiltD(0.9), a)) * (1.0 - e * 0.8);
  vec3 ab = -log(max(cLin, vec3(0.03))) * 0.55;
  vec3 lt = bgLin * exp(-ab * e * (1.6 + 0.6 * dust)) ;
  lt = mix(lt, lab2lin(u_pal[5]), pow(e, 3.0) * 0.25);
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
