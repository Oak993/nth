using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-4.3, CZ-10.4: the preview's working dots are the real ComposerWorkingDots in the colour the workingDots setting gives the preview's maker, shown in Running.
[Collection("Headless")]
public sealed class ComposerPreviewWorkingDotsTests(HeadlessSession headless)
{
    private static Border Stage(ComposerPreview preview) => new() { Width = 496, Height = 212, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = preview };

    private static ComposerWorkingDots Dots(ComposerPreview preview) =>
        LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(preview).Single();

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.4: in Running the preview's dots wear the setting's colour for the preview maker, and follow the setting and the maker switch
    public void The_preview_dots_wear_the_settings_colour_for_the_preview_maker_and_follow_both() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            session.Pick(PreviewState.Running);
            Assert.Same(WorkingDotsColor.Of(WorkingDotsSettings.Default, "Claude"), Dots(preview).Tint);
            var model = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model };
            preview.WorkingDots = model;
            Assert.Same(WorkingDotsColor.Of(model, "Claude"), Dots(preview).Tint);
            session.Pick("OpenAI");
            Assert.Same(WorkingDotsColor.Of(model, "OpenAI"), Dots(preview).Tint);
            var custom = new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "coral" };
            preview.WorkingDots = custom;
            Assert.Same(WorkingDotsColor.Of(custom, "OpenAI"), Dots(preview).Tint);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.4: dots that arrive when Running is picked, after the setting changed at rest, wear the colour chosen now
    public void Dots_that_arrive_later_wear_the_colour_chosen_now() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        var custom = new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "teal" };
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            Assert.False(preview.DotsShown);
            preview.WorkingDots = custom;
            session.Pick(PreviewState.Running);
            Assert.Same(WorkingDotsColor.Of(custom, "Claude"), Dots(preview).Tint);
        });
    });
}
