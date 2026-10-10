using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port.ModelsPalette;

// ED-NEW-stutterfix-2: the OpenRouter picker's rows past its first screens join the list a slice per frame (each row made with the
// picker, so its search, selection and colours run as before), the extent exact from the first frame; a search, a key press or a
// scroll into them puts them in first. The count's labels are made as a count is first shown, in their order.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-2")]
public sealed class OpenRouterPickerDeferredFillTests(HeadlessSession headless)
{
    private const int Count = OpenRouterDeferredRig.Count;
    private const double Extent = Count * 47 + 6 * 24 + 2; // rows 46 + 1, maker headers 24, the list's 2 below

    [Fact]
    public void A_long_picker_opens_with_its_first_screens_and_fills_in_over_frames_at_the_same_extent() => headless.OnUiThread(() =>
    {
        using var rig = new OpenRouterDeferredRig();
        Assert.Equal(Count, rig.Picker.Rows.Count); // every row is made with the picker
        var first = rig.Shown;
        Assert.InRange(first.Length, 9, Count - 1);
        Assert.Equal(rig.Picker.Rows.Take(first.Length), first);
        Assert.Equal(Extent, rig.Scroll.Extent.Height, 0.5);
        for (var frame = 0; frame < 600 && rig.Shown.Length < Count; frame++) { rig.Step(16); Assert.Equal(Extent, rig.Scroll.Extent.Height, 0.5); }
        Assert.Equal(rig.Picker.Rows, rig.Shown);
        Assert.Equal(Extent, rig.Scroll.Extent.Height, 0.5);
        Assert.Equal(6, rig.Picker.GetVisualDescendants().OfType<PickerFold>().Count(f => f is not PickerRow && f.Child is StackPanel));
    });

    [Fact]
    public void A_search_and_its_keys_reach_rows_not_yet_in_the_list() => headless.OnUiThread(() =>
    {
        using var rig = new OpenRouterDeferredRig();
        Assert.True(rig.Shown.Length < 60);
        rig.Picker.Search.Text = "model"; Dispatcher.UIThread.RunJobs();
        Assert.Equal(Count, rig.Shown.Length);
        Assert.Equal("486 models", rig.Picker.CountText);
        for (var i = 0; i < 60; i++) rig.Key(Key.Down);
        Assert.Equal(rig.Picker.Rows[60].Model.Id, rig.Picker.ActiveId);
        rig.Step(500);
        var row = rig.Picker.Rows[60];
        var y = row.TranslatePoint(default, (Visual)rig.Scroll.Content!)!.Value.Y;
        Assert.InRange(y, rig.Scroll.Offset.Y - 0.5, rig.Scroll.Offset.Y + rig.Scroll.Viewport.Height - row.Bounds.Height + 0.5);
        rig.Key(Key.Enter);
        Assert.Equal(row.Model.Id, rig.Picked);
        rig.Picker.Search.Text = "model 485"; Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 model", rig.Picker.CountText);
    });

    [Fact]
    public void Scrolling_past_the_rows_in_the_list_puts_in_the_rows_it_reaches() => headless.OnUiThread(() =>
    {
        using var rig = new OpenRouterDeferredRig();
        var scroll = rig.Scroll;
        scroll.Offset = new Vector(0, 6000); rig.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); rig.Step(16);
        Assert.Equal(6000, scroll.Offset.Y, 0.5);
        var content = (Visual)scroll.Content!;
        var inView = rig.Shown.Where(r => r.TranslatePoint(default, content)!.Value.Y is var y && y + 46 > 6000 && y < 6000 + scroll.Viewport.Height).ToArray();
        Assert.True(inView.Length >= (int)(scroll.Viewport.Height / 47) - 1, $"{inView.Length} rows in the viewport");
        Assert.Equal(Extent, scroll.Extent.Height, 0.5);
    });

    [Fact]
    public void Count_labels_are_made_as_a_count_is_first_shown_and_keep_their_order() => headless.OnUiThread(() =>
    {
        using var rig = new OpenRouterDeferredRig();
        var grid = (Grid)Assert.Single(rig.Picker.GetVisualDescendants().OfType<PickerCount>()).Parent!;
        var width = grid.Bounds.Width;
        Assert.Equal(1, rig.Picker.CountOpacity("486 models"));
        rig.Picker.Search.Text = "model 48"; Dispatcher.UIThread.RunJobs();
        var some = rig.Picker.CountText;
        Assert.NotEqual("486 models", some);
        rig.Picker.Search.Text = "model 485"; Dispatcher.UIThread.RunJobs();
        rig.Step(500);
        Assert.Equal(1, rig.Picker.CountOpacity("1 model"));
        Assert.Equal(0, rig.Picker.CountOpacity("486 models"));
        Assert.Equal(0, rig.Picker.CountOpacity(some));
        static int N(string text) => int.Parse(text.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        var labels = grid.Children.OfType<PickerCount>().Select(c => ((TextBlock)c.Child!).Text!).ToArray();
        Assert.Equal(new[] { "1 model", some, "486 models" }.OrderBy(N), labels);
        Assert.Equal(width, grid.Bounds.Width, 0.01);
    });
}
