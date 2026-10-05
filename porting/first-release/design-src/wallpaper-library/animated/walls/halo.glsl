// Halo: one wide, soft ring of light resting off-centre, its colour turning slowly round it, the inside faintly lit by
// it. Out of focus throughout, so it reads as light, not as a shape.
// Palette: 0 ground, 1 to 3 the ring's colours round the circle, 4 the light inside, 5 unused highlight.
// Options: x = motion (how much the ring breathes), y = width.
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float wd = mix(0.22, 0.5, u_opt.y);
  vec2 c = vec2(0.55, -0.12) + orbit(a, 0.03 * m);
  vec2 d = p - c;
  float r = length(d);
  float R = 0.95 + 0.04 * m * sin(a * 2.0);
  float ang = atan(d.y, d.x) + a;
  vec3 acc = vec3(0.0);
  float ws = 0.0;
  for (int i = 0; i < 3; i++) {
    float w = pow(0.5 + 0.5 * cos(ang - float(i) * 2.0943951), 2.0);
    acc += u_pal[i + 1] * w;
    ws += w;
  }
  vec3 rc = acc / ws;
  float rr = (r - R) / wd;
  float ring = exp(-rr * rr);
  vec3 col = mix(u_pal[0], setL(u_pal[0], u_pal[0].x * 0.95), smoothstep(0.0, 2.0, length(p + vec2(0.8, 0.5))));
  col = mix(col, mix(u_pal[4], rc, 0.3), 0.4 * exp(-(r * r) / 0.6));
  col = mix(col, rc, ring * 0.75);
  return outLab(col);
}
