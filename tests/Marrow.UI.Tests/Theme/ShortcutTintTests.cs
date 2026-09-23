using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.UI.Tests.Theme;

/// <summary>Tests for <see cref="ShortcutTint"/> (DroppyCode/UI/Theme/ShortcutTint.swift, B2 1fbf81e).</summary>
public sealed class ShortcutTintTests
{
    /// <summary>
    /// Threads section is tinted with fixed blue Color(red: 0.040, green: 0.478, blue: 1.000),
    /// from Chrome.blue, not theme-derived.
    /// </summary>
    [Trait("Req", "PR-THEME-043")] // DroppyCode/UI/Theme/ShortcutTint.swift:1-12
    [Trait("Req", "UI-THEME-048")]
    [Fact]
    public void ThreadsSectionTintIsChromeBluE()
    {
        var tint = AppShortcutSection.Threads.Tint();
        var expected = Chrome.Blue;

        Assert.Equal(expected.Kind, tint.Kind);
        Assert.Equal(expected.Red, tint.Red, precision: 6);
        Assert.Equal(expected.Green, tint.Green, precision: 6);
        Assert.Equal(expected.Blue, tint.Blue, precision: 6);
        Assert.Equal(expected.Opacity, tint.Opacity, precision: 6);
    }

    /// <summary>
    /// Window section is tinted with fixed purple Color(red: 0.686, green: 0.322, blue: 0.871),
    /// approximately #AF52DE, not theme-derived.
    /// </summary>
    [Trait("Req", "PR-THEME-043")] // DroppyCode/UI/Theme/ShortcutTint.swift:1-12
    [Trait("Req", "UI-THEME-048")]
    [Fact]
    public void WindowSectionTintIsPurple()
    {
        var tint = AppShortcutSection.Window.Tint();
        var expected = new SwiftColor(red: 0.686, green: 0.322, blue: 0.871);

        Assert.Equal(expected.Red, tint.Red, precision: 6);
        Assert.Equal(expected.Green, tint.Green, precision: 6);
        Assert.Equal(expected.Blue, tint.Blue, precision: 6);
        Assert.Equal(expected.Opacity, tint.Opacity, precision: 6);
    }
}
