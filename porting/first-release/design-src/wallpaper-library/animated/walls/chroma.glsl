// Chroma: a field of pure colour that breathes. Five colours sit at five slowly circling points and flow into one
// another across the whole frame, mixed the way light mixes, through clean, rich in-between hues, never through grey
// (the mix keeps its chroma). Where two colours meet the field lifts a little, like a sheet of light, and one soft
// glow drifts across it; a fine still grain keeps the broad fields photographic.
// Palette: 0 the deep edge, 1 first colour, 2 second, 3 third, 4 fourth, 5 the light.
// Options: x = motion (how far the colours drift), y = edges (crisp meetings to melted ones).
// Perspective: the colours are far (tiltD -1), the light on them near (tiltD 0.8).
const float T = 36.0;

vec3 slot(float i) {
  vec3 c = u_pal[1];
  if (i > 0.5) c = u_pal[2];
  if (i > 1.5) c = u_pal[3];
  if (i > 2.5) c = u_pal[4];
  if (i > 3.5) c = mix(u_pal[1], u_pal[3], 0.5);
  return c;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float soft = u_opt.y;
  vec2 q = p + tiltD(-1.0);
  q += 0.12 * vec2(sin(q.y * 1.3 + a), sin(q.x * 1.1 - a + 1.0)) * m;
  float k = mix(2.6, 1.3, soft);
  float wsum = 0.0;
  vec2 ab = vec2(0.0);
  float L = 0.0;
  float C = 0.0;
  float w1 = 0.0;
  for (int i = 0; i < 5; i++) {
    float f = float(i);
    float an = f * 1.2566 + 0.4;
    vec2 c = vec2(1.15 * cos(an), 0.72 * sin(an)) * (0.75 + 0.15 * sin(f * 2.7));
    c += m * 0.22 * vec2(sin(a + f * 2.3), cos(a + f * 1.7)) + m * 0.06 * vec2(sin(2.0 * a - f), cos(3.0 * a + f));
    vec3 col = slot(f);
    float br = 0.5 + 0.5 * sin(a + f * 1.9);
    float d = length(q - c) / (0.8 + 0.18 * m * (br - 0.5));
    float w = 1.0 / pow(d * d + 0.02, k * 0.5);
    wsum += w;
    ab += col.yz * w;
    L += col.x * w;
    C += length(col.yz) * w;
    w1 += w * w;
  }
  vec2 mab = ab / wsum;
  vec3 lab = vec3(L / wsum, normalize(mab + 1e-5) * mix(length(mab), C / wsum, 0.9));
  // where two colours meet the field lifts a little, as a sheet of light would, softly
  float meet = sat((1.0 - w1 / (wsum * wsum)) / 0.75);
  vec2 qs = p + tiltD(0.8);
  float fold = smoothstep(0.2, 1.0, meet);
  lab.x += 0.035 * fold * (0.6 + 0.4 * sin(qs.x * 0.9 - qs.y * 0.7 + a));
  // the dark ground at the edges of the frame
  float vig = smoothstep(2.3, 0.5, length(p * vec2(0.7, 1.0)));
  lab = mix(u_pal[0], lab, mix(mix(0.8, 0.4, u_dark), 1.0, vig));
  // a fine, still grain, as in a photograph of light, so the large fields never band
  vec3 gh = hash33(vec3(floor(p / u_px), 7.0));
  lab.x += gh.x * 0.006;
  vec3 lin = lab2lin(lab);
  // one soft glow drifting across, where the light is strongest
  vec2 gp = qs - vec2(0.3 * sin(a), 0.2 * cos(a)) * m - vec2(-0.2, 0.25);
  lin += lab2lin(u_pal[5]) * exp(-dot(gp, gp) * 1.8) * mix(0.05, 0.1, u_dark);
  return lin2srgb(shoulder(lin));
}
