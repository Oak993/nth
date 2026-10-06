// Summit: a snow mountain in the last light, long after the valley has gone into shadow. The faces turned to the
// set sun glow rose; the faces turned away hold a cold blue; ribs of dark rock show where it is too steep for snow.
// A plume of spindrift streams from the summit ridge in the high wind, and a lower ridge stands dark in front.
// Palette: 0 the high sky, 1 the sky low, 2 snow in shadow, 3 snow in the glow, 4 rock, 5 the brightest glow.
// Options: x = motion (how hard the wind blows the plume), y = glow (faint to strong).
const float T = 60.0;

// the main massif: its height above the base line, and in w its shading slope across the face
float massif(float x) {
  float r = 0.62 - 0.85 * abs(x - 0.25) + 0.05 * sin(x * 7.0 + 1.0) + 0.025 * sin(x * 17.0 + 0.4);
  r = max(r, 0.38 - 0.55 * abs(x + 0.75) + 0.03 * sin(x * 11.0));
  return r + 0.012 * sin(x * 41.0);
}

// the relief of the face: ridged noise, sharp along its crests, its gullies running down the slope
float face(vec2 q) {
  vec2 r = vec2(q.x * 3.2 + 0.4 * sin(q.y * 2.0), q.y * 1.3);
  float h = 0.0;
  float w = 0.5;
  for (int i = 0; i < 3; i++) {
    h += w * (1.0 - abs(gnoise(vec3(r, 1.7 + float(i)))));
    r = r * 2.1 + vec2(0.3, 1.1);
    w *= 0.45;
  }
  return h * 0.12;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float gk = mix(0.5, 1.3, u_opt.y);
  // the sky
  vec2 pf = p + tiltD(-1.0);
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(-0.4, 1.0, pf.y));
  vec3 col = sky;
  // the far range, faint in the blue
  float far = -0.22 + 0.12 * sin(pf.x * 2.3 + 1.0) + 0.05 * sin(pf.x * 5.7) + 0.02 * sin(pf.x * 13.0);
  col = mix(col, mix(u_pal[2], u_pal[1], 0.6), smoothstep(edgePx(), -edgePx(), pf.y - far));
  // the massif
  vec2 pm = p + tiltD(0.0);
  float base = -0.35;
  float top = base + massif(pm.x);
  float inM = smoothstep(edgePx(), -edgePx(), pm.y - top);
  // its face: a relief of sharp ribs and gullies (ridged noise) on the cone of the mountain, lit from the west
  float hgt = top - pm.y;
  float e = 0.006;
  float h0 = face(pm);
  float hx = face(pm + vec2(e, 0.0)) - h0;
  float hy = face(pm + vec2(0.0, e)) - h0;
  float cone = (pm.x < 0.25 + 0.02 * sin(pm.y * 9.0)) ? 1.0 : -1.0;
  vec3 n = normalize(vec3(-hx / e * 1.6 + 0.4 * cone, -hy / e * 1.2 + 0.2, 1.0));
  vec3 Ld = normalize(vec3(0.85 - 0.4 * u_sun.x, 0.15, 0.5));
  float lit = sat(dot(n, Ld));
  float glowH = smoothstep(-0.45, 0.05, pm.y) * (0.8 + 0.2 * sin(a + 0.6)) * gk;
  vec3 snow = mix(u_pal[2], u_pal[3], sat(smoothstep(0.25, 0.85, lit) * glowH));
  snow = mix(snow, u_pal[5], pow(sat(lit * glowH), 4.0) * smoothstep(0.5, 0.0, hgt) * 0.5);
  // rock where the face is too steep to hold snow: the sharp crests' flanks
  float steep = length(vec2(hx, hy)) / e;
  float rock = smoothstep(0.75, 1.25, steep) * smoothstep(0.02, 0.1, hgt);
  snow = mix(snow, mix(u_pal[4], u_pal[3], 0.3 * lit * glowH), rock * 0.8);
  col = mix(col, snow, inM);
  // the spindrift: a soft plume streaming right from the summit, its field travelling with the wind
  float D = 3.0;
  float R = D / TAU;
  vec2 sp = pm - vec2(0.27, base + 0.62);
  float ph = TAU * sp.x / D - a;
  float pl = fbm3(vec3(R * cos(ph), R * sin(ph), sp.y * 4.0) * 2.0);
  float plume = smoothstep(-0.1, 0.35, pl) * exp(-pow((sp.y - 0.06 * sp.x) / (0.03 + 0.12 * max(sp.x, 0.0)), 2.0)) * smoothstep(-0.02, 0.08, sp.x) * exp(-max(sp.x, 0.0) * 1.3);
  col = mix(col, mix(u_pal[3], u_pal[5], 0.4), sat(plume * 0.55 * m * gk));
  // valley mist at the mountain's foot
  float mist = exp(-pow((p.y + 0.42) / 0.16, 2.0)) * (0.7 + 0.3 * lnoise(vec2(p.x * 1.5, 0.0), a, 0.3 * m));
  col = mix(col, mix(u_pal[2], u_pal[1], 0.5), mist * 0.45);
  // the dark ridge in front
  vec2 pn = p + tiltD(1.0);
  float near = -0.62 + 0.14 * sin(pn.x * 1.7 + 2.2) + 0.05 * sin(pn.x * 4.3 + 0.5) + 0.015 * sin(pn.x * 19.0);
  vec3 nc = mix(u_pal[4], u_pal[2], 0.25);
  nc.x *= 0.8 + 0.12 * smoothstep(-1.0, near, pn.y);
  col = mix(col, nc, smoothstep(edgePx(), -edgePx(), pn.y - near));
  return outLab(col);
}
