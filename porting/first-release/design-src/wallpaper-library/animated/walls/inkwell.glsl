// Inkwell: two inks dropped into still water, billowing and unfurling into fine tendrils. In light mode the ink absorbs
// light as real ink does in clear water (Beer-Lambert): thin veils stay pale and dense plumes go deep. In dark mode the
// same clouds glow, lit from the side.
// Palette: 0 water, 1 water glow, 2 first ink, 3 second ink, 4 where the inks scatter light, 5 light through the water.
// Options: x = motion (how far the ink unfurls), y = density.
const float T = 60.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float den = mix(0.55, 1.45, u_opt.y);
  vec3 o = vec3(orbit(a, 0.22 * m), 0.28 * m * sin(a + 1.0));
  vec2 w = vec2(fbm3(vec3(p * 0.8, 0.0) + o), fbm3(vec3(p * 0.8 + 3.3, 1.0) - o.yxz));
  vec2 q = p + 0.6 * w;
  float f = fbm4(vec3(q * 1.5, 2.0) + o * 0.6);
  float fil = 1.0 - abs(gnoise(vec3(q * 3.4, 4.0) + o * 0.5));
  fil = fil * fil * fil;
  float eA = sat(1.0 - length((q - vec2(-0.55, 0.3)) * vec2(0.8, 0.62)));
  float eB = sat(1.0 - length((q - vec2(0.75, -0.25)) * vec2(0.75, 0.7)));
  float dA = eA * eA * sat(f * 1.6 + 0.45) * (0.55 + 0.7 * fil) * den;
  float dB = eB * eB * sat(-f * 1.4 + 0.45) * (0.55 + 0.7 * fil) * den;
  vec3 water = lab2lin(mix(u_pal[0], u_pal[1], smoothstep(-1.4, 1.2, p.y + 0.3 * p.x)));
  vec3 iA = lab2lin(u_pal[2]);
  vec3 iB = lab2lin(u_pal[3]);
  vec3 abA = -log(max(iA, vec3(0.015)));
  vec3 abB = -log(max(iB, vec3(0.015)));
  vec3 lt = water * exp(-(abA * dA + abB * dB) * 1.5);
  lt += lab2lin(u_pal[5]) * 0.05 * smoothstep(0.8, -1.2, p.y - p.x * 0.4);
  vec3 glow = iA * dA + iB * dB;
  vec3 dk = water * exp(-(abA * dA + abB * dB) * 0.35) + glow * 0.9 + lab2lin(u_pal[4]) * pow(sat(dA + dB), 2.0) * fil * 0.25;
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
