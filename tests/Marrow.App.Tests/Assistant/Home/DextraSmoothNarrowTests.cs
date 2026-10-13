using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Assistant.Home;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-3")]
public sealed class DextraSmoothNarrowTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(800, 1200)]
    [InlineData(500, 1400)]
    public void A_tall_stacked_home_gives_spare_room_to_the_conversation(double width, double height) => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var jobs = new FakeMissionJobs();
        var home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls())
        {
            Conversation = () => new Border(), ConversationEmpty = () => false,
        });
        using var stage = new HomeStage(home, width, height);
        Assert.True(home.Stacked);
        var convo = stage.Box(home.ConversationColumn);
        var board = stage.Box(home.BoardColumn);
        Assert.True(convo.Height > 420, $"conversation={convo.Height}, board bottom={board.Bottom}, window={height}");
        Assert.InRange(board.Bottom, height - 14, height);
        Assert.True(board.Top >= convo.Bottom);
    });

    [Theory]
    [InlineData(800, 700)]
    [InlineData(500, 550)]
    public void A_short_stack_scrolls_to_every_mission_and_resize_keeps_the_order(double width, double height) => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var jobs = new FakeMissionJobs();
        jobs.Set([.. Enumerable.Range(0, 12).Select(i => HomeSamples.Job("Long mission title " + i, MissionState.Working))]);
        var home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls()));
        using var stage = new HomeStage(home, width, height, Avalonia.Media.FlowDirection.RightToLeft);
        var scroll = home.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height + 1);
        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height); stage.Layout();
        Assert.InRange(home.Board.Missions.AllCards.Max(c => stage.Box(c).Bottom), 1, height);
        stage.Resize(1100, 900); Assert.False(home.Stacked);
        stage.Resize(width, height); Assert.True(home.Stacked);
        Assert.True(stage.Box(home.BoardColumn).Top >= stage.Box(home.ConversationColumn).Bottom);
    });
}
