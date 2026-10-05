// Stillwater: the light of a pool thrown softly onto a pale wall: slow, wide, out-of-focus ripples of brightness over
// a two-colour wash, never sharp enough to pull the eye.
// Palette: 0 wall, 1 wall's second colour, 2 shade, 3 the rippled light, 4 warm edge of the light, 5 unused highlight.
// Options: x = motion (how strongly the water moves), y = shimmer (how bright the ripples are).
const float T = 80.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.4, 1.3, u_opt.x);
  float sh = mix(0.35, 1.3, u_opt.y);
  vec2 x = p * 0.8 + 0.08 * m * vec2(lnoise(p * 0.6, a, 0.3), lnoise(p * 0.6 + 4.0, a, 0.3));
  float hxx = 0.0;
  float hyy = 0.0;
  float hxy = 0.0;
  for (int i = 0; i < 7; i++) {
    float f = float(i);
    float an = f * 2.39996 + 1.1;
    float kk = 15.0 + 3.5 * sin(f * 1.3);
    vec2 k = vec2(cos(an), sin(an)) * kk;
    float n = mod(f, 2.0) < 0.5 ? 1.0 : -1.0;
    float A = 0.26 / (kk * kk) * (0.65 + 0.35 * m);
    float s = -A * sin(dot(k, x) + n * a + f * 2.1);
    hxx += s * k.x * k.x;
    hyy += s * k.y * k.y;
    hxy += s * k.x * k.y;
  }
  float J = 1.0 + 1.25 * (hxx + hyy) + 1.6 * (hxx * hyy - hxy * hxy);
  float I = 1.9 / (abs(J) + 0.9) - 1.0;
  float lightArea = smoothstep(1.7, 0.1, length((p - vec2(0.35, 0.35)) * vec2(0.65, 1.0)));
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.2, 1.2, p.y - 0.4 * p.x));
  col = mix(col, u_pal[2], 0.3 * smoothstep(0.6, 2.0, length(p * vec2(0.65, 1.0))));
  vec3 lc = mix(u_pal[4], u_pal[3], lightArea);
  col = mix(col, lc, sat(I * 0.75 * sh * lightArea + 0.16 * lightArea));
  return outLab(col);
}
