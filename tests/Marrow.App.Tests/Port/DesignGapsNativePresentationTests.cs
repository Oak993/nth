using System.Numerics;
using Marrow.App.Port;

namespace Marrow.App.Tests.Port;

// The pinned WinUI graph exposes the blur separately and the common root above blur + framebuffer.
public sealed class DesignGapsNativePresentationTests
{
    [Fact]
    public void Open_changes_only_blur_and_close_changes_only_the_common_root_then_restores()
    {
        var platform = new Platform();
        var native = platform._glSurface._window;
        using (var presentation = Assert.IsType<SettingsNativePresentation>(SettingsNativePresentation.Bind(platform)))
        {
            presentation.Glass(.5);
            Assert.Equal(.4f, native._blur.Opacity);
            Assert.Equal(.9f, native._target.Root.Opacity);
            presentation.Close(.25, .985, new Vector2(800, 600));
            Assert.Equal(.225f, native._target.Root.Opacity);
            Assert.Equal(new Vector3(.985f, .985f, 1), native._target.Root.Scale);
            Assert.Equal(new Vector3(400, 300, 0), native._target.Root.CenterPoint);
            Assert.Equal(.4f, native._blur.Opacity); // common root owns the close fade, never a second blur fade
            Assert.Equal(0, native._blur.Disposals);
        }
        Assert.Equal(.8f, native._blur.Opacity);
        Assert.Equal(.9f, native._target.Root.Opacity);
        Assert.Equal(Vector3.One, native._target.Root.Scale);
        Assert.Equal(new Vector3(5, 6, 0), native._target.Root.CenterPoint);
        Assert.Equal(1, native._target.Root.Disposals); // acquired COM getter reference, not the backend's blur
        Assert.Equal(0, native._blur.Disposals);
    }

    [Fact]
    public void A_renderer_that_has_not_created_its_native_surface_is_not_mistaken_for_a_live_root()
    {
        Assert.Null(SettingsNativePresentation.Bind(new object()));
    }

    public sealed class Platform { public readonly Surface _glSurface = new(); }
    public sealed class Surface { public readonly NativeWindow _window = new(); }
    public sealed class NativeWindow
    {
        public readonly Shared _shared = new();
        public readonly NativeVisual _blur = new() { Opacity = .8f };
        public readonly Target _target = new();
    }
    public sealed class Shared { public object SyncRoot { get; } = new(); }
    public sealed class Target { public NativeVisual Root { get; } = new() { Opacity = .9f, CenterPoint = new Vector3(5, 6, 0) }; }
    public sealed class NativeVisual : IDisposable
    {
        public float Opacity { get; set; } = 1;
        public Vector3 Scale { get; set; } = Vector3.One;
        public Vector3 CenterPoint { get; set; }
        public int Disposals { get; private set; }
        public void SetOpacity(float value) => Opacity = value;
        public void SetScale(Vector3 value) => Scale = value;
        public void SetCenterPoint(Vector3 value) => CenterPoint = value;
        public void Dispose() => Disposals++;
    }
}
