using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;

namespace Marrow.UI.Tests.Theme;

/// <summary>Tests for <see cref="ShortcutTint"/> (DroppyCode/UI/Theme/ShortcutTint.swift, B2 1fbf81e).</summary>
public sealed class ShortcutTintTests
{
    /// <summary>
    /// Threads section is tinted with fixed blue Color(red: 0.040, green: 0.478, blue: 1.000),
    /// from Chrome.blue, not theme-derived (:8).
    /// </summary>
    [Trait("Req", "PR-THEME-043")] // DroppyCode/UI/Theme/ShortcutTint.swift:1-12
    [Trait("Req", "UI-THEME-048")]
    [Fact]
    public void ThreadsSectionTintIsChromeBluE()
    {
        var tint = AppShortcutSection.Threads.Tint();
        var expected = Marrow.UI.Chrome.Chrome.Blue;

        // Both should be the same instance (Chrome.Blue is a singleton)
        Assert.Same(expected, tint);
    }

    /// <summary>
    /// Window section is tinted with fixed purple Color(red: 0.686, green: 0.322, blue: 0.871),
    /// approximately #AF52DE, not theme-derived (:9).
    /// </summary>
    [Trait("Req", "PR-THEME-043")] // DroppyCode/UI/Theme/ShortcutTint.swift:1-12
    [Trait("Req", "UI-THEME-048")]
    [Fact]
    public void WindowSectionTintIsPurple()
    {
        var tint = AppShortcutSection.Window.Tint();
        var expected = new SwiftColor(red: 0.686, green: 0.322, blue: 0.871);

        // Resolve both to Avalonia colors in light theme and compare
        // The components should match within floating-point precision
        var tintColor = tint.Resolve(ThemeVariant.Light);
        var expectedColor = expected.Resolve(ThemeVariant.Light);

        Assert.Equal(expectedColor.R, tintColor.R);
        Assert.Equal(expectedColor.G, tintColor.G);
        Assert.Equal(expectedColor.B, tintColor.B);
        Assert.Equal(expectedColor.A, tintColor.A);
    }
}
