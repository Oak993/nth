// Velvet: draped satin. Two systems of folds cross: broad rounded bulges between narrow creases, lit from the top
// left, so every fold carries a bright ridge, a long sheen and a deep crease; a warm rim light catches the far sides.
// Palette: 0 crease shadow, 1 body, 2 lit cloth, 3 sheen, 4 colour in the creases, 5 rim light.
// Options: x = motion (how far the cloth shifts), y = folds (broad to fine).
const float T = 48.0;

float drape(vec2 q, float a, float m) {
  float w = q.y * 0.8 + 0.5 * m * sin(q.x * 0.7 + a);
  float u = q.x + 0.35 * sin(q.y * 1.1 + 0.4 * m * sin(a)) + 0.16 * m * sin(q.y * 2.3 - a + 1.0);
  return 0.5 * sin(u * 2.6 + 0.8 * sin(w)) + 0.26 * sin(u * 4.7 - 0.6 * w + 1.3) + 0.1 * sin(u * 8.1 + q.y * 0.9 + 2.0 * a);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float fd = mix(0.7, 1.4, u_opt.y);
  vec2 q = rot(p, 0.5) * fd;
  float e = 0.003;
  float h0 = drape(q, a, m);
  float hx = drape(q + vec2(e, 0.0), a, m);
  float hy = drape(q + vec2(0.0, e), a, m);
  vec2 gr = vec2(hx - h0, hy - h0) / e;
  vec3 n = normalize(vec3(-gr * 0.4, 1.0));
  vec3 L = normalize(vec3(-0.38, 0.42, 0.82));
  float dif = dot(n, L);
  vec3 H = normalize(L + vec3(0.0, 0.0, 1.0));
  float nh = max(dot(n, H), 0.0);
  float spec = pow(nh, 48.0);
  float sheen = pow(nh, 6.0);
  float s = sat(dif * 0.95 + 0.18);
  float crease = smoothstep(-0.2, -0.75, h0);
  vec3 col = mix(u_pal[0], u_pal[1], smoothstep(0.05, 0.6, s));
  col = mix(col, u_pal[2], smoothstep(0.55, 0.95, s));
  col = mix(col, u_pal[4], 0.45 * crease * (1.0 - s));
  col = mix(col, u_pal[3], sat(spec * 0.95 + sheen * 0.3));
  float rim = pow(sat(dot(n.xy, normalize(vec2(0.85, -0.4))) * 2.2), 2.0);
  col = mix(col, u_pal[5], rim * 0.35);
  col.x *= 0.9 + 0.1 * smoothstep(2.0, 0.3, length(p - vec2(-0.5, 0.5)));
  return outLab(col);
}
