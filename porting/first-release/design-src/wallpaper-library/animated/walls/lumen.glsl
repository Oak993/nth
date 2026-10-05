// Lumen: a shaft of sunlight sweeping slowly across a quiet wall, as the sun moves past a window. Inside the shaft,
// the light that passed through a glass on the sill lies in a soft caustic, a gentle net of brighter light that wavers;
// a cool pool answers from the far corner. Low in contrast, slow, and never a plain gradient.
// Palette: 0 wall, 1 wall low, 2 the sunlight, 3 the cool pool, 4 the caustic light, 5 unused highlight.
// Options: x = motion (how far the shaft sweeps), y = glow.
const float T = 70.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float gl = mix(0.45, 1.4, u_opt.y);
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.2, 1.0, p.y));
  // the shaft: a slanted band from the top left whose position swings slowly across the wall
  vec2 dir = normalize(vec2(0.62, -0.78));
  vec2 nrm = vec2(-dir.y, dir.x);
  float pos = -0.25 + 0.55 * m * sin(a) + 0.08 * m * sin(2.0 * a + 1.0) + u_sun.x;
  float across = dot(p, nrm) - pos;
  float along = dot(p, dir);
  float wid = 0.72 + 0.08 * sin(a + 1.3);
  float shaft = smoothstep(wid, wid * 0.2, abs(across)) * (0.55 + 0.45 * smoothstep(2.2, -0.6, along));
  // the caustic, in the shaft's own coordinates, from a softly moving surface
  vec2 x = vec2(across * 2.3, along * 1.5) + 0.06 * vec2(lnoise(p * 0.7, a, 0.3), lnoise(p * 0.7 + 3.0, a, 0.3));
  float hxx = 0.0;
  float hyy = 0.0;
  float hxy = 0.0;
  for (int i = 0; i < 5; i++) {
    float f = float(i);
    float an = f * 2.39996 + 0.7;
    float kk = 11.0 + 2.5 * sin(f * 1.9);
    vec2 k = vec2(cos(an), sin(an)) * kk;
    float s = -0.26 / (kk * kk) * sin(dot(k, x) + (mod(f, 2.0) < 0.5 ? 1.0 : -1.0) * (1.0 + floor(f / 2.0)) * a + f * 1.7);
    hxx += s * k.x * k.x;
    hyy += s * k.y * k.y;
    hxy += s * k.x * k.y;
  }
  float J = 1.0 + 1.5 * (hxx + hyy) + 2.0 * (hxx * hyy - hxy * hxy);
  float caus = sat(1.45 / (abs(J) + 0.45) - 1.0) * smoothstep(wid * 1.05, wid * 0.15, abs(across)) * (0.6 + 0.4 * smoothstep(2.0, -0.4, along));
  vec3 sun = hueShift(u_pal[2], 0.1 * sin(a + 0.5));
  col = mix(col, sun, sat(shaft * 0.68 * gl));
  col = mix(col, u_pal[4], sat(caus * 0.3 * gl));
  vec2 d2 = rot(p - vec2(1.05 + 0.12 * m * cos(a + 2.0), -0.72), 0.4) / vec2(1.0, 0.65);
  col = mix(col, u_pal[3], sat(exp(-dot(d2, d2)) * 0.55 * gl));
  return outLab(col);
}
