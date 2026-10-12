using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 10 (web surfaces W16a; ED-NEW-web-w16a-1): a live answer is a named group; steppers and sliders are range
// controls with their label, bounds and value; tables expose their column headers and rows; toggles, chips and checklist
// boxes are toggles; tabs and segments are tab items and radio buttons in a set; every control is named.
[Collection("Headless")]
public sealed class NthUiAccessibilityTests(HeadlessSession headless)
{
    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_stepper_and_slider_are_named_range_controls_with_their_bounds_and_value() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        Assert.Equal("Live answer", Peer(rig.View).GetName());
        var stepper = rig.ViewOf<NthUiStepperView>().Stepper;
        var peer = Peer(stepper);
        Assert.Equal(AutomationControlType.Spinner, peer.GetAutomationControlType());
        Assert.Equal("Instances", peer.GetName());
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.Equal((1d, 24d, 3d, 1d), (range.Minimum, range.Maximum, range.Value, range.SmallChange));
        Assert.Equal("Fewer instances", AutomationProperties.GetName(NthUiRig.Named<Button>(stepper, "Fewer instances")));
        NthUiRig.Named<Button>(stepper, "More instances");
        Assert.True(stepper.Focusable);
        rig.Key(stepper, Key.Up);
        Assert.Equal(4d, range.Value);
        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        var sliderPeer = Peer(slider);
        Assert.Equal(AutomationControlType.Slider, sliderPeer.GetAutomationControlType());
        Assert.Equal("Traffic", sliderPeer.GetName());
        var sliderRange = (IRangeValueProvider)sliderPeer;
        Assert.Equal((0d, 200d, 40d, 5d), (sliderRange.Minimum, sliderRange.Maximum, sliderRange.Value, sliderRange.SmallChange));
        rig.Key(slider, Key.End);
        Assert.Equal(200d, sliderRange.Value);
        rig.Key(slider, Key.PageDown);
        Assert.Equal(150d, sliderRange.Value);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Tables_expose_their_column_headers_and_name_each_row_by_its_cells() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var table = rig.Control("table");
        var headers = table.GetVisualDescendants().OfType<TextBlock>().Where(AutomationProperties.GetIsColumnHeader).Select(t => t.Text).ToList();
        Assert.Equal(["Item", "Monthly"], headers);
        Assert.Equal(AutomationControlType.Table, AutomationProperties.GetControlTypeOverride(table));
        var rows = rig.View.Document.Nodes.Where(n => n.Kind == "row").Select(n => rig.View.SlotFor(n.Key) ?? rig.View.GetVisualDescendants().OfType<NthUiSlot>().First(s => s.Key == n.Key)).ToList();
        Assert.Equal(["Item Compute, Monthly $101", "Item Egress, Monthly $24", "Item Storage, Monthly $9", "Item Total per month, Monthly $134"], rows.Select(s => AutomationProperties.GetName(s.Child!)));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Toggles_chips_and_checklist_boxes_are_toggles_and_segments_and_tabs_sit_in_a_set() => headless.OnUiThread(() =>
    {
        using var quiz = new NthUiRig(NthUiView.Show(NthUiSamples.Quiz, culture: NthUiRig.English));
        var toggle = quiz.ViewOf<NthUiToggleView>().Switch;
        var togglePeer = Peer(toggle);
        Assert.Equal("Show the answer", togglePeer.GetName());
        Assert.Equal(ToggleState.Off, Assert.IsAssignableFrom<IToggleProvider>(togglePeer).ToggleState);
        using var lite = new NthUiRig(NthUiView.Show(NthUiSamples.Lite, culture: NthUiRig.English));
        var chip = NthUiRig.Named<ToggleButton>(lite.Control("chips"), "Living room");
        Assert.Equal(ToggleState.On, Assert.IsAssignableFrom<IToggleProvider>(Peer(chip)).ToggleState);
        var box = lite.Control("checklist").GetVisualDescendants().OfType<NthUiCheck>().First();
        Assert.Equal("Close the curtains at night", Peer(box).GetName());
        Assert.IsAssignableFrom<IToggleProvider>(Peer(box));
        using var hosting = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var segments = hosting.ViewOf<NthUiSegmentedView>().Track.Items.ToList();
        Assert.All(segments, s => Assert.Equal(AutomationControlType.RadioButton, Peer(s).GetAutomationControlType()));
        Assert.Equal([1, 2, 3], segments.Select(AutomationProperties.GetPositionInSet));
        Assert.All(segments, s => Assert.Equal(3, AutomationProperties.GetSizeOfSet(s)));
        Assert.Equal(["Europe", "US", "Asia"], segments.Select(s => Peer(s).GetName()));
        using var tabs = new NthUiRig(NthUiView.Show(NthUiSamples.Tabs, culture: NthUiRig.English));
        var items = tabs.ViewOf<NthUiTabsView>().Track.Items.ToList();
        Assert.All(items, s => Assert.Equal(AutomationControlType.TabItem, Peer(s).GetAutomationControlType()));
        Assert.Equal(ToggleState.On, ((IToggleProvider)Peer(items[0])).ToggleState);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Steps_announce_their_place_and_title_and_every_action_is_named() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Steps, culture: NthUiRig.English));
        var title = rig.Control("steps").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Install");
        Assert.Equal("Step 1 of 3: Install", AutomationProperties.GetName(title));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(title));
        using var actions = new NthUiRig(NthUiView.Show(NthUiSamples.Actions, culture: NthUiRig.English));
        var buttons = actions.View.GetVisualDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        using var code = new NthUiRig(NthUiView.Show(NthUiSamples.Code, culture: NthUiRig.English));
        NthUiRig.Named<Button>(code.View, "Copy code");
    });
}
