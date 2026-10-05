// Clay: one soft, rounded form in matte clay, like a smooth river stone, floating a little above a seamless studio
// sweep. A large soft key light from the upper left models it, the sweep's own colour bounces back into its lower
// edge, and its shadow on the floor softens and widens as it rises and sharpens as it settles. It sways and rises
// very slowly, and its outline breathes a little, so it never looks like a still render. It sits right of centre.
// Palette: 0 the floor, 1 the wall of the sweep, 2 the form's shadow side, 3 the form's body, 4 the form's lit face,
// 5 the soft highlight.
// Options: x = motion (how far it rises and sways), y = softness of the light (crisp to soft).
const float T = 64.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float sf = u_opt.y;
  vec2 L2 = normalize(vec2(-0.55, 0.7) + u_sun);
  // the sweep: floor curving up into the wall, lit from the upper left
  vec2 pb = p + tiltD(-1.0);
  float horizon = -0.32;
  vec3 col = mix(u_pal[0], u_pal[1], smoothstep(horizon - 0.55, horizon + 0.45, pb.y));
  col.x += 0.035 * smoothstep(2.4, 0.0, length(pb - vec2(-1.1, 1.0) - u_sun)) - 0.025 * smoothstep(0.8, 2.6, length(pb - vec2(-0.4, 0.1)));
  // the form
  vec2 q = p + tiltD(1.0);
  float lift = 0.5 + 0.5 * sin(a);
  vec2 c = vec2(0.78 + 0.03 * m * sin(a + 1.3), -0.06 + 0.05 * m * (lift - 0.5));
  vec2 d = q - c;
  float sway = 0.32 + 0.16 * m * sin(a + 0.6);
  vec2 dr = rot(d, -sway);
  float ang = atan(dr.y, dr.x);
  float R = 0.36 * (1.0 + 0.15 * cos(2.0 * ang) + 0.035 * cos(3.0 * ang + 1.0) + 0.012 * m * sin(2.0 * ang + 2.0 * a));
  float r = length(d);
  float nr = r / R;
  float inside = smoothstep(1.0 + edgePx() / R * 1.2, 1.0 - edgePx() / R * 1.2, nr);
  // the shadow on the floor first, so the form covers it
  float fy = horizon - 0.3;
  vec2 sp = (q - vec2(c.x + 0.16 - 0.1 * L2.x, fy)) / vec2(0.42 + 0.08 * lift * m, 0.068 + 0.02 * lift * m);
  float shadow = exp(-dot(sp, sp) * mix(1.6, 0.9, sf)) * (0.75 - 0.25 * lift * m / 1.4);
  float contact = exp(-dot(sp, sp) * 4.0) * 0.35 * (1.0 - 0.5 * lift);
  col = mix(col, vec3(col.x * 0.8, mix(col.yz, u_pal[2].yz, 0.3)), sat(shadow * 0.55 + contact));
  // the form's surface: a flattened dome, so the top is broad and the edge turns away
  float z = sqrt(max(1.0 - min(nr, 1.0) * min(nr, 1.0), 0.0));
  vec3 n = normalize(vec3(d / R, z * 1.5 + 0.05));
  vec3 L = normalize(vec3(L2 * 0.85, 0.75));
  float wrap = mix(0.25, 0.6, sf);
  float dif = sat((dot(n, L) + wrap) / (1.0 + wrap));
  vec3 fc = mix(u_pal[2], u_pal[3], smoothstep(0.05, 0.6, dif));
  fc = mix(fc, u_pal[4], smoothstep(0.55, 1.0, dif));
  float spec = pow(max(dot(n, normalize(L + vec3(0.0, 0.0, 1.0))), 0.0), mix(26.0, 9.0, sf));
  fc = mix(fc, u_pal[5], spec * 0.22);
  // the floor's colour bouncing into the lower edge, and a little occlusion where it turns away
  float bounce = pow(1.0 - z, 2.0) * sat(-n.y * 1.4 + 0.2);
  fc = mix(fc, mix(u_pal[0], u_pal[3], 0.4), bounce * 0.4);
  fc.x -= 0.03 * pow(1.0 - z, 3.0);
  col = mix(col, fc, inside);
  return outLab(col);
}
