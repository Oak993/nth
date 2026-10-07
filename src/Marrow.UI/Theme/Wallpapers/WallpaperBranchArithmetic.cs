// Copyright 2016 The SwiftShader Authors. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Text.RegularExpressions;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>ED-399: reference highp evaluation at authored hard branches.</summary>
internal static class WallpaperBranchArithmetic
{
    // SwiftShader ShaderCore.cpp, Apache-2.0 (see tools/wallpaper-fidelity/BRANCH-PARITY.md).
    // The pinned reference uses legacy log2/exp2 and separate fp32 mul/add.
    // These helpers cover the finite positive radii and nonnegative powers used
    // by the two walls. Keep ordinary operations separate; mix() would fuse.
    private const string Log = """
        float wl_ref_log2(float x) {
            // ES2 RuntimeEffect lacks bit casts. These exact powers-of-two
            // scalings recover the same binary32 exponent and [1,2) mantissa.
            float exponent = 0.;
            float m = x;
            if (m < 5.421010862427522e-20) { m *= 1.8446744073709552e19; exponent -= 64.; }
            if (m < 2.3283064365386963e-10) { m *= 4294967296.; exponent -= 32.; }
            if (m < .0000152587890625) { m *= 65536.; exponent -= 16.; }
            if (m < .00390625) { m *= 256.; exponent -= 8.; }
            if (m < .0625) { m *= 16.; exponent -= 4.; }
            if (m < .25) { m *= 4.; exponent -= 2.; }
            if (m < .5) { m *= 2.; exponent -= 1.; }
            if (m < 1.) { m *= 2.; exponent -= 1.; }
            if (m >= 1.8446744073709552e19) { m *= 5.421010862427522e-20; exponent += 64.; }
            if (m >= 4294967296.) { m *= 2.3283064365386963e-10; exponent += 32.; }
            if (m >= 65536.) { m *= .0000152587890625; exponent += 16.; }
            if (m >= 256.) { m *= .00390625; exponent += 8.; }
            if (m >= 16.) { m *= .0625; exponent += 4.; }
            if (m >= 4.) { m *= .25; exponent += 2.; }
            if (m >= 2.) { m *= .5; exponent += 1.; }
            float numerator = (0.095428179*m + 0.47779095)*m + 0.19782813;
            float denominator = ((0.016618466*m + 0.20350508)*m + 0.27382900)*m + 0.040496687;
            return exponent + (m - 1.)*(numerator/denominator);
        }
        float wl_ref_log(float x) { return 0.693147181*wl_ref_log2(x); }

        """;

    private const string Power = """
        float wl_ref_exp2(float x) {
            x = clamp(x, -126.999992, 128.);
            // SwiftShader RoundInt rounds ties to even, independently of the GPU's round().
            float base = floor(x - .5);
            float remainder = (x - .5) - base;
            int i = int(base);
            if (remainder > .5 || (remainder == .5 && mod(base, 2.) != 0.)) i += 1;
            // Reconstruct 2^i exactly, without Skia's approximate exp2().
            float scale = 1.;
            float n = abs(float(i));
            if (n >= 64.) { scale *= i < 0 ? 5.421010862427522e-20 : 1.8446744073709552e19; n -= 64.; }
            if (n >= 32.) { scale *= i < 0 ? 2.3283064365386963e-10 : 4294967296.; n -= 32.; }
            if (n >= 16.) { scale *= i < 0 ? .0000152587890625 : 65536.; n -= 16.; }
            if (n >= 8.) { scale *= i < 0 ? .00390625 : 256.; n -= 8.; }
            if (n >= 4.) { scale *= i < 0 ? .0625 : 16.; n -= 4.; }
            if (n >= 2.) { scale *= i < 0 ? .25 : 4.; n -= 2.; }
            if (n >= 1.) { scale *= i < 0 ? .5 : 2.; }
            float f = x - float(i);
            float p = 0.00187757669;
            p = p*f + 0.00898933969;
            p = p*f + 0.0558263175;
            p = p*f + 0.240153611;
            p = p*f + 0.693153083;
            p = p*f + 1.;
            return scale*p;
        }
        float wl_ref_pow(float x, float y) {
            if (x == 0. || x == 1.) return x;
            return wl_ref_exp2(wl_ref_log2(x)*y);
        }

        """;

    private const string Angle = """
        float wl_ref_atan01(float x) {
            float x2 = x*x;
            float p = -0.0161657367 + x2*0.0028662257;
            p = 0.0429096138 + x2*p;
            p = -0.0752896400 + x2*p;
            p = 0.1065626393 + x2*p;
            p = -0.1420889944 + x2*p;
            p = 0.1999355085 + x2*p;
            p = -0.3333314528 + x2*p;
            return x + x*(x2*p);
        }
        float wl_ref_atan2(float y, float x) {
            bool lower = y < 0.;
            float theta = lower ? -3.14159265 : 0.;
            float x0 = lower ? -x : x;
            float y0 = abs(y);
            bool left = x0 < 0.;
            theta += left ? 1.57079632 : 0.;
            float x1 = left ? y0 : x0;
            float y1 = left ? -x0 : y0;
            bool flip = y1 >= x1;
            float x2 = flip ? y1 : x1;
            float y2 = flip ? x1 : y1;
            if (x2 == 0.) return theta;
            float a = wl_ref_atan01(y2/x2);
            theta += flip ? 1.57079632 - a : a;
            return lower && left && flip ? -a : theta;
        }

        """;

    public static string Helpers(string id) => id switch
    {
        "tunnel" => Log + Power + Angle,
        "stairwell" => Log + Angle,
        _ => "",
    };

    public static string Translate(string id, string source)
    {
        if (id is not ("tunnel" or "stairwell")) return source;
        source = Regex.Replace(source, @"\batan\s*\(", "wl_ref_atan2(");
        return id == "tunnel"
            ? Regex.Replace(source, @"\bpow\s*\(\s*t\s*,\s*\.85\s*\)", "wl_ref_pow(t, .85)")
            : Regex.Replace(source, @"\blog\s*\(", "wl_ref_log(");
    }
}
