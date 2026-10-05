// Corona: an aurora seen from straight beneath it. Its rays are vertical, so from below they all run toward one point
// overhead, just above the top of the window: a crown of light. Each curtain has a bright, sharp lower border, farthest
// from that point, folded and slowly travelling; its rays climb from it toward the crown, shimmering and fading from
// the border's colour through the body's to the crown's high, faint one.
// Palette: 0 the sky overhead, 1 the sky toward the edges, 2 lower border, 3 body, 4 crown, 5 the fringe just under the
// border.
// Options: x = motion (how far the curtains fold and drift), y = glow.
// Perspective: the stars are farthest, the lowest curtain nearest (tiltD 1, 0.2, -0.6).
const float T = 60.0;

float stars(vec2 p, float a) {
  float sc = 28.0;
  vec2 q = p * sc;
  vec2 id = floor(q);
  vec3 h = hash33(vec3(id, 5.3)) * 0.5 + 0.5;
  vec2 c = id + 0.5 + (h.xy - 0.5) * 0.7;
  float d = length(q - c) / sc;
  float r = 0.85 * u_px;
  float s = exp(-(d * d) / (r * r)) * step(0.86, h.z) * (h.z - 0.86) * 6.5;
  float vis = sat((1.0 / sc) / u_px / 9.0 - 0.5);
  return s * (0.7 + 0.3 * sin(a * 3.0 + h.x * TAU)) * vis;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float glow = mix(0.5, 1.5, u_opt.y);
  vec2 V = vec2(0.42, 2.25);
  vec2 d = p - V;
  float r = length(d);
  float th = atan(d.x, -d.y);
  vec3 sky = mix(u_pal[0], u_pal[1], smoothstep(1.2, 3.8, r));
  vec3 skyLin = lab2lin(sky);
  float fine = sat(r * 0.016 / u_px / 3.0 - 0.3);
  vec3 add = vec3(0.0);
  float wsum = 0.0;
  for (int i = 0; i < 3; i++) {
    float f = float(i);
    // the lowest curtain is the nearest: each lies at its own depth for the perspective
    vec2 dd = p + tiltD(1.0 - 0.8 * f) - V;
    float r = length(dd);
    float th = atan(dd.x, -dd.y);
    float R0 = 3.05 - 0.62 * f;
    float u = th * (2.6 + 0.4 * f) + f * 2.7;
    float fold = 0.12 * sin(u * 2.2 + a + f * 1.9) + 0.05 * sin(u * 4.1 - 2.0 * a + f) + 0.025 * sin(u * 8.3 + 3.0 * a + f * 2.0);
    fold += 0.16 * m * lnoise(vec2(u * 0.9, f * 3.0), a + f, 0.5) + 0.05 * lnoise(vec2(u * 3.1, f * 7.0), 2.0 * a, 0.3);
    float rb = R0 + fold;
    float dr = rb - r;
    float h = 0.5 + 0.12 * f;
    float prof = dr < 0.0 ? exp(-(dr * dr) / 0.0012) : exp(-dr / h) * (0.35 + 0.65 * exp(-(dr * dr) / 0.03));
    float cov = smoothstep(-0.3, 0.45, lnoise(vec2(u * 0.55 + 4.0 * f, 9.0 + f), a, 0.4 + 0.25 * m));
    cov *= 0.55 + 0.6 * sat(0.5 + lnoise(vec2(u * 2.6 + 1.0, 4.0 * f + 2.0), 2.0 * a, 0.35));
    float k = th * 70.0 + fold * 6.0;
    float rays = 0.55 + 0.55 * lnoise(vec2(k, 3.0 * f + r * 0.4), 2.0 * a, 0.12 + 0.25 * m);
    rays = mix(0.85, rays, fine) * (0.75 + 0.45 * lnoise(vec2(th * 18.0, f * 5.0), a, 0.3 + 0.2 * m));
    rays = mix(1.0, max(rays, 0.0) * 1.3, smoothstep(-0.02, 0.1, dr));
    vec3 col = mix(u_pal[2], u_pal[3], smoothstep(0.02, 0.3, dr));
    col = mix(col, u_pal[4], smoothstep(0.2, 0.75, dr));
    col = mix(col, u_pal[5], smoothstep(0.0, -0.05, dr) * mix(0.3, 0.8, u_dark));
    float I = prof * cov * rays * (1.0 - 0.18 * f) * (1.0 + 0.9 * smoothstep(0.2, 0.8, dr));
    add += lab2lin(col) * I;
    wsum += I;
  }
  vec3 avg = add / max(wsum, 1e-4);
  vec3 dk = skyLin + add * glow * 0.8 + vec3(stars(p + tiltD(-1.0), a)) * (1.0 - sat(wsum * 1.4)) * 0.9;
  dk += avg * sat(wsum) * 0.05 * glow;
  float al = 1.0 - exp(-wsum * glow * 4.0);
  vec3 lt = mix(skyLin, avg, al * 0.88);
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
