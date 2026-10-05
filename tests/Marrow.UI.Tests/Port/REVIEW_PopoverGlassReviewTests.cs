using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Styling;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// Independent review of unit/glass-g2 (da391c33d). Both facts are RED on that commit.
public sealed class REVIEW_PopoverGlassReviewTests
{
    private static double Luminance(double v)
    {
        var c = v / 255;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Contrast(double a, double b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    // PopoverGlassCardTests.The_glass_keeps_its_text_readable_over_any_backdrop models the Dark label as opaque white; the app's
    // label is SwiftColor.LabelColor, white at 0.85 in Dark (black at 0.85 in Light, which that test does model).
    [Fact]
    public void REVIEW_the_real_dark_label_keeps_4_5_to_1_over_a_white_desktop()
    {
        var tint = PopoverHost.Tint.Resolve(ThemeVariant.Dark);
        var t = tint.A / 255.0;
        var glass = Math.Min((int)255, (int)PopoverGlass.DarkCap) * (1 - t) + 255 * t;
        var label = SwiftColor.LabelColor.Resolve(ThemeVariant.Dark);
        var a = label.A / 255.0;
        var text = label.R * a + glass * (1 - a);
        Assert.True(Contrast(text, glass) >= 4.5, $"Dark over a white desktop: glass {glass:F1}, label {text:F1}, {Contrast(text, glass):F2}:1");
    }

    // PopoverGlass.Update, SetDark and Dispose catch a fixed list of exception types and the file note promises that a missing
    // member ends in Failure (the opaque panel). Api.Call and Api.Get raise MissingMethodException / MissingMemberException for a
    // method or property that is not there, which no catch lists: it would escape into the layout pass and crash the app.
    [Fact]
    public void REVIEW_a_missing_composition_method_raises_something_the_glass_catches()
    {
        var api = typeof(PopoverGlass).GetNestedType("Api", BindingFlags.NonPublic)!;
        var call = api.GetMethod("Call", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = Record.Exception(() => call.Invoke(null, [new object(), typeof(string), "NoSuchMethodInAvalonia13", Array.Empty<object?>()]));
        var inner = (error as TargetInvocationException)?.InnerException ?? error;
        Assert.True(inner is TargetInvocationException or COMException or InvalidCastException or ArgumentException or NullReferenceException or InvalidOperationException,
            $"{inner?.GetType().Name} is not caught by PopoverGlass.Update (PopoverGlass.cs:104), so a renamed Avalonia method crashes instead of falling back");
    }
}
