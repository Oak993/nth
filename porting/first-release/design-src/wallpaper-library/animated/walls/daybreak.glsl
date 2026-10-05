// Daybreak: a quiet horizon at first light. The sky runs from a cool zenith to a warm band at the horizon, a low sun
// glows through haze and rests on still water below, and the colours drift very slightly warmer and back.
// Palette: 0 zenith, 1 upper sky, 2 horizon band, 3 water, 4 sun glow, 5 sun core.
// Options: x = motion (how far the sun drifts), y = glow.
const float T = 80.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float gl = mix(0.4, 1.4, u_opt.y);
  float y0 = -0.32 + 0.015 * sin(p.x * 0.9 + 1.0);
  vec2 sun = vec2(0.42 + 0.16 * m * sin(a), y0 + 0.13 + 0.05 * m * sin(a + 1.2));
  float h = p.y - y0;
  vec3 sky = mix(u_pal[2], u_pal[1], smoothstep(0.0, 0.55, h));
  sky = mix(sky, u_pal[0], smoothstep(0.45, 1.4, h));
  float band = 0.008 * sin(h * 22.0 + 1.5 * sin(p.x * 1.1 + a)) * smoothstep(0.0, 0.2, h) * smoothstep(0.9, 0.3, h);
  sky.x += band;
  vec3 water = mix(setL(u_pal[3], u_pal[3].x + 0.03), u_pal[3], smoothstep(0.0, 0.6, -h));
  vec3 col = mix(water, sky, smoothstep(-u_px, u_px, h));
  col = mix(col, mix(u_pal[2], u_pal[3], 0.5), exp(-(h * h) / (u_px * u_px * 4.0 + 0.00002)) * 0.5);
  vec2 ds = p - sun;
  float g = exp(-dot(ds, ds) / 0.16) * 0.42 + exp(-dot(ds, ds) / 0.6) * 0.18;
  float core = exp(-dot(ds, ds) / 0.004);
  float rx = (p.x - sun.x) / 0.16;
  float ry = h / 0.45;
  float refl = exp(-rx * rx - ry * ry) * 0.3 * (0.85 + 0.15 * sin(-h * 60.0 + a * 2.0)) * smoothstep(u_px, -u_px, h);
  col = mix(col, u_pal[4], sat((g + refl) * gl));
  col = mix(col, u_pal[5], sat(core * 0.7 * gl));
  col = hueShift(col, 0.1 * sin(a));
  return outLab(col);
}
