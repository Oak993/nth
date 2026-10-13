using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 16 "text contrast 4.5:1"; ED-NEW-dextra-as17-8): every line of text in the org chart, its detail and the sidebar's Astur rows reads at 4.5:1 or better against what it sits on, in both
// themes, on the window and on the lightest card surface of each theme; the status chip of what needs the owner is the ink with the inverse on it.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-8")]
public sealed class OrgChartContrastTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color FillOf(Control control) => Solid(Assert.IsType<Border>(control).Background);

    private static double Contrast(IBrush? text, Color under) => AccentPalette.Contrast(Over(Solid(text), under), under);

    private static Color[] Surfaces(string theme) => theme == "Dark" ? [AccentPalette.DarkReference, AccentPalette.DarkCard] : [AccentPalette.LightReference, AccentPalette.LightCard];

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_small_line_of_a_node_and_its_chips_reads_at_4_5_to_1_or_better(string theme)
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var jobs = new[] { Task("a", "Next video: Why lenses fog up", MissionState.Working, 30, HomeSamples.Head("Reader", HeadState.Working)), Task("s", "Weekly update", MissionState.NeedsYou) };
            var chart = new OrgChart();
            chart.Show(TeamSnapshot.Build(Team(ChannelA, Store), jobs, [Card("s", "Send")]));
            using var stage = new HomeStage(chart, 375, 700);
            stage.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            stage.Layout();

            foreach (var surface in Surfaces(theme))
            foreach (var node in chart.All)
            {
                var card = Nodes<Border>(node).First(b => b.BorderThickness == new Thickness(1) && b.Background is not null);
                var under = Over(Solid(card.Background), surface);
                Assert.True(Contrast(node.NameText.Foreground, under) >= 4.5, $"{theme} name of {node.Member!.Name} on {surface}");
                Assert.True(Contrast(node.WorkText.Foreground, under) >= 4.5, $"{theme} work line of {node.Member!.Name} on {surface}: {Contrast(node.WorkText.Foreground, under):0.00}");
                var chipUnder = Over(FillOf(node.StatusChip), under);
                Assert.True(Contrast(node.StatusText.Foreground, chipUnder) >= 4.5, $"{theme} status chip of {node.Member!.Name} on {surface}: {Contrast(node.StatusText.Foreground, chipUnder):0.00}");
                foreach (var button in node.ChipButtons)
                {
                    var face = Nodes<Border>(button).First(b => b.Background is not null);
                    var under2 = Over(Solid(face.Background), under);
                    foreach (var text in Nodes<TextBlock>(button))
                        Assert.True(Contrast(text.Foreground, under2) >= 4.5, $"{theme} chip '{text.Text}' on {surface}: {Contrast(text.Foreground, under2):0.00}");
                }
            }
        });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_details_labels_values_and_buttons_read_at_4_5_to_1_or_better(string theme)
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA), [Task("a", "Voiceover", MissionState.Working)], []) };
            var panel = new TeamPanel(source, new TeamActions { Add = () => { }, Talk = _ => { }, Edit = _ => { }, Retire = _ => System.Threading.Tasks.Task.FromResult<string?>(null) });
            using var stage = new HomeStage(panel, 700, 900);
            stage.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            stage.Layout();
            Click(panel.Chart.Kids[0].Face);
            stage.Layout();

            var card = Assert.IsType<Border>(panel.Detail.Child);
            foreach (var surface in Surfaces(theme))
            {
                var under = Over(Solid(card.Background), surface);
                foreach (var text in Nodes<TextBlock>(card).Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.GetSelfAndLogicalAncestors().OfType<DetailButton>().FirstOrDefault() is null))
                    Assert.True(Contrast(text.Foreground, under) >= 4.5, $"{theme} '{text.Text}' on {surface}: {Contrast(text.Foreground, under):0.00}");
                foreach (var button in new[] { panel.Detail.Pause, panel.Detail.Settings, panel.Detail.Retire, panel.Detail.Talk, panel.Add })
                {
                    var face = Nodes<Border>(button).First(b => b.Background is not null);
                    var label = Nodes<TextBlock>(button).Single();
                    var faceColour = face.Background is ISolidColorBrush ? Solid(face.Background) : Colors.Transparent;
                    var under2 = Over(faceColour, under);
                    Assert.True(Contrast(label.Foreground, under2) >= 4.5, $"{theme} button '{label.Text}' on {surface}: {Contrast(label.Foreground, under2):0.00}");
                }
            }
        });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_sidebar_rows_names_lines_and_badge_read_at_4_5_to_1_or_better(string theme)
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var tree = new AsturTree(new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA, Store), [Task("a", "Voiceover", MissionState.Working), Task("s", "Send", MissionState.NeedsYou)], [Card("s", "Send")]) });
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);
            stage.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            stage.Layout();

            foreach (var surface in Surfaces(theme))
            {
                Assert.True(Contrast(Nodes<TextBlock>(tree.Toggle).First().Foreground, surface) >= 4.5, $"{theme} group label on {surface}");
                foreach (var row in tree.Rows)
                {
                    var texts = Nodes<TextBlock>(row.Button).Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.IsEffectivelyVisible).ToList();
                    foreach (var text in texts.Where(t => t.Text != row.BadgeCount))
                        Assert.True(Contrast(text.Foreground, surface) >= 4.5, $"{theme} '{text.Text}' on {surface}: {Contrast(text.Foreground, surface):0.00}");
                }
                var badge = tree.Rows[1].Badge;
                var badgeText = Nodes<TextBlock>(badge).Single();
                Assert.True(Contrast(badgeText.Foreground, Solid(badge.Background)) >= 4.5, $"{theme} badge on {surface}");
            }
        });
    }
}
