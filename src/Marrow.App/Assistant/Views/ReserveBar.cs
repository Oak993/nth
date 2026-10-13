// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.4, 11.5; the approved canvas "Usage reserves"; unit AS-13; ED-NEW-dextra-as13-10). No Swift twin.
// One account's bar: what is used, what the plan adds, the reserve (hatched) from the floor to the end, and the floor itself as a
// tick. Drawn, not laid out: a value that ticks calls InvalidateVisual and nothing is measured again. The bar says nothing by
// colour alone: the floor is a tick and the reserve a hatch, and the row beside it says every number in words.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>The bar of the canvas's Reserves panel (used, planned, reserve, floor).</summary>
public sealed class ReserveBar : Control
{
    public const double BarHeight = 8;
    public const double BoxHeight = 16;
    public const double HatchStep = 6;
    public const double FloorTickWidth = 2;

    public ReserveBar()
    {
        Height = BoxHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Percent used now, 0 to 100.</summary>
    public double Used { get; private set; }

    /// <summary>Percent the plan adds on top of what is used.</summary>
    public double Planned { get; private set; }

    /// <summary>The floor that binds, or null for no reserve (a floor of 100 or more is none).</summary>
    public double? Floor { get; private set; }

    /// <summary>Where the floor stood before it eased, when it has moved (shown as a small dot above the bar).</summary>
    public double? Unadjusted { get; private set; }

    public void Set(double used, double planned, double? floor, double? unadjusted = null)
    {
        var next = (Clamp(used), Math.Max(0, double.IsFinite(planned) ? planned : 0), floor is { } f && f < 100 && double.IsFinite(f) ? Clamp(f) : (double?)null,
            unadjusted is { } u && floor is { } e && u < e - 0.05 ? Clamp(u) : (double?)null);
        if (next == (Used, Planned, Floor, Unadjusted)) return;
        (Used, Planned, Floor, Unadjusted) = next;
        InvalidateVisual();
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, BoxHeight);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        var theme = ActualThemeVariant;
        var top = (BoxHeight - BarHeight) / 2;
        var track = new RoundedRect(new Rect(0, top, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(new SolidColorBrush(Chrome.Overlay(0.10).Resolve(theme)), null, track);
        using (context.PushClip(track))
        {
            if (Floor is { } floor)
            {
                var reserveLeft = width * floor / 100;
                context.DrawRectangle(new SolidColorBrush(Chrome.Overlay(0.05).Resolve(theme)), null, new Rect(reserveLeft, top, width - reserveLeft, BarHeight));
                var pen = new Pen(new SolidColorBrush(Chrome.Overlay(0.32).Resolve(theme)), 1);
                for (var x = reserveLeft - BarHeight; x < width; x += HatchStep)
                    context.DrawLine(pen, new Point(x, top + BarHeight), new Point(x + BarHeight, top));
            }
            var accent = Chrome.Accent.Resolve(theme);
            var usedWidth = width * Used / 100;
            context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(0, top, usedWidth, BarHeight));
            var plannedWidth = Math.Min(width - usedWidth, width * Planned / 100);
            if (plannedWidth > 0)
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(accent.A * 0.45), accent.R, accent.G, accent.B)), null, new Rect(usedWidth, top, plannedWidth, BarHeight));
        }
        if (Floor is { } tick)
        {
            var x = Math.Min(width - FloorTickWidth, Math.Max(0, width * tick / 100 - FloorTickWidth / 2));
            context.DrawRectangle(new SolidColorBrush(Chrome.PrimaryText.Resolve(theme)), null, new Rect(x, top - 2, FloorTickWidth, BarHeight + 4));
        }
        if (Unadjusted is { } before)
        {
            var x = width * before / 100;
            context.DrawEllipse(new SolidColorBrush(Chrome.PrimaryText.Resolve(theme)), null, new Point(x, top - 4), 2, 2);
        }
    }
}

/// <summary>An account's name, its numbers in words and its bar: one row of the plan card and of the Reserves view.</summary>
public sealed class ReserveBarRow : Border
{
    private readonly TextBlock name = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock numbers = new() { TextWrapping = TextWrapping.Wrap };

    public ReserveBarRow()
    {
        SwiftFont.System(12.5, SwiftFont.Weight.Semibold).Apply(name);
        name.Foreground = Chrome.PrimaryText.Brush(name);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(numbers);
        numbers.Foreground = Chrome.SecondaryText.Brush(numbers);
        Bar = new ReserveBar();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3, Children = { name, numbers, Bar } };
        Child = stack;
        Focusable = false;
    }

    public ReserveBar Bar { get; }

    public string AccountName => name.Text ?? "";

    public string Numbers => numbers.Text ?? "";

    public void Set(string accountName, string line, double used, double planned, double? floor, double? unadjusted = null)
    {
        name.Text = accountName;
        numbers.Text = line;
        Bar.Set(used, planned, floor, unadjusted);
        // One sentence for a screen reader: the bar itself is raw.
        AutomationProperties.SetName(this, accountName + ": " + line);
    }

    /// <summary>"22.0% used · plan +12.0% · floor 76.6% · weekly" (the canvas's line).</summary>
    public static string Line(double used, double? planned, double? floor, string window, bool rough = false, string? extra = null)
    {
        var parts = new List<string> { $"{used.ToString("0.0", CultureInfo.InvariantCulture)}% used" };
        if (planned is { } plan) parts.Add($"plan +{Math.Max(0, plan).ToString("0.0", CultureInfo.InvariantCulture)}%");
        parts.Add(floor is { } f && f < 100 ? $"floor {f.ToString("0.0", CultureInfo.InvariantCulture)}%" : "no reserve");
        if (window.Length > 0) parts.Add(window);
        if (rough) parts.Add("estimate rough");
        if (extra is { Length: > 0 }) parts.Add(extra);
        return string.Join(" \U000000B7 ", parts);
    }
}
