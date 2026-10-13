using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 3.3 "Mission card", 12.6b, 16 "text contrast 4.5:1", "narrow titles"; the canvas's mCard and .mc CSS;
// ED-NEW-dextra-as8-4): a card keeps the row's fixed slots, takes two title lines below 520 px, drops its slots when Done, dims its
// mark, track and chips (never its text below 4.5:1), and offers Open, Redirect, Review and its activity log.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-4")]
public sealed class MissionCardTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static (MissionCard Card, FakeMissionJobs Jobs) Make(MissionJob job, TaskActions? actions = null)
    {
        var jobs = new FakeMissionJobs();
        jobs.Set(job);
        MissionCard? made = null;
        var card = new MissionCard(jobs, () => made?.Show(jobs.Jobs[0], Now), actions);
        made = card;
        card.Show(job, Now);
        return (card, jobs);
    }

    [Fact]
    public void Below_520_px_a_title_takes_two_lines_and_the_label_becomes_its_glyph()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            const string Long = "Compare the espresso machines under $800 and write up the three best with their running costs";
            var (wide, _) = Make(Job(Long, MissionState.Working));
            var (narrow, _) = Make(Job(Long, MissionState.Working));
            narrow.Narrow = true;
            using var stage = new HomeStage(new StackPanel { Width = 375, Children = { wide, narrow } }, 400);

            Assert.Equal(TextWrapping.NoWrap, wide.Title.TextWrapping);
            Assert.Equal(TaskMetrics.LabelWidth, wide.Label.Bounds.Width);
            Assert.Equal(TextWrapping.Wrap, narrow.Title.TextWrapping);
            Assert.Equal(2, narrow.Title.MaxLines);
            Assert.Equal(TaskMetrics.NarrowLabelWidth, narrow.Label.Bounds.Width);
            Assert.True(narrow.Title.Bounds.Height > wide.Title.Bounds.Height * 1.5, $"{narrow.Title.Bounds.Height} vs {wide.Title.Bounds.Height}");
            Assert.True(narrow.Title.Bounds.Height < wide.Title.Bounds.Height * 2.5);
            Assert.Equal("Working", AutomationProperties.GetName(narrow.Label)); // the word stays its name
        });
    }

    [Fact]
    public void A_Done_card_drops_its_two_empty_slots()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var task = Job("Tidy the README", MissionState.Working);
            var (card, jobs) = Make(task);
            using var stage = new HomeStage(card, 600);
            Assert.True(card.Slots.IsVisible);

            jobs.Replace(Moved(task, MissionState.Done));
            card.Show(jobs.Jobs[0], Now);
            stage.Layout();
            Assert.False(card.Slots.IsVisible);
            Assert.Equal("finished", card.Agents.Text);
        });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_detail_and_meta_lines_read_at_4_5_to_1_or_better_in_both_themes(string theme)
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (card, _) = Make(Job("Fix the login timeout", MissionState.Working));
            using var stage = new HomeStage(card, 600);
            stage.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            stage.Layout();

            var fill = Solid(((Border)card.Child!).Background);
            var surfaces = theme == "Dark" ? new[] { AccentPalette.DarkReference, AccentPalette.DarkCard } : new[] { AccentPalette.LightReference, AccentPalette.LightCard };
            foreach (var surface in surfaces)
            {
                var under = Over(fill, surface);
                foreach (var ink in new[] { card.Activity.Ink, card.Elapsed.Ink, card.Agents.Ink, card.Spend.Ink })
                {
                    var contrast = AccentPalette.Contrast(Over(Solid(ink), under), under);
                    Assert.True(contrast >= 4.5, $"{theme} on {surface}: {contrast:0.00}:1");
                }
            }
        });
    }

    [Theory]
    [InlineData(MissionState.Paused)]
    [InlineData(MissionState.Stopping)]
    [InlineData(MissionState.Stopped)]
    public void A_dimmed_card_dims_its_mark_track_and_chips_never_its_text(MissionState state)
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var task = Moved(Job("Espresso machines under $800", MissionState.Working, 5, Head("Scout", HeadState.Working)), state);
            var (card, _) = Make(task);
            using var stage = new HomeStage(card, 600);
            stage.Window.RequestedThemeVariant = ThemeVariant.Light;
            stage.Layout();

            Assert.True(task.Dimmed);
            Assert.Equal(1, card.Opacity);
            Assert.Equal(.5, card.Mark.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(card.Mark.Opacity));
            Assert.Equal(.5, card.Track.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(card.Track.Opacity));
            foreach (var text in new Control[] { card.Title, card.Activity, card.Elapsed, card.Agents, card.Spend, card.Label })
                Assert.Equal(1, text.Opacity);
            var fill = Solid(((Border)card.Child!).Background);
            var under = Over(fill, AccentPalette.LightCard);
            Assert.True(AccentPalette.Contrast(Over(Solid(card.Title.Foreground), under), under) >= 4.5);
        });
    }

    [Fact]
    public void Its_actions_open_the_lead_chat_and_its_activity_log()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var task = Job("Fix the login timeout", MissionState.Working);
            MissionJob? opened = null, traced = null;
            var (card, _) = Make(task, new TaskActions(Open: j => opened = j, Lineage: j => traced = j));
            using var stage = new HomeStage(card, 600);

            Assert.NotNull(card.OpenButton);
            Assert.Null(card.RedirectButton);
            Assert.Null(card.ReviewButton);
            var lineage = Assert.IsType<Button>(card.LineageButton);
            Assert.Equal("Show its activity", AutomationProperties.GetName(lineage));
            Assert.Equal("Every step it took, and who allowed it", ToolTip.GetTip(lineage));

            Click(card.OpenButton!);
            Click(lineage);
            Assert.Equal(task.Id, opened?.Id);
            Assert.Equal(task.Id, traced?.Id);

            var (bare, _) = Make(task);
            Assert.Null(bare.LineageButton);
            Assert.False(bare.ActionsBox.IsVisible);
        });
    }
}
