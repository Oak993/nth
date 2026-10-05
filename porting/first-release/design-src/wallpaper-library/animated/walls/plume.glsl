// Plume: a peacock's feather seen close, its colour made by structure, not pigment. From the shaft, fine barbs sweep
// out and curve; each throws back one pure, metallic colour that depends on the angle between it, the light and the eye,
// so as the light slowly circles a wave of colour rolls along the vane, from green through blue to violet and gold.
// Only a band across the feather is in focus; beyond it the barbs melt into soft colour, as in a macro photograph.
// Palette: 0 background, 1 background glow, 2 first sheen colour, 3 second, 4 third, 5 the light on the shaft.
// Options: x = motion (how far the light swings and the feather sways), y = depth of field (deep to shallow).
// Perspective: the background is the far layer, the feather near.
const float T = 56.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float dof = mix(0.4, 1.5, u_opt.y);
  vec2 pb = p + tiltD(-1.0) - vec2(-0.5, 0.3);
  vec3 col = lab2lin(mix(u_pal[0], u_pal[1], exp(-dot(pb, pb) * 0.5)));
  vec2 q = p + tiltD(0.6);
  // the shaft: a gentle curve from the lower left, swaying a little
  float sway = 0.03 * m * sin(a + 0.6);
  vec2 o = vec2(-1.9, -1.25);
  vec2 dir = normalize(vec2(1.0, 0.62));
  vec2 r = q - o;
  float u = dot(r, dir);
  float v0 = dot(r, vec2(-dir.y, dir.x));
  float bend = 0.07 * u * u - 0.02 * u * u * u + sway * u;
  float v = v0 - bend;
  float side = v >= 0.0 ? 1.0 : -1.0;
  float av = abs(v);
  // the vane's outline: wide in the middle, tapering to the tip, its edge ragged where barbs end at different lengths
  float len = 3.9;
  float s = u / len;
  float wv = 0.62 * pow(sat(sin(3.14159265 * pow(sat(s), 0.75))), 0.7);
  float k = 140.0;
  float ph = (u - av * 1.25 - av * av * 0.35) * k + 2.5 * gnoise(vec3(u * 3.0, av * 2.0, side));
  float bi = floor(ph / TAU);
  vec3 bh = hash33(vec3(bi, side, 3.3)) * 0.5 + 0.5;
  float edge = wv * (0.86 + 0.14 * (0.5 + 0.5 * gnoise(vec3(u * 4.5, side * 3.0, 1.0))) + 0.04 * bh.x);
  float inV = smoothstep(edge, edge - 0.12, av) * smoothstep(-0.05, 0.03, s) * smoothstep(1.02, 0.92, s);
  // focus: sharp in a band across the feather, soft away from it
  float fd = abs(u - 2.0 - 0.15 * v);
  float blur = sat(fd * 0.5 * dof);
  float fine = sat(TAU / k / u_px / 5.0 - 0.3);
  float sharpw = mix(5.0, 0.6, blur) * mix(1.0, 0.5, 1.0 - fine);
  float barb = pow(0.5 + 0.5 * cos(ph), sharpw);
  barb = mix(0.5, barb, fine * (1.0 - blur * 0.85));
  // the colour: set by the angle of each barb against the circling light
  vec2 bdir = normalize(vec2(1.25 + 0.7 * av + 0.4 * sin(u * 1.3), side * (1.0 - 0.6 * av)));
  float la = a + 0.5 * m * sin(a * 2.0);
  float c = dot(bdir, vec2(cos(la), sin(la))) * 0.5 + 0.5;
  float w2 = c * 1.4 - 0.25 + 0.4 * s + 1.1 * av / max(wv, 0.1) * (0.5 + 0.5 * sin(la - u * 0.8)) + 0.06 * bh.y;
  vec3 sc = mix(u_pal[2], u_pal[3], smoothstep(0.2, 0.9, w2));
  sc = mix(sc, u_pal[4], smoothstep(0.85, 1.6, w2));
  float shine = 0.25 + 0.95 * pow(sat(sin(c * 3.14159265)), 2.0);
  vec3 bl = lab2lin(sc) * shine;
  // the shaft itself: a thin pale line with a highlight
  float shaft = exp(-v * v / (0.00012 + 0.00002 * blur * 40.0)) * smoothstep(1.0, 0.85, s) * smoothstep(-0.02, 0.05, s);
  vec3 dk = col * (1.0 - inV * 0.85) + bl * inV * (0.18 + 1.05 * barb) * (1.0 - 0.5 * smoothstep(0.4, 1.0, av / max(edge, 1e-3))) + lab2lin(u_pal[5]) * shaft * 0.6;
  vec3 lt = mix(col, col * mix(vec3(1.0), bl / max(dot(bl, vec3(0.2126, 0.7152, 0.0722)), 0.05) * 0.8, 0.85) * (0.58 + 0.6 * barb) * (0.85 + 0.25 * shine), inV);
  lt = mix(lt, lab2lin(u_pal[5]), shaft * 0.5);
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
