using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :749-845;
// UI/Chrome/Chrome.swift:1006-1027,1055-1080. Fonts: ED-059; secondary: ED-062.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofRowsTests(HeadlessSession headless)
{
    private static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    private static Avalonia.Controls.Window Show(Control view)
    {
        var window = new Avalonia.Controls.Window { Content = view, Width = 500, Height = 600 };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void Row(ChromeRow row, string title, string detail)
    {
        // ED-NEW-textsize-2: the Settings row's vertical padding is Claude's (Chrome.SettingsRowVerticalPadding, 18), the source's 11 before.
        var border = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16, 18, 10, 18), border.Padding);
        var grid = Assert.IsType<Grid>(border.Child);
        // :1062,1072: VStack, Spacer(minLength:12), control; HStack has two 16pt gaps.
        Assert.Equal(new[] { 16.0, 12.0, 16.0 }, grid.ColumnDefinitions.Skip(1).Take(3).Select(c => c.Width.Value));
        var labels = Assert.IsType<StackPanel>(grid.Children[0]); Assert.Equal(2, labels.Spacing);
        var titleText = Assert.IsType<TextBlock>(labels.Children[0]);
        // The check row's sentence sits in the box that eases its height when the words swap (OD-0076); at rest the box holds the sentence alone.
        var detailText = Assert.IsType<TextBlock>(labels.Children[1] is SentenceHeightGlide box ? Assert.Single(box.Children) : labels.Children[1]);
        Assert.Equal(title, titleText.Text); Assert.Equal(detail, detailText.Text);
        // ED-NEW-textsize-2: title 14 and detail 13 on a 20 pt line (the source's 13 and 11); ED-NEW-textsize-1: whole words wrap.
        Assert.Equal(14, titleText.FontSize); Assert.Equal(13, detailText.FontSize); Assert.Equal(20, detailText.LineHeight);
        Assert.Equal(TextWrapping.WrapWithOverflow, detailText.TextWrapping);
        Assert.Equal(Chrome.PrimaryText.Resolve(titleText.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(titleText.Foreground).Color);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(detailText.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(detailText.Foreground).Color);
        Assert.Equal(HorizontalAlignment.Right, grid.Children[1].HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, grid.Children[1].VerticalAlignment);
    }

    [Theory]
    [Trait("Req", "UI-UPDATE-021")] // App/Views/Settings/AboutUpdateViews.swift:822-845; UI/Chrome/Chrome.swift:1055-1080
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:836-844 (detail order and sentences, OD-0012 F1 product name)
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:822-845 (title, button label, disabled while checking, detail order)
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:826-833 (Check now | Checking, enabled | disabled)
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:826-833 (label and disabled state share isChecking)
    [Trait("Decision", "ED-059")] // PR-SETTINGS-240/PR-UPDATE-076 partial: every sentence/metric, not a manufactured capture reference.
    [InlineData(false, null, false, false)] [InlineData(false, null, false, true)]
    [InlineData(false, "The request timed out.", true, false)] [InlineData(false, "The request timed out.", true, true)]
    [InlineData(false, null, true, false)] [InlineData(false, null, true, true)]
    [InlineData(true, "The request timed out.", true, false)] [InlineData(true, "The request timed out.", true, true)]
    public void Check_row_renders_all_precedence_forms_with_and_without_the_named_checked_suffix(bool checking, string? error, bool ready, bool checkedBefore) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], "");
        Set(checker, nameof(UpdateChecker.IsChecking), checking); Set(checker, nameof(UpdateChecker.LastError), error);
        if (ready) Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
        if (checkedBefore) Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now.AddHours(-2));
        using var shared = UpdateChecker.OverrideShared(checker); var view = new AboutUpdateCheckRow(); var window = Show(view);
        var suffix = checkedBefore ? "Checked 2 hours ago." : "Not checked yet."; // :837 named relative date, ED-096 under en_US fixture
        var expected = checking ? "Asking GitHub for the latest release…" : error is not null ? error + " " + suffix : ready ? "nth 9.0.0 is ready to install. " + suffix : "nth checks GitHub for new versions a few times a day. " + suffix;
        Row(Assert.IsType<ChromeRow>(view.Child), "Software update", expected);
        var button = Assert.Single(view.GetVisualDescendants().OfType<Button>()); Assert.Equal(!checking, button.IsEnabled);
        Assert.Equal(checking ? "Checking…" : "Check now", Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
        var surface = Assert.Single(view.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
        // :830-831 .buttonStyle(.glass).controlSize(.small) under the window's accent tint (WindowManager.swift:162,191): the reference
        // video shows an accent capsule (interactive while enabled), grey and translucent while disabled; the looks are measured in
        // AboutUpdateViewsCheckNowLookTests, so this row test only pins which material goes with which state.
        Assert.Equal(GlassShapeKind.Capsule, surface.Shape.Kind); Assert.Equal(!checking, surface.IsInteractive); Assert.Equal(checking ? 0.0 : 1.0, surface.TintOpacity); // ED-356: the disabled wash is the material's film now (it was 0.11 over the dark veil)
        window.Close();
    });

    [Theory]
    [Trait("Req", "UI-UPDATE-017")] // App/Views/Settings/AboutUpdateViews.swift:763-791; UI/Chrome/Chrome.swift:1055-1080; OD-0012 H
    [Trait("Req", "UI-UPDATE-018")] // App/Views/Settings/AboutUpdateViews.swift:765-776; UpdateChecker.swift:213
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Artifact_row_omits_only_unknown_segments_and_real_release_link_uses_the_stored_tag_url(bool date, bool size) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var update = AboutUpdateViewsProofLifecycleTests.Release() with { ReleasedAt = date ? new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero) : null, Size = size ? 128_400_000L : null };
        var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""); Set(checker, nameof(UpdateChecker.Update), update);
        using var shared = UpdateChecker.OverrideShared(checker); using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var page = new AboutSoftwareUpdateSection(); var window = Show(page);
        var expected = (date ? "Released September 13, 2026 · " : "") + (size ? "128.4 MB · " : "") + "Update & restart installs it in place and relaunches.";
        var row = Assert.Single(page.GetVisualDescendants().OfType<ChromeRow>()); Row(row, "Unsigned Windows installer", expected);
        var card = Assert.Single(page.GetVisualDescendants().OfType<ChromeCard>()); var fill = Assert.IsType<Border>(card.Child);
        Assert.Equal(new CornerRadius(16), fill.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(fill));
        Assert.Equal(Chrome.PrimaryText.Opacity(0.03).Resolve(fill.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
        var button = Assert.Single(row.GetVisualDescendants().OfType<Button>()); Assert.True(button.IsEnabled); Assert.Equal("Open the release on GitHub", ToolTip.GetTip(button));
        var label = Assert.IsType<StackPanel>(button.Content); Assert.Equal(4, label.Spacing);
        var text = Assert.IsType<TextBlock>(label.Children[0]); Assert.Equal("Release notes", text.Text); Assert.Equal(12, text.FontSize); Assert.Equal(FontWeight.Medium, text.FontWeight);
        var arrow = Assert.IsType<SwiftImage>(label.Children[1]); Assert.Equal("arrow.up.right", arrow.SymbolName);
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(arrow));
        Assert.Equal(Chrome.Accent.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        Assert.Equal(Chrome.Accent.Resolve(arrow.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(arrow.Foreground).Color);
        var previous = ShellWorkspace.OpenWithDefaultApp; string? opened = null; ShellWorkspace.OpenWithDefaultApp = url => opened = url;
        try
        {
            var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal("https://gitlab.com/droppyformac1/droppy-code/-/releases/v9.0.0", opened);
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; window.Close(); }
    });

    [Theory]
    [Trait("Req", "UI-UPDATE-016")] // App/Views/Settings/AboutUpdateViews.swift:749-762 (with existing fallback test and Markdown link test)
    [InlineData("")] [InlineData("## New features\n- Parsed card")] [InlineData("Raw notes")]
    [InlineData("Raw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes\n\nRaw notes")]
    public void Raw_fallback_uses_padded_markdown_and_source_card_and_scrolls_only_above_the_cap(string notes) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""); Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release(notes));
        using var shared = UpdateChecker.OverrideShared(checker); using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var page = new AboutSoftwareUpdateSection(); var window = Show(page);
        var raw = notes.StartsWith("Raw", StringComparison.Ordinal);
        Assert.Equal(raw, page.GetVisualDescendants().OfType<MarkdownView>().Any());
        Assert.Equal(notes.StartsWith("##", StringComparison.Ordinal), page.GetVisualDescendants().OfType<UpdateReleaseSectionCards>().Any());
        if (raw)
        {
            var markdown = Assert.Single(page.GetVisualDescendants().OfType<MarkdownView>()); Assert.Equal(notes, markdown.Text);
            Assert.Equal(12.5, MarkdownView.GetMarkdownPointSize(markdown)); Assert.Equal(new Thickness(16), markdown.Margin); Assert.Equal(HorizontalAlignment.Stretch, markdown.HorizontalAlignment);
            var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>()); Assert.Equal(240, scroll.MaxHeight); Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.Equal(notes.Contains('\n', StringComparison.Ordinal), scroll.Extent.Height > scroll.Viewport.Height);
            var at = scroll.TranslatePoint(new Point(30, 30), window)!.Value;
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            window.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            window.MouseWheel(at, new Vector(0, -1), RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroll.Offset.Y); // ED-NEW-softscroll-1: the notes glide (OD-0071), nothing moves in the report's own frame
            var glide = new List<double>();
            for (var frame = 0; frame < 60; frame++)
            {
                runtime.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); glide.Add(scroll.Offset.Y);
            }
            Assert.Equal(notes.Contains('\n', StringComparison.Ordinal), scroll.Offset.Y > 0); // :756 basedOnSize: short content cannot scroll
            if (scroll.Offset.Y > 0) { Assert.InRange(glide[0], 0.01, 99.99); Assert.Equal(Math.Min(100, scroll.Extent.Height - scroll.Viewport.Height), scroll.Offset.Y, 3); } // a notch: 100 points at the default 3 lines (ED-NEW-softscroll-2)
            // Round 3: :749-762 carries no .scrollIndicators modifier, unlike the sites this template's presenter
            // recipe is shared with (:139/:363/:781-787 .scrollIndicators(.never)/.hidden), so a real indicator part
            // must exist and only be visible while the content actually overflows.
            var bar = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollBar>());
            Assert.Equal(Orientation.Vertical, bar.Orientation);
            Assert.Equal(notes.Contains('\n', StringComparison.Ordinal), bar.IsVisible); // Auto: visible only above the cap
            var card = page.GetVisualDescendants().OfType<ChromeCard>().First(); var fill = Assert.IsType<Border>(card.Child);
            Assert.Equal(new CornerRadius(16), fill.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(fill));
            Assert.Equal(Chrome.PrimaryText.Opacity(0.03).Resolve(fill.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-016")] // App/Views/Settings/AboutUpdateViews.swift:749-762; MarkdownView's own link path
    public void Raw_fallback_markdown_link_opens_through_the_real_text_hit_test() => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("example.com"); // R-TEST-10: the release notes' link asks example.com for its favicon when it appears (found by the network audit with the cache's short-cuts off, ED-256 (10))
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        const string url = "https://example.com/about-update-notes";
        var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""); Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release("Raw [release link](" + url + ")"));
        using var shared = UpdateChecker.OverrideShared(checker); using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var page = new AboutSoftwareUpdateSection(); var window = Show(page);
        var markdown = Assert.Single(page.GetVisualDescendants().OfType<MarkdownView>());
        var link = Assert.Single(markdown.GetVisualDescendants().OfType<LinkTextView>());
        Point? local = null;
        for (var y = 1; y < link.Bounds.Height && local is null; y++)
            for (var x = 1; x < link.Bounds.Width; x++)
                if (link.Link(new Point(x, y))?.AbsoluteString == url) { local = new Point(x, y); break; }
        Assert.NotNull(local);
        var previous = ShellWorkspace.OpenWithDefaultApp; string? opened = null; ShellWorkspace.OpenWithDefaultApp = value => opened = value;
        try
        {
            var at = link.TranslatePoint(local.Value, window)!.Value;
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            window.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(url, opened);
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; window.Close(); }
    });
}
