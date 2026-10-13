using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.NthUi;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.NthUi;

[Trait("Decision", "ED-NEW-web-review-16")]
public sealed class WebReviewAccentTests
{
    [Fact]
    public void Live_answer_accent_reads_are_allocation_free_and_still_resolve_the_live_accent()
    {
        using var accent = SwiftColor.AccentSource.Override(static () => Color.FromRgb(0, 120, 215));
        for (var i = 0; i < 10; i++) _ = NthUiTokens.Accent;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) _ = NthUiTokens.Accent;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var token = NthUiTokens.Accent;
        var first = token.Resolve(ThemeVariant.Light);
        using var changed = SwiftColor.AccentSource.Override(static () => Color.FromRgb(215, 0, 0));
        Assert.NotEqual(first, token.Resolve(ThemeVariant.Light));
        Assert.Equal(HighContrastPalette.Black.Highlight, token.Resolve(ThemeVariant.Light, HighContrastPalette.Black));
        Assert.Equal(0, allocated);
    }
}
