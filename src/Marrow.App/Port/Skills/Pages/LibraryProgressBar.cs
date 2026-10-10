// PORT-ONLY: nth motion pack step 3 option B (OD-0136 amendment 2, ED-NEW-nth-motion). The bar of a download that is running: the nth progress bar, 4 px, whose fill springs to each new value and
// whose light passes inside the fill. (LibraryBar stays the still bar of the usage meters, which are not a job in progress.)
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>A running download's bar: a 4 point nth bar whose fill springs to each new <see cref="Set"/> value (a step of progress is never a jump).</summary>
internal sealed class LibraryProgressBar : Decorator
{
    private readonly NthProgressBar bar = new() { Height = 4 };

    public LibraryProgressBar()
    {
        Height = 4;
        IsHitTestVisible = false;
        Child = bar;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    internal NthProgressBar Bar => bar;

    /// <summary>The fraction the fill is drawn at now (it follows <see cref="Target"/>).</summary>
    public double Drawn => bar.Shown;

    public double Target => bar.Value;

    public void Set(double value, bool animated)
    {
        var clamped = Math.Clamp(value, 0, 1);
        if (Target == clamped) return;
        if (!animated || clamped < bar.Value) bar.Jump(clamped); // a new download starts from the left: the fill never runs backwards
        else bar.Value = clamped;
    }
}
