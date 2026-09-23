using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// The live-resize remedy `stretch` (OD-0017) as ED-101 builds it. Expectations come from ED-101 and
// porting/probes/p4b/RESULT.md, "How the production chrome should implement it" and "What the strip is": Avalonia 12.1.2
// sizes the content SpriteVisual `_visual` absolutely (ResizeIfNeeded: `_visual.SetSize(size)`), its surface brush
// reads back Stretch Uniform, and the remedy makes the content visual Size (0,0) with RelativeSizeAdjustment (1,1),
// the brush Stretch Fill, and parks a detached sprite visual in `_visual` for ResizeIfNeeded. On close the real visual
// goes back before WinUiCompositedWindow.Dispose releases `_visual`. Nothing changes at rest (RESULT.md "Rest
// geometry"). The real-window tests run Avalonia.Win32 12.1.2 in an isolated load context (Fixtures/ResizeStretchFixtures.cs).
[Collection("Headless")]
public sealed class ResizeStretchTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-101")]
    public void Avalonia_12_1_2_has_every_member_the_remedy_resolves()
    {
        var readings = RealWin32.Run(nameof(RealWindowDriver.Members));

        Assert.Equal("12.1.2.0", readings["avalonia.base.version"]);
        Assert.Equal("12.1.2.0", readings["avalonia.controls.version"]);
        Assert.Equal("12.1.2.0", readings["avalonia.win32.version"]);
        var required = readings["required"].Split(';');
        foreach (var member in new[]
        {
            "Avalonia.Controls.Window.PlatformImpl",
            "Avalonia.Win32.WindowImpl._glSurface",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface._window",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow._shared",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow._visual",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow._surfaceBrush",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositionShared.SyncRoot",
            "Avalonia.Win32.WinRT.Composition.WinUiCompositionShared.Compositor",
            "Avalonia.Win32.WinRT.IVisual.SetSize",
            "Avalonia.Win32.WinRT.IVisual2.SetRelativeSizeAdjustment",
            "Avalonia.Win32.WinRT.IVisual2.get_RelativeSizeAdjustment",
            "Avalonia.Win32.WinRT.ICompositionSurfaceBrush.SetStretch",
            "Avalonia.Win32.WinRT.ICompositionSurfaceBrush.get_Stretch",
            "Avalonia.Win32.WinRT.CompositionStretch.Fill",
            "Avalonia.Win32.WinRT.ICompositor.CreateSpriteVisual",
            "MicroCom.Runtime.MicroComRuntime.QueryInterface",
        })
            Assert.Contains(member, required);
        Assert.Equal("", readings["missing"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void After_the_first_frame_the_content_visual_follows_the_window_and_the_brush_stretches_to_fill()
    {
        var r = RealWin32.Run(nameof(RealWindowDriver.ApplyResizeClose));

        Assert.Equal("Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface", r["surface.type"]);
        // As Avalonia made it: absolute size equal to the client area, no relative size, brush Uniform.
        Assert.Equal(r["before.client.px"], r["before.content.size"]);
        Assert.Equal("0,0", r["before.content.relative"]);
        Assert.Equal("Uniform", r["before.stretch"]);
        // The read-backs after the remedy.
        Assert.Equal("True", r["applied"]);
        Assert.Equal("0", r["logs"]);
        Assert.Equal("0,0", r["after.content.size"]);
        Assert.Equal("1,1", r["after.content.relative"]);
        Assert.Equal("Fill", r["after.stretch"]);
        Assert.Equal("False", r["after.parked.is.content"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void ResizeIfNeeded_sizes_the_parked_visual_while_the_content_visual_stays_relative()
    {
        var r = RealWin32.Run(nameof(RealWindowDriver.ApplyResizeClose));

        Assert.Equal("True", r["resized.seen"]);
        Assert.NotEqual(r["before.client.px"], r["resized.client.px"]);
        Assert.Equal(r["resized.client.px"], r["resized.parked.size"]);
        Assert.Equal("0,0", r["resized.content.size"]);
        Assert.Equal("1,1", r["resized.content.relative"]);
        Assert.Equal("Fill", r["resized.stretch"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void On_close_the_real_visual_is_back_in_place_before_Avalonia_disposes_the_surface()
    {
        var r = RealWin32.Run(nameof(RealWindowDriver.ApplyResizeClose));

        Assert.Equal("True", r["closed.surface.released"]);
        Assert.Equal("True", r["closed.visual.is.content"]);
        Assert.Equal("True", r["closed.content.disposed"]);
        Assert.Equal("True", r["closed.parked.disposed"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void Attach_applies_the_remedy_after_the_first_frame_and_undoes_it_on_close()
    {
        var r = RealWin32.Run(nameof(RealWindowDriver.AttachClose));

        Assert.Equal("True", r["pending.before.show"]);
        Assert.Equal("True", r["applied"]);
        Assert.Equal("Fill", r["stretch"]);
        Assert.Equal("True", r["closed.surface.released"]);
        Assert.Equal("False", r["closed.visual.is.parked"]);
        Assert.Equal("True", r["closed.parked.disposed"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void Nothing_changes_at_rest()
    {
        var r = RealWin32.Run(nameof(RealWindowDriver.AtRest));

        Assert.Equal("True", r["applied"]);
        Assert.Equal("True", r["frames.seen"]);
        foreach (var key in new[] { "position", "client.size", "client.px", "window.rect", "state", "scene.px" })
            Assert.Equal(r["before." + key], r["after." + key]);
        // The surface Avalonia renders is the client area, so Stretch Fill maps it one to one.
        Assert.Equal(r["after.client.px"], r["after.scene.px"]);
    }

    // ED-101: "if any member is missing or a read-back differs it logs once and leaves the window as Avalonia made it";
    // RESULT.md step 3. Each case changes nothing but what the remedy sees (ResizeStretch.ReadBack, ResizeStretch.Unresolved)
    // on a real Win32 window after its first frame, and applies twice: the window must be exactly as before, with
    // Avalonia's content visual in `_visual`, its absolute size equal to the client area, RelativeSizeAdjustment (0,0)
    // and the brush Uniform, and one log that names the cause.
    [Theory]
    [Trait("Decision", "ED-101")]
    [InlineData(nameof(RealWindowDriver.RelativeReadBackDiffers), "RelativeSizeAdjustment (0,0)")]
    [InlineData(nameof(RealWindowDriver.StretchReadBackDiffers), "Stretch Uniform")]
    [InlineData(nameof(RealWindowDriver.ReadBackThrows), "read-back refused")]
    [InlineData(nameof(RealWindowDriver.MemberMissing), "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow._surfaceBrush")]
    public void When_a_member_is_missing_or_a_read_back_differs_it_logs_once_and_leaves_the_real_window_as_Avalonia_made_it(
        string scenario, string cause)
    {
        var r = RealWin32.Run(scenario);

        // As Avalonia made it before the attempt.
        Assert.Equal(r["before.client.px"], r["before.content.size"]);
        Assert.Equal("0,0", r["before.content.relative"]);
        Assert.Equal("Uniform", r["before.stretch"]);
        // Not applied, logged once.
        Assert.Equal("False", r["applied"]);
        Assert.Equal("1", r["logs"]);
        Assert.Contains(cause, r["log"], StringComparison.Ordinal);
        // Left as Avalonia made it.
        Assert.Equal("True", r["after.visual.is.content"]);
        Assert.Equal("False", r["after.content.disposed"]);
        Assert.Equal(r["before.client.px"], r["after.client.px"]);
        Assert.Equal(r["after.client.px"], r["after.content.size"]);
        Assert.Equal("0,0", r["after.content.relative"]);
        Assert.Equal("Uniform", r["after.stretch"]);
        // Avalonia's ResizeIfNeeded keeps sizing its own content visual, and close releases it as usual.
        Assert.Equal("True", r["resized.seen"]);
        Assert.Equal("True", r["resized.visual.is.content"]);
        Assert.Equal("0,0", r["resized.content.relative"]);
        Assert.Equal("Uniform", r["resized.stretch"]);
        Assert.Equal("True", r["closed.surface.released"]);
        Assert.Equal("True", r["closed.content.disposed"]);
    }

    [Fact]
    [Trait("Decision", "ED-101")]
    public void Without_the_Win32_composition_members_it_logs_once_and_leaves_the_window_as_Avalonia_made_it() => headless.OnUiThread(() =>
    {
        var logs = new List<string>();
        using var log = ResizeStretch.Log.Override(new ResizeStretchLog(logs.Add));
        var first = new Window { Width = 400, Height = 300 };
        var second = new Window { Width = 400, Height = 300 };
        first.Show();
        second.Show();
        Dispatcher.UIThread.RunJobs();
        var platform = first.PlatformImpl;

        Assert.Null(ResizeStretch.Apply(first));
        Assert.Null(ResizeStretch.Apply(second));
        var attached = ResizeStretch.Attach(second);
        Dispatcher.UIThread.RunJobs();

        Assert.True(attached.IsCompleted);
        Assert.Null(attached.Result);
        Assert.Single(logs);
        Assert.Contains("_glSurface", logs[0], StringComparison.Ordinal);
        Assert.Same(platform, first.PlatformImpl);
        Assert.True(first.IsVisible);
        first.Close();
        second.Close();
    });
}
