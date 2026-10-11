using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-4). The pure mapping from UIA property values to a tree node and
// from pattern HRESULTs to error codes. UIA itself is never called: values come from a dictionary.
[Trait("Decision", "ED-NEW-pcctl-4")]
public sealed class UiaReaderTests
{
    private static PcUiNode Node(Dictionary<int, object?> values) => UiaNodes.Node(id => values.GetValueOrDefault(id));

    [Fact]
    public void A_button_maps_role_name_id_bounds_runtime_id_shortcut_and_actions()
    {
        var node = Node(new()
        {
            [30000] = new[] { 42, 1234, 5 }, [30001] = new[] { 10.4, 20.6, 80.0, 24.0 }, [30004] = "button", [30005] = "Save",
            [30011] = "SaveButton", [30006] = "Ctrl+S", [30010] = true, [30009] = true, [30031] = true,
        });
        Assert.Equal("42.1234.5", node.RuntimeId);
        Assert.Equal("button", node.Role);
        Assert.Equal("Save", node.Name);
        Assert.Equal("SaveButton", node.AutomationId);
        Assert.Equal("Ctrl+S", node.Shortcut);
        Assert.Equal(new PcRect(10, 21, 80, 24), node.Bounds);
        Assert.Equal(PcActions.Invoke | PcActions.Focus, node.Actions);
        Assert.Equal(PcStates.None, node.States);
    }

    [Fact]
    public void Pattern_states_count_only_when_their_pattern_is_available()
    {
        var withPatterns = Node(new()
        {
            [30041] = true, [30086] = 1, [30028] = true, [30070] = 0, [30036] = true, [30079] = true, [30010] = false,
            [30034] = true, [30058] = true, [30008] = true,
        });
        Assert.Equal(PcStates.Checked | PcStates.Collapsed | PcStates.Selected | PcStates.Disabled | PcStates.Scrollable | PcStates.Focused, withPatterns.States);
        var without = Node(new() { [30086] = 1, [30070] = 1, [30079] = true, [30058] = true });
        Assert.Equal(PcStates.None, without.States);
        Assert.Equal(PcStates.Mixed, Node(new() { [30041] = true, [30086] = 2 }).States & PcStates.Mixed);
        Assert.Equal(PcStates.Expanded, Node(new() { [30028] = true, [30070] = 2 }).States & PcStates.Expanded);
        Assert.Equal(PcStates.None, Node(new() { [30028] = true, [30070] = 3 }).States);
    }

    [Fact]
    public void A_password_field_never_carries_its_value()
    {
        var node = Node(new() { [30043] = true, [30045] = "hunter2", [30019] = true });
        Assert.True(node.Has(PcStates.Password));
        Assert.Null(node.Value);
    }

    [Fact]
    public void Values_come_from_the_value_pattern_else_the_range_value_in_invariant_digits()
    {
        Assert.Equal("report.txt", Node(new() { [30043] = true, [30045] = "report.txt" }).Value);
        Assert.Equal("42.5", Node(new() { [30033] = true, [30047] = 42.5 }).Value);
        Assert.Null(Node(new() { [30045] = "no pattern" }).Value);
        Assert.True(Node(new() { [30043] = true, [30046] = true }).Has(PcStates.ReadOnly));
        Assert.True(Node(new() { [30033] = true, [30048] = true }).Has(PcStates.ReadOnly));
    }

    [Fact]
    public void Unsupported_values_and_missing_properties_fall_back_safely()
    {
        // UIA answers an unsupported property with a sentinel COM object; any value of the wrong type counts as missing.
        var node = Node(new() { [30005] = new object(), [30001] = new object(), [30003] = 50000, [30010] = new object() });
        Assert.Equal("", node.Name);
        Assert.Equal("button", node.Role);
        Assert.True(node.Bounds.IsEmpty);
        Assert.Equal("", node.RuntimeId);
        Assert.False(node.Has(PcStates.Disabled));
        Assert.Equal("element", Node(new()).Role);
    }

    [Fact]
    public void Runtime_ids_round_trip_through_their_dotted_text()
    {
        Assert.Equal(new[] { 42, -7, 0 }, UiaNodes.ParseRuntimeId("42.-7.0"));
        Assert.Null(UiaNodes.ParseRuntimeId("42.x"));
        Assert.Null(UiaNodes.ParseRuntimeId(""));
        Assert.Equal("", UiaNodes.RuntimeIdText(null));
    }

    [Fact]
    public void Pattern_hresults_map_to_stable_error_codes()
    {
        Assert.True(UiaPatterns.Outcome(0, "press").Done);
        Assert.Equal(PcErrorCode.Disabled, UiaPatterns.Outcome(UiaPatterns.ElementNotEnabled, "press").Error);
        Assert.Equal(PcErrorCode.StaleElement, UiaPatterns.Outcome(UiaPatterns.ElementNotAvailable, "press").Error);
        Assert.Equal(PcErrorCode.Timeout, UiaPatterns.Outcome(UiaPatterns.Timeout, "press").Error);
        var other = UiaPatterns.Outcome(unchecked((int)0x80004005), "press");
        Assert.Equal(PcErrorCode.NotSupported, other.Error);
        Assert.Equal("press failed (0x80004005)", other.Reason);
    }
}
