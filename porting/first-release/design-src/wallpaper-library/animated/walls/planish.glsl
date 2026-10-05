// Planish: a sheet of hand-hammered metal, every blow of the hammer leaving a shallow dish, so the surface is a field
// of small curved mirrors. Two soft studio lights glide slowly across it; each dish catches them at its own angle and
// throws back a little crescent of light, so the shine travels over the sheet in a shimmer. The metal's own colour
// deepens where it reflects the dark of the room.
// Palette: 0 the metal in shadow, 1 the metal, 2 the first light, 3 the second light, 4 the room's dark, 5 the brightest
// highlight.
// Options: x = motion (how far the lights glide), y = dishes (large to small).
const float T = 60.0;

// the sheet's height: each blow a shallow paraboloid dish; a smooth minimum over the nearby blows rounds the ridges
float sheet(vec2 q) {
  vec2 c0 = floor(q);
  float acc = 0.0;
  for (int j = 0; j < 3; j++) {
    for (int i = 0; i < 3; i++) {
      vec2 c = c0 + vec2(float(i) - 1.0, float(j) - 1.0);
      vec3 h = hash33(vec3(c, 7.0)) * 0.5 + 0.5;
      vec2 d = c + 0.5 + 0.42 * (h.xy - 0.5) * 2.0 - q;
      acc += exp(-dot(d, d) * (7.0 + 3.0 * h.z));
    }
  }
  return -log(acc + 1e-4) / 8.5;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float S = mix(2.4, 4.2, u_opt.y);
  vec2 q = p * S;
  float e = 0.01;
  float h0 = sheet(q);
  vec2 slope = vec2(sheet(q + vec2(e, 0.0)) - h0, sheet(q + vec2(0.0, e)) - h0) / e * 0.55;
  slope += 0.02 * vec2(gnoise(vec3(p * 12.0, 1.0)), gnoise(vec3(p * 12.0, 2.0)));
  vec3 n = normalize(vec3(-slope, 1.0));
  // the reflected direction looks up into the studio: two soft lights and the dark between
  vec3 r = reflect(vec3(0.0, 0.0, -1.0), n);
  vec2 rv = r.xy / max(r.z, 0.2) * 1.2 + p * 0.75;
  vec2 l1 = vec2(-0.8 + 0.7 * m * sin(a), 0.5) + u_sun;
  vec2 l2 = vec2(1.0 + 0.6 * m * sin(a + 2.3), -0.35 + 0.2 * cos(a));
  float s1 = exp(-dot(rv - l1, rv - l1) / 0.6);
  float s2 = exp(-dot(rv - l2, rv - l2) / 0.45);
  float room = smoothstep(0.0, 1.0, s1 + s2);
  vec3 metal = mix(u_pal[0], u_pal[1], 0.5 + 0.5 * sat(dot(n.xy, normalize(vec2(-0.5, 0.8))) * 3.0));
  vec3 col = mix(mix(u_pal[4], metal, 0.65), metal, room * 0.6);
  col = mix(col, u_pal[2], sat(s1 * 1.2) * 0.75);
  col = mix(col, u_pal[3], sat(s2 * 1.2) * 0.7);
  col = mix(col, u_pal[5], pow(sat(s1 + s2), 6.0) * 0.45);
  return outLab(col);
}
