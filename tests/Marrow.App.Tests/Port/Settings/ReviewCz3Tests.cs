using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// REVIEW (CZ-3): the preview's send glyph ink follows the enabled state as the real SendDraftState's `ink` does (Apply: ink.Set(White.Opacity(dim))).
[Collection("Headless")]
public sealed class ReviewCz3Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-251")] // REVIEW of CZ-3: customization-SPEC CZ-4.3, the real SendDraftState ink
    public void REVIEW_the_send_arrow_is_full_white_once_Typing_enables_the_disc() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        PreviewHost.Show(new Border { Width = 496, Height = 212, Child = preview }, (window, _) =>
        {
            window.UpdateLayout();
            // OD-0136 amendment 3 (nth step 9): the arrow is drawn by the nth send face, which draws its disc and glyph through one layer at the face's Dim alpha
            double Ink() => preview.Send.GetVisualDescendants().OfType<Marrow.UI.Port.Nth.NthSendFace>().First().Dim;
            var rest = Ink();
            session.Pick(PreviewState.Typing);
            runtime.AdvanceMilliseconds(300); frames.Advance(TimeSpan.FromMilliseconds(300)); // the ink moves with the disc's fill, 0.12 s
            window.UpdateLayout();
            var typing = Ink();
            Assert.True(typing > rest + .3, $"rest ink {rest:F2}, typing ink {typing:F2}: the arrow stays dimmed on the enabled disc");
            session.Pick(PreviewState.Rest);
            runtime.AdvanceMilliseconds(300); frames.Advance(TimeSpan.FromMilliseconds(300));
            Assert.True(Ink() < typing - .3, "and it dims again with the disc at Rest");
        }, runtime: runtime, frames: frames);
    });
}
