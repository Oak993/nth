using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Fixtures;

// Managed implementations only: no COM client, native worker or real provider is constructed.
internal sealed class PcFakeUiaPatterns : IUIAutomationInvokePattern, IUIAutomationTogglePattern,
    IUIAutomationSelectionItemPattern, IUIAutomationExpandCollapsePattern, IUIAutomationValuePattern,
    IUIAutomationRangeValuePattern, IUIAutomationScrollPattern, IUIAutomationScrollItemPattern, IUIAutomationWindowPattern
{
    public List<string> Mutations { get; } = [];
    public Action? BeforeResolve { get; set; }
    public Action? BeforeRangeRead { get; set; }
    public Action? AfterMutation { get; set; }
    public int ResolveResult { get; set; }
    public int MutationResult { get; set; }
    public object? ResolvedPattern { get; set; }
    public bool MissingPattern { get; set; }

    public (int HResult, object? Pattern) Resolve(int id)
    {
        BeforeResolve?.Invoke();
        return (ResolveResult, MissingPattern ? ResolvedPattern : this);
    }

    private int Mutate(string name)
    {
        Mutations.Add(name);
        AfterMutation?.Invoke();
        return MutationResult;
    }

    public int Focus() => Mutate("focus");
    public int Invoke() => Mutate("invoke");
    public int Toggle() => Mutate("toggle");
    public int Select() => Mutate("select");
    public int Expand() => Mutate("expand");
    public int Collapse() => Mutate("collapse");
    public int SetValue(string value) => Mutate("value:" + value);
    public int SetValue(double value) => Mutate("range:" + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public int Scroll(int horizontalAmount, int verticalAmount) => Mutate("scroll");
    public int ScrollIntoView() => Mutate("scroll_into_view");
    public int Close() => Mutate("close");
    public int SetWindowVisualState(int state) => Mutate("window:" + state.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public int WaitForInputIdle(int milliseconds, out int success) { success = 1; return 0; }
    public int get_CurrentValue(out string? value) { value = "5"; return 0; }
    public int get_CurrentValue(out double value) { value = 5; return 0; }
    public int get_CurrentIsReadOnly(out int readOnly) { BeforeRangeRead?.Invoke(); readOnly = 0; return 0; }
    public int get_CurrentMinimum(out double value) { BeforeRangeRead?.Invoke(); value = 0; return 0; }
    public int get_CurrentMaximum(out double value) { BeforeRangeRead?.Invoke(); value = 10; return 0; }
}
