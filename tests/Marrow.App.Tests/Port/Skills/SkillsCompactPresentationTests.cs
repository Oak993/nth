using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class SkillsCompactPresentationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // sd-page.js:367, sd-page.css:483-485,489: visible completion icon and actual 26px enlarge face.
    public void The_compact_done_body_has_its_check_and_the_enlarge_face_uses_the_locked_size() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        h.Controller.OpenDownloadAll(); h.Wait(h.Controller.Continue());
        var compact = new SkillsProgressPopover(h.Page.Model!, () => { });
        h.Root.Children.Add(compact); h.Settle();
        Assert.Equal("done", compact.PhaseKey);
        Assert.Contains(compact.GetVisualDescendants().OfType<SwiftImage>(), i => i.SymbolName == "checkmark.circle.fill");
        var face = Assert.IsType<Border>(compact.Enlarge.Content);
        Assert.Equal(26, face.Width);
        Assert.Equal(26, face.Height);
        Assert.NotNull(face.Background);
    });
}
