using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
public sealed class GapComposerPlateStructureTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T45")]
    public void The_plate_is_a_Border_and_the_margin_attachment_stays_outside_its_clip(bool glassPanel) => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { Attachment = VoiceAttachment.Outside });
        var (view, window) = rig.Open(600);
        try
        {
            rig.Main.OnMain(() => RenderBudget.SetIsOnGlassPanel(Host(window), glassPanel));
            rig.PumpWindow(window);
            rig.Main.OnMain(() =>
            {
                var plate = Assert.IsType<Border>(view.Child);
                Assert.Equal(view.Bounds.Size, plate.Bounds.Size);
                Assert.Equal(glassPanel, plate.ClipToBounds);
                var attach = One<ComposerAttachButton>(view);
                Assert.Equal(-44, Frame(attach, view).X, .5);
                // The panel may clip the plate's contents, but never the margin button.
                Assert.DoesNotContain(plate, attach.GetVisualAncestors());
                Assert.Contains(view, attach.GetVisualAncestors());
            });
        }
        finally { rig.Close(window); }
    });
}
