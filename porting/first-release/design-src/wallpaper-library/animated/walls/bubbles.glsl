// Bubbles: soap bubbles drifting through still air. Each is almost clear in the middle and coloured toward its rim,
// where the film is seen at a slant and reflects the most; the colour is thin-film interference, banded by the film's
// thickness, which drains slowly downward and swirls as the bubble turns. A soft window and a smaller second one glint
// on each; the bubbles wobble a little as they float.
// Palette: 0 background, 1 background glow, 2 first film colour, 3 second, 4 third, 5 the window glint.
// Options: x = motion (how far the bubbles drift and swirl), y = colour (a thin, faint film to a rich, thick one).
// Perspective: the background is the far layer; each bubble's depth follows its size.
const float T = 60.0;

vec4 bub(float i, float a, float m) {
  vec4 b = vec4(0.95, -0.28, 0.5, 0.0);
  if (i > 0.5) b = vec4(-1.0, 0.42, 0.3, 1.7);
  if (i > 1.5) b = vec4(-0.28, -0.62, 0.17, 3.1);
  if (i > 2.5) b = vec4(0.12, 0.62, 0.11, 4.4);
  if (i > 3.5) b = vec4(1.45, 0.7, 0.08, 5.6);
  b.xy += m * vec2(0.06 * sin(a + b.w) + 0.02 * sin(2.0 * a + b.w * 2.0), 0.07 * cos(a + b.w * 1.3) + 0.02 * sin(3.0 * a - b.w));
  return b;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float rich = mix(0.5, 1.4, u_opt.y);
  vec2 pb = p + tiltD(-1.0) - vec2(-0.4, 0.3);
  vec3 bg = mix(u_pal[0], u_pal[1], exp(-dot(pb, pb) * 0.4));
  vec3 bgLin = lab2lin(bg);
  vec3 col = bgLin;
  vec3 glintC = lab2lin(u_pal[5]);
  for (int i = 0; i < 5; i++) {
    float f = float(i);
    vec4 b = bub(f, a, m);
    // the larger a bubble, the nearer it floats: its own depth for the perspective
    vec2 d = p + tiltD(clamp((b.z - 0.22) * 4.0, -1.0, 1.0)) - b.xy;
    float r = length(d);
    float ang = atan(d.y, d.x);
    float R = b.z * (1.0 + 0.025 * m * sin(2.0 * ang + 2.0 * a + b.w) + 0.015 * m * sin(3.0 * ang - a + b.w));
    float e = r / R;
    float cov = smoothstep(1.0 + 1.5 * edgePx() / R, 1.0 - 1.5 * edgePx() / R, e);
    vec2 q = d / R;
    float nz = sqrt(max(1.0 - e * e, 0.0));
    // film thickness: thicker toward the bottom, swirled about the bubble's axis as it turns
    vec2 sw = rot(q, 0.9 * m * sin(a + b.w) + 0.6 * (1.0 - e));
    float th = 1.1 - 0.55 * sw.y + 0.35 * gnoise(vec3(sw * 1.8, b.w) + vec3(orbit(a, 0.3 * m), 0.2 * sin(a)));
    th *= rich;
    float ph = th * TAU * 1.3;
    float w0 = pow(0.5 + 0.5 * cos(ph), 2.0);
    float w1 = pow(0.5 + 0.5 * cos(ph - 2.0943951), 2.0);
    float w2 = pow(0.5 + 0.5 * cos(ph - 4.1887902), 2.0);
    vec3 fc = (u_pal[2] * w0 + u_pal[3] * w1 + u_pal[4] * w2) / (w0 + w1 + w2 + 1e-4);
    vec3 fl = lab2lin(fc);
    // reflectance grows toward the rim (a slanted film reflects more)
    float fres = 0.012 + 0.988 * pow(1.0 - nz, 3.4);
    float amt = fres * mix(0.55, 0.9, sat(rich - 0.4));
    vec3 film = fl * amt;
    // glints of a soft window, upper left, and a small second one lower right
    vec2 g1 = rot(q - vec2(-0.4, 0.44), 0.75) / vec2(0.1, 0.24);
    vec2 g2 = rot(q - vec2(0.52, -0.5), 0.75) / vec2(0.04, 0.09);
    vec2 a1 = g1 * g1;
    vec2 a2 = g2 * g2;
    float gl = exp(-pow(a1.x * a1.x + a1.y * a1.y, 0.75) * 1.2) * 0.9 + 0.45 * exp(-pow(a2.x * a2.x + a2.y * a2.y, 0.75) * 1.2);
    // the ground reflected dimly in the lower half
    vec3 dk = col * (1.0 - 0.2 * amt) + film * 1.1 + glintC * gl * 0.85;
    vec3 tintL = fl / max(dot(fl, vec3(0.2126, 0.7152, 0.0722)), 1e-3);
    vec3 lt = col * mix(vec3(1.0), tintL * 0.9, sat(amt * 1.5)) * (1.0 - 0.08 * smoothstep(0.0, -0.9, q.y) * (1.0 - nz)) + glintC * gl * 0.6;
    vec3 c = mix(lt, dk, u_dark);
    // the thin dark line of the film's edge, seen exactly edge-on
    c *= 1.0 - 0.25 * smoothstep(0.96, 1.0, e) * mix(1.0, 0.4, u_dark);
    col = mix(col, c, cov);
  }
  return lin2srgb(shoulder(col));
}
