using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 and 7.4 (web surfaces W16a; ED-NEW-web-w16a-1): inputs write their state through the runtime; a change
// recomputes every let and re-renders the nodes that read it inside the same frame (no frame is delivered between the
// press and the check); chips, checklists, tabs and steps keep their own state; saved state reopens the view as left.
[Collection("Headless")]
public sealed class NthUiStateTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_stepper_recomputes_the_table_in_the_same_frame() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var table = rig.TextsOf("table");
        Assert.Equal(["Item", "Monthly", "Compute", "$101", "Egress", "$24", "Storage", "$9", "Total per month", "$134"], table);
        var stepper = rig.ViewOf<NthUiStepperView>();
        Assert.Equal("3", stepper.Stepper.Shown);
        stepper.Stepper.Press(1);
        Assert.Equal(4, rig.View.Runtime.Get("instances").AsNumber);
        Assert.Equal("4", stepper.Stepper.Shown);
        Assert.Equal(["Item", "Monthly", "Compute", "$134", "Egress", "$24", "Storage", "$9", "Total per month", "$167"], rig.TextsOf("table"));
        stepper.Stepper.Press(-1);
        stepper.Stepper.Press(-1);
        Assert.Equal("$67", rig.TextsOf("table")[3]);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_stepper_stops_at_its_bounds_and_disables_the_button_that_would_pass_them() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("state n = 2\nstepper n label=\"Seats\" min=1 max=3", culture: NthUiRig.English));
        var stepper = rig.ViewOf<NthUiStepperView>().Stepper;
        var fewer = NthUiRig.Named<Button>(stepper, "Fewer seats");
        var more = NthUiRig.Named<Button>(stepper, "More seats");
        stepper.Press(-1);
        Assert.Equal(1, rig.View.Runtime.Get("n").AsNumber);
        Assert.False(fewer.IsEnabled);
        stepper.Press(-1);
        Assert.Equal(1, rig.View.Runtime.Get("n").AsNumber);
        rig.Key(stepper, Key.End);
        Assert.Equal(3, rig.View.Runtime.Get("n").AsNumber);
        Assert.False(more.IsEnabled);
        Assert.True(fewer.IsEnabled);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_slider_and_the_segmented_control_write_their_state_and_the_table_follows() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var slider = rig.ViewOf<NthUiSliderView>();
        Assert.Equal("40 GB/day", slider.Readout);
        rig.Key(slider.Slider, Key.Right);
        Assert.Equal(45, rig.View.Runtime.Get("traffic").AsNumber);
        Assert.Equal("45 GB/day", slider.Readout);
        Assert.Equal("$27", rig.TextsOf("table")[5]);
        rig.Key(slider.Slider, Key.Home);
        Assert.Equal(0, rig.View.Runtime.Get("traffic").AsNumber);
        var segmented = rig.ViewOf<NthUiSegmentedView>();
        Assert.Equal(0, segmented.Track.Selected);
        rig.Key(segmented.Track, Key.Right);
        Assert.Equal("us", rig.View.Runtime.Get("region").AsText);
        Assert.Equal(1, segmented.Track.Selected);
        Assert.Equal("$92", rig.TextsOf("table")[3]);
        rig.Click(NthUiRig.Named<ToggleButton>(segmented.Track, "Asia"));
        Assert.Equal("ap", rig.View.Runtime.Get("region").AsText);
        Assert.Equal(2, segmented.Track.Selected);
        Assert.Equal([false, false, true], segmented.Track.Items.Cast<ToggleButton>().Select(t => t.IsChecked == true));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_dragged_slider_snaps_to_its_step_through_the_runtime() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        var peer = (Avalonia.Automation.Provider.IRangeValueProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(slider);
        peer.SetValue(77);
        Assert.Equal(75, rig.View.Runtime.Get("traffic").AsNumber);
        Assert.Equal(75, slider.Value);
        Assert.Equal(75, peer.Value);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Filter_chips_show_and_hide_rows_and_a_count_that_reads_them() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Filter, culture: NthUiRig.English));
        rig.Settle();
        Assert.Contains("C#", rig.TextsOf("table"));
        Assert.DoesNotContain("Swift", rig.TextsOf("table"));
        Assert.Contains("1 shown", rig.Texts());
        var swift = NthUiRig.Named<ToggleButton>(rig.Control("chips"), "Swift");
        Assert.False(swift.IsChecked);
        rig.Click(swift);
        Assert.True(swift.IsChecked);
        Assert.Contains("2 shown", rig.Texts());
        rig.Settle();
        Assert.Contains("Swift", rig.TextsOf("table"));
        rig.Click(NthUiRig.Named<ToggleButton>(rig.Control("chips"), "C#"));
        rig.Settle();
        Assert.DoesNotContain("C#", rig.TextsOf("table"));
        Assert.Contains("1 shown", rig.Texts());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Tabs_swap_their_panel_and_text_that_reads_the_tab_follows() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Tabs, culture: NthUiRig.English));
        rig.Settle();
        Assert.Contains("$9 per month", rig.Texts());
        Assert.DoesNotContain("$29 per month", rig.Texts());
        var tabs = rig.ViewOf<NthUiTabsView>();
        Assert.Equal(0, tabs.Track.Selected);
        rig.Click(NthUiRig.Named<ToggleButton>(tabs.Track, "Team"));
        rig.Settle();
        Assert.Equal(1, tabs.Track.Selected);
        Assert.Contains("$29 per month", rig.Texts());
        Assert.DoesNotContain("$9 per month", rig.Texts());
        Assert.Contains("You are looking at team", rig.Texts());
        rig.Key(tabs.Track, Key.Left);
        rig.Settle();
        Assert.Contains("You are looking at starter", rig.Texts());
        Assert.Contains("$9 per month", rig.Texts());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Steps_move_with_previous_and_next_and_show_their_place() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Steps, culture: NthUiRig.English));
        rig.Settle();
        var steps = rig.Control("steps");
        Assert.Contains("1 of 3", rig.Texts());
        Assert.Contains("Install the dependencies.", rig.Texts());
        var previous = NthUiRig.Named<Button>(steps, "Previous step");
        var next = NthUiRig.Named<Button>(steps, "Next step");
        Assert.False(previous.IsEnabled);
        rig.Click(next);
        rig.Settle();
        Assert.Contains("2 of 3", rig.Texts());
        Assert.Contains("Configure", rig.Texts());
        Assert.Contains("Copy the example settings.", rig.Texts());
        Assert.DoesNotContain("Install the dependencies.", rig.Texts());
        rig.Click(next);
        rig.Settle();
        Assert.False(next.IsEnabled);
        Assert.Contains("The preview opens in Visus.", rig.Texts());
        Assert.Equal(2, rig.View.Runtime.Get("step").AsNumber);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Checklist_ticks_change_the_bound_list_and_its_count() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Checklist, culture: NthUiRig.English));
        Assert.Contains("1 of 3 done", rig.Texts());
        var boxes = rig.Control("checklist").GetVisualDescendants().OfType<NthUiCheck>().ToList();
        Assert.Equal([true, false, false], boxes.Select(b => b.IsChecked == true));
        rig.Click(boxes[1]);
        Assert.Equal([true, true, false], boxes.Select(b => b.IsChecked == true));
        Assert.Contains("2 of 3 done", rig.Texts());
        rig.Click(boxes[0]);
        Assert.Contains("1 of 3 done", rig.Texts());
        Assert.Equal(["changelog"], rig.View.Runtime.Get("done").Items.Select(v => v.AsText));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Form_fields_write_their_states_as_the_reader_types_and_picks() => headless.OnUiThread(() =>
    {
        NthUiDateView.Clock = () => new DateTime(2026, 10, 12);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Form, culture: NthUiRig.English));
        var field = rig.ViewOf<NthUiTextFieldView>();
        field.Box.Text = "Ada";
        Assert.Equal("Ada", rig.View.Runtime.Get("name").AsText);
        var select = rig.ViewOf<NthUiSelectView>();
        var first = select.Shown;
        rig.Key(select.Button, Key.Down);
        Assert.NotEqual(first, select.Shown);
        Assert.Contains(rig.View.Runtime.Get("time").AsText, new[] { "early", "mid", "late" });
        select.Open();
        rig.Pump();
        Assert.True(select.Menu.IsOpen);
        rig.Click(NthUiRig.Named<Button>((Avalonia.Visual)select.Menu.Child!, "8 pm"));
        Assert.False(select.Menu.IsOpen);
        Assert.Equal("late", rig.View.Runtime.Get("time").AsText);
        Assert.Equal("8 pm", select.Shown);
        var date = rig.ViewOf<NthUiDateView>();
        rig.Key(date.Button, Key.Up);
        var day = rig.View.Runtime.Get("day").AsText;
        Assert.True(string.CompareOrdinal(day, "2026-10-12") >= 0 && string.CompareOrdinal(day, "2026-12-31") <= 0, day);
        rig.Key(date.Button, Key.PageUp);
        Assert.Equal(DateTime.Parse(day, System.Globalization.CultureInfo.InvariantCulture).AddMonths(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), rig.View.Runtime.Get("day").AsText);
        Assert.Equal(DateTime.Parse(rig.View.Runtime.Get("day").AsText, System.Globalization.CultureInfo.InvariantCulture).ToString("D", NthUiRig.English), date.Shown);
        var toggle = rig.ViewOf<NthUiToggleView>();
        Assert.False(toggle.Switch.IsChecked);
        rig.Click(toggle.Switch);
        Assert.True(rig.View.Runtime.Get("outside").IsTruthy);
        Assert.True(toggle.Switch.IsChecked);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_date_picked_from_the_month_stays_inside_its_bounds() => headless.OnUiThread(() =>
    {
        NthUiDateView.Clock = () => new DateTime(2026, 10, 12);
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Form, culture: NthUiRig.English));
        var date = rig.ViewOf<NthUiDateView>();
        date.Open();
        rig.Pump();
        var month = (Avalonia.Visual)date.Menu.Child!;
        var days = month.GetVisualDescendants().OfType<Button>().Where(b => (Avalonia.Automation.AutomationProperties.GetName(b) ?? "").Contains("October", StringComparison.Ordinal)).ToList();
        Assert.Equal(31, days.Count);
        Assert.All(days.Take(11), b => Assert.False(b.IsEnabled));
        Assert.True(days[11].IsEnabled);
        rig.Click(days[20]);
        Assert.Equal("2026-10-21", rig.View.Runtime.Get("day").AsText);
        Assert.False(date.Menu.IsOpen);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Saved_state_reopens_the_view_as_the_reader_left_it() => headless.OnUiThread(() =>
    {
        string saved;
        using (var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English)))
        {
            rig.ViewOf<NthUiStepperView>().Stepper.Press(1);
            rig.Key(rig.ViewOf<NthUiSegmentedView>().Track, Key.End);
            saved = rig.View.SaveState();
        }
        using var reopened = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English, savedState: saved));
        Assert.Equal("4", reopened.ViewOf<NthUiStepperView>().Stepper.Shown);
        Assert.Equal(2, reopened.ViewOf<NthUiSegmentedView>().Track.Selected);
        Assert.Equal(NthUiValue.FormatNumber(Math.Round(4 * .051 * 730), NthUiRig.English), reopened.TextsOf("table")[3].TrimStart('$'));
    });
}
