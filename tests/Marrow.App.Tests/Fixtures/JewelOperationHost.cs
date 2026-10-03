using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

/// <summary>OD-0090: a control that draws one knob operation, so a test renders it the way a slider's view does (headless:
/// the CPU renderer).</summary>
public sealed class JewelOperationHost : Control
{
    internal JewelKnobOperation? Operation { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Operation is { } op) context.Custom(op);
    }
}
