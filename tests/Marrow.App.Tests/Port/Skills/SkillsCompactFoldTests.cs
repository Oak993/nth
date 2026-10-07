using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class SkillsCompactFoldTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // CSS:111-113: .34s --ez height and .26s CSS ease opacity.
    public void State_folds_have_independent_locked_height_and_opacity() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var fold = new SkillsCompactFold(new Border { Width = 100, Height = 40 });
        h.Root.Children.Add(fold); h.Settle();
        fold.Show(true, animate: true);
        Assert.Equal(0, fold.Opacity);
        Assert.Equal(0, fold.Fraction);
        h.Step(130);
        Assert.Equal(.802403387584857, fold.Opacity, 6);
        Assert.Equal(.909329294681186, fold.Fraction, 6);
        h.Step(130);
        Assert.Equal(1, fold.Opacity);
        Assert.True(fold.Fraction < 1);
        h.Step(80);
        Assert.Equal(1, fold.Fraction);
        fold.Show(false, animate: true);
        h.Step(130);
        Assert.Equal(1 - .802403387584857, fold.Opacity, 6);
        Assert.False(fold.IsEnabled);
        h.Step(210);
        Assert.Equal(0, fold.Fraction);
    }, motion: true);
}
