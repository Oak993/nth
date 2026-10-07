using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Collection("Headless")]
public sealed class WallpaperBackdropTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Builtin_backdrop_is_below_glass_and_uses_the_existing_opacity()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); var clock = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var time = SwiftClock.Provider.Override(clock);
            var backdrop = new global::Marrow.UI.Chrome.WindowBackdrop(.5);
            Assert.Empty(backdrop.GetLogicalDescendants().OfType<WallpaperView>());
            backdrop.UpdateLibrary(new(WallpaperKind.Static, "riptide"));
            var view = Assert.Single(backdrop.GetLogicalDescendants().OfType<WallpaperView>());
            Assert.Equal("riptide", view.Selection.Id); Assert.Equal(.5, view.Opacity);
            Assert.Contains(backdrop.GetLogicalDescendants().OfType<Control>(), c => SwiftGlass.GetEffect(c) is not null);
            backdrop.Update(.8, null); Assert.Equal(.8, view.Opacity);
            backdrop.UpdateLibrary(new(WallpaperKind.Animated, "borealis"));
            Assert.Equal("borealis", Assert.Single(backdrop.GetLogicalDescendants().OfType<WallpaperView>()).Selection.Id);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Clearing_library_restores_the_custom_picture_and_none_retains_upstream_clear_default()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); var clock = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var time = SwiftClock.Provider.Override(clock);
            using var image = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
            var backdrop = new global::Marrow.UI.Chrome.WindowBackdrop(.5, image);
            backdrop.UpdateLibrary(new(WallpaperKind.Static, "riptide"));
            Assert.Single(backdrop.GetLogicalDescendants().OfType<WallpaperView>());
            backdrop.UpdateLibrary(new());
            clock.Advance(TimeSpan.FromSeconds(.6)); frames.Advance(TimeSpan.FromSeconds(.6));
            Assert.Empty(backdrop.GetLogicalDescendants().OfType<WallpaperView>());
            Assert.Same(image, Assert.Single(backdrop.GetLogicalDescendants().OfType<Image>()).Source);
            backdrop.Update(.5, null); clock.Advance(TimeSpan.FromSeconds(.6)); frames.Advance(TimeSpan.FromSeconds(.6)); Assert.Empty(backdrop.GetLogicalDescendants().OfType<Image>());
        });
    }
}
