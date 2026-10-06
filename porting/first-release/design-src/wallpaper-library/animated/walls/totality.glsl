// Totality: a total eclipse of the sun. The moon is a disc of perfect black; round it the corona spreads in pearly
// streamers, long along the sun's equator and short and brushed at its poles, finely striated, slowly shimmering. A thin
// rim of rose-red chromosphere hugs the limb with a few small prominences, and at one point on the edge the last bead of
// sunlight burns as a diamond. The sky is the deep blue of totality, with the glow of a far sunset all along the horizon.
// Palette: 0 the sky high, 1 the horizon glow, 2 the corona, 3 the chromosphere, 4 the far land, 5 the diamond.
// Options: x = motion (how much the corona shimmers), y = corona (short and faint to long and bright).
// Dynamic: u_sun moves the eclipse a little. Perspective: the sky and eclipse far (tiltD -1), the land near (tiltD 1).
const float T = 60.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float cr = mix(0.6, 1.4, u_opt.y);
  vec2 q = p + tiltD(-1.0);
  vec2 S = vec2(0.72, 0.36) + u_sun * vec2(0.4, 0.3);
  float R = 0.14;
  vec2 d = q - S;
  float r = length(d) / R;
  float ang = atan(d.y, d.x);
  // the sky: deep blue overhead, warming to the glow of a sunset all round the horizon
  // through a rose band between the glow and the blue, so the blend never greys
  vec3 mid = mix(u_pal[3], u_pal[0], 0.55);
  vec3 sky = mix(u_pal[1], mid, smoothstep(-0.8, -0.35, q.y));
  sky = mix(sky, u_pal[0], smoothstep(-0.45, 0.3, q.y));
  vec3 col = lab2lin(sky);
  col *= 1.0 + 0.25 * exp(-max(r - 1.0, 0.0) * 0.25);
  // the corona: brightness falling with distance, streamers brushed along it
  float eq = abs(cos(ang - 0.35));
  float reach = mix(0.55, 1.0, eq) * cr;
  float ra = ang + 0.05 * m * sin(a);
  vec2 dirv = vec2(cos(ra), sin(ra));
  float str = 0.55 + 0.45 * gnoise(vec3(dirv * 6.0, r * 0.15) + vec3(orbit(a, 0.35 * m), 0.0));
  str *= 0.7 + 0.3 * gnoise(vec3(dirv * 22.0, 1.0) + vec3(orbit(a, 0.2 * m), 0.3));
  float fine = 0.85 + 0.15 * sin(ang * 140.0 + 2.0 * gnoise(vec3(dirv * 3.0, 2.0)));
  float rr = max(r - 1.0, 0.0);
  float cor = (exp(-rr / (0.35 * reach)) * 0.9 + exp(-rr / (1.6 * reach * str)) * 0.45 * str) * fine * step(1.0, r + 0.02);
  vec3 corC = lab2lin(u_pal[2]);
  col += corC * cor * mix(0.75, 1.0, u_dark);
  // the chromosphere and its prominences, a thin rose rim at the limb
  float prom = pow(max(gnoise(vec3(dirv * 5.0, 7.0)), 0.0), 2.0) * 3.0;
  float chrom = exp(-pow((r - 1.0) / (0.012 + 0.03 * prom), 2.0)) * (0.5 + 0.5 * prom) * step(0.995, r);
  col += lab2lin(u_pal[3]) * chrom * 1.2;
  // the moon: black, its edge crisp
  float disc = smoothstep(1.0 + 1.3 * edgePx() / R, 1.0 - 1.3 * edgePx() / R, r);
  col = mix(col, lab2lin(setL(u_pal[0], 0.06)) * 0.6, disc);
  // the diamond: the last bead of sunlight, with a short starburst
  vec2 bd = S + R * vec2(cos(-0.75), sin(-0.75)) * 1.003;
  vec2 bq = q - bd;
  float bl = length(bq);
  float bead = 0.5 + 0.5 * sin(a);
  bead = bead * bead;
  float burst = exp(-bl * bl / 0.0004) * 1.6 + exp(-bl * 9.0) * 0.35;
  float rays = pow(abs(cos(atan(bq.y, bq.x) * 3.0)), 90.0) * exp(-bl * 10.0) * 0.45;
  col += lab2lin(u_pal[5]) * (burst * (1.0 - disc * 0.6) + rays * (1.0 - disc)) * mix(0.35, 1.0, bead);
  // a few stars and a bright planet in the dark of totality
  vec2 sg = q * 24.0;
  vec3 sh = hash33(vec3(floor(sg), 4.4)) * 0.5 + 0.5;
  float sd = length(fract(sg) - 0.5 - (sh.xy - 0.5) * 0.6) / 24.0;
  col += vec3(exp(-sd * sd / (0.7 * u_px * u_px)) * step(0.94, sh.z) * (sh.z - 0.94) * 9.0) * smoothstep(-0.2, 0.4, q.y) * sat((1.0 / 24.0) / u_px / 9.0 - 0.5) * u_dark * (1.0 - disc) * (1.0 - sat(cor));
  // the far land, dark against the horizon glow
  vec2 ql = p + tiltD(1.0);
  float land = -0.74 + 0.04 * sin(ql.x * 2.1 + 0.5) + 0.02 * sin(ql.x * 5.3) + 0.008 * sin(ql.x * 13.0 + 1.0);
  float lc = smoothstep(edgePx() * 1.3, -edgePx() * 1.3, ql.y - land);
  col = mix(col, lab2lin(u_pal[4]), lc);
  return lin2srgb(shoulder(col));
}
