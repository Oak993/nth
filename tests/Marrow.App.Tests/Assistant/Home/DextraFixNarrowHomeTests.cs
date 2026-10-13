using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Assistant.Home;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-fix-8")]
public sealed class DextraFixNarrowHomeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(800, 700)]
    [InlineData(500, 550)]
    public void Every_mission_is_reachable_in_a_short_stacked_home(double width, double height)
    {
        headless.OnUiThread(() =>
        {
            using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var jobs = new FakeMissionJobs();
            jobs.Set([.. Enumerable.Range(0, 12).Select(i => HomeSamples.Job("Long mission title " + i, MissionState.Working))]);
            var home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(home, width, height, Avalonia.Media.FlowDirection.RightToLeft);
            Assert.True(home.Stacked);
            Assert.True(stage.Box(home.BoardColumn).Top >= stage.Box(home.ConversationColumn).Bottom);
            var scroller = home.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Extent.Height > s.Viewport.Height + 1);
            Assert.NotNull(scroller);
            scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
            stage.Layout();
            var last = home.Board.Missions.AllCards.OrderBy(c => stage.Box(c).Bottom).Last();
            var box = stage.Box(last);
            Assert.InRange(box.Bottom, 1, height);
        });
    }
}
