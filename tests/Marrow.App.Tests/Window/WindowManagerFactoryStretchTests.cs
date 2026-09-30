using System.Numerics;
using Avalonia;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Source: WindowManager.swift (B3 52bc792):93-101, WindowChrome.swift:107-132.
// Windows-only FACTORY-STRETCH adoption proof: OD-0017 / ED-101, not whole PR-WIN-007/023 acceptance.
[Collection("WindowManagerNative")]
public sealed class WindowManagerFactoryStretchTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-101")]
    public void The_real_main_factory_applies_stretch_and_reconfiguration_keeps_the_same_visuals() => native.Run(subject =>
    {
        N.Until(() => native.OnUi(() => WindowManagerStretchProbe.IsApplied(subject.Main)),
            "FACTORY-STRETCH: the factory main window never adopted the first-frame remedy.");
        native.OnUi(() =>
        {
            N.RequireOwner(subject.MainProbe.Handle);
            Assert.True(subject.Main.CanResize);
            var probe = new WindowManagerStretchProbe(subject.Main);
            Assert.NotNull(probe.Remedy);
            Assert.NotSame(probe.Content, probe.Parked);
            Assert.Same(probe.Parked, probe.CurrentVisual);
            Assert.Equal(Vector2.Zero, probe.ContentSize);
            Assert.Equal(Vector2.One, probe.ContentRelative);
            Assert.Equal("Fill", probe.Stretch);
            WindowChrome.Configure(subject.Main);
            Assert.Same(probe.Remedy, WindowManagerStretchProbe.RemedyFor(subject.Main));
            Assert.Same(probe.Parked, probe.CurrentVisual);
            Assert.Equal(Vector2.One, probe.ContentRelative);
        });
    }, factType: typeof(WindowManagerFactoryStretchTests));

    [Fact]
    [Trait("Decision", "ED-101")]
    public void The_nonresizable_Settings_factory_keeps_Avalonias_own_presentation() => native.Run(subject =>
    {
        N.Until(() => native.OnUi(() => WindowManagerStretchProbe.HasFirstSurface(subject.Settings)),
            "Settings never created its native composited surface.");
        // After the surface exists, observe a subsequent frame; a wrong factory attachment cannot hide
        // behind a pending first-frame retry. The dispatcher and renderer continue pumping off this driver.
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnUi(() => subject.Settings.RequestAnimationFrame(_ => frame.TrySetResult()));
        frame.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
        native.OnUi(() =>
        {
            N.RequireOwner(subject.SettingsProbe.Handle);
            Assert.False(subject.Settings.CanResize);
            Assert.False(WindowManagerStretchProbe.IsApplied(subject.Settings));
            var probe = new WindowManagerStretchProbe(subject.Settings);
            Assert.Null(probe.Remedy);
            Assert.Null(probe.Parked);
            Assert.Same(probe.Content, probe.CurrentVisual);
            Assert.Equal(Vector2.Zero, probe.ContentRelative);
            Assert.Equal("Uniform", probe.Stretch);
            Assert.Equal(probe.ClientPixels, probe.ContentSize);
            WindowChrome.Configure(subject.Settings);
            Assert.False(WindowManagerStretchProbe.IsApplied(subject.Settings));
            Assert.Same(probe.Content, probe.CurrentVisual);
        });
    }, factType: typeof(WindowManagerFactoryStretchTests));

    [Fact]
    [Trait("Decision", "ED-101")]
    public void Factory_resize_sizes_only_the_parked_visual_and_close_restores_content_before_disposal() => native.Run(subject =>
    {
        N.Until(() => native.OnUi(() => WindowManagerStretchProbe.IsApplied(subject.Main)),
            "FACTORY-STRETCH: the main factory did not apply the remedy.");
        var probe = native.OnUi(() => new WindowManagerStretchProbe(subject.Main));
        var initial = native.OnUi(() => probe.ClientPixels);
        native.OnUi(() => { subject.Main.Width += 64; subject.Main.Height += 48; });
        N.Until(() => native.OnUi(() => probe.ClientPixels != initial && probe.ParkedSize == probe.ClientPixels),
            "The resized factory window never presented a parked visual at its new native client size.");
        var restoredInClosed = false;
        native.OnUi(() =>
        {
            Assert.Equal(Vector2.Zero, probe.ContentSize);
            Assert.Equal(Vector2.One, probe.ContentRelative);
            Assert.Equal("Fill", probe.Stretch);
            Assert.Same(probe.Parked, probe.CurrentVisual);
            Assert.False(probe.ContentDisposed);
            Assert.False(probe.ParkedDisposed);
            subject.Main.Closed += (_, _) => restoredInClosed = ReferenceEquals(probe.CurrentVisual, probe.Content);
            // Fixture-only teardown: close this owned HWND despite the production retained-close handler.
            subject.Main.Closing += (_, e) => e.Cancel = false;
            subject.Main.Close();
            Assert.True(restoredInClosed); // Restore precedes native surface disposal, not merely eventual cleanup.
        });
        N.Until(() => native.OnUi(() => probe.SurfaceReleased), "Native composited surface survived the owned close.");
        native.OnUi(() =>
        {
            Assert.Same(probe.Content, probe.CurrentVisual);
            Assert.True(probe.ContentDisposed);
            Assert.True(probe.ParkedDisposed);
            Assert.False(N.IsWindow(subject.MainProbe.Handle));
        });
    }, factType: typeof(WindowManagerFactoryStretchTests));
}
