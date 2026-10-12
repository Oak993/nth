// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2, extended to v4 by the owner's 2026-10-11 "lock and ship every light"; ED-NEW-lightv2-1): the v4 lights' SkSL, translated from E:/Droppy/work/design/composer-light-v4/index.html (each variant's lightFn), with the same maths and constants.
namespace Marrow.UI.Port.ComposerLights;

internal static partial class ShaderLightShaders
{
    /// <summary>The v4 lights' light functions, by the catalogue's id.</summary>
    internal static IReadOnlyDictionary<string, string> V4Functions { get; } = new Dictionary<string, string>
    {
        // O Crystallum, sapphire edge
        ["crystallum"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float s1 = TAU * (gu * 3. - t * 2.);
              float glint = pow(.5 + .5 * sin(s1), 18.) + .7 * pow(.5 + .5 * sin(TAU * (gu * 5. + t * 3.) + 1.7), 24.);
              float3 tint = pal(gu + t);
              float3 light = mix(uCore, tint, .4);
              float3 ir = float3(0.);
              for (int i = 0; i < 3; i++) {
                float fi = float(i);
                float pos = -(.6 + fi * 2.6) - .5 * sin(TAU * (gu * 2. + t * (i == 1 ? -1. : 1.)) + fi);
                float3 z = (float3(gd - pos) - float3(-.45, 0., .45) * (1. + fi * .6)) / (.55 + gaa * .4 + fi * .35);
                ir += exp(-z * z) * (1. - fi * .28);
              }
              float fo = d * .012;
              float3 fire = float3(pow(.5 + .5 * sin(s1 - fo), 18.), pow(.5 + .5 * sin(s1), 18.), pow(.5 + .5 * sin(s1 + fo), 18.));
              float lift = 1. + .35 * uEnergy + k;
              outer = (mix(uCore, tint, .6) * haloP(d, uSpread, .45, 1.6) * .16 + mix(float3(1.), tint, .3) * fire * haloP(d, uSpread * .5, .3, 0.) * .8) * gn * lift;
              rim = light * ir * (.3 + 1.1 * glint) * gn * lift;
              inner = (light * exp(di / 9.) * .1 + uCore * glint * exp(di / 4.) * .15) * gn;
            }
            """,
        // P Radiantia, god rays
        ["radiantia"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float2 s0 = uC + float2(sin(TAU * t) * uB.x * .3, 8.);
              float2 v = gp - s0;
              float ang = atan(v.y, v.x) / TAU;
              float rays = ring(ang, 9., 1., 0.) * .6 + ring(ang, 23., -2., 3.) * .4;
              rays = rays * rays * rays * 2.2;
              float br = .86 + .14 * sin(TAU * t * 2.);
              float reach = exp(-d / (uSpread * (.5 + .25 * uEnergy)));
              float3 col = mix(uCore, pal(ang + t), .5 + .3 * smoothstep(0., uSpread, d));
              float soft = 1. - exp(-d / 8.);
              outer = (col * rays * reach * soft * .4 + mix(uCore, uAvg, .5) * haloP(d, uSpread * .5, .6, 1.) * .3) * gn * br * (1. + k);
              float sun = exp(-length(v * float2(.32, 1.)) / 24.);
              inner = (mix(uCore, pal(t), .3) * sun * .45 + uAvg * exp(di / 8.) * .08) * gn * br;
              rim = mix(uCore, col, .5) * exp(-abs(gd) / (.8 + gaa * .5)) * (.25 + .5 * exp(-length(v * float2(.25, 1.)) / 60.)) * gn * br;
            }
            """,
        // Q Nacrea, thin-film pearl
        ["nacrea"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float th = .35 + .3 * ring(gu, 3., 1., 2.) + .25 * ring(gu - t, 7., -1., 5.) - di * .02;
              float3 film = .5 + .5 * cos(TAU * th * 3. * float3(1., 1.22, 1.44));
              float3 tint = pal(gu + t);
              float3 pearl = mix(uCore, film, .6) * mix(float3(1.), tint, .35);
              float sheen = exp(-sq(wrapd(gu - t * 2.) / .06)) + uWork * .7 * exp(-sq(wrapd(gu - t * 2. + .5) / .06));
              float band = exp(-sq((gd + 1.) / 3.5));
              float lift = .8 + .4 * uEnergy + k;
              rim = pearl * band * (.45 + .8 * sheen) * gn * lift;
              float3 pO = mix(mix(uCore, film, .3) * mix(float3(1.), tint, .35), uAvg, farMix(d));
              outer = pO * haloP(d, uSpread, .5, .5) * (.22 + .25 * sheen) * gn * lift;
              inner = pearl * exp(di / 8.) * (.16 + .2 * sheen) * gn;
            }
            """,
        // R Aurum, molten pour
        ["aurum"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float lu = fract(t * 2.);
              float b1 = fract(lu - gu);
              float b2 = fract(lu + .5 - gu);
              float h1 = exp(-b1 * 2.6) * smoothstep(0., .02, b1) * (1. - smoothstep(.85, 1., b1));
              float h2 = exp(-b2 * 2.6) * smoothstep(0., .02, b2) * (1. - smoothstep(.85, 1., b2)) * uWork;
              float heat = max(h1, h2);
              float hO = max(exp(-b1 * 2.6) * smoothstep(0., .12, b1) * (1. - smoothstep(.72, 1., b1)), exp(-b2 * 2.6) * smoothstep(0., .12, b2) * (1. - smoothstep(.72, 1., b2)) * uWork);
              float bulge = ring(gu - t * 4., 5., 1., 1.);
              float th = (2.6 + 2.6 * bulge) * (1. + .35 * heat);
              float b = (gd + th * .3) / th;
              float band = exp(-b * b * 1.4);
              float spk = exp(-sq((b + .35) / .22)) * (.4 + .6 * bulge);
              float3 metal = pal(gu + t);
              float3 hot = mix(metal, uCore, heat * heat * .65);
              float lift = 1. + .3 * uEnergy + k;
              rim = (hot * band * (.22 + 1.05 * heat) + uCore * spk * (.25 + .8 * heat)) * gn * lift;
              outer = mix(mix(metal, uCore, hO * hO * .5), uAvg, farMix(d)) * haloP(d, uSpread * (.45 + .55 * hO), .45, 1.) * (.1 + .5 * hO) * gn * lift;
              inner = hot * exp(di / 6.) * (.06 + .28 * heat) * gn;
            }
            """,
        // S Stellaris, starlight dust (the perimeter and the corner stretch take the page's capsule to any rounded rectangle,
        // as rimU() does: on a capsule, e == 0, they are the page's own, term for term)
        ["stellaris"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float a = uB.x - uR;
              float e = max(uB.y - uR, 0.);
              float P = 4. * a + 4. * e + TAU * uR;
              float N = floor(P / 18.);
              float cs = gu * N;
              float cd = (d - 3.) / 16.;
              float3 sc = float3(0.);
              if (cd > 0. && d < uSpread * 1.3) {
                float2 id = float2(mod(floor(cs), N), floor(cd));
                float h = h21(id + 3.7);
                float h2 = h21(id * 1.31 + 8.2);
                float h3 = h21(id * .77 + 1.9);
                float alive = step(mix(.86, .72, uEnergy), h);
                float stretch = (abs(gq.x) > a && abs(gq.y) >= e) ? (uR + d) / uR : 1.;
                float2 dv = float2((fract(cs) - (.3 + .4 * h2)) * (P / N) * stretch, (fract(cd) - (.3 + .4 * h3)) * 16.);
                float kk = 2. + floor(h2 * 4.);
                float tw = .5 + .5 * sin(TAU * (t * kk + h3));
                tw = tw * tw * tw;
                float r2 = dot(dv, dv);
                float spikes = exp(-abs(dv.x) / .45 - abs(dv.y) / 1.5) + exp(-abs(dv.y) / .45 - abs(dv.x) / 1.5);
                float glow = exp(-r2 / 1.1) + .08 * exp(-sqrt(r2) / 2.) + spikes * .25 * smoothstep(.6, 1., tw);
                float star = alive * tw * exp(-d / (uSpread * .45)) * glow;
                sc = mix(uCore, pal(h + t), .45) * star;
              }
              float3 tint = pal(gu + t);
              float lift = 1. + .3 * uEnergy + k;
              outer = (sc * 1.3 + mix(tint, uAvg, farMix(d)) * haloP(d, uSpread * .6, .5, 1.) * .16) * gn * lift;
              rim = mix(uCore, tint, .4) * exp(-abs(gd) / (.8 + gaa * .5)) * .45 * gn * lift;
              inner = tint * exp(di / 7.) * .12 * gn;
            }
            """,
        // T Velum, aurora curtain
        ["velum"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float x = gq.x;
              float topY = uC.y - uB.y;
              float hem = topY - 8. - 9. * sin(x * .011 + TAU * t) - 5. * sin(x * .029 - TAU * t * 2.);
              float hh = hem - gp.y;
              float fx = x + 22. * sin(x * .017 + TAU * t + 1.3);
              float folds = .55 + .45 * sin(fx * .075);
              float streak = vnoise(float2(fx * .06, hh * .006) + circ(TAU * t) * .8);
              float H = uSpread * (.9 + .5 * uEnergy);
              float v = clamp(hh / H, 0., 1.5);
              float body = smoothstep(-4., 3., hh) * exp(-max(hh, 0.) / (H * .55));
              float env = exp(-sq(max(abs(x) - uB.x * .8, 0.) / uSpread));
              float curtain = body * env * (folds * .7 + .3) * (.55 + .7 * streak);
              float3 col = mix(mix(uPal[0], uPal[1], smoothstep(0., .5, v)), uPal[2], smoothstep(.45, 1.2, v));
              float hemGlow = exp(-sq(hh / 3.)) * env * (.6 + .4 * folds);
              float3 tint = pal(gu + t);
              float lift = (1. + .4 * uEnergy) * (1. + k);
              outer = (col * curtain * .55 + uPal[0] * hemGlow * .35 + mix(tint, uAvg, farMix(d)) * haloP(d, uSpread * .4, .5, 1.) * .12) * gn * lift;
              rim = mix(uPal[0], uCore, .3) * exp(-abs(gd) / (.8 + gaa * .5)) * .4 * gn * lift;
              inner = (col * curtain * .2 + uPal[0] * exp(di / 7.) * .1) * gn;
            }
            """,
        // U Lamina, holographic foil
        ["lamina"] = """
            float3 rb(float h) { return clamp(abs(fract(h + float3(0., 2. / 3., 1. / 3.)) * 6. - 3.) - 1., 0., 1.); }
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float gc = dot(gp, float2(.94, .34));
              float hue = gc * .0035 + d * .0015 + t + .12 * sin(TAU * (gu * 2. - t));
              float3 holo = mix(mix(rb(hue), float3(1.), .35), pal(hue), .3);
              float sx = uC.x + uB.x * 1.25 * sin(TAU * t);
              float sweep = exp(-sq((gp.x - sx + (gp.y - uC.y) * .5) / 110.));
              float micro = .9 + .1 * sin(gc * 1.3);
              float band = exp(-sq((gd + 1.) / 3.));
              float lift = 1. + .35 * uEnergy + k;
              rim = holo * band * (.3 + .9 * sweep) * micro * gn * lift;
              outer = holo * haloP(d, uSpread, .4, .8) * (.1 + .3 * sweep) * gn * lift;
              inner = holo * exp(di / 7.) * (.06 + .14 * sweep) * gn;
            }
            """,
        // V Fulgor, lens flare halo (the page mirrored its ghosts through the canvas centre, uSize * .5; the app has no canvas
        // size, so the mirror is the same point in the composer's terms: on the page's 340 px stage the canvas centre is
        // (uC.x, uC.y - 86) = 60 px above the composer's top edge. The ghost loop's locals gp/gd are renamed ghp/ghd)
        ["fulgor"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float d = max(gd, 0.);
              float di = min(gd, 0.);
              float k = keyAt(gp);
              float3 tint = pal(gu + t);
              float3 acc = mix(tint, uAvg, farMix(d));
              float br = .82 + .18 * sin(TAU * t * 2.);
              float2 fp = float2(uC.x + sin(TAU * t) * uB.x * .72, uC.y - uB.y);
              float2 fv = gp - fp;
              float fr = length(fv);
              float fl = .7 + .3 * uEnergy + k;
              float hot = exp(-fr / 5.) * .9 + exp(-fr / 26.) * .25;
              float streak = exp(-abs(fv.y) / (.9 + abs(fv.x) * .012)) * exp(-abs(fv.x) / (uSpread * 2.2));
              float3 lens = mix(uCore, pal(.6 + t), .55);
              // the page's canvas centre (uSize * .5), in the composer's terms: 60 px above its top edge, on its centre line
              float2 cs = float2(uC.x, uC.y - uB.y - 60.);
              float3 ghosts = float3(0.);
              for (int i = 0; i < 3; i++) {
                float fi = float(i);
                float f = i == 0 ? .55 : (i == 1 ? 1.15 : 1.7);
                float2 ghp = cs + (cs - fp) * f;
                float ghd = length(gp - ghp) - (9. + fi * 11.);
                float disc = (1. - smoothstep(-2.5, 2.5, ghd)) * .35 + exp(-sq(ghd / 1.6)) * .5;
                ghosts += pal(.2 + fi * .3 + t) * disc * (.07 - fi * .015);
              }
              outer = (acc * haloP(d, uSpread, .5, .4) * .4 * br + lens * (hot * .9 + streak * .55) * fl + ghosts * fl) * gn;
              rim = (mix(uCore, tint, .45) * exp(-abs(gd) / (.75 + gaa * .5)) * (.35 + .4 * br) + uCore * exp(-fr / 40.) * exp(-abs(gd) / .8) * .8 * fl) * gn;
              inner = (acc * exp(di / 8.) * .14 * br + lens * (hot * .4 + streak * .3) * fl) * gn;
            }
            """,
    };
}
