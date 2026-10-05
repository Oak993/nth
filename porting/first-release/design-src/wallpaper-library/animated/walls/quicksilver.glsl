// Quicksilver: a sea of liquid metal under an evening sky. Long, slow swells roll across it toward the horizon, and
// every one mirrors the sky exactly: deep colour where the surface tips toward the zenith, the glow of the horizon where
// it faces away, and a broken path of hot light below the low sun. Near swells are large and smooth; far off they crowd
// into a fine shimmer and then a flat bright line at the horizon.
// Palette: 0 the sky overhead, 1 the glow on the horizon, 2 the second glow, 3 the metal's tint, 4 the sky's mid
// colour, 5 the sun.
// Options: x = motion (how high the swells run), y = swell (long and glassy to short and choppy).
// Dynamic: u_sun moves the sun. Perspective: the sky is far, the sea near.
const float T = 40.0;

vec3 sky(vec3 r, vec2 sunP) {
  float e = r.y;
  vec3 c = mix(u_pal[1], u_pal[4], smoothstep(0.0, 0.22, e));
  c = mix(c, u_pal[0], smoothstep(0.15, 0.75, e));
  float az = r.x / max(r.z, 0.05) - sunP.x;
  float sd = length(vec2(az * 0.8, e - sunP.y));
  c = mix(c, u_pal[2], exp(-sd * sd * 3.0) * 0.6 * smoothstep(0.5, -0.05, e));
  vec3 lin = lab2lin(c);
  lin += lab2lin(u_pal[5]) * (exp(-sd * sd * 700.0) * 2.0 + exp(-sd * sd * 30.0) * 0.25);
  return lin;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.4, 1.4, u_opt.x);
  float sw = mix(0.75, 1.5, u_opt.y);
  float hy = 0.02;
  vec2 sunP = vec2(0.35, 0.09) + u_sun * vec2(0.6, 0.4);
  vec2 ps = p + tiltD(-1.0);
  vec3 col;
  if (ps.y > hy) {
    col = sky(normalize(vec3(ps.x, ps.y - hy, 1.6)), sunP);
  } else {
    vec2 q = p + tiltD(0.8);
    float dy = max(hy - q.y, 0.002);
    float z = 1.1 / dy;
    vec2 W = vec2(q.x * z, z);
    // the swells: travelling waves whose speeds are whole numbers of turns per loop; their height fades with distance
    // as each one becomes smaller than a pixel
    float foot = z * z * u_px / 1.1;
    vec2 g = vec2(0.0);
    for (int i = 0; i < 8; i++) {
      float f = float(i);
      float an = 0.18 + 0.32 * sin(f * 2.3 + 0.4) * (0.4 + 0.15 * f);
      vec2 k = vec2(sin(an), cos(an)) * (0.7 + 0.55 * f + 0.08 * f * f) * sw;
      float kk = length(k);
      float A = 0.3 * m / (1.0 + 2.0 * f) * exp(-foot * kk * 1.2);
      float n = 1.0 + floor(f * 0.5);
      float ph = dot(k, W) - n * a + f * 1.7;
      g += A * kk * cos(ph) * k / kk;
    }
    vec3 nrm = normalize(vec3(-g.x, 1.0, -g.y));
    vec3 d = normalize(vec3(q.x, q.y - hy, 1.6));
    vec3 r = reflect(d, nrm);
    r.y = abs(r.y);
    vec3 env = sky(r, sunP);
    vec3 tint = lab2lin(vec3(0.85, u_pal[3].yz));
    tint /= max(dot(tint, vec3(0.2126, 0.7152, 0.0722)), 1e-3);
    float fres = 0.72 + 0.28 * pow(1.0 - sat(dot(-d, nrm)), 5.0);
    col = env * tint * fres;
    // the far sea melts into a bright line at the horizon
    col = mix(col, sky(normalize(vec3(q.x, 0.004, 1.6)), sunP) * 0.95, smoothstep(0.04, 0.0, dy) * 0.8);
  }
  return lin2srgb(shoulder(col));
}
