// Ridgeline: range after range of mountains at the last of the sun, each ridge paler and bluer than the one in front
// as the air between them thickens, the sky burning from deep colour overhead to a hot glow on the horizon. Mist lies in
// the valleys and drifts slowly between the ranges, catching the light; the sun's glow breathes as thin cloud passes.
// Palette: 0 the sky overhead, 1 the glow on the horizon, 2 the nearest range, 3 the far ranges (the air), 4 the mist,
// 5 the sun.
// Options: x = motion (how far the mist drifts), y = haze (clear air to thick haze).
// Dynamic: u_sun moves the sun along the horizon. Perspective: each range is its own layer, the sky the farthest.
const float T = 72.0;

float ridge(float x, float i) {
  float s = i * 1.73;
  float h = 0.5 * sin(x * (1.1 + 0.25 * i) + s) + 0.25 * sin(x * (2.3 + 0.4 * i) + s * 2.1) + 0.12 * sin(x * (4.7 + 0.9 * i) + s * 0.7);
  h += 0.05 * sin(x * (9.8 + 2.0 * i) + s * 3.3) + 0.022 * sin(x * (21.0 + 3.0 * i) + s * 1.9);
  // rock: finer, sharper detail on the nearer ranges
  h += (0.012 * abs(sin(x * 43.0 + s)) + 0.006 * abs(sin(x * 97.0 + s * 2.0))) * i / 4.0;
  return h;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float hz = mix(0.6, 1.5, u_opt.y);
  vec2 sun = vec2(0.45, -0.12) + u_sun;
  // the sky: deep overhead, burning toward the horizon, brightest near the sun
  vec2 ps = p + tiltD(-1.0);
  float sky = smoothstep(-0.35, 1.0, ps.y);
  vec3 col = lab2lin(mix(u_pal[1], u_pal[0], pow(sky, 0.8)));
  vec2 sd = (ps - sun) * vec2(0.6, 1.0);
  float glow = exp(-dot(sd, sd) * 2.2) * (0.85 + 0.15 * sin(a + 0.4));
  col += lab2lin(u_pal[1]) * glow * 0.45 + lab2lin(u_pal[5]) * (exp(-dot(sd, sd) * 60.0) * 0.9 + exp(-dot(sd, sd) * 9.0) * 0.18);
  // faint rays fanning up from the sun behind the far ridge
  vec2 rv = ps - sun;
  float ra = atan(rv.x, rv.y);
  float rays = 0.5 + 0.5 * sin(ra * 15.0 + 1.5 * sin(ra * 4.0 + 0.5 * sin(a)) + a);
  col += lab2lin(u_pal[1]) * rays * rays * exp(-length(rv) * 1.6) * smoothstep(0.0, 0.3, rv.y + 0.2) * 0.12 * (0.7 + 0.3 * sin(a + ra * 3.0));
  // thin high cloud catching the light
  float cl = fbm3(vec3(ps.x * 0.9 + 0.2 * m * sin(a), ps.y * 5.0, 2.0) + vec3(orbit(a, 0.2 * m), 0.0));
  col = mix(col, lab2lin(mix(u_pal[1], u_pal[5], 0.3)), smoothstep(0.1, 0.5, cl) * smoothstep(-0.05, 0.4, ps.y) * smoothstep(0.95, 0.35, ps.y) * 0.25);
  // the ranges, far to near; each further one is lifted toward the colour of the air
  for (int i = 0; i < 5; i++) {
    float f = float(i);
    float z = f / 4.0;
    vec2 q = p + tiltD(-0.7 + 1.7 * z);
    float base = -0.08 - 0.19 * f;
    float amp = 0.11 + 0.05 * f;
    float y = base + amp * ridge(q.x * (1.0 + 0.15 * f) + f * 3.1, f);
    float d = q.y - y;
    float ew = edgePx() * 1.2 + 0.01 * (1.0 - z) * hz;
    float cov = smoothstep(ew, -ew, d);
    float air = (1.0 - z) * 0.85 * hz;
    vec3 rc = mix(u_pal[2], u_pal[3], sat(air));
    // the slopes facing the sun keep a little warm light; the valleys fill with shade
    rc = setL(rc, rc.x + 0.05 * (1.0 - z) * exp(-pow((q.x - sun.x) * 0.9, 2.0)));
    vec3 lin = lab2lin(rc);
    lin = mix(lin, lab2lin(u_pal[1]), glow * (1.0 - z) * 0.35);
    // the mist in the valley just below this ridge's crest
    float mist = smoothstep(-0.04, -0.22 - 0.04 * f, d) * sat(0.55 + 0.6 * lnoise(vec2(q.x * 0.9 + f * 7.0, d * 7.0), a + f, 0.45 * m));
    lin = mix(lin, lab2lin(u_pal[4]) * (0.8 + 0.4 * glow), mist * (0.35 - 0.05 * f) * hz);
    col = mix(col, lin, cov);
  }
  return lin2srgb(shoulder(col));
}
