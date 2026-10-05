using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;

namespace Marrow.App.Tests.Fixtures;

// Glass-stack calibration exercises the production site, including its real scrim, Caster, glass and clipping.
// No head is selected: the measurements are of the card surface, clear of the strip's controls and stage text.
internal static class HydraCardCalibrationSite
{
    internal const string Backdrop = @"E:\Droppy\work\scratch\glass-g3\calib\card18_backdrop.png";
    internal static string Evidence => Marrow.App.Tests.Window.Native.GalleryRun.OptionalOutputFolder() ?? Path.GetTempPath();

    internal static (Panel Host, HydraPanel Card, Bitmap Bitmap) Create(AppModel model, ThreadRuntime runtime)
    {
        if (!File.Exists(Backdrop)) throw new InvalidOperationException("CALIBRATION_PRECONDITION: missing remade g3 backdrop: " + Backdrop);
        var bitmap = new Bitmap(Backdrop);
        var image = new Image { Source = bitmap, Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var card = new HydraPanel(runtime, [], new Size(400, 267), null, null, _ => { }, () => { }, () => { }, isPoppedOut: true)
        {
            Margin = new Thickness(60, 60, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        AppEnvironment.SetAppModel(card, model);
        return (new Panel { Children = { image, card } }, card, bitmap);
    }
}
