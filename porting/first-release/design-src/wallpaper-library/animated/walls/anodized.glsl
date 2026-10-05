// Anodized: a sheet of spun, anodized aluminium, its brushing in fine circles round a centre far off the lower left.
// Light on circular brushing gathers into a broad radial streak, the way it does on a machined lid; as the light moves
// slowly the streak swings round the centre and the fine circles shimmer inside it. The colour is the metal's own dye.
// Palette: 0 the metal in shade, 1 the metal's body, 2 the lit metal, 3 the streak of light, 4 the second tint the
// curve of the sheet catches, 5 the brightest specular.
// Options: x = motion (how far the light swings), y = brushing (fine to coarse).
const float T = 72.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float br = u_opt.y;
  vec2 S = vec2(-2.1, -1.55);
  vec2 d = p - S;
  float r = length(d);
  float ang = atan(d.y, d.x);
  // the brushing: circles of slightly different depth, band-limited so they never alias at small sizes
  float f1 = mix(110.0, 45.0, br);
  float f2 = mix(260.0, 120.0, br);
  float k1 = sat(0.5 / (f1 * u_px) - 0.6);
  float k2 = sat(0.5 / (f2 * u_px) - 0.6);
  float bru = gnoise(vec3(r * f1, ang * 1.4, 1.0)) * 0.6 * k1 + gnoise(vec3(r * f2, ang * 2.5, 2.0)) * 0.4 * k2
            + 0.3 * gnoise(vec3(r * 7.0, ang * 1.2, 3.0)) + 0.45 * gnoise(vec3(r * 24.0, ang * 0.9, 4.0)) * sat(0.5 / (24.0 * u_px) - 0.6);
  // the streak: where the brushing's tangent faces the light, the radial line toward the light's direction
  float la = 0.6 + 0.15 * m * sin(a + 1.1) + 0.04 * m * sin(2.0 * a + 1.0) + 0.3 * u_sun.x;
  float off = ang - la;
  float streak = gauss(off, 0.11 + 0.02 * sin(a + 0.5)) * 0.65 + gauss(off, 0.32) * 0.4;
  streak *= smoothstep(0.9, 2.2, r) * smoothstep(5.6, 3.0, r);
  // the sheet bends gently, so one side catches a second, cooler tint
  vec3 col = mix(u_pal[0], u_pal[1], smoothstep(-1.2, 1.2, p.y + 0.4 * p.x));
  col = mix(col, u_pal[4], 0.3 * smoothstep(0.2, 1.6, p.x - 0.6 * p.y));
  col = mix(col, u_pal[2], 0.45 * gauss(off, 0.5));
  col.x += 0.008 * bru + 0.006 * sin(r * 15.0 + 0.6 * gnoise(vec3(r * 2.0, ang, 5.0)));
  col = mix(col, u_pal[3], sat(streak * (0.88 + 0.25 * bru)) * 0.7);
  col = mix(col, u_pal[5], sat(pow(streak, 3.0) * (0.7 + 0.6 * bru)) * 0.3);
  return outLab(col);
}
