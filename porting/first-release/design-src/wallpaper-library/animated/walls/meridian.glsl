// Meridian: a slow-turning gradient. Five colours lie round a point below and to the right of the window and turn once
// a loop; the angle bends gently with distance, the centre melts into the colours' average and a soft light lifts it.
// Palette: 0 to 4 the five colours round the circle, 5 the light at the centre.
// Options: x = motion (how much the gradient sways as it turns), y = softness (how long the colour blends are).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.5, u_opt.x);
  float sp = mix(5.0, 1.6, u_opt.y);
  vec2 c = vec2(1.95, -1.35) + orbit(a, 0.08 * m);
  vec2 d = p - c;
  float r = length(d);
  float ang = 2.0 * atan(d.y, d.x) - a + 0.35 * m * sin(r * 1.3 - a) + 0.5 * sin(r * 0.9);
  // the stops are mixed in LCh: lightness and chroma by weight, hue as a weighted mean on the circle, so the blend
  // between two stops keeps its colour instead of passing through grey
  float Ls = 0.0;
  float Cs = 0.0;
  vec2 hv = vec2(0.0);
  float ws = 0.0;
  for (int i = 0; i < 5; i++) {
    float w = pow(0.5 + 0.5 * cos(ang - float(i) * 1.2566371), sp);
    vec3 s = u_pal[i];
    float C = length(s.yz);
    Ls += s.x * w;
    Cs += C * w;
    hv += s.yz / max(C, 1e-4) * w;
    ws += w;
  }
  vec3 col = vec3(Ls / ws, normalize(hv + 1e-5) * (Cs / ws));
  col = mix(col, u_pal[5], 0.14 * exp(-dot(p - vec2(-0.6, 0.6), p - vec2(-0.6, 0.6)) / 0.9));
  float sheen = gauss(dot(p, normalize(vec2(0.85, 0.5))) + 0.35 + 0.25 * sin(a + 1.0) * m, 0.5);
  col.x += 0.045 * sheen * (1.0 - 0.4 * u_dark);
  col.x *= 1.0 - 0.07 * smoothstep(1.0, 2.6, length(p - c * 0.5));
  return outLab(col);
}
