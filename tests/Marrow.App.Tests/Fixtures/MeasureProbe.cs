using Avalonia;
using Avalonia.Controls;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A stand-in section of a fixed height that counts how often it is measured (CZ-T68: nothing measures a section during a scroll).</summary>
internal sealed class MeasureProbe : Border
{
    /// <summary>How many times <see cref="MeasureOverride"/> has run.</summary>
    internal int Measures { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        Measures++;
        return base.MeasureOverride(availableSize);
    }
}
