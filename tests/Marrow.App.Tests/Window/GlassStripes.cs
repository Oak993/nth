using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// ED-236: the high-contrast stripes GlassWindowNativeTests shows behind a glass window.
internal sealed class Stripes : Control
{
    private IBrush? fill;

    /// <summary>A brush drawn over the whole backdrop instead of the stripes (the corner tests' bands of four colours): a busier backdrop that is still the same down every column.</summary>
    public IBrush? Fill
    {
        get => fill;
        set { fill = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        if (fill is not null)
        {
            context.FillRectangle(fill, new Rect(Bounds.Size));
            return;
        }
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var step = 6 / scale; // six physical pixels
        for (var x = 0.0; x < Bounds.Width; x += 2 * step)
        {
            context.FillRectangle(Brushes.White, new Rect(x, 0, step, Bounds.Height));
            context.FillRectangle(Brushes.Black, new Rect(x + step, 0, step, Bounds.Height));
        }
    }
}
