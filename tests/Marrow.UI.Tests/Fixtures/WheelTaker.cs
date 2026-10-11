using Avalonia.Controls;
using Avalonia.Input;

namespace Marrow.UI.Tests.Fixtures;

// ED-NEW-softscroll-1: a control that takes the wheel itself (as the terminal's history does), for SoftScrollEverywhereTests.
public sealed class WheelTaker : Border
{
    public int Reports;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Reports++;
        e.Handled = true;
    }
}
