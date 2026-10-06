// Orbs: four soft-touch pebbles floating in a seamless studio. A wide soft box lights them from the upper left; the
// shadow side keeps a deeper, richer colour (the way lacquered clay holds its pigment), the floor's light comes back
// from below, each pebble tints its neighbours and the wall behind with its own colour, and their shadows fall soft and
// wide on the sweep. The pebbles turn slowly as they float, so the soft box slides over their curves.
// Palette: 0 backdrop, 1 backdrop low (the sweep's floor), 2 large orb, 3 second orb, 4 small orb, 5 the soft box.
// Options: x = motion (how far the orbs float), y = gloss (matte clay to lacquer).
// Dynamic: u_sun moves the soft box (and so the shading, the window and the shadows). Perspective: the wall is the far
// layer (tiltD -1) and each orb sits at a depth set by its distance from the wall.
const float T = 48.0;

// centre (x, y), radius, depth of the centre (larger is nearer)
vec4 orbAt(float i, float a, float m) {
  vec4 s = vec4(0.98, -0.55, 0.62, 0.15);
  if (i > 0.5) s = vec4(-1.08, 0.5, 0.3, 0.05);
  if (i > 1.5) s = vec4(0.22, -0.74, 0.25, 0.6);
  if (i > 2.5) s = vec4(1.38, 0.42, 0.15, 0.4);
  float ph = i * 1.9;
  s.xy += m * vec2(0.03 * sin(a + ph) + 0.01 * sin(2.0 * a + ph * 1.3), 0.045 * cos(a + ph) + 0.012 * sin(3.0 * a - ph));
  return s;
}

vec3 orbCol(float i) {
  vec3 c = u_pal[2];
  if (i > 0.5) c = u_pal[3];
  if (i > 1.5) c = u_pal[4];
  if (i > 2.5) c = mix(u_pal[3], u_pal[4], 0.5);
  return c;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.5, u_opt.x);
  float gloss = u_opt.y;
  // the soft box: its direction follows the time of day (u_sun), and the shadows and the mirrored window follow it
  vec2 L2 = vec2(-0.55, 0.62) + u_sun;
  vec3 Ld = normalize(vec3(L2, 0.62));
  vec2 sdir = -normalize(L2) * 0.47;
  vec2 pw = p + tiltD(-1.0);
  vec3 key = lab2lin(vec3(u_pal[5].x, u_pal[5].yz * 0.6));

  // the sweep: a wall that curves into a floor, lit from the upper left
  float sweep = smoothstep(-1.2, 0.5, pw.y);
  vec3 bd = mix(u_pal[1], u_pal[0], sweep);
  vec3 bg = lab2lin(bd);
  bg *= 0.8 + 0.32 * smoothstep(2.6, 0.0, length(pw - vec2(-1.2, 1.1) - u_sun));
  vec3 bleed = vec3(0.0);
  float shade = 1.0;
  for (int k = 0; k < 4; k++) {
    vec4 s = orbAt(float(k), a, m);
    float lift = 0.2 + s.w;
    vec2 sc = s.xy + sdir * lift;
    float pen = s.z * (0.45 + 1.1 * lift);
    float ds = length((pw - sc) * vec2(0.92, 1.12)) - s.z * 0.9;
    shade *= 1.0 - mix(0.3, 0.55, u_dark) * smoothstep(pen, -pen * 0.7, ds) / (1.0 + 0.8 * lift);
    // light that leaves the orb's lit side and lands, coloured, on the wall around it
    float dg = length(pw - s.xy + vec2(0.1, -0.05) * s.z) / s.z;
    bleed += lab2lin(orbCol(float(k))) * exp(-dg * dg * 0.7) * 0.1 / (1.0 + lift);
  }
  vec3 col = bg * shade + bleed * mix(0.5, 1.0, u_dark) * shade;

  // the orbs, far to near
  for (int k = 0; k < 4; k++) {
    float fi = float(k);
    vec4 s = orbAt(fi, a, m);
    // each orb at its own depth for the perspective: the far ones barely move, the near ones most
    s.xy -= tiltD(s.w * 2.4 - 0.6);
    // a pebble: an ellipsoid that turns slowly about the view axis
    float sq = 0.025 + 0.012 * fi;
    float tw = fi * 1.3 + 0.35 * sin(a + fi * 2.0);
    vec2 q = rot(p - s.xy, -tw) / (s.z * vec2(1.0 + sq, 1.0 - sq));
    float rr = dot(q, q);
    float rl = sqrt(rr);
    float cov = smoothstep(1.0 + 1.5 * edgePx() / s.z, 1.0 - 1.5 * edgePx() / s.z, rl);
    float nz = sqrt(max(1.0 - rr, 0.0));
    vec3 n = normalize(vec3(rot(q / vec2(1.0 + sq, 1.0 - sq), tw), nz));
    vec3 P = vec3(p, s.w + s.z * nz);
    vec3 alb = orbCol(fi);
    // half-Lambert from the wide soft box; the terminator keeps colour instead of greying
    float dif = pow(sat(0.5 + 0.5 * dot(n, Ld)), 1.6);
    vec3 lit = setL(alb, alb.x * mix(mix(0.5, 0.62, 1.0 - u_dark), 1.1, dif));
    lit.yz *= mix(1.05, 0.94, dif);
    lit = hueShift(lit, 0.16 * (dif - 0.5));
    vec3 c = lab2lin(lit);
    // the floor's light from below, and the wall's light at the rim
    c += lab2lin(u_pal[1]) * sat(-n.y * 0.8 + 0.1) * 0.16 * (1.0 - n.z * 0.5);
    float fr = pow(1.0 - n.z, 2.5);
    c = mix(c, lab2lin(setL(bd, bd.x * 1.06)) + bleed * 0.5, fr * mix(0.25, 0.45, gloss));
    // neighbours: occlusion where they come close, and their colour bounced across
    for (int j = 0; j < 4; j++) {
      float fj = float(j);
      float other = step(0.5, abs(fj - fi));
      vec4 o = orbAt(fj, a, m);
      vec3 D = vec3(o.xy, o.w) - P;
      float dl = length(D);
      float ff = sat(dot(n, D / dl)) * (o.z * o.z) / (dl * dl) * other;
      c *= 1.0 - 0.4 * ff;
      c += lab2lin(orbCol(fj)) * ff * 0.3;
    }
    // the soft box mirrored: a broad satin sheen, and on lacquer a soft-edged window with a brighter core
    vec3 H = normalize(Ld + vec3(0.0, 0.0, 1.0));
    float nh = sat(dot(n, H));
    float sheen = pow(nh, mix(5.0, 14.0, gloss)) * mix(0.1, 0.18, gloss);
    vec3 r = reflect(vec3(0.0, 0.0, -1.0), n);
    vec2 bx = rot(r.xy - L2 * 0.88, 0.6) / vec2(0.36, 0.2);
    float box = exp(-pow(dot(bx, bx), mix(1.0, 2.5, gloss)) * 1.6);
    float hot = pow(nh, mix(60.0, 400.0, gloss)) * gloss;
    c += key * (sheen + box * gloss * (0.18 + 0.3 * gloss) + hot * 0.6);
    col = mix(col, c, cov);
  }
  col *= 0.94 + 0.06 * smoothstep(2.3, 0.6, length(p * vec2(0.8, 1.0)));
  return lin2srgb(shoulder(col));
}
