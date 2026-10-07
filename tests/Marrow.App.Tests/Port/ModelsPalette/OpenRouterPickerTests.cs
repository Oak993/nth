using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.ModelsPalette;

// PORT-ONLY: approved Q-PICKER A, Main.dc.html v4/v5; ED-NEW-models-palette-5.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-modelspalette-5")]
public sealed class OpenRouterPickerTests(HeadlessSession headless)
{
    [Fact]
    public void Real_picker_filters_all_words_retains_rows_and_moves_the_key_highlight() => headless.OnUiThread(() =>
    {
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        string? picked = null;
        var picker = new OpenRouterPicker([
            new("anthropic/claude-opus", "Claude Opus", "Anthropic", "#CC6B47"),
            new("moonshotai/kimi-k2", "Kimi K2", "Moonshot AI", "#3A4150"),
            new("moonshotai/kimi-k1", "Kimi K1", "Moonshot AI", "#3A4150")], "anthropic/claude-opus", id => picked = id, () => { });
        var window = new Avalonia.Controls.Window { Content = picker }; window.Show();
        try
        {
            var rows = picker.Rows.ToArray();
            picker.Search.Text = "  MOONSHOT kimi "; Dispatcher.UIThread.RunJobs();
            Assert.Equal("2 models", picker.CountText);
            Assert.Equal("moonshotai/kimi-k2", picker.ActiveId);
            Assert.Equal(rows, picker.Rows);
            picker.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Assert.Equal("moonshotai/kimi-k1", picker.ActiveId);
            picker.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("moonshotai/kimi-k1", picked);
            picker.Search.Text = "no-such-model"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("0 models", picker.CountText); Assert.Null(picker.ActiveId);
            Assert.Equal("No models match “no-such-model”", picker.EmptyText);
            picker.ClearSearch(); Assert.Equal("3 models", picker.CountText); Assert.Null(picker.ActiveId);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_card_and_retained_rows_fold_on_the_source_curve_and_count_has_two_clocks() => headless.OnUiThread(() =>
    {
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        var clock = new ManualTimeProvider(); var frames = new ManualFrameSource();
        using var time = SwiftClock.Provider.Override(clock); using var source = FrameClock.Source.Override(frames);
        var picker = new OpenRouterPicker([
            new("anthropic/a", "Alpha", "Anthropic", "#CC6B47"),
            new("moonshotai/kimi", "Kimi", "Moonshot AI", "#3A4150")], "anthropic/a", _ => { }, () => { });
        var window = new Avalonia.Controls.Window { Content = picker }; window.Show();
        try
        {
            var allHeight = picker.ListHeight;
            picker.Search.Text = "kimi"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 model", picker.CountText);
            Assert.Equal(allHeight, picker.ListHeight);
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
            Step(80);
            Assert.Equal(0, picker.CountOpacity("1 model"), 8);
            Assert.InRange(picker.CountOpacity("2 models"), 0, 1);
            Step(90);
            var p = Animation.TimingCurve(.25,.1,.25,1,.34).Progress(.17);
            Assert.Equal(1-p, picker.Rows[0].FoldAmount, 6);
            Assert.Equal(.5, picker.Rows[0].FoldOpacity, 6);
            Assert.Equal(allHeight + (picker.TargetListHeight-allHeight)*p, picker.ListHeight, 6);
            Step(170);
            Assert.Equal(picker.TargetListHeight, picker.ListHeight);
            Assert.Equal(0, picker.Rows[0].FoldAmount);
            Assert.Equal(1, picker.CountOpacity("1 model")); Assert.Equal(0, picker.CountOpacity("2 models"));
            Assert.Equal(330, picker.Width); Assert.Equal(10, OpenRouterPicker.ChipClearance);
            Assert.Equal(28, picker.SearchFieldHeight); Assert.Equal(440, OpenRouterPicker.MaximumListHeight);
        }
        finally { window.Close(); }
    });
}
