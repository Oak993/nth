using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-15")]
public sealed class WebReviewBrushTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discarded_dynamic_brushes_are_collected_while_their_control_stays_attached(bool live) => headless.OnUiThread(() =>
    {
        var control = new Border();
        var window = new Window { Content = control };
        window.Show();
        try
        {
            var refs = WebReviewBrushReferences.Drop(control, live);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Equal(0, refs.Count(reference => reference.TryGetTarget(out _)));
            GC.KeepAlive(control);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Repeated_role_brush_reads_do_not_allocate_or_multiply_control_subscriptions() => headless.OnUiThread(() =>
    {
        var control = new Border();
        var color = SwiftColor.Dynamic(SwiftColor.Black, SwiftColor.White).InHighContrast(ContrastRole.Text);
        var brush = color.Brush(control);
        for (var i = 0; i < 10; i++) _ = color.Brush(control);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) _ = color.Brush(control);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        EnvironmentValues.SetAccessibilityHighContrast(control, HighContrastPalette.Black);
        ContrastSurface.MatchTheme(control);
        Assert.Equal(HighContrastPalette.Black.For(ContrastRole.Text), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
        EnvironmentValues.SetAccessibilityHighContrast(control, HighContrastPalette.White);
        ContrastSurface.MatchTheme(control);
        Assert.Equal(HighContrastPalette.White.For(ContrastRole.Text), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
        Assert.Equal(0, allocated);
    });
}
