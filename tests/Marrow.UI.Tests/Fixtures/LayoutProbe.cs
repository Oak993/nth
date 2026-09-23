using Avalonia;
using Avalonia.Controls;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// A measuring instrument, not a sample of a Swift type: a leaf control that wants a fixed size, records the
/// constraint its measure pass was given and the size its arrange pass was given, and draws nothing. Layout
/// rounding is off so the recorded numbers are the exact doubles the layout above it computed (a SwiftUI
/// proposal is not rounded; AppZoom.swift:48-50 divides it as a CGFloat).
/// </summary>
public sealed class LayoutProbe : Control
{
    private readonly Size wanted;

    public LayoutProbe(Size wanted)
    {
        this.wanted = wanted;
        UseLayoutRounding = false;
    }

    /// <summary>Every constraint MeasureOverride received, in order.</summary>
    public List<Size> Measured { get; } = [];

    /// <summary>Every final size ArrangeOverride received, in order.</summary>
    public List<Size> Arranged { get; } = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        Measured.Add(availableSize);
        return wanted;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Arranged.Add(finalSize);
        return finalSize;
    }
}
