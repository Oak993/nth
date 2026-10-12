using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// A native head's footer in place of a chat box (App/Views/Chat/HydraPanel.swift:439-473): a mini spinner or an outcome mark,
/// the footer status text and a step count, on a flat 16-point rounded bar filled with the panel control fill, inset 20 points
/// from each side and 14 from the bottom.
/// </summary>
[Collection("Headless")]
public sealed class HydraHeadFooterTests(HeadlessSession headless)
{
    private static HydraHeadInfo Info(HydraHeadInfo.Status status, long toolCalls = 0, string? summary = null) =>
        new HydraHeadInfo(0, "Task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
        {
            HydraHeadInfoStatus = status, ToolCalls = toolCalls, Summary = summary,
        };

    private void Run(HydraHeadInfo info, Action<HydraHeadFooter, Avalonia.Controls.Window> test, ThemeVariant? theme = null) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the mini spinner asks for frames
        var footer = new HydraHeadFooter(info) { VerticalAlignment = VerticalAlignment.Bottom };
        var window = new Avalonia.Controls.Window { Content = new Grid { Children = { footer } }, Width = 420, Height = 200 };
        if (theme is not null) window.RequestedThemeVariant = theme;
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            test(footer, window);
        }
        finally { window.Close(); }
    });

    private static Border Bar(HydraHeadFooter footer) => Assert.IsType<Border>(footer.Child);

    private static Rect Where(Control control, Control root) => new(control.TranslatePoint(default, root) ?? default, control.Bounds.Size);

    private static Color Fill(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:466-471 padded 14 horizontally and 10 vertically, a 16-point continuous rounded rectangle, then padded 20 horizontally and 14 at the bottom
    public void The_bar_is_a_16_point_rounded_rectangle_inset_20_from_each_side_and_14_from_the_bottom() => Run(Info(HydraHeadInfo.Status.Running), (footer, window) =>
    {
        var bar = Bar(footer);
        Assert.Equal(new CornerRadius(16), SwiftShapes.StatedCornerRadius(bar)); // one line tall the bar is 37 pt, in the band: its own CornerRadius is what draws it (OD-0102, ED-297 (8))
        Assert.Equal(new Thickness(14, 10), bar.Padding);
        var rect = Where(bar, window);
        Assert.Equal(20, rect.X);
        Assert.Equal(420 - 20, rect.Right);
        Assert.Equal(200 - 14, rect.Bottom);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:469 Chrome.panelControlFill(isDark: colorScheme == .dark), RenderBudget.swift:93-98: glassTint mixed toward white (dark) or black (light) by 0.12 or 0.04, at 0.55 or 0.42
    public void The_bar_is_the_flat_panel_control_fill_of_the_current_theme(bool dark)
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Run(Info(HydraHeadInfo.Status.Running), (footer, window) =>
        {
            Assert.Equal(theme, footer.ActualThemeVariant);
            var expected = Chrome.GlassTint.Mix(dark ? SwiftColor.White : SwiftColor.Black, dark ? 0.12 : 0.04).Opacity(dark ? 0.55 : 0.42).Resolve(theme);
            Assert.Equal(expected, Fill(Bar(footer).Background));
            // Flat: a second glass here would sample the same pixels twice.
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Control>(footer), control => SwiftGlass.GetEffect(control) is not null);
        }, theme);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:469 the fill follows the colour scheme: it is read where the footer is drawn
    public void The_fill_follows_a_theme_change() => Run(Info(HydraHeadInfo.Status.Running), (footer, window) =>
    {
        var light = Fill(Bar(footer).Background);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dark = Fill(Bar(footer).Background);
        Assert.NotEqual(light, dark);
        Assert.Equal(Chrome.GlassTint.Mix(SwiftColor.White, 0.12).Opacity(0.55).Resolve(ThemeVariant.Dark), dark);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:447-449 if info.status == .running { MiniSpinner() }; Calculi WU6 (ED-NEW-calculi-wu6-1): the quiet Tripus beads
    public void A_running_head_shows_the_quiet_beads_and_no_outcome_mark() => Run(Info(HydraHeadInfo.Status.Running), (footer, _) =>
    {
        var beads = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(footer));
        Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsPlace.HydraHeadFooter, beads.Place);
        Assert.True(beads.Beads.Quiet);
        Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsState.Hydra, beads.Beads.Current.State);
        Assert.Empty(LogicalQuery.SelfAndDescendants<SwiftImage>(footer));
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, "checkmark.circle.fill", 0)]
    [InlineData(HydraHeadInfo.Status.Failed, "xmark.circle.fill", 1)]
    [InlineData(HydraHeadInfo.Status.Stopped, "stop.circle.fill", 2)]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:450-452 Image(systemName:) .font(.system(.callout)), green for completed, red for failed, secondary text for stopped
    public void A_finished_head_shows_a_filled_circle_mark_in_green_red_or_grey(HydraHeadInfo.Status status, string symbol, int colourIndex) =>
        Run(Info(status), (footer, _) =>
        {
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(footer));
            var mark = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(footer));
            Assert.Equal(symbol, mark.SymbolName);
            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout), SwiftImage.GetFont(mark));
            var expected = new[] { Chrome.Success, Chrome.Danger, Chrome.SecondaryText }[colourIndex].Resolve(ThemeVariant.Light);
            Assert.Equal(expected, Fill(mark.Foreground));
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:454-458 Text(verbatim: HydraStatusText.footer(info)).font(.system(.callout)).foregroundStyle(Chrome.secondaryText).lineLimit(1).truncationMode(.tail)
    public void The_status_text_is_the_footer_wording_at_12_points_in_secondary_text_on_one_line_truncated_at_the_tail() =>
        Run(Info(HydraHeadInfo.Status.Failed, summary: string.Join(" ", Enumerable.Repeat("a very long summary that cannot fit on one line", 8))), (footer, _) =>
        {
            var text = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(footer), t => t.Text!.StartsWith("Failed", StringComparison.Ordinal));
            Assert.Equal(HydraStatusText.Footer(Info(HydraHeadInfo.Status.Failed, summary: string.Join(" ", Enumerable.Repeat("a very long summary that cannot fit on one line", 8)))), text.Text);
            Assert.Equal(12, text.FontSize);
            Assert.Equal(Chrome.SecondaryText.Resolve(ThemeVariant.Light), Fill(text.Foreground));
            Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
            Assert.True(text.Bounds.Height < 20, "one line");
            Assert.True(Bar(footer).Bounds.Width <= 380);
        });

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1 step")]
    [InlineData(2, "2 steps")]
    [InlineData(12, "12 steps")]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:460-464 if info.toolCalls > 0 { Text(verbatim: info.toolCalls == 1 ? "1 step" : "\(info.toolCalls) steps").font(.system(.subheadline).monospacedDigit()) } in secondaryText
    public void The_step_count_shows_when_any_tool_ran_at_11_points_in_monospaced_digits(long toolCalls, string? expected) =>
        Run(Info(HydraHeadInfo.Status.Running, toolCalls), (footer, _) =>
        {
            var steps = LogicalQuery.SelfAndDescendants<TextBlock>(footer).Where(t => t.Text!.EndsWith("step", StringComparison.Ordinal) || t.Text.EndsWith("steps", StringComparison.Ordinal)).ToArray();
            if (expected is null)
            {
                Assert.Empty(steps);
                return;
            }
            var block = Assert.Single(steps);
            Assert.Equal(expected, block.Text);
            Assert.Equal(11, block.FontSize);
            Assert.Contains(block.FontFeatures!, f => f.Tag == "tnum" && f.Value == 1);
            Assert.Equal(Chrome.SecondaryText.Resolve(ThemeVariant.Light), Fill(block.Foreground));
            // At the bar's trailing end, after the status text.
            Assert.Equal(Where(Bar(footer), footer).Right - 14, Where(block, footer).Right, 0.51);
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-178")] // HydraPanel.swift:446 HStack(spacing: 8): the spinner or mark and the text are 8 points apart
    public void The_leading_mark_and_the_status_text_are_eight_points_apart() => Run(Info(HydraHeadInfo.Status.Completed), (footer, _) =>
    {
        var mark = Where(Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(footer)), footer);
        var text = Where(Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(footer)), footer);
        Assert.Equal(mark.Right + 8, text.X, 0.51);
        Assert.Equal(Where(Bar(footer), footer).X + 14, mark.X, 0.51);
    });
}
