using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// REVIEW (CZ-3): the preview's send glyph ink follows the enabled state as the real SendDraftState's `ink` does (Apply: ink.Set(White.Opacity(dim))).
[Collection("Headless")]
public sealed class ReviewCz3Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")] // REVIEW of CZ-3: customization-SPEC CZ-4.3, the real SendDraftState ink
    public void REVIEW_the_send_arrow_is_full_white_once_Typing_enables_the_disc() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(new Border { Width = 496, Height = 212, Child = preview }, (window, _) =>
        {
            window.UpdateLayout();
            double Ink() => ((preview.Send.GetVisualDescendants().OfType<SwiftImage>().First().Foreground as ISolidColorBrush)!.Color.A) / 255.0;
            var rest = Ink();
            session.Pick(PreviewState.Typing);
            window.UpdateLayout();
            var typing = Ink();
            Assert.True(typing > rest + .3, $"rest ink {rest:F2}, typing ink {typing:F2}: the arrow stays dimmed on the enabled disc");
        });
    });
}
