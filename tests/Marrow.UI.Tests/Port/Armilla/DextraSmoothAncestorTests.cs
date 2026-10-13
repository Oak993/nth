using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-9")]
public sealed class DextraSmoothAncestorTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void An_unseen_mark_places_its_new_glyph_at_full_size_and_resumes_visible(bool coveredBeforeHide, bool hideAncestor) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        AmbientWindowState? availability = null;
        AmbientWindowState.TestFactory = window => availability = new AmbientWindowState(window, test: true);
        var mark = new ArmillaMark { MarkSize = 20, Kind = ArmillaKind.Row, State = ArmillaState.Working };
        var parent = new Border { Child = mark };
        var window = new Window { Width = 100, Height = 100, Content = parent };
        try
        {
            window.Show(); Pump(clock, 8);
            // Observe the existing render engine while its hidden visual has no callbacks. No extra runtime seam/work.
            var handler = Assert.IsType<ArmillaHandler>(typeof(ArmillaMark).GetField("handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mark));
            var engine = Assert.IsType<ArmillaEngine>(typeof(ArmillaHandler).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler));
            Assert.NotNull(availability);
            if (coveredBeforeHide) { availability.SetAvailable(false); Pump(clock, 4); }
            if (hideAncestor) parent.IsVisible = false;
            Pump(clock, 4);
            var drawn = mark.FramesDrawn;
            if (!coveredBeforeHide) { mark.IsVisible = false; mark.IsVisible = true; }
            mark.Glyph = ArmillaGlyph.Done; Pump(clock, 4);
            Assert.Equal(drawn, mark.FramesDrawn);
            Assert.Equal(1, engine.GlyphScale); // DESIGN 2.5: a glyph beginning off screen is placed at full size.
            Assert.False(engine.Visible);
            availability.SetAvailable(true);
            parent.IsVisible = true; Pump(clock, 6);
            Assert.True(mark.FramesDrawn > drawn);
            Assert.True(engine.Visible);
            Assert.Equal(1, engine.GlyphScale);
        }
        finally { window.Close(); Pump(clock, 1); AmbientWindowState.TestFactory = null; }
    });

    private static void Pump(ManualTimeProvider clock, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(17));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
