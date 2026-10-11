using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port;

// Warm actual launch/grow/glow/still drawing and JIT the compositor entry points before the stage is shown.
// This creates no GPU object: the painter used by the window still belongs exclusively to its compositor.
public static class AuroraBootPreparation
{
    private static readonly Lazy<bool> prepared = new(() =>
    {
        using var work = AuroraBootTrace.Begin("Boot: before-show drawing/JIT");
        foreach (var method in typeof(AuroraBootHandler).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
        using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Boot drawing preparation made no surface.");
        using var painter = new AuroraBootPainter();
        var window = new Rect(0, 0, 64, 42);
        foreach (var reduce in new[] { false, true })
        foreach (var dark in new[] { false, true })
        {
            var sequence = new AuroraBootSequence(window, reduce);
            // OD-0142: the nth pop (.1, .3), draw (.7), bead (1.1), the settled hold (1.5), the grow and glow.
            foreach (var seconds in new[] { .1, .3, .7, 1.1, 1.5, 1.9, 2.2, 2.8, 3.8 })
                painter.Draw(surface.Canvas, new Size(64, 64), window, sequence.At(seconds, 0), seconds, dark, reduce);
            painter.Draw(surface.Canvas, new Size(64, 64), window, sequence.At(4.6, null), 4.6, dark, reduce);
        }
        return true;
    });
    public static void Prepare() => _ = prepared.Value;
}
