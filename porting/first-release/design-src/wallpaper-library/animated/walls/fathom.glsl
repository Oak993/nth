// Fathom: looking up from a few metres under a calm sea. The whole sky is squeezed into one round window overhead (the
// window real divers see, set by the angle at which water bends light), its rim brightest where the horizon is
// pressed together and trembling with the small waves above. Outside it the surface is a mirror of the deep. Soft
// shafts of light lean down from the window and fade into blue.
// Palette: 0 the deep, 1 the water near the surface, 2 the sky in the window, 3 the bright rim, 4 the shafts of light,
// 5 the sun in the window.
// Options: x = motion (how much the surface trembles and the shafts sway), y = clarity (murky to clear).
const float T = 64.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float cl = mix(0.5, 1.3, u_opt.y);
  vec2 W = vec2(-0.55, 1.18);
  // the deep: darker away from the window
  vec2 qd = p + tiltD(-1.0);
  float dist = length((qd - W) * vec2(0.8, 1.0));
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(0.4, 2.6, dist));
  // the shafts, fanning down from the window and swaying
  vec2 dw = p - W;
  float ang = atan(dw.x, -dw.y);
  float sh = lnoise(vec2(ang * 6.0, 1.0), a, 0.32 * m) * 0.6 + lnoise(vec2(ang * 13.0, 3.0), a, 0.28 * m) * 0.4;
  float shaft = smoothstep(0.05, 0.6, sh) * smoothstep(2.8, 0.7, length(dw)) * smoothstep(0.55, 0.95, length(dw));
  col = mix(col, u_pal[4], sat(shaft * 0.32 * cl));
  // the window: its edge rippled by the small waves, the sky squeezed inside it
  vec2 qw = p + tiltD(1.0);
  vec2 dq = (qw - W) * vec2(1.0, 1.12);
  float r = length(dq);
  float wa = atan(dq.y, dq.x);
  vec2 cw = vec2(cos(wa), sin(wa));
  float rip = 0.024 * m * lnoise(cw * 2.2, a, 0.4) + 0.009 * (0.5 + 0.5 * m) * lnoise(cw * 7.0 + 3.0, a, 0.5);
  float R = 0.88 + rip;
  float ed = r - R;
  float inside = smoothstep(0.03 + 0.01 * u_focus, -0.025 - 0.01 * u_focus, ed);
  float u = sat(r / R);
  vec3 sky = mix(u_pal[2], u_pal[3], smoothstep(0.55, 1.0, u));
  vec2 sn = dq - vec2(0.32, -0.2) - u_sun;
  sky = mix(sky, u_pal[5], 0.65 * exp(-dot(sn, sn) / 0.05));
  // the sky trembles too: a faint net of brighter light over it
  float tw = lnoise(dq * 5.0, a, 0.35 * m);
  sky.x += 0.02 * tw;
  col = mix(col, sky, inside * 0.92);
  // the bright rim, and the darker ring just outside it where the surface turns to a mirror
  col = mix(col, u_pal[3], 0.4 * exp(-(ed * ed) / 0.0016));
  col.x -= 0.012 * exp(-((ed - 0.07) * (ed - 0.07)) / 0.006);
  return outLab(col);
}
