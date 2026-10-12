// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "Keyboard: focus a point, Up and Down change it by the axis step", 10 "nth-ui
// components are native Avalonia controls with roles, names and values"; ED-NEW-web-w16b-1). No Swift counterpart. One
// editable point as a real range control: it draws nothing and takes no pointer (the plot owns the drag), but it is what
// keyboard and assistive technology reach. Up, Down, Page Up and Page Down change the value by the axis step (range keys of
// NthUiRange); Left, Right, Home and End move to the neighbouring point, and only the point last reached is a tab stop, so a
// chart is one stop in the tab order, not one per point.
using Avalonia.Automation;
using Avalonia.Input;

namespace Marrow.UI.NthUi.Charts;

/// <summary>An editable point's keyboard and assistive-technology control.</summary>
public sealed class NthUiChartHandle : NthUiRange
{
    public NthUiChartHandle(int index)
    {
        Index = index;
        Focusable = true;
        IsHitTestVisible = false;
        Width = NthUiChartPlot.HandleSize;
        Height = NthUiChartPlot.HandleSize;
    }

    protected override Type StyleKeyOverride => typeof(NthUiChartHandle);

    /// <summary>The point's place in the series.</summary>
    public int Index { get; }

    /// <summary>The reader asked for the point one place along (+1) or back (-1).</summary>
    public event Action<int, int>? Stepped;

    /// <summary>The reader asked for the first point (false) or the last (true).</summary>
    public event Action<int, bool>? Jumped;

    /// <summary>The point took focus (true) or lost it (false).</summary>
    public event Action<int, bool>? FocusChanged;

    /// <summary>Whether the last focus came from the keyboard (the ring shows then, not after a press).</summary>
    public bool ShowsFocus { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ltr = FlowDirection == Avalonia.Media.FlowDirection.LeftToRight;
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                Stepped?.Invoke(Index, ltr ? -1 : 1);
                return;
            case Key.Right:
                e.Handled = true;
                Stepped?.Invoke(Index, ltr ? 1 : -1);
                return;
            case Key.Home:
                e.Handled = true;
                Jumped?.Invoke(Index, false);
                return;
            case Key.End:
                e.Handled = true;
                Jumped?.Invoke(Index, true);
                return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ShowsFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional or NavigationMethod.Unspecified;
        FocusChanged?.Invoke(Index, true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ShowsFocus = false;
        FocusChanged?.Invoke(Index, false);
    }

    /// <summary>Names the point for assistive technology: its category, and the value as the reader sees it.</summary>
    public void Describe(string name, string status)
    {
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetItemStatus(this, status);
    }
}
