// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2, extended to v3 by the owner's 2026-10-11 "lock and ship every light"; ED-NEW-lightv2-1): the v3 lights' SkSL, translated from E:/Droppy/work/design/composer-light-v3/index.html (each variant's lightFn), with the same maths and constants.
namespace Marrow.UI.Port.ComposerLights;

internal static partial class ShaderLightShaders
{
    /// <summary>The v3 lights' light functions, by the catalogue's id.</summary>
    internal static IReadOnlyDictionary<string, string> V3Functions { get; } = new Dictionary<string, string>
    {
        // H Nebula, nebula
        ["nebula"] = """
            float fbm3(float2 p) { float s = 0.; float a = .5; for (int i = 0; i < 3; i++) { s += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.2); a *= .5; } return s / .875; }
            float fbm2(float2 p) { return vnoise(p) * .667 + vnoise(p * 2.03 + float2(17.1, 9.2)) * .333; }
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float mF = exp(-sq(d / (uSpread * .32)));
              float mB = exp(-sq(d / (uSpread * .46)));
              float3 tint = pal(gu + t);
              float front = 0.; float back = 0.; float wq = .5;
              if (mB > .006 || gd < 0.) {
                float2 pb = (gp + float2(0., -7.)) * float2(.0065, .013) + circ(TAU * t) * .55 + float2(3.1, 1.7);
                back = fbm2(pb);
                float2 pf = gp * float2(.009, .03);
                float2 w = float2(vnoise(pf * .7 + circ(1.3 - TAU * t * 2.) * .5), vnoise(pf * .7 + float2(5.2, 1.3) + circ(TAU * t * 2.) * .5));
                wq = w.x;
                front = fbm3(pf + (w - .5) * 1.6 + circ(-TAU * t) * .35);
              }
              float bd = smoothstep(.3, .85, back);
              float fd = smoothstep(.36, .88, front);
              float3 cB = pal(gu + t + .5 + .15 * back);
              float3 cF = pal(gu + t + .25 * (wq - .5));
              float3 glow = mix(tint, uCore, .35);
              float core = exp(-d / 7.) * .32 + exp(-d / 22.) * .14;
              float lift = .85 + .35 * uEnergy + k * .8;
              outer = (cB * bd * mB * .3 + cF * fd * mF * .8 + glow * core) * gn * lift;
              rim = glow * exp(-abs(gd) / (.9 + gaa * .5)) * .5 * gn * lift;
              inner = (cF * fd * exp(di / 8.) * .25 + tint * exp(di / 7.) * .18) * gn;
            }
            """,
        // I Serica, silk
        ["serica"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float topY = uC.y - uB.y;
              float hx = gq.x;
              float3 acc = float3(0.);
              for (int i = 0; i < 3; i++) {
                float fi = float(i);
                float dir = i == 1 ? -1. : 1.;
                float base = uSpread * (.2 + .3 * fi);
                float fr = .006 + .0025 * fi;
                float ph = TAU * t * dir + fi * 2.1;
                float yc = topY - base - uSpread * .16 * sin(hx * fr + ph) - 6. * sin(hx * fr * 2.3 - ph * 2. + fi);
                float tw = cos(hx * fr * 1.7 + ph + fi * 1.3);
                float w = (3. + 9. * abs(tw)) * (1. + .4 * uEnergy);
                float yy = (gp.y - yc) / w;
                float band = exp(-yy * yy * 1.8);
                float sheen = pow(clamp(1. - abs(tw), 0., 1.), 6.) * .9;
                float env = exp(-sq(max(abs(hx) - uB.x * (.7 + .2 * fi), 0.) / (uSpread * 1.4)));
                float3 c = pal(hx / (2. * uB.x) * .6 + t + fi * .27);
                acc += c * band * (.35 + sheen) * env * (1. - .22 * fi);
              }
              float k = keyAt(gp);
              float3 rc = pal(gu + t);
              outer = (acc * (.55 + .3 * uEnergy) + mix(rc, uAvg, farMix(d)) * haloP(d, uSpread * .6, .5, 1.) * .25) * gn * (1. + k * .8);
              rim = rc * exp(-abs(gd) / (.8 + gaa * .5)) * .5 * gn * (1. + k);
              inner = (acc * .3 + rc * exp(di / 7.) * .2) * gn;
            }
            """,
        // J Favilla, ember drift
        ["favilla"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float topY = uC.y - uB.y;
              float cellW = 22.;
              float rise = uSpread * 1.5 + 30.;
              float sp = 0.;
              float3 spc = float3(0.);
              float cx = floor(gp.x / cellW);
              float thr = mix(.62, .18, uEnergy);
              if (gp.y < topY + 6. && gp.y > topY - rise - 12.) {
                for (int j = -1; j <= 1; j++) {
                  for (int l = 0; l < 2; l++) {
                    float ci = cx + float(j);
                    float fl = float(l);
                    float h1 = h21(float2(ci, fl * 7.3 + 1.));
                    float h2 = h21(float2(ci * 1.7 + 3.1, fl + 5.));
                    float kr = 2. + floor(h1 * 4.);
                    float ph = h2 + t * kr;
                    float f = fract(ph);
                    float sd = mod(floor(ph), kr);
                    float h3 = h21(float2(ci + sd * 11.3, fl * 3.7 + sd));
                    float alive = step(thr, h21(float2(ci * 3.3 + sd, fl + sd * 1.9)));
                    float px = (ci + .5 + .6 * (h3 - .5)) * cellW + sin(TAU * (t * 3. + h2) + f * 4.) * (3. + 6. * f);
                    float py = topY - 2. - f * rise * (.6 + .4 * h3);
                    float on = alive * smoothstep(0., .08, f) * pow(1. - f, 1.6) * (1. - smoothstep(uB.x - 40., uB.x, abs(px - uC.x)));
                    float r = length(gp - float2(px, py));
                    float sz = .9 + 1.1 * h3;
                    float glow = exp(-r * r / (sz * sz)) + .18 * exp(-r / (sz * 4.));
                    float tw = .75 + .25 * sin(TAU * (t * 8. + h1 * 3.));
                    sp += glow * on * tw;
                    spc += pal(.1 + .25 * f + h3 * .15) * glow * on * tw;
                  }
                }
              }
              float up = 1. - smoothstep(-.9, .3, nrm.y);
              float3 rc = mix(pal(gu + t), uCore, .25);
              float3 cb = mix(rc, uAvg, farMix(d));
              float k = keyAt(gp);
              outer = (cb * haloP(d, uSpread * mix(.5, 1., up), .45, 1.) * mix(.3, .75, up) * .55 + (spc * .9 + uCore * sp * .35) * (1.4 + .6 * uEnergy)) * gn * (1. + k * .7);
              rim = rc * exp(-abs(gd) / (.8 + gaa * .5)) * (.3 + .6 * up) * gn * (1. + k);
              inner = rc * exp(di / 7.) * (.15 + .3 * up) * gn;
            }
            """,
        // K Unda, pulse rings
        ["unda"] = """
            float ringAt(float d, float r, float rmax) {
              if (r <= 0. || r > rmax) return 0.;
              float w = 3. + r * .12;
              float x = (d - r) / w;
              float f = 1. - r / rmax;
              return (exp(-x * x * 2.) + .35 * exp(-sq((d - r * .78) / (w * 1.3)))) * f * f;
            }
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float rmax = uSpread * 2.;
              float rings = 0.;
              for (int i = 0; i < 3; i++) {
                float ph = fract(t * 4. + float(i) / 3.);
                rings += ringAt(d, ph * rmax, rmax) * (.3 + .35 * uEnergy) * (i == 2 ? uWork : 1.);
              }
              float v = rmax / 2.4;
              rings += ringAt(d, uPulse.x * v, rmax) * uPulseA.x;
              rings += ringAt(d, uPulse.y * v, rmax) * uPulseA.y;
              rings += ringAt(d, uPulse.z * v, rmax) * uPulseA.z;
              rings += ringAt(d, uPulse.w * v, rmax) * uPulseA.w;
              float3 c = mix(pal(gu + t + d / (uSpread * 3.)), uAvg, farMix(d) * .5);
              float fresh = uPulseA.x * exp(-uPulse.x * 3.);
              outer = (c * rings * 1.6 + c * haloP(d, uSpread * .5, .5, 1.) * .16) * gn;
              rim = c * exp(-abs(gd) / (.8 + gaa * .5)) * (.4 + .4 * fresh) * gn;
              inner = c * (exp(di / 7.) * .14 + exp(di / 4.) * fresh * .06) * gn;
            }
            """,
        // L Argenta, liquid metal
        ["argenta"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float th = 4.5 + 2. * uEnergy;
              float b = gd / th;
              float lip = 1. - smoothstep(.75, 1.15, abs(b));
              float a = clamp(b, -1., 1.) * 1.2 - nrm.y * .55;
              float bands = .5 + .5 * sin(TAU * (gu * 3. - t * 2.) + a * 3.5);
              float spk = pow(bands, 14.) + .6 * pow(.5 + .5 * sin(TAU * (gu * 5. + t * 3.) - a * 2.), 24.);
              float horizon = smoothstep(-.15, .2, a);
              float3 env = mix(float3(.02, .022, .028), float3(.5, .53, .58), horizon) + float3(.25) * exp(-sq((a - .35) / .08));
              float3 tint = pal(gu + t);
              float3 metal = env * mix(float3(1.), tint, .45) + (uCore + tint * .3) * spk * (1.6 + 1.2 * uEnergy);
              float k = keyAt(gp);
              rim = metal * lip * (.6 + k) * gn * 1.4;
              outer = mix(tint, uAvg, farMix(d)) * haloP(d, uSpread, .45, 1.) * (.25 + .7 * spk * exp(-d / 18.)) * gn * (1. + k);
              inner = (tint * exp(di / 6.) * .15 + uCore * spk * exp(di / 3.) * .1) * gn;
            }
            """,
        // M Vesper, dusk horizon
        ["vesper"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float hgt = uSpread * (1.05 + .12 * sin(TAU * t * 2.) + .35 * uEnergy);
              float hz = uC.y + uB.y * .2;
              float up = hz - gp.y;
              float ex = exp(-sq(max(abs(gq.x) - uB.x * .55, 0.) / (uSpread * 1.3)));
              float v = clamp(up / hgt, 0., 1.6);
              float3 sky = v < .33 ? mix(uPal[0], uPal[1], v / .33) : (v < .8 ? mix(uPal[1], uPal[2], (v - .33) / .47) : mix(uPal[2], uPal[3], clamp((v - .8) / .6, 0., 1.)));
              float fall = up > 0. ? exp(-up / (hgt * .55)) : exp(up / 10.) * .5 + .5 * exp(up / 3.);
              float sunx = uC.x + sin(TAU * t) * uB.x * .45;
              float2 sp = gp - float2(sunx, hz - 6.);
              float sun = exp(-length(sp * float2(1., 1.6)) / (26. + 14. * uEnergy)) * .9 + exp(-length(sp) / 90.) * .25;
              float line = exp(-abs(up) / 1.1) * ex * .5;
              float frost = .8 + .4 * vnoise(float2(gp.x * .01, gp.y * .09) + circ(TAU * t) * .5);
              float3 sunc = mix(uPal[0], uCore, .55);
              float k = keyAt(gp);
              outer = (sky * fall * ex * .55 * frost + sunc * sun * .8 + uCore * line) * gn * (1. + k * .7);
              rim = sunc * exp(-abs(gd) / (.8 + gaa * .5)) * (.3 + .7 * exp(-abs(gp.x - sunx) / 160.)) * gn * (1. + k);
              inner = (sky * fall * ex * .25 + sunc * sun * .4) * gn;
            }
            """,
        // N Neonis, neon tube
        ["neonis"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float3 c = mix(uPal[0], uPal[2], .5 + .5 * sin(TAU * (gu + t)));
              float3 cs = c / max(max(c.r, max(c.g, c.b)), .001);
              float hum = .93 + .05 * sin(TAU * t * 12.) + .02 * sin(TAU * (gu * 4. - t * 6.));
              float k = keyAt(gp);
              float tube = exp(-sq(gd / (1.1 + gaa * .4)));
              float core = exp(-sq(gd / (.55 + gaa * .3)));
              rim = (cs * tube * 1.3 + mix(cs, float3(1.), .7) * core * .9) * hum * (1. + k) * gn * 1.2;
              float3 h1 = haloP(d, uSpread * .22, .7, 0.);
              float3 h2 = haloP(d, uSpread, .2, 0.);
              outer = cs * (h1 * .55 + h2 * .5) * hum * (1. + k * .8) * gn;
              inner = cs * (exp(di / 4.) * .35 + exp(di / 14.) * .06) * hum * gn;
            }
            """,
    };
}
