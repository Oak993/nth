// Shore: looking straight down at the edge of a calm sea. A thin sheet of water slides up the sand, slows, and slides
// back, again and again, its edge a fine lace of foam; the sand it leaves is wet and holds the sky like a mirror,
// and dries paler further up. The sea beyond deepens toward the top. Slow, quiet and real.
// Palette: 0 dry sand, 1 wet sand, 2 the sky held in the wet sand, 3 the thin water, 4 the foam, 5 the deeper sea.
// Options: x = motion (how far the water runs), y = foam (faint to clear).
const float T = 60.0;

// the reach of the water at x: a run up the sand and a slower slide back, four times a loop, each wave reaching a
// little further or less far than the last, the front oblique to the beach
float reach(float x, float a, float m) {
  float ph = 4.0 * a + 0.55 * x + 0.25 * sin(x * 1.3 + a);
  float run = 0.5 + 0.5 * sin(ph + 0.6 * sin(ph));
  run = run * run * (3.0 - 2.0 * run);
  float big = 0.72 + 0.28 * sin(a + 0.5 * x + 1.0);
  return run * big * (0.2 + 0.2 * m);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float fo = mix(0.3, 1.2, u_opt.y);
  vec2 q = rot(p, 0.12);
  // the sand: fine grain and faint ripple marks, fixed
  float grain = gnoise(vec3(q * 60.0, 1.0)) * sat(0.02 / u_px / 3.0 - 0.2);
  float ripple = gnoise(vec3(q.x * 2.0, q.y * 9.0 + 1.5 * gnoise(vec3(q * 1.2, 3.0)), 2.0));
  float line = 0.36 + 0.05 * sin(q.x * 1.3 + 1.0) + 0.03 * gnoise(vec3(q.x * 2.5, 0.0, 4.0));
  float edge = line - reach(q.x, a, m);
  float top = line - 0.48;
  // how wet the sand is: wet from the highest reach down, drying above it
  float wet = smoothstep(top - 0.25, top + 0.02, q.y);
  vec3 sand = mix(u_pal[0], u_pal[1], wet);
  sand.x += 0.012 * grain + 0.008 * ripple * (1.0 - wet * 0.6);
  // the wet sand holds the sky
  float mirror = wet * smoothstep(top - 0.05, line + 0.1, q.y);
  sand = mix(sand, u_pal[2], 0.6 * mirror * (0.7 + 0.3 * smoothstep(-1.0, 1.0, q.x)));
  // the water: thin at its edge, deeper toward the sea
  float depth = q.y - edge;
  float wa = smoothstep(-0.004 - 0.01 * u_focus, 0.01, depth);
  vec3 water = mix(u_pal[3], u_pal[5], smoothstep(0.0, 0.9, depth));
  water = mix(water, u_pal[2], 0.25 * smoothstep(0.6, 0.0, depth));
  vec3 col = mix(sand, mix(sand, water, 0.35 + 0.65 * smoothstep(0.0, 0.6, depth)), wa);
  // the foam: a bright line on the edge and lace just behind it
  float lace = smoothstep(0.15, 0.55, gnoise(vec3(q.x * 26.0, depth * 34.0, 5.0 + 0.3 * m * sin(a))) * 0.7 + gnoise(vec3(q.x * 9.0, depth * 9.0, 8.0)) * 0.5 + 0.1);
  float foam = gauss(depth - 0.005, 0.006 + 0.004 * u_focus) * 0.7 + lace * gauss(depth - 0.045, 0.04) * 0.4;
  col = mix(col, u_pal[4], sat(foam * fo) * 0.75);
  return outLab(col);
}
