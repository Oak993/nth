using SkiaSharp;

namespace Marrow.App.Views.Composer;

public readonly record struct StrikeInkPalette(SKColor Core, SKColor MidA, SKColor MidB, SKColor HaloA, SKColor HaloB)
{
    public SKColor At(int i) => i switch { 0 => Core, 1 => MidA, 2 => MidB, 3 => HaloA, 4 => HaloB, _ => throw new ArgumentOutOfRangeException(nameof(i)) };
}
