using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrMergeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("Hydra merged !7.\n HTTPS://example.org/a \n details ", "Hydra merged !7", "HTTPS://example.org/a \n details", "HTTPS://example.org/a", "details")]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://example.org/a", "Hydra opened !7 but could not merge it", "https://example.org/a", "https://example.org/a", "")]
    [InlineData("Hydra did not merge..\n  refused\nsecond line \n", "Hydra did not merge.", "refused\nsecond line", null, "refused\nsecond line")]
    [InlineData("Hydra did not merge.\nhttps://example.org/a trailing words", "Hydra did not merge", "https://example.org/a trailing words", null, "https://example.org/a trailing words")]
    [InlineData("Hydra did not merge.\nftp://example.org/a", "Hydra did not merge", "ftp://example.org/a", null, "ftp://example.org/a")]
    [InlineData("No body.", "No body", "", null, "")]
    [InlineData("", "Hydra", "", null, "")]
    [InlineData("\n\nHydra merged.\n body", "Hydra merged", "body", null, "body")]
    [Trait("Req", "PR-HYDRAMERGE-088")] // App/Views/Chat/TimelineRows.swift:802-808, :332-334, :376-387
    public void Outcome_splits_once_drops_one_stop_trims_the_body_and_separates_only_a_clean_http_first_line(
        string text, string title, string body, string? link, string details)
    {
        // Every expected field follows the quoted split(maxSplits:1, omittingEmptySubsequences:true) and helpers.
        var outcome = new HydraMergeOutcome(new UserMessage { Text = text });
        Assert.Equal(title, outcome.Title);
        Assert.Equal(body, outcome.Body);
        Assert.Equal(link, outcome.Link?.AbsoluteString);
        Assert.Equal(details, outcome.Details);
    }

    [Theory]
    [InlineData("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7")]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://gitlab.com/g/p/-/merge_requests/7")]
    [InlineData("Hydra did not merge.")]
    [Trait("Req", "PR-HYDRAMERGE-088")] // App/Views/Chat/TimelineRows.swift:1099-1126
    [Trait("Req", "PR-HYDRAMERGE-105")] // App/Views/Chat/TimelineRows.swift:1099-1126, :1148-1152
    public void Empty_details_disable_the_title_remove_the_chevron_and_leave_only_the_independent_browser_link(string text)
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presenter = new TimelineRowsPrPopupPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var opened = new List<string>();
            using var browser = Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
            Avalonia.Controls.Window? window = null;
            try
            {
                h.Main.OnMain(() =>
                {
                    var row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = text }));
                    window = Show(row);
                    var buttons = LogicalQuery.SelfAndDescendants<Button>(row).ToArray();
                    var title = buttons[0];
                    Assert.False(title.IsEnabled);
                    Assert.Equal("", ToolTip.GetTip(title)); // Swift .help(outcome.details.isEmpty ? "" : "Show the message")
                    Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(title), image => image.SymbolName == "chevron.right");
                    Click(window, title);
                    Assert.DoesNotContain(presenter.Calls, call => call.Presented);
                    var outcome = new HydraMergeOutcome(new UserMessage { Text = text });
                    if (outcome.Link is { } link)
                    {
                        Assert.Equal(2, buttons.Length);
                        Assert.Same(title.Parent, buttons[1].Parent);
                        Assert.Equal("Open in the browser", ToolTip.GetTip(buttons[1]));
                        Click(window, buttons[1]);
                        Assert.Equal([link.AbsoluteString], opened);
                    }
                    else { Assert.Single(buttons); Assert.Empty(opened); }
                });
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-105")] // App/Views/Chat/TimelineRows.swift:966-971, :1095-1128, :1142-1161
    public void Real_pointer_clicks_on_title_track_and_Open_route_to_their_separate_destinations()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presenter = new TimelineRowsPrPopupPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var opened = new List<string>();
            using var browser = Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
            HydraMergeRow? row = null;
            Avalonia.Controls.Window? window = null;
            Button? title = null;
            try
            {
                h.Main.OnMain(() =>
                {
                    row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage
                        { Text = "Hydra did not merge.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThe report." }));
                    window = Show(row);
                    title = LogicalQuery.SelfAndDescendants<Button>(row).Single(button => Equals(ToolTip.GetTip(button), "Show the message"));
                    Assert.True(title.IsEnabled);
                    Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(title), image => image.SymbolName == "chevron.right");
                    var track = LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row).Single();
                    Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(track));
                    Click(window, title);
                });
                h.Main.OnMain(() =>
                {
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.True(popup.IsOpen);
                    Assert.Equal(PlacementMode.Bottom, popup.Placement);
                    Assert.Same(title, popup.PlacementTarget);
                    Assert.NotNull(popup.Child);
                    Click(window!, title!);
                });
                h.Main.OnMain(() =>
                {
                    Assert.False(presenter.Calls.Last().Presented);
                    Click(window!, LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!).Single());
                });
                h.Main.OnMain(() =>
                {
                    Assert.True(presenter.Calls.Last().Presented);
                    var link = LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => Equals(ToolTip.GetTip(button), "Open in the browser"));
                    Assert.Same(title!.Parent, link.Parent);
                    var parent = Assert.IsType<StackPanel>(title.Parent);
                    Assert.Equal(title, parent.Children[0]);
                    Assert.Equal(link, parent.Children[1]);
                    Click(window!, link);
                    Assert.Equal(["https://gitlab.com/g/p/-/merge_requests/7"], opened);
                    // Browser action must not consume the title's action or toggle its report state.
                    Assert.True(Property<bool>(Field(row!, "state")!, "IsShowingReport"));
                });
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }
}
