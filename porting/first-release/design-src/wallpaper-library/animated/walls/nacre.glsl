// Nacre: mother of pearl. A slow field is cut into many fine layers like the inside of a shell. Each layer is a
// shade lighter or darker than its neighbour and carries its own faint hue, which turns with the direction the layer
// faces, so the surface shimmers as it drifts; a soft sheen lies across it.
// Palette: 0 the pearl, 1 to 4 the hues the layers take in turn, 5 the sheen.
// Options: x = motion (how far the layers drift), y = layers (few to many).
const float T = 56.0;

float fld(vec2 p, float a, float m) {
  vec2 q = p + 0.25 * vec2(gnoise(vec3(p * 0.4, 3.0) + vec3(orbit(a, 0.15 * m), 0.0)), 0.0);
  return 0.38 * fbm3(vec3(q * 0.55, 0.0) + vec3(orbit(a, 0.2 * m), 0.25 * m * sin(a + 1.0))) + 0.42 * q.y + 0.2 * q.x + 0.06 * dot(q, q);
}

// the layer colour: the pack's four hues blended round the circle in LCh (lightness and chroma by weight, hue as a
// weighted mean of directions), so two opposite hues never cancel into grey
vec3 hues(float ph) {
  float L = 0.0;
  float C = 0.0;
  vec2 hv = vec2(0.0);
  float ws = 0.0;
  for (int i = 0; i < 4; i++) {
    float w = pow(0.5 + 0.5 * cos(ph - float(i) * 1.5707963), 3.0);
    vec3 s = u_pal[1 + i];
    float c = length(s.yz);
    L += s.x * w;
    C += c * w;
    hv += s.yz / max(c, 1e-4) * w;
    ws += w;
  }
  return vec3(L / ws, normalize(hv + 1e-5) * (C / ws));
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float N = mix(7.0, 20.0, u_opt.y);
  float e = 0.004;
  float f0 = fld(p, a, m);
  vec2 gv = vec2(fld(p + vec2(e, 0.0), a, m) - f0, fld(p + vec2(0.0, e), a, m) - f0) / e;
  float g = length(gv) + 1e-4;
  vec2 nd = gv / g;
  float x = f0 * N;
  float id = floor(x);
  float fr = x - id;
  float dLo = fr / (N * g);
  float dHi = (1.0 - fr) / (N * g);
  float view = dot(nd, vec2(cos(a), sin(a)));
  float ph = id * 0.7 + 2.2 * view + 0.8 * f0;
  vec3 c = hues(ph);
  vec3 cPrev = hues(ph - 0.7);
  vec3 cNext = hues(ph + 0.7);
  float step1 = 0.5 * (1.0 - smoothstep(0.0, u_px * 1.5, dLo));
  float step2 = 0.5 * (1.0 - smoothstep(0.0, u_px * 1.5, dHi));
  c = mix(c, cPrev, step1);
  c = mix(c, cNext, step2);
  // dark: the base keeps its own colour and the layers glow over it, brightest where they face the light; light: a pearl
  vec3 base = mix(u_pal[0], hueShift(u_pal[0], 0.6 * view), 0.5 * u_dark);
  vec3 col = mix(base, c, mix(0.55, 0.8, u_dark));
  float facing = sat(0.5 + 0.5 * dot(nd, normalize(vec2(-0.5, 0.85))));
  col.x += u_dark * (0.07 * facing - 0.03) + 0.018 * sin(id * 2.3) + 0.022 * exp(-dHi / 0.008) - 0.02 * exp(-dLo / 0.012);
  col.yz *= 1.0 + 0.25 * u_dark * facing;
  float sheen = gauss(dot(p, vec2(0.6, 0.8)) - 0.3 + 0.2 * sin(a), 0.55);
  col = mix(col, u_pal[5], sheen * 0.25);
  return outLab(col);
}
