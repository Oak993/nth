// Limewash: late sun on a limewashed wall. A glass of water stands on the sill just out of frame, and all it leaves on
// the wall is light: a soft pool tinted by the glass and, inside it, the bright heart-shaped caustic that water in a
// glass focuses, its cusp brightest, trembling as the water settles. No object, only light on plaster. The limewash is
// cloudy and brushed, the way the real finish is; the sun moves a little, so the patch leans and the light breathes.
// Palette: 0 the wall in shade, 1 the wall in sun, 2 the tinted pool of light, 3 the caustic's core, 4 the limewash's
// cloud, 5 the warm edge of the sunlight.
// Options: x = motion (how much the water trembles and the sun moves), y = caustic (soft to bright).
const float T = 76.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float ck = mix(0.45, 1.3, u_opt.y);
  // the limewash: cloudy patches and broad crossing brush strokes, fixed on the wall
  float cloud = fbm3(vec3(p * 1.3, 1.0));
  float brush = gnoise(vec3(rot(p, 0.7) * vec2(1.1, 4.5), 2.0)) + gnoise(vec3(rot(p, -0.5) * vec2(1.3, 5.0), 3.0));
  float lime = cloud + 0.22 * brush;
  // the spot of sun from a window to the right, its edge soft
  vec2 sd = vec2(0.3, -0.12) + u_sun;
  float lean = 0.06 * m * sin(a + 1.0);
  vec2 sq = rot(p - sd, 0.32 + lean);
  float spot = smoothstep(0.32, -0.05, max(abs(sq.x - 0.25) - 1.25, abs(sq.y) - 1.7));
  vec3 col = mix(u_pal[0], u_pal[1], spot * (0.9 + 0.1 * sin(a + 0.4)));
  col = mix(col, u_pal[5], 0.35 * spot * smoothstep(-0.3, 0.3, max(abs(sq.x - 0.25) - 1.25, abs(sq.y) - 1.7) + 0.25));
  col = mix(col, u_pal[4], 0.22 * smoothstep(-0.3, 0.5, lime));
  col.x += 0.028 * lime;
  // the light the glass throws: an oblique pool, its cusp toward the glass in the lower right, opening up the wall
  vec2 cusp = vec2(1.1, -0.72) + 0.012 * m * vec2(sin(2.0 * a + 0.3), cos(3.0 * a));
  vec2 ax = normalize(vec2(-0.78 - lean * 1.5, 0.62));
  vec2 dq = p - cusp;
  vec2 q = vec2(dot(dq, ax), dot(dq, vec2(-ax.y, ax.x)) * 1.35);
  float r = length(q);
  float th = atan(q.y, q.x);
  // the caustic: the near part of a cardioid whose cusp sits at the origin, brightest at the cusp and fading along its
  // arms, with the light it gathers lying soft inside; the water's trembling bends it a little
  float A = 0.3 + 0.01 * m * sin(3.0 * a + 1.0);
  float wob = 0.014 * m * lnoise(vec2(cos(th), sin(th)) * 1.5, a, 0.45);
  float rc = A * (1.0 + cos(th)) + wob;
  float w1 = 0.016 + 0.05 * r + 0.008 * u_focus;
  float d1 = r - rc;
  // only the half nearest the cusp gathers enough light to show: an open crescent, brightest at the cusp
  float arms = smoothstep(0.9, 2.2, abs(th)) * exp(-r / (1.2 * A));
  float line = exp(-d1 * d1 / (w1 * w1)) * arms;
  // the water disperses a little: just outside the line the light is tinted, just inside it is whiter
  float d1o = d1 - 1.4 * w1;
  float fringe = exp(-d1o * d1o / (w1 * w1 * 1.5)) * arms;
  float fill = smoothstep(0.02, -0.1, d1) * arms * 0.3;
  float cuspGlow = exp(-r * r / 0.006) * 0.55;
  float caus = line + fill + cuspGlow;
  // the tinted pool the glass throws round it, and the glass's faint grey shade beyond
  vec2 pc = (q - vec2(A * 0.9, 0.0)) * vec2(1.0, 1.25);
  float pool = exp(-dot(pc, pc) / 0.14);
  float shadeRing = sat(exp(-dot(pc, pc) / 0.4) - pool);
  col = mix(col, vec3(col.x * mix(0.9, 0.93, u_dark), mix(col.yz, u_pal[2].yz, 0.3)), sat(shadeRing) * 0.35 * spot);
  col = mix(col, u_pal[2], sat(pool * 0.45 + fringe * 0.45 * ck) * spot);
  col = mix(col, u_pal[3], sat(caus * 0.9 * ck) * spot);
  return outLab(col);
}
