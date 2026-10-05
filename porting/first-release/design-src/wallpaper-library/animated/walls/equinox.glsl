// Equinox: a ringed planet seen close, the sun low beyond its right shoulder. The rings are thousands of fine bands
// with dark gaps between them; they cross in front of the planet and pass behind it, the planet casts its shadow across
// them, and they lay their own thin stripes of shadow on its cloud belts. The belts turn slowly, faint spokes drift
// round the rings, and a thin haze of light rims the planet's edge.
// Palette: 0 space, 1 the bright cloud belts, 2 the dark belts, 3 the bright rings, 4 the inner, darker rings, 5 the
// sunlight on the rim.
// Options: x = motion (how turbulent the turning belts are), y = rings (sparse and faint to dense and bright).
// Dynamic: u_sun moves the sun. Perspective: the stars are far (tiltD -1), the planet and rings near (tiltD 0.6).
const float T = 90.0;

float ringDensity(float r, float rk, float fp) {
  // fp: how far r moves across one pixel; bands finer than that fade out instead of aliasing
  // r in planet radii; the rings run from 1.25 to 2.25 with a wide dark gap at 1.95
  float d = smoothstep(1.24, 1.32, r) * smoothstep(2.27, 2.2, r);
  float bands = 0.68 + 0.12 * sin(r * 61.0) * sat(1.6 - 61.0 * fp) + 0.09 * sin(r * 143.0 + 1.0) * sat(1.6 - 143.0 * fp) + 0.07 * sin(r * 311.0 + 2.0) * sat(1.6 - 311.0 * fp) + 0.04 * sin(r * 701.0) * sat(1.6 - 701.0 * fp);
  float zones = mix(0.35, 1.0, smoothstep(1.45, 1.6, r)) * mix(1.0, 0.6, smoothstep(2.0, 2.1, r));
  float gap = 1.0 - 0.92 * exp(-pow((r - 1.93) / 0.025, 2.0)) - 0.6 * exp(-pow((r - 2.13) / max(0.006, fp), 2.0));
  return sat(d * bands * zones * gap * rk);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.5, u_opt.x);
  float rk = mix(0.6, 1.25, u_opt.y);
  vec2 ps = p + tiltD(-1.0);
  vec3 col = lab2lin(mix(u_pal[0], setL(u_pal[0], u_pal[0].x * 1.3 + 0.01), smoothstep(1.2, -1.0, ps.y + ps.x * 0.3)));
  // a few faint stars
  vec2 sg = ps * 30.0;
  vec3 sh = hash33(vec3(floor(sg), 3.3)) * 0.5 + 0.5;
  float sd = length(fract(sg) - 0.5 - (sh.xy - 0.5) * 0.6) / 30.0;
  col += vec3(exp(-sd * sd / (0.7 * u_px * u_px)) * step(0.9, sh.z) * (sh.z - 0.9) * 6.0 * sat((1.0 / 30.0) / u_px / 9.0 - 0.5)) * u_dark;

  vec2 q = p + tiltD(0.6);
  vec2 C = vec2(-1.05, -0.95);
  float R = 1.08;
  vec3 N = normalize(vec3(-0.3, 0.92, 0.25));
  vec3 S = normalize(vec3(vec2(0.8, 0.32) + u_sun, mix(0.6, -0.38, u_dark)));
  vec2 d = (q - C) / R;
  float rr = dot(d, d);
  float inP = smoothstep(1.0 + 1.5 * edgePx() / R, 1.0 - 1.5 * edgePx() / R, sqrt(rr));
  float zs = sqrt(max(1.0 - rr, 0.0));
  vec3 Pn = vec3(d, zs);
  // the ring plane along this line of sight
  float zr = -(d.x * N.x + d.y * N.y) / N.z;
  vec3 X = vec3(d, zr);
  float r = length(X);
  // the planet: belts set by latitude about the ring axis, turning with longitude
  vec3 e1 = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
  vec3 e2 = cross(N, e1);
  float lat = dot(Pn, N);
  float phi = atan(dot(Pn, e2), dot(Pn, e1)) + a;
  vec3 lq = vec3(lat * 5.0, 1.4 * cos(phi), 1.4 * sin(phi));
  float turb = fbm3(lq) * sqrt(max(1.0 - lat * lat, 0.0));
  float w = lat * 1.0 + (0.025 + 0.025 * m) * turb;
  float belt = 0.5 + 0.3 * sin(w * 18.0 + 0.6 * sin(w * 5.0)) + 0.14 * sin(w * 47.0 + 1.0) + 0.06 * sin(w * 113.0);
  vec3 pc = mix(u_pal[2], u_pal[1], smoothstep(0.1, 0.9, belt));
  pc = mix(pc, u_pal[2], smoothstep(0.75, 0.95, abs(lat)) * 0.5);
  float lit = dot(Pn, S);
  float dif = smoothstep(-0.05, 0.7, lit);
  // the rings' shadow on the planet
  float tS = -dot(Pn, N) / dot(S, N);
  float fp = 1.2 * edgePx() / R / 0.45;
  float rsh = tS > 0.0 ? ringDensity(length(Pn + S * tS), rk, fp * 2.0) : 0.0;
  vec3 planet = lab2lin(pc) * (dif * (1.0 - 0.75 * rsh) + 0.015);
  float limb = pow(1.0 - zs, 3.0);
  planet += lab2lin(u_pal[5]) * limb * smoothstep(-0.1, 0.5, lit) * 0.5;
  planet *= 0.75 + 0.25 * zs;
  col = mix(col, planet, inP);
  // the haze of light round the lit limb, outside the disc
  float rim = exp(-max(sqrt(rr) - 1.0, 0.0) * 40.0) * (1.0 - inP) * smoothstep(-0.2, 0.6, dot(normalize(vec3(d, 0.0)), S));
  col += lab2lin(u_pal[5]) * rim * 0.35;
  // the rings: in front of the planet, or anywhere off its disc
  float vis = (rr > 1.0 || zr > zs) ? 1.0 : 0.0;
  float dens = ringDensity(r, rk, fp) * vis;
  // spokes drifting round, and the planet's shadow across the rings
  float ang = atan(dot(X, e2), dot(X, e1));
  float spokes = 1.0 - 0.18 * smoothstep(0.3, 0.9, gnoise(vec3(1.2 * cos(ang - a), 1.2 * sin(ang - a), r * 3.0)));
  float b = dot(X, S);
  float disc = b * b - (dot(X, X) - 1.0);
  float shadow = (b < 0.0 && disc > 0.0) ? 0.08 : 1.0;
  float facing = abs(dot(S, N));
  vec3 rc = mix(u_pal[4], u_pal[3], smoothstep(1.4, 1.8, r));
  vec3 ring = lab2lin(rc) * mix(0.8, 0.45 + 0.9 * facing + 0.5 * smoothstep(0.0, -0.3, S.z), u_dark) * shadow * spokes;
  col = mix(col, ring, dens * mix(0.9, 0.85, u_dark));
  return lin2srgb(shoulder(col));
}
