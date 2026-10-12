using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// web W1 (DESIGN 4.2, 13.3): the shared Splitter's automation peer: a named thumb whose localized type carries the
// orientation, a range value always inside its range (collapsed included), the state's text, the percentage and the
// help hint in the help text, set-value clamped, and property changes raised as the value moves.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-2")]
public sealed class SplitterAutomationTests(HeadlessSession headless)
{
    [Fact]
    public void The_peer_is_a_named_thumb_with_its_orientation_and_a_range_value_inside_its_range() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        rig.Splitter.HelpHint = "Double-click for the default size";
        var peer = ControlAutomationPeer.CreatePeerForElement(rig.Splitter);
        Assert.Equal(AutomationControlType.Thumb, peer.GetAutomationControlType());
        Assert.Equal("vertical splitter", peer.GetLocalizedControlType());
        Assert.Equal("Resize test pane", peer.GetName());
        Assert.True(peer.IsKeyboardFocusable());
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.Equal(0, range.Minimum); // the collapsed position is part of the range
        Assert.Equal(440, range.Maximum);
        Assert.Equal(268, range.Value);
        Assert.Equal(8, range.SmallChange);
        Assert.Equal(48, range.LargeChange);
        Assert.False(range.IsReadOnly);
        Assert.Equal("268", ((IValueProvider)peer).Value);
        Assert.Equal("61 %. Double-click for the default size", peer.GetHelpText());

        rig.Splitter.IsActive = false;
        Assert.False(peer.IsKeyboardFocusable());
    });

    [Fact]
    public void Collapsed_the_value_is_the_collapsed_position_still_inside_the_range_with_its_own_text() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        rig.FocusByKeyboard();
        rig.Key(Key.Space);
        var peer = ControlAutomationPeer.CreatePeerForElement(rig.Splitter);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.Equal(0, range.Value);
        Assert.InRange(range.Value, range.Minimum, range.Maximum);
        Assert.Equal("Sidebar hidden", ((IValueProvider)peer).Value);
        Assert.True(rig.Splitter.LastReport.IsCollapsed);
    });

    [Fact]
    public void Set_value_clamps_into_the_range_and_moves_the_pane_and_a_read_only_target_refuses() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.Splitter));
        range.SetValue(300);
        rig.Settle();
        Assert.Equal(300, rig.Size, 3);
        range.SetValue(5000);
        rig.Settle();
        Assert.Equal(440, rig.Size, 3);
        Assert.Equal(440, range.Value);

        var plain = new Splitter(SplitterOrientation.Horizontal, new SplitterTarget
        {
            OnReport = () => SplitterMath.Report(10, false, new SplitterLimits(0, 20, 10)),
        });
        var peer = ControlAutomationPeer.CreatePeerForElement(plain);
        Assert.Equal("horizontal splitter", peer.GetLocalizedControlType());
        var readOnly = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.True(readOnly.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => readOnly.SetValue(5));
        Assert.Equal("corner splitter",
            ControlAutomationPeer.CreatePeerForElement(new Splitter(SplitterOrientation.Both, new SplitterTarget())).GetLocalizedControlType());
    });

    [Fact]
    public void Value_changes_raise_range_and_value_property_changes() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var peer = ControlAutomationPeer.CreatePeerForElement(rig.Splitter);
        var changes = new List<(AutomationProperty Property, object? Old, object? New)>();
        peer.PropertyChanged += (_, e) => changes.Add((e.Property, e.OldValue, e.NewValue));
        rig.FocusByKeyboard();
        rig.Key(Key.Right);
        Assert.Contains(changes, change => change.Property == RangeValuePatternIdentifiers.ValueProperty && Equals(change.Old, 268.0) && Equals(change.New, 276.0));
        Assert.Contains(changes, change => change.Property == ValuePatternIdentifiers.ValueProperty && Equals(change.New, "276"));
    });

    [Fact]
    public void Every_reported_value_lies_inside_its_reported_range_through_drags_keys_and_collapses() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.Splitter));
        var random = new Random(41);
        var keys = new[] { Key.Left, Key.Right, Key.Home, Key.End, Key.Enter, Key.Space };
        rig.FocusByKeyboard();
        for (var i = 0; i < 200; i++)
        {
            if (random.Next(3) == 0)
            {
                var at = rig.Centre;
                rig.Down(at);
                rig.Move(at + rig.Along(random.Next(-400, 400)), RawInputModifiers.LeftMouseButton);
                Assert.InRange(range.Value, range.Minimum, range.Maximum);
                rig.Up(at + rig.Along(random.Next(-400, 400)));
                rig.FocusByKeyboard();
            }
            else
            {
                rig.Key(keys[random.Next(keys.Length)], random.Next(2) == 0 ? RawInputModifiers.Shift : RawInputModifiers.None);
            }
            Assert.InRange(range.Value, range.Minimum, range.Maximum);
            if (random.Next(4) == 0) rig.Settle();
        }
    });
}
