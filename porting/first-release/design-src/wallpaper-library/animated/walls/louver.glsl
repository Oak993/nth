// Louver: late sun through a window's slatted blind, laid across a quiet wall and folding down onto the floor below.
// The bars of light lean with the sun; their edges soften the farther they fall from the blind and warm a little at the
// penumbra, as real sunlight does. The patch slides slowly as the sun moves and the slats tilt a touch.
// Palette: 0 wall, 1 wall's lower tone, 2 sunlight, 3 the warm penumbra, 4 floor, 5 the brightest light.
// Options: x = motion (how far the light slides), y = slats (wide to narrow).
const float T = 80.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float P = mix(0.24, 0.12, u_opt.y);
  float yf = -0.68;
  float onFloor = smoothstep(yf + u_px, yf - u_px, p.y);
  // a point on the floor is lit by the ray that would have reached the wall above the fold
  float df = max(yf - p.y, 0.0);
  vec2 q = mix(p, vec2(p.x - df * 1.6, yf + df * 0.55), onFloor);
  // the patch of light from the blind, sliding along the sun's path
  vec2 c = vec2(0.45, 0.02) + vec2(-0.7 * u_sun.x, 0.6 * u_sun.y) + vec2(0.45, -0.18) * m * sin(a) + vec2(0.05, 0.04) * m * sin(2.0 * a + 0.7);
  float ang = -0.42 + 0.04 * m * sin(a + 1.2);
  vec2 r = rot(q - c, -ang);
  r.x += 0.22 * r.y;
  float soft = 0.018 + 0.05 * sat(0.5 + 0.35 * (q.x - q.y));
  float px = smoothstep(1.05 + soft * 5.0, 1.05 - soft * 5.0, abs(r.x));
  float py = smoothstep(0.6 + soft * 4.0, 0.6 - soft * 4.0, abs(r.y));
  float win = px * py;
  // the slats: bars of light across the patch, their duty breathing as the slats tilt
  float duty = 0.36 + 0.08 * sin(a + 0.4);
  float s = r.y / P;
  float f = abs(fract(s) - 0.5) * 2.0;
  float bar = smoothstep(duty - soft / P, duty + soft / P, f);
  float penum = exp(-pow((f - duty) / (soft / P + 0.02), 2.0));
  // the room
  vec3 wc = mix(u_pal[1], u_pal[0], smoothstep(-0.9, 1.0, p.y + 0.25 * p.x));
  vec3 fc = mix(u_pal[4], u_pal[1], smoothstep(-1.0, yf, p.y) * 0.4);
  vec3 base = mix(wc, fc, onFloor);
  base.x -= 0.03 * exp(-abs(p.y - yf) / 0.03);
  vec3 lin = lab2lin(base);
  float light = win * bar;
  lin += lab2lin(u_pal[2]) * light * mix(0.75, 0.5, u_dark) * (1.0 - 0.2 * onFloor);
  lin += lab2lin(u_pal[3]) * win * penum * 0.08;
  lin += lab2lin(u_pal[5]) * light * exp(-dot(r, r) / 0.5) * 0.12;
  // the light bounced off the floor warms the bottom of the wall a little
  lin += lab2lin(u_pal[2]) * 0.05 * exp(-max(p.y - yf, 0.0) / 0.25) * (1.0 - onFloor) * smoothstep(1.2, 0.0, abs(p.x - c.x + 0.4));
  lin *= 1.0 + 0.02 * gnoise(vec3(p * 160.0, 2.0));
  return lin2srgb(shoulder(lin));
}
