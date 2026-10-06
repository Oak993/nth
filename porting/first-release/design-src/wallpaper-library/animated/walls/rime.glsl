// Rime: frost on a window pane on a winter morning, with the low sun coming up behind it. The frost has grown in from
// the frame in feathered fronds, thickest in the corners, leaving the middle of the pane clear; the light behind
// glows through the fronds and every crystal edge catches it. The frost breathes a little as the sun warms the glass,
// and the glow drifts as the sun climbs.
// Palette: 0 the sky behind high, 1 the sky behind low, 2 the sun's glow, 3 the frost, 4 the frost in the light,
// 5 the glints.
// Options: x = motion (how much the frost breathes and the glow drifts), y = frost (light to heavy).
const float T = 70.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float fr = mix(0.7, 1.35, u_opt.y);
  // the world behind the glass, far out of focus
  vec2 gc = vec2(0.55, -0.25) + vec2(0.22, 0.12) * m * sin(a) + u_sun;
  float gg = length((p - gc) * vec2(0.8, 1.0));
  vec3 back = mix(u_pal[1], u_pal[0], smoothstep(-1.0, 1.0, p.y));
  back = mix(back, u_pal[2], exp(-gg * gg / 0.35) * 0.85 + exp(-gg / 0.8) * 0.2);
  vec3 lin = lab2lin(back);
  // how far in from the frame the frost has grown: thickest in the corners
  float ax = 1.6;
  vec2 ed = vec2(ax - abs(p.x), 1.0 - abs(p.y));
  float edge = min(ed.x * 0.8, ed.y * (0.75 + 0.35 * step(0.0, p.y))) - 0.22 * sat(1.0 - length(ed) * 0.8);
  float reach = (0.42 + 0.08 * m * sin(a + 1.0)) * fr;
  // the fronds: stems growing in from the frame, each feathered with side branches at sixty degrees
  vec2 gd = normalize(-p * vec2(0.35, 1.0) + vec2(0.0, 1e-3));
  vec2 gn = vec2(-gd.y, gd.x);
  vec2 q = p * 3.0;
  vec2 wq = q + 0.35 * vec2(gnoise(vec3(q * 0.5, 1.0)), gnoise(vec3(q * 0.5, 3.0)));
  vec2 sq = vec2(dot(wq, gd), dot(wq, gn));
  float stem = sat(1.0 - abs(gnoise(vec3(sq.x * 0.6, sq.y * 3.2, 5.0))));
  vec2 b1 = rot(sq, 1.05);
  vec2 b2 = rot(sq, -1.05);
  float br = max(sat(1.0 - abs(gnoise(vec3(b1.x * 1.2, b1.y * 9.0, 7.0)))), sat(1.0 - abs(gnoise(vec3(b2.x * 1.2, b2.y * 9.0, 8.0)))));
  float f3 = sat(1.0 - abs(gnoise(vec3(wq * 7.0, 2.0))));
  float fern = pow(stem, 10.0) * 0.8 + pow(br, 14.0) * 0.7 * smoothstep(0.3, 0.8, stem) + pow(f3, 14.0) * 0.08;
  float grow = smoothstep(reach + 0.1, reach - 0.3, edge + 0.22 * gnoise(vec3(p * 1.3, 4.0)) + 0.08 * gnoise(vec3(p * 4.0, 6.0)));
  float dens = sat(grow * (0.18 + 1.6 * fern) * 0.85);
  // frost lit from behind: it scatters the glow toward us, brighter where the glow is
  float glow = exp(-gg * gg / 0.6);
  vec3 fc = mix(lab2lin(u_pal[3]), lab2lin(u_pal[4]), sat(glow * 1.2));
  // in daylight the clear glass between the fronds reads a touch deeper, so the white frost stands out
  lin *= 1.0 - 0.14 * grow * (1.0 - dens) * (1.0 - u_dark);
  lin = mix(lin, fc, dens * mix(0.95, 0.85, u_dark));
  // glints on the crystal edges, turning with the light
  vec3 sh = hash33(vec3(floor(p * 140.0), 3.0));
  float gl = step(0.985, sh.x * 0.5 + 0.5) * grow * fern * (0.5 + 0.5 * sin(sh.y * TAU + a * 2.0));
  lin += lab2lin(u_pal[5]) * gl * (0.25 + 0.6 * glow);
  // the faint mist of breath on the clear glass
  lin = mix(lin, fc, 0.06 * (1.0 - grow));
  return lin2srgb(shoulder(lin));
}
