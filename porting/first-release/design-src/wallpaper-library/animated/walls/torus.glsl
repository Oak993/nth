// Torus: a ring of solid clear glass hanging in front of soft fields of colour, turning slowly on a thread. Seen through
// its round tube the colours behind are turned over and squeezed, split into a fine spectrum at the edges, with the
// darker band and the bright inner lines a glass rod always shows; where the ring passes in front of itself, the near
// side bends the far side too. As it turns, the ring narrows toward edge-on and opens again.
// Palette: 0 the ground, 1 first field of colour, 2 second field, 3 third field, 4 the glass tint, 5 the white of the
// highlights.
// Options: x = motion (how far the ring turns), y = thickness of the glass.
// Perspective: the fields of colour are far (tiltD -1), the ring near (tiltD 0.8).
const float T = 48.0;

vec3 fields(vec2 q, float a, float m) {
  vec3 c = u_pal[0];
  vec2 c1 = vec2(-0.9, 0.45) + m * 0.12 * vec2(sin(a), cos(a));
  vec2 c2 = vec2(0.9, -0.55) + m * 0.1 * vec2(cos(a + 2.0), sin(a + 2.0));
  vec2 c3 = vec2(0.45, 0.75) + m * 0.1 * vec2(sin(a + 4.0), cos(a + 4.0));
  float w1 = exp(-dot(q - c1, q - c1) * 0.9);
  float w2 = exp(-dot(q - c2, q - c2) * 0.8);
  float w3 = exp(-dot(q - c3, q - c3) * 1.4);
  c = mix(c, u_pal[1], w1);
  c = mix(c, u_pal[2], w2);
  c = mix(c, u_pal[3], w3 * 0.9);
  return lab2lin(c);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float rt = mix(0.08, 0.15, u_opt.y);
  vec2 pf = p + tiltD(-1.0);
  vec3 bg = fields(pf, a, m);
  vec2 q = p + tiltD(0.8);
  vec2 C = vec2(0.42, 0.02);
  float Rm = 0.6;
  float phi = 0.55 + 0.75 * m * sin(a + 0.9) + 0.15 * m * sin(2.0 * a + 1.0);
  float psi = 0.42;
  float cf = cos(phi);
  float sf = sin(phi);
  float cp = cos(psi);
  float sp = sin(psi);
  // the nearest point of the ring's centre line, on its near half and on its far half
  float dN = 9.0;
  float dF = 9.0;
  float tN = 0.0;
  float tF = 0.0;
  for (int i = 0; i < 40; i++) {
    float th = float(i) * TAU / 40.0;
    vec2 P = C + Rm * vec2(cos(th) * cf, sin(th) * cp + cos(th) * sf * sp);
    float Z = sin(th) * sp - cos(th) * sf * cp;
    float d = length(q - P);
    if (Z >= 0.0) { if (d < dN) { dN = d; tN = th; } } else { if (d < dF) { dF = d; tF = th; } }
  }
  vec3 col = bg;
  vec3 wht = lab2lin(u_pal[5]);
  vec3 tint = lab2lin(vec3(0.85, u_pal[4].yz * 0.7));
  tint /= max(dot(tint, vec3(0.2126, 0.7152, 0.0722)), 1e-3);
  // far half first, then the near half over it
  for (int j = 0; j < 2; j++) {
    float th0 = j == 0 ? tF : tN;
    // this half of the ring runs over [lo, lo + pi]: the near half where the ring faces us, the far half behind
    float lo = atan(sf * cp, sp) + (j == 0 ? 3.14159265 : 0.0);
    // refine the nearest point with two Newton-like steps along the curve
    float th = th0;
    for (int k = 0; k < 3; k++) {
      vec2 P = C + Rm * vec2(cos(th) * cf, sin(th) * cp + cos(th) * sf * sp);
      vec2 D = Rm * vec2(-sin(th) * cf, cos(th) * cp - sin(th) * sf * sp);
      th += clamp(dot(q - P, D) / max(dot(D, D), 1e-4), -0.12, 0.12);
      float rel = th - lo;
      rel -= TAU * floor(rel / TAU);
      rel = rel > 3.14159265 + 0.5 * (TAU - 3.14159265) ? 0.0 : min(rel, 3.14159265);
      th = lo + rel;
    }
    vec2 P = C + Rm * vec2(cos(th) * cf, sin(th) * cp + cos(th) * sf * sp);
    vec2 D = normalize(Rm * vec2(-sin(th) * cf, cos(th) * cp - sin(th) * sf * sp));
    vec2 nrm = vec2(-D.y, D.x);
    float sv = dot(q - P, nrm);
    // beyond the end of this half the other half takes over: a straight cut, not a cap
    float along = abs(dot(q - P, D));
    float v = sv / rt;
    float av = abs(v);
    float cov = smoothstep(1.0 + edgePx() / rt, 1.0 - edgePx() / rt, av) * smoothstep(0.06, 0.03, along);
    float nz = sqrt(max(1.0 - v * v, 0.0));
    // through the rod: what lies behind, turned over and squeezed, a little split by colour
    vec2 base = P - nrm * sv * 2.6;
    vec3 thru;
    thru.r = fields(base - nrm * sv * 0.12 + tiltD(-1.0), a, m).r;
    thru.g = fields(base + tiltD(-1.0), a, m).g;
    thru.b = fields(base + nrm * sv * 0.14 + tiltD(-1.0), a, m).b;
    thru = mix(thru, (col - bg) * 0.6 + thru, 0.5);
    thru *= mix(vec3(1.0), tint, 0.35) * (0.8 + 0.35 * nz);
    vec3 vk = av * vec3(0.985, 1.0, 1.016);
    vec3 e1 = (vk - 0.9) / 0.035;
    vec3 e2 = (vk - 0.6) / 0.06;
    vec3 e3 = (vk - 0.76) / 0.08;
    vec3 line = exp(-e1 * e1) * 1.0 + exp(-e2 * e2) * 0.25;
    thru *= 1.0 - 0.6 * exp(-e3 * e3);
    thru += wht * line * mix(0.35, 0.6, u_dark);
    // the highlight of the light above, along the upper side of the tube
    vec3 n3 = vec3(nrm * v, nz);
    vec3 H = normalize(normalize(vec3(-0.45, 0.75, 0.55)) + vec3(0.0, 0.0, 1.0));
    float nh = sat(dot(n3, H));
    thru += wht * (pow(nh, 90.0) * 0.9 + pow(nh, 12.0) * 0.06);
    thru = mix(thru, bg * 0.55, smoothstep(0.94, 1.0, av) * 0.4);
    col = mix(col, thru, cov);
  }
  return lin2srgb(shoulder(col));
}
