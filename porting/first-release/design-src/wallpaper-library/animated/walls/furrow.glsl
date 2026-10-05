// Furrow: a dry garden seen from above: pale gravel raked into fine straight furrows that bend into rings round three
// stones, each stone with a collar of moss. The low sun moves across the garden, so the furrows catch the light on one
// flank and then the other, and the stones' long shadows swing slowly round them.
// Palette: 0 the gravel in shade, 1 the gravel, 2 the gravel in the sun, 3 the stones, 4 the moss, 5 the light's warmth.
// Options: x = motion (how far the sun moves), y = furrows (wide to fine).
const float T = 80.0;

// the stones: centre, radius
vec3 stone(float i) {
  return i < 0.5 ? vec3(-0.98, -0.5, 0.3) : (i < 1.5 ? vec3(1.12, 0.46, 0.17) : vec3(1.42, 0.2, 0.1));
}

// the garden's height: straight furrows, rings raked round each stone out to a border, and the stones themselves;
// y = how much of a stone is here
vec2 garden(vec2 p, float K) {
  float sv = p.y + 0.03 * sin(p.x * 0.8);
  float st = 0.0;
  float rim = 9.0;
  for (int i = 0; i < 3; i++) {
    vec3 c = stone(float(i));
    vec2 q = p - c.xy;
    float d = length(q);
    float an = atan(q.y, q.x);
    // the stone's outline is irregular, as a real stone's is
    float r = c.z * (1.0 + 0.12 * sin(an * 3.0 + c.x * 4.0) + 0.06 * sin(an * 5.0 + c.y * 3.0));
    float rz = c.z * 2.7;
    // inside the raked rings the furrows follow the stone; the rings stop at their own outermost furrow
    float inR = smoothstep(rz + 0.02, rz - 0.02, d);
    sv = mix(sv, d - r, inR);
    rim = min(rim, abs(d - rz));
    st = max(st, sat(1.0 - (d * d) / (r * r)));
  }
  float fur = 0.5 - 0.5 * cos(sv * K * TAU);
  float h = 0.012 * fur * (1.0 - smoothstep(0.0, 0.15, st)) + 0.13 * sqrt(st);
  return vec2(h, st);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float K = mix(9.0, 17.0, u_opt.y);
  float az = 2.6 + 0.6 * m * sin(a + 0.9) + 0.12 * m * sin(2.0 * a) - 0.8 * u_sun.x;
  vec3 L = normalize(vec3(cos(az), sin(az), 0.55 + 0.6 * u_sun.y));
  vec2 g0 = garden(p, K);
  float e = 0.0025;
  float hx = garden(p + vec2(e, 0.0), K).x - g0.x;
  float hy = garden(p + vec2(0.0, e), K).x - g0.x;
  // the border where the rings meet the straight furrows is a step; keep its slope from spiking
  vec2 gg = vec2(hx, hy) / e;
  gg *= min(1.0, (0.35 + 2.5 * g0.y) / max(length(gg), 1e-4));
  vec3 n = normalize(vec3(-gg, 1.0));
  float dif = dot(n, L);
  // the stones' long shadows on the gravel
  float sh = 1.0;
  vec2 ld = normalize(L.xy);
  for (int i = 0; i < 3; i++) {
    vec3 c = stone(float(i));
    vec2 q = p - c.xy;
    float along = dot(q, ld);
    float across = abs(dot(q, vec2(-ld.y, ld.x)));
    float len = c.z * 2.2 / max(L.z, 0.2);
    float s = smoothstep(c.z * 1.05, c.z * 0.75, across + 0.15 * max(-along, 0.0)) * smoothstep(0.0, -c.z * 0.4, along) * smoothstep(-len, -len * 0.4, along);
    sh = min(sh, 1.0 - 0.6 * s);
  }
  float lit = sat(dif / max(L.z, 0.3)) * sh;
  vec3 gravel = mix(u_pal[0], u_pal[1], smoothstep(0.3, 0.9, lit));
  gravel = mix(gravel, u_pal[2], smoothstep(0.9, 1.5, lit) * 0.8);
  gravel.x += 0.025 * gnoise(vec3(p * 260.0, 1.0)) + 0.012 * gnoise(vec3(p * 60.0, 2.0));
  // the stones and their moss
  float st = g0.y;
  vec3 stc = u_pal[3];
  stc.x *= 0.55 + 0.9 * sat(dif);
  stc.x += 0.08 * pow(sat(dif), 6.0);
  stc.x += 0.04 * gnoise(vec3(p * 40.0, 4.0));
  // moss in patches on the stone's lower flanks, never a ring
  float mp = gnoise(vec3(p * 9.0, 6.0)) + 0.5 * gnoise(vec3(p * 23.0, 8.0));
  float moss = smoothstep(0.0, 0.2, st) * smoothstep(0.6, 0.2, st) * smoothstep(0.1, 0.45, mp);
  vec3 col = mix(gravel, stc, smoothstep(0.0, 0.06, st));
  col = mix(col, mix(u_pal[4], u_pal[2], 0.2 * sat(dif)), moss * 0.75);
  // the warmth of the low sun across the garden
  col = mix(col, u_pal[5], 0.16 * sat(dot(p, ld) * 0.4 + 0.5) * sh * sat(lit));
  return outLab(col);
}
