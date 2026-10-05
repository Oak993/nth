using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (V-13.1): the package's colour palettes `Va` (voice-glow.js:62-226) are carried value for value: 8 variants x dark and light x 7 colours.
public sealed partial class VoiceGlowPaletteTests
{
    [GeneratedRegex(@"(?<name>\w+): \{\s*dark: \[(?<dark>.*?)\],\s*(?://[^\n]*\n\s*)*light: \[(?<light>.*?)\]", RegexOptions.Singleline)]
    private static partial Regex Variant();

    [GeneratedRegex(@"rgb\((\d+), (\d+), (\d+)\)")]
    private static partial Regex Rgb();

    private static string Design([CallerFilePath] string here = "")
    {
        var text = File.ReadAllLines(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "porting", "first-release", "design-src", "customization", "voice-glow.js")));
        return string.Join('\n', text.Skip(61).Take(165));   // lines 62 to 226: the `Va` table
    }

    private static GlowRgb[] Colors(string list) => [.. Rgb().Matches(list).Select(m => new GlowRgb(byte.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), byte.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), byte.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))];

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_palettes_are_the_packages_value_for_value()
    {
        var found = Variant().Matches(Design()).ToList();
        Assert.Equal(8, found.Count);
        Assert.Equal(found.Select(m => m.Groups["name"].Value), VoiceGlowPalettes.Variants);
        foreach (var m in found)
        {
            var name = m.Groups["name"].Value;
            var dark = Colors(m.Groups["dark"].Value);
            var light = Colors(m.Groups["light"].Value);
            Assert.Equal(7, dark.Length);
            Assert.Equal(7, light.Length);
            Assert.Equal(dark, VoiceGlowPalettes.Of(name, dark: true));
            Assert.Equal(light, VoiceGlowPalettes.Of(name, dark: false));
        }
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void An_unknown_variant_is_refused_as_the_package_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VoiceGlowPalettes.Of("rainbow", dark: true));
        Assert.Equal(new GlowRgb(255, 70, 120), VoiceGlowPalettes.Of("colorful", dark: true)[0]);
        Assert.Equal(new GlowRgb(255, 201, 21), VoiceGlowPalettes.Of("colorful", dark: false)[0]);
    }
}
