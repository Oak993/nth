// Medusa: moon jellies drifting in deep water, lit from within. Each bell is almost clear: its light gathers at the
// scalloped rim, along the fine canals that run out from the crown, and in the four glowing rings at its heart; a fringe
// of hair-fine tentacles hangs from the rim and four ruffled arms trail below. The bells pulse, contracting and
// opening, each in its own slow rhythm, rising a little at every beat.
// Palette: 0 the deep water, 1 the water toward the light, 2 first bell, 3 second bell, 4 the tentacles and arms,
// 5 the glow of the rings.
// Options: x = motion (how strongly the bells pulse and drift), y = glow.
// Perspective: the water is far (tiltD -1); each jelly's depth follows its size.
const float T = 40.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float gl = mix(0.5, 1.5, u_opt.y);
  vec2 pw = p + tiltD(-1.0);
  vec3 water = lab2lin(mix(u_pal[0], u_pal[1], smoothstep(-1.2, 1.4, pw.y + 0.2 * pw.x)));
  // light from the surface, in soft shafts
  float shaft = 0.5 + 0.5 * sin(pw.x * 4.0 + pw.y * 1.2 + 0.4 * sin(a));
  water *= 1.0 + 0.12 * shaft * smoothstep(-0.6, 1.2, pw.y);
  vec3 col = water;
  vec3 sumC = vec3(0.0);
  float sumW = 0.0;
  vec3 glowC = lab2lin(u_pal[5]);
  vec3 tl = lab2lin(u_pal[4]);
  for (int i = 0; i < 3; i++) {
    float f = float(i);
    vec2 c = vec2(-0.95, 0.38);
    float S = 0.42;
    float tilt = 0.28;
    vec3 bc = u_pal[2];
    if (i == 1) { c = vec2(0.98, 0.1); S = 0.56; tilt = -0.2; bc = u_pal[3]; }
    if (i == 2) { c = vec2(0.12, 0.74); S = 0.2; tilt = 0.12; bc = mix(u_pal[2], u_pal[3], 0.5); }
    float pulse = 0.5 + 0.5 * sin(a * 2.0 + f * 2.1);
    float pk = pow(pulse, 3.0);
    c += m * vec2(0.04 * sin(a + f * 1.7), 0.05 * cos(a + f) + 0.03 * pk);
    vec2 q = rot(p + tiltD((S - 0.3) * 4.0) - c, -tilt) / S;
    // the bell: a low dome, its rim scalloped; it narrows and deepens as it contracts
    float wB = 1.0 - 0.13 * m * pk;
    float hB = 0.5 + 0.1 * m * pk;
    float ax = abs(q.x / wB);
    float top = hB * pow(max(1.0 - pow(ax, 2.2), 0.0), 0.55);
    float scal = 0.018 * abs(sin(q.x / wB * 14.0));
    float bot = -0.06 + 0.16 * ax * ax - 0.05 * m * pk - scal * smoothstep(0.6, 1.0, ax);
    float ew = 2.0 * edgePx() / S;
    float inB = smoothstep(1.0 + ew, 1.0 - ew, ax) * smoothstep(bot - ew, bot + ew, q.y) * smoothstep(top + ew, top - ew, q.y);
    float h = sat((q.y - bot) / max(top - bot, 0.05));
    float rimT = exp(-pow((q.y - top) / 0.035, 2.0)) * smoothstep(1.02, 0.9, ax);
    float edge = exp(-pow((q.y - bot) / 0.028, 2.0)) * smoothstep(1.06, 0.95, ax);
    // the radial canals, seen through the dome
    float ang = atan(q.x / wB, max(q.y + 0.35, 0.05));
    float canals = pow(0.5 + 0.5 * cos(ang * 22.0), 14.0) * inB * (1.0 - h) * 0.6;
    // the four rings at the heart, seen from the side as horseshoes
    float rings = 0.0;
    for (int k = 0; k < 4; k++) {
      float fk = float(k);
      float an = fk * 1.5708 + 0.4;
      vec2 rc = vec2(cos(an) * 0.2 * wB, 0.16 + sin(an) * 0.06);
      vec2 rq = (q - rc) / vec2(0.13, 0.07);
      float rr = abs(length(rq) - 0.75);
      rings += exp(-rr * rr / 0.12) * 0.6 + exp(-dot(rq, rq) * 0.8) * 0.15;
    }
    rings *= inB;
    float body = inB * (0.08 + 0.35 * pow(ax, 2.5) + 0.12 * (1.0 - h) + 0.2 * pow(h, 6.0));
    // the fringe of fine tentacles from the rim, and four ruffled arms below
    float tent = 0.0;
    for (int k = 0; k < 13; k++) {
      float fk = float(k);
      float x0 = (-0.98 + fk * 0.163) * wB;
      float yy = -0.06 + 0.16 * (x0 / wB) * (x0 / wB) - 0.05 * m * pk - q.y;
      float sway = (0.08 * m * sin(yy * 3.5 - a * 2.0 + fk * 1.3 + f) + 0.04 * sin(yy * 1.7 + fk)) * yy;
      float dx = q.x - x0 * (1.0 - 0.15 * yy) - sway;
      tent += exp(-dx * dx / (pow(edgePx() * 0.9 / S, 2.0) + 0.000015)) * smoothstep(0.0, 0.04, yy) * exp(-yy * 1.3) * 0.8;
    }
    float arm = 0.0;
    for (int k = 0; k < 2; k++) {
      float fk = float(k);
      float yy = -0.06 - q.y;
      float dx = q.x - (fk - 0.5) * 0.22 - 0.09 * m * sin(yy * 4.0 - a * 2.0 + fk * 2.0) * yy;
      float w = 0.05 + 0.028 * sin(yy * 22.0 + fk + a) + 0.01 * sin(yy * 41.0);
      arm += smoothstep(w, w * 0.4, abs(dx)) * smoothstep(0.0, 0.1, yy) * exp(-yy * 1.3);
    }
    vec3 bl = lab2lin(bc);
    vec3 L = bl * (body + rimT * 0.6 + canals * 0.5) + mix(bl, glowC, 0.6) * edge * 0.9 + mix(bl, glowC, 0.45) * rings * 0.4 + tl * (tent * 0.55 + arm * 0.3);
    vec2 hq = q - vec2(0.0, 0.18);
    float halo = exp(-dot(hq, hq) * 1.6) * 0.1;
    L += bl * halo;
    col += L * gl * u_dark;
    float w = body + rimT * 0.5 + canals * 0.4 + edge * 0.7 + rings * 0.5 + tent * 0.6 + arm * 0.35 + halo;
    sumC += mix(mix(bl, glowC, sat(rings * 0.5)), tl, sat((tent + arm) / max(w, 1e-3))) * w;
    sumW += w;
  }
  vec3 avg = sumC / max(sumW, 1e-4);
  vec3 lt = mix(water, avg * 0.9, sat(1.0 - exp(-sumW * 2.6 * gl)) * 0.95);
  return lin2srgb(shoulder(mix(lt, col, u_dark)));
}
