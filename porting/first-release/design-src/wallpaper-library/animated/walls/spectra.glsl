// Spectra: a glass prism turning a little in a beam of white light; the beam leaves it as a fan of spectrum that sweeps
// slowly across the room, through a faint haze. In light mode it is the spectrum falling on a pale wall.
// Palette: 0 background, 1 background low, 2 haze, 3 the spectrum's first colour, 4 its last colour (the fan runs from
// 3 to 4 the long way round the hue circle), 5 the white of the beam.
// Options: x = motion (how far the prism turns), y = brightness.
const float T = 60.0;

float tri(vec2 q, float r, float rp) {
  float d = -1e3;
  for (int k = 0; k < 3; k++) {
    float an = rp - 1.5707963 + float(k) * 2.0943951;
    d = max(d, dot(q, vec2(cos(an), sin(an))));
  }
  return d - r * 0.5;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float br = mix(0.55, 1.45, u_opt.y);
  float h3 = atan(u_pal[3].z, u_pal[3].y);
  float h4 = atan(u_pal[4].z, u_pal[4].y);
  if (h4 < h3) h4 += TAU;
  float Ls = 0.5 * (u_pal[3].x + u_pal[4].x);
  float Cs = 0.5 * (length(u_pal[3].yz) + length(u_pal[4].yz));
  vec2 P0 = vec2(-0.82, -0.1);
  float rp = 0.07 * m * sin(a) + 0.025 * m * sin(2.0 * a + 1.0);
  float R = 0.5;
  float dt = tri(p - P0, R, rp);
  float inside = smoothstep(u_px, -u_px, dt);

  vec3 bg = mix(u_pal[1], u_pal[0], smoothstep(-1.1, 1.0, p.y + 0.25 * p.x));
  vec3 bgLin = lab2lin(bg);
  float vig = 1.0 - 0.35 * smoothstep(0.6, 2.2, length(p * vec2(0.75, 1.0)));
  float haze = 0.78 + 0.22 * lnoise(p * 1.3, a, 0.35 * m);
  vec3 wht = lab2lin(u_pal[5]);

  // the white beam coming in from the left, stopping at the prism
  vec2 dIn = normalize(vec2(1.0, 0.16 + 0.5 * rp));
  vec2 B0 = P0 + vec2(-0.08, 0.03);
  float along = dot(p - B0, dIn);
  float across = dot(p - B0, vec2(-dIn.y, dIn.x));
  float beam = (gauss(across, 0.016 + u_px) + 0.18 * gauss(across, 0.09)) * smoothstep(0.02, -0.04, along) * (1.0 - inside);

  // the fan: angle about the exit point, s runs -1..1 across the spectrum
  vec2 O = P0 + vec2(0.1, -0.03);
  vec2 dv = p - O;
  float r = length(dv);
  float ang = atan(dv.y, dv.x);
  float axis = -0.2 + 2.2 * rp;
  float hw = 0.2 + 0.015 * m * sin(a + 2.0);
  float s = (ang - axis) / hw;
  float fanI = smoothstep(1.08, 0.62, abs(s)) * smoothstep(0.0, 0.35, r) * (1.0 - inside) / (1.0 + 0.35 * r);
  fanI *= step(0.0, dv.x + 0.25);
  vec3 sc = lab2lin(lch(Ls, Cs, mix(h4, h3, sat(s * 0.5 + 0.5))));
  float s2 = (ang - axis + 0.62 + 1.2 * rp) / 0.12;
  float fan2 = smoothstep(1.1, 0.4, abs(s2)) * smoothstep(0.0, 0.3, r) * (1.0 - inside) * 0.22 / (1.0 + 0.6 * r);
  vec3 sc2 = lab2lin(lch(Ls, Cs * 0.8, mix(h3, h4, sat(s2 * 0.5 + 0.5))));
  float halo = gauss(s, 1.25) * smoothstep(0.0, 0.5, r) * (1.0 - inside) * 0.07;

  // the glass: a faint body, a bright rim on the faces toward the light, the light inside it
  float rim = exp(-abs(dt) / (1.4 * u_px + 0.002));
  vec2 nq = normalize(p - P0 + 1e-4);
  float lit = 0.45 + 0.55 * sat(dot(nq, normalize(vec2(-0.6, 0.8))));
  vec2 pq = p - P0;
  float path = gauss(dot(pq, vec2(-dIn.y, dIn.x)) + 0.03 + 0.12 * sat(dot(pq, dIn) + 0.1), 0.035 + 0.06 * sat(dot(pq, dIn) + 0.15));
  float inner = inside * (0.012 + 0.2 * path * smoothstep(-0.3, 0.05, dot(pq, dIn)));
  float face = inside * gauss(dot(pq, normalize(vec2(-0.6, 0.8))) - 0.12, 0.12) * 0.06;
  vec3 inTint = sc * inside * path * smoothstep(-0.05, 0.12, dot(pq, dIn)) * 0.35;

  vec3 light = sc * fanI * haze + sc2 * fan2 + wht * (beam * 1.1 + halo * haze * 0.4);
  vec3 dk = bgLin * vig * (1.0 - 0.3 * inside) + light * br + lab2lin(u_pal[2]) * haze * 0.05 + wht * (inner + face + rim * lit * 0.55) + inTint * br;
  float shadow = smoothstep(0.12, -0.08, tri(p - P0 - vec2(0.07, -0.09), R, rp)) * (1.0 - inside);
  vec3 lt = bgLin * (1.0 - 0.1 * shadow) * mix(1.0, vig, 0.4);
  lt = mix(lt, sc * 1.05, sat(fanI * haze * 0.85 * br));
  lt = mix(lt, sc2, sat(fan2 * 1.6 * br));
  lt = mix(lt, wht, sat(beam * 0.9 + inner * 1.5 + rim * lit * 0.7));
  lt = mix(lt, lab2lin(u_pal[2]), inside * 0.2);
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
