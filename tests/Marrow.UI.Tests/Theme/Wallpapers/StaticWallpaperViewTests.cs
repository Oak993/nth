using Avalonia.Controls;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Collection("Headless")]
[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-4")]
public sealed class StaticWallpaperViewTests(HeadlessSession headless)
{
    [Fact]
    public void Replacing_live_inputs_moves_the_minute_timer_and_detaching_disposes_it()
    {
        headless.OnUiThread(() =>
        {
            var first = new ManualTimeProvider(); var second = new ManualTimeProvider();
            var view = new WallpaperView
            {
                Width = 32, Height = 20,
                Selection = WallpaperLibraryStore.Normalize(new(WallpaperKind.Static, "riptide", "accent", Options:
                    new Dictionary<string, double> { ["dynamic"] = 1 })),
                StaticInputs = new(first, () => "#005FB8", () => default),
            };
            var window = new Window { Width = 32, Height = 20, Content = view };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.Equal(1, first.ActiveTimers);
                view.StaticInputs = new(second, () => "#C42B3A", () => default);
                Assert.Equal(0, first.ActiveTimers); Assert.Equal(1, second.ActiveTimers);
                window.Content = null; Assert.Equal(0, second.ActiveTimers);
            }
            finally { window.Close(); }
        });
    }
}
