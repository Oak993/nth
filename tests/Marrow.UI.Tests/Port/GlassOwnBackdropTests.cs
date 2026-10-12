// PORT-ONLY (OD-0121, OD-0125; ED-NEW-glasslook-4): GlassEffect.OwnBackdrop, the voice pill's frost. The approved page's pill (voice-composer.html
// .vpill, backdrop-filter: blur(6px) saturate(1.7)) is blurred by Chrome from the pixels inside its border box alone, snapped to the device pixel
// grid, with mirrored edges (measured in Chrome 154: a black line 4 px above the element does not reach it; two black rows at its top edge read as
// four). A piece that asks for its own backdrop reads only that box; every other glass reads round its shape.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassOwnBackdropTests(HeadlessSession headless)
{
    private const int Left = 60, Top = 40, Width = 300, Height = 40, WindowWidth = 420, WindowHeight = 120;

    // The voice pill's glass (VoicePill.ApplyTint): a capsule, no bend, no contact line, the release look; with or without its own backdrop.
    private static Border Pill(bool own)
    {
        var piece = new Border
        {
            Background = Brushes.Transparent, Width = Width, Height = Height, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Left, Top, 0, 0),
        };
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).WithoutRefraction().WithoutContactLine();
        SwiftGlass.SetEffect(piece, (own ? effect.WithOwnBackdrop() : effect).WithLook(GlassLook.Release));
        return piece;
    }

    private static Border Bar(double left, double top, double width, double height, byte level) => new()
    {
        Width = width, Height = height, Margin = new Thickness(left, top, 0, 0), Background = new SolidColorBrush(Color.FromRgb(level, level, level)),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };

    private static Panel Backdrop(byte level, params Border[] bars)
    {
        var panel = new Panel { Background = new SolidColorBrush(Color.FromRgb(level, level, level)) };
        foreach (var bar in bars) panel.Children.Add(bar);
        return panel;
    }

    private static byte[] Render(Control backdrop, Control glass, bool light)
    {
        var window = new Window
        {
            Width = WindowWidth, Height = WindowHeight, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = Brushes.Black,
            Content = new Panel { Children = { backdrop, glass } },
        };
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            Assert.Equal(new PixelSize(WindowWidth, WindowHeight), size);
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            return bytes;
        }
        finally { window.Close(); }
    }

    // The largest channel difference between two frames over the piece's box.
    private static int InTheBox(byte[] a, byte[] b) =>
        Enumerable.Range(Top, Height).SelectMany(y => Enumerable.Range(Left, Width).Select(x => (y * WindowWidth + x) * 4))
            .Max(p => Enumerable.Range(0, 3).Max(c => Math.Abs(a[p + c] - b[p + c])));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-glasslook-4")]
    public void A_piece_with_its_own_backdrop_reads_nothing_past_its_box(bool light) => headless.OnUiThread(() =>
    {
        // bright bars 4 pt above the piece (the canvas composer's rim over the inline pill is 3 to 4 px above it) and 2 pt past its right end
        Panel Barred() => Backdrop(110, Bar(0, Top - 4, WindowWidth, 2, 250), Bar(Left + Width + 2, 0, 2, WindowHeight, 250));
        var ownBarred = Render(Barred(), Pill(own: true), light);
        var ownPlain = Render(Backdrop(110), Pill(own: true), light);
        Assert.True(InTheBox(ownBarred, ownPlain) == 0, $"inside its box the piece changes by {InTheBox(ownBarred, ownPlain)} levels when the bars past it are drawn");
        // every other glass reads round its shape, so the same bars reach its frost (what makes the check above able to fail)
        var roundBarred = Render(Barred(), Pill(own: false), light);
        var roundPlain = Render(Backdrop(110), Pill(own: false), light);
        Assert.True(InTheBox(roundBarred, roundPlain) >= 4, $"a glass that reads round its shape changes by {InTheBox(roundBarred, roundPlain)} levels inside its box");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-4")]
    public void Only_a_glass_that_asks_reads_its_own_backdrop()
    {
        Assert.False(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).OwnBackdrop);
        Assert.True(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).WithOwnBackdrop().OwnBackdrop);
    }
}
