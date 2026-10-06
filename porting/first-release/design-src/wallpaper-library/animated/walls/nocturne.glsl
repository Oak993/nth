// Nocturne: a long exposure of the night sky above a dark ridge, the last of the twilight still low in the west. Every
// star has drawn an arc round the pole, the bright ones a little thicker and each with its own faint colour, from blue
// to pale gold. The whole sky of trails turns slowly round the pole, as the earth does, and comes back to itself once
// a loop: the trails are laid in rings whose pattern repeats six times round, and the sky turns one sixth of a turn.
// Palette: 0 the sky at the zenith, 1 the twilight low in the sky, 2 the twilight's glow, 3 blue stars, 4 gold stars,
// 5 the ridge.
// Options: x = motion (how fast the sky turns is the loop; this sets how much the twilight breathes), y = trails
// (short to long).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float L = mix(0.35, 0.95, u_opt.y);
  vec2 pole = vec2(-1.15, 1.25) + tiltD(-1.0);
  // the sky
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(-0.6, 1.0, p.y));
  float tw = exp(-pow((p.y + 0.55) / 0.45, 2.0)) * (0.55 + 0.45 * smoothstep(1.6, -0.8, p.x - u_sun.x));
  sky = mix(sky, u_pal[2], tw * (0.65 + 0.1 * m * sin(a)));
  vec3 lin = lab2lin(sky);
  // the trails, in the sky's rotating frame
  vec2 v = p + tiltD(-1.0) - pole;
  float r = length(v);
  float ph = atan(v.y, v.x) - a / 6.0;
  float dr = 0.032;
  float k = floor(r / dr);
  vec3 trails = vec3(0.0);
  for (int j = 0; j < 2; j++) {
    float kk = k + float(j);
    float rc = kk * dr;
    float S = 6.0 * max(1.0, floor(rc * 1.6));
    float sec = TAU / S;
    float si = floor(ph / sec);
    for (int i = 0; i < 3; i++) {
      float idx = si + float(i);
      float w = mod(idx, S / 6.0);
      vec3 h = hash33(vec3(kk, w, 3.0)) * 0.5 + 0.5;
      float on = step(0.45, h.z);
      float rs = rc + (h.y - 0.5) * dr * 0.8;
      // the arc runs back from the star's place through an angle set by the exposure, at most two sectors
      float end = (idx + 0.2 + 0.6 * h.x) * sec;
      float len = min(L * (0.75 + 0.25 * h.y) * 0.75, sec * 1.95);
      float dph = end - ph;
      float along = smoothstep(-0.002, 0.004, dph) * smoothstep(len, len * 0.85, dph);
      float br = pow((h.z - 0.45) / 0.55, 4.0);
      float wd = (0.0008 + 0.0018 * br) + edgePx() * 0.6;
      float d = abs(r - rs);
      float line = exp(-d * d / (wd * wd)) * along * on;
      vec3 sc = mix(lab2lin(u_pal[3]), lab2lin(u_pal[4]), h.x);
      trails += sc * line * (0.12 + 1.2 * br);
    }
  }
  // the trails fade toward the twilight, where the sky is too bright to hold them
  lin += trails * (1.0 - 0.8 * tw) * mix(0.55, 0.85, u_dark);
  // the ridge
  vec2 q = p + tiltD(1.0);
  float rid = -0.72 + 0.1 * sin(q.x * 1.3 + 0.4) + 0.05 * sin(q.x * 3.7 + 1.0) + 0.02 * sin(q.x * 9.0) + 0.008 * sin(q.x * 31.0);
  lin = mix(lin, lab2lin(u_pal[5]), smoothstep(edgePx(), -edgePx(), q.y - rid));
  return lin2srgb(shoulder(lin));
}
