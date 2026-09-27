using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :190-252,666-671.
[Collection("Headless")]
public sealed class AboutUpdateViewsModifierTests(HeadlessSession headless)
{
    private static UpdateReleaseSectionCards Cards() => new(new UpdateReleaseNotesDigest
        { Sections = new ValueList<UpdateReleaseNotesDigest.Section>([]) }, "1.7.0");

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // App/Views/Settings/AboutUpdateViews.swift:228
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:228
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:228
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:228
    public void Both_release_card_texts_center_their_line_after_scaling_or_trimming() => headless.OnUiThread(() =>
    {
        var cards = Cards();
        var window = new Avalonia.Controls.Window { Content = cards, Width = 180, Height = 200 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        Assert.All(cards.GetVisualDescendants().OfType<TextBlock>(), text => Assert.Equal(TextAlignment.Center, text.TextAlignment));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // App/Views/Settings/AboutUpdateViews.swift:202
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:202
    public void Release_cards_keep_their_natural_vertical_height_even_in_a_tall_parent() => headless.OnUiThread(() =>
    {
        var cards = Cards();
        var window = new Avalonia.Controls.Window { Content = cards, Width = 300, Height = 400 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var natural = cards.DesiredSize.Height; // Plain-button templates exist only after attach; desired is their intrinsic measure.
        Assert.Equal(natural, cards.Bounds.Height);
        window.Close();
    });

    [Theory]
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:660-663
    [Trait("Req", "UI-UPDATE-006")] // App/Views/Settings/AboutUpdateViews.swift:631-635
    [InlineData(false)]
    [InlineData(true)]
    public void The_pill_keeps_its_natural_width_and_exact_capsule_height_in_a_larger_parent(bool installing) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            using var checker = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], UpdateChecker.DefaultFeed));
            var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
            if (installing) progress.Begin();
            var pill = new AboutVersionPill();
            var window = new Avalonia.Controls.Window { Content = pill, Width = 400, Height = 150 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var natural = pill.DesiredSize; // Read the attached template's intrinsic measure, before comparing its arranged bounds.
            Assert.Equal(natural.Width, pill.Bounds.Width);
            Assert.Equal(natural.Height, pill.Bounds.Height);
            window.Close();
        });
    });

    [Theory]
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:666-671
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:671,675 (with ready pointer/standing proofs)
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:671
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:671
    [InlineData(false)]
    [InlineData(true)]
    public void Ready_background_uses_the_0_point_18_second_ease_in_out_unless_motion_is_reduced(bool reduced) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], UpdateChecker.DefaultFeed));
        using var shared = UpdateChecker.OverrideShared(checker);
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        AboutVersionPill pill = null!; Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            pill = new AboutVersionPill(); EnvironmentValues.SetAccessibilityReduceMotion(pill, reduced);
            window = new Avalonia.Controls.Window { Content = pill, Width = 300, Height = 100 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            typeof(UpdateChecker).GetProperty(nameof(UpdateChecker.Update), BindingFlags.Public | BindingFlags.Instance)!.SetValue(checker,
                new AvailableUpdate { Version = "9.0.0", Tag = "v9.0.0", Notes = "",
                    DownloadURL = SwiftURL.Create("http://127.0.0.1:1/artifact")!, PageURL = SwiftURL.Create("https://example.com/release")! });
        });
        runtime.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var face = Assert.Single(pill.GetVisualDescendants().OfType<Border>(), border =>
                border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "Update & restart"));
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(face.Background);
            if (reduced) Assert.InRange(brush.Color.A, (byte)50, (byte)52); // blue .2, no animation.
            else
            {
                Assert.Equal((byte)0, brush.Color.A);
                runtime.Time.Advance(TimeSpan.FromMilliseconds(90)); frames.Advance(TimeSpan.FromMilliseconds(90)); Dispatcher.UIThread.RunJobs();
                Assert.InRange(brush.Color.A, (byte)24, (byte)27); // symmetric easeInOut is halfway at .09/.18 => .1 alpha.
                runtime.Time.Advance(TimeSpan.FromMilliseconds(90)); frames.Advance(TimeSpan.FromMilliseconds(90)); Dispatcher.UIThread.RunJobs();
                Assert.InRange(brush.Color.A, (byte)50, (byte)52);
            }
            window.Close();
        });
    });
}
