// Molten: drops of liquid chrome that drift, merge and part over a studio backdrop. Each drop is a smooth dome, and
// everything seen on it is the studio it mirrors: a bright sky above a crisp horizon, a dark floor below, a long soft
// box overhead and a strip light at the side whose sharp edges roll over the soft curves as the drops move. Toward the
// rim the reflection turns to the backdrop and brightens (Fresnel), with a thin bright lip. A pack can add a
// thin-film tint (anodised titanium): its hue turns with the angle of the surface.
// Palette: 0 backdrop, 1 sky, 2 coloured gel at the side, 3 floor, 4 thin film (its chroma is the film's strength; 0 is
// plain chrome), 5 soft-box light (its chroma tints the metal, as gold or rose gold).
// Options: x = motion (how far the drops travel), y = depth (flat discs to tall domes).
const float T = 40.0;

float field(vec2 p, float a, float m, out vec2 g) {
  float F = 0.0;
  g = vec2(0.0);
  for (int i = 0; i < 6; i++) {
    float f = float(i);
    float n = mod(f, 2.0) < 0.5 ? 1.0 : -1.0;
    vec2 c = vec2(-1.05 + 0.42 * f, 0.18 * sin(f * 2.3 + 0.7) - 0.04) + orbit(n * a + f * 1.9, (0.16 + 0.04 * mod(f, 3.0)) * m);
    c.y += 0.08 * m * sin(2.0 * a + f);
    float r = 0.27 + 0.11 * sin(f * 1.7 + 0.4) + 0.025 * m * sin(a + f * 2.2);
    vec2 d = p - c;
    float w = exp(-dot(d, d) / (r * r));
    F += w;
    g += w * (-2.0 * d / (r * r));
  }
  return F;
}

vec3 env(vec3 r, float a) {
  float up = r.y + 0.32 * r.z;
  float side = r.x;
  vec3 skyTop = setL(u_pal[1], u_pal[1].x * 0.86);
  vec3 sky = mix(u_pal[1], skyTop, smoothstep(0.05, 0.9, up));
  vec3 flo = mix(u_pal[3], setL(u_pal[3], u_pal[3].x * 0.55), smoothstep(-0.05, -0.8, up));
  vec3 c = mix(flo, sky, smoothstep(-0.035, 0.035, up));
  float gel = smoothstep(0.22, 0.52, side) * smoothstep(-0.5, -0.1, up) * smoothstep(0.95, 0.45, up);
  c = mix(c, mix(u_pal[2], setL(u_pal[2], u_pal[2].x * 1.12), smoothstep(0.3, 0.8, side)), gel * 0.9);
  vec3 lin = lab2lin(c);
  vec3 L5 = lab2lin(vec3(u_pal[5].x, 0.0, 0.0));
  float bx = smoothstep(0.26, 0.22, abs(side + 0.18 + 0.03 * sin(a))) * smoothstep(0.13, 0.09, abs(up - 0.62));
  float strip = smoothstep(0.08, 0.04, abs(side + 0.72)) * smoothstep(0.55, 0.35, abs(up - 0.15));
  lin += L5 * (bx * 1.6 + strip * 0.7);
  float back = smoothstep(-0.6, -0.98, r.z);
  lin = mix(lin, lab2lin(u_pal[0]) * 1.25 + 0.04, back * 0.85);
  return lin;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.4, 1.5, u_opt.x);
  float depth = mix(0.35, 1.25, u_opt.y);
  float th = 0.5;
  vec2 g;
  float F = field(p, a, m, g);
  float e = max(F - th, 0.0);
  vec2 gh = g / (2.0 * sqrt(e + 0.0012)) * 0.55 * depth;
  vec3 n = normalize(vec3(-gh, 1.0));
  vec3 r = reflect(vec3(0.0, 0.0, -1.0), n);
  vec3 lin = env(r, a);
  // the metal: tinted by the soft box's colour (gold, rose gold), and a thin film whose hue turns with the angle
  vec3 tl = lab2lin(vec3(0.8, u_pal[5].yz * 2.2));
  lin *= tl / max(dot(tl, vec3(0.2126, 0.7152, 0.0722)), 1e-3);
  float fs = smoothstep(0.03, 0.11, length(u_pal[4].yz));
  vec3 lab = lin2lab(lin);
  float fh = atan(u_pal[4].z, u_pal[4].y) + (1.0 - n.z) * 5.0 + 0.8 * sin(a + p.x);
  float fc = mix(length(lab.yz), 0.13 + 0.05 * (1.0 - n.z), fs * 0.8);
  vec2 film = vec2(cos(fh), sin(fh)) * fc;
  lab.yz = mix(lab.yz, film, fs * 0.75 * (1.0 - smoothstep(-0.3, -0.9, r.z) * 0.75));
  lin = lab2lin(lab);
  float fres = pow(1.0 - n.z, 5.0);
  lin = mix(lin, vec3(1.0), fres * 0.22);
  float lip = smoothstep(0.012, 0.0, e) * smoothstep(-0.1, 0.6, n.y + 0.3);
  lin += lab2lin(vec3(u_pal[5].x, 0.0, 0.0)) * lip * 0.25;
  // the backdrop: a seamless sweep with a soft light and the drops' contact shadow
  vec3 bd = mix(u_pal[0], setL(u_pal[0], u_pal[0].x * mix(1.18, 1.04, 1.0 - u_dark) + 0.02), smoothstep(-1.2, 0.9, p.y));
  vec3 bLin = lab2lin(bd);
  vec2 gs;
  float Fs = field(p - vec2(0.05, -0.11), a, m, gs);
  bLin *= 1.0 - mix(0.18, 0.42, u_dark) * smoothstep(0.08, 0.55, Fs);
  bLin *= 0.88 + 0.12 * smoothstep(2.1, 0.3, length(p - vec2(0.0, 0.4)));
  float w = length(g) * u_px * 1.3 + 1e-4;
  float cov = smoothstep(th - w, th + w, F);
  return lin2srgb(shoulder(mix(bLin, lin, cov)));
}
