using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraPanel.swift (B3 52bc792), :396-436 HydraHeadTranscript.body: the head's body with
// `.safeAreaInset(edge: .bottom, spacing: 0)` holding the chat box or the footer, so the body ends where the inset begins.
// Found on a native screenshot run: the panel's chat box drew across the top of the panel, over its header strip.
[Collection("Headless")]
public sealed class HydraHeadTranscriptLayoutTests(HeadlessSession headless)
{
    private const double PanelHeight = 280;

    private void Run(HydraHeadInfo.Kind kind, bool showsDetails, Action<HydraHeadTranscript, Control, Control> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext();
        context.Kind(context.First, kind);
        context.Model.Settings.HydraShowsHeadDetails = showsDetails;
        var transcript = new HydraHeadTranscript(context.Model.Thread(context.First.Id)!, PanelHeight, "/work/project", "Project");
        var host = new Border { Child = transcript, Width = 420, Height = PanelHeight };
        AppEnvironment.SetAppModel(host, context.Model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 420, Height = PanelHeight };
        AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(window));
        try
        {
            window.Show();
            context.Drain();
            for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Control bottom = kind == HydraHeadInfo.Kind.Droppy
                ? transcript.GetVisualDescendants().OfType<ComposerArea>().Single()
                : transcript.GetVisualDescendants().OfType<HydraHeadFooter>().Single();
            Control body = showsDetails
                ? transcript.GetVisualDescendants().OfType<ThreadTimeline>().Single()
                : transcript.GetVisualDescendants().OfType<HydraHeadProgress>().Single();
            test(transcript, bottom, body);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(HydraHeadInfo.Kind.Droppy, false)]
    [InlineData(HydraHeadInfo.Kind.Droppy, true)]
    [InlineData(HydraHeadInfo.Kind.Native, false)]
    [InlineData(HydraHeadInfo.Kind.Native, true)]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:419-432 the chat box (Droppy-run head) or the footer (native head) is the bottom inset of the body
    public void The_chat_box_or_footer_sits_at_the_bottom_edge_and_the_body_ends_where_it_begins(HydraHeadInfo.Kind kind, bool showsDetails) =>
        Run(kind, showsDetails, (transcript, bottom, body) =>
        {
            var bottomTop = bottom.TranslatePoint(default, transcript)!.Value.Y;
            Assert.Equal(PanelHeight, bottomTop + bottom.Bounds.Height, 1);
            Assert.True(bottomTop > PanelHeight / 2, $"the bottom inset starts at {bottomTop} of {PanelHeight}, not at the top");
            var bodyTop = body.TranslatePoint(default, transcript)!.Value.Y;
            Assert.True(bodyTop + body.Bounds.Height <= bottomTop + 0.5, $"the body ends at {bodyTop + body.Bounds.Height}, past the inset's start {bottomTop}");
        });
}
