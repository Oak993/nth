// Kiln: very close to a tall vase turning slowly on a potter's wheel under a studio softbox. Its flambe glaze ran in
// the kiln: one colour streams down through another in long drips, pools deeper in a band where the wall turns, and is
// flecked with a few dark pinholes. The softbox lies in the glaze as one tall, soft highlight, rippled where the drips
// stand proud. The vase turns exactly once a loop, so the glaze passes under the light and comes round again.
// Palette: 0 the vase's shaded side, 1 the first glaze, 2 the second glaze, 3 the pooled glaze, 4 the flecks, 5 the
// softbox's light.
// Options: x = motion (how strongly the light swells; the turn is the loop), y = drips (few to many).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float dr = mix(0.6, 1.6, u_opt.y);
  // the vase: a cylinder wider than the window, its surface point at angle th, turned by the loop
  float Rv = 1.95;
  float th = asin(clamp(p.x / Rv, -0.99, 0.99));
  float ang = th + a;
  float Rn = 1.6;
  vec3 cq = vec3(Rn * cos(ang), Rn * sin(ang), p.y * 0.42);
  // the drips: streams running down the wall, long in height and narrow around
  vec3 dq = vec3(cq.xy * 1.4 * dr, cq.z * 0.9);
  float run = fbm3(dq + vec3(0.0, 0.0, 0.3 * fbm3(dq * 1.7 + 3.0)));
  float streak = gnoise(vec3(cq.xy * 9.0 * dr, cq.z * 0.35 + 5.0)) * 0.6 + gnoise(vec3(cq.xy * 21.0 * dr, cq.z * 0.5 + 2.0)) * 0.4;
  float g2 = smoothstep(-0.3, 0.45, run * 0.8 + 0.35 * streak + 0.5 * (p.y - 0.1));
  // the band where the glaze pooled as it ran
  float pool = smoothstep(-0.45, -1.05, p.y + 0.12 * gnoise(vec3(cq.xy * 2.0, 1.0)) + 0.05 * streak);
  vec3 glaze = mix(u_pal[1], u_pal[2], g2);
  glaze = mix(glaze, u_pal[3], sat(pool * 0.7));
  // pinholes, fixed to the glaze
  float fl = smoothstep(0.78, 0.86, gnoise(vec3(cq.xy * 45.0, cq.z * 45.0)));
  glaze = mix(glaze, u_pal[4], fl * 0.4);
  // the light: the round of the vase, and the softbox's tall reflection, rippled by the drips' relief
  float lit = 0.5 + 0.5 * cos(th * 1.2 + 0.35 - u_sun.x * 0.5);
  vec3 col = mix(u_pal[0], glaze, sat(lit));
  float relief = 0.06 * (run + 0.4 * streak);
  float hx = th + relief * 3.5 + 0.32 - 0.6 * u_sun.x + 0.03 * sin(p.y * 2.0 + 0.5);
  float sb = exp(-pow(hx / (0.04 + 0.02 * smoothstep(1.0, -1.0, p.y)), 2.0)) * smoothstep(1.3, 0.1, abs(p.y - 0.2)) * (0.75 + 0.25 * smoothstep(-1.0, 0.8, p.y)) * (0.8 + 0.2 * m * sin(a * 2.0 + 0.5));
  float sb2 = exp(-pow((th - 0.95 + relief) / 0.22, 2.0)) * 0.18;
  // the dark of the studio reflected beside the light, which is what makes a glaze read as glass
  float dk = exp(-pow((hx - 0.2) / 0.12, 2.0));
  col.x -= 0.06 * dk;
  col = mix(col, u_pal[5], sat(sb * 0.7 + sb2));
  // the glaze's gloss: a faint sheen everywhere it faces the light
  col.x += 0.03 * cos(th * 2.0 + 0.5) + 0.02 * relief;
  return outLab(col);
}
