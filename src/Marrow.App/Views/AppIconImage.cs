using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Marrow.App.Views;

/// <summary>
/// <c>Image(nsImage: NSApp.applicationIconImage)</c>: RootView.swift:307, NewThreadPrompt.swift:17, AboutUpdateViews.swift:554 and
/// SettingsView.swift:971. OD-0112 / OD-0033: the app icon is the packaged Aurora icon, Assets/icon.png (512 px, with its
/// margin, as an application icon image has). One immutable bitmap serves every site for the life of the app.
/// </summary>
internal static class AppIconImage
{
    private static readonly Lazy<Bitmap> Shared = new(() =>
    {
        using var resource = AssetLoader.Open(new Uri("avares://Marrow.App/Assets/icon.png"));
        return new Bitmap(resource);
    });

    /// <summary>The packaged app icon bitmap.</summary>
    public static Bitmap Bitmap => Shared.Value;

    /// <summary>The icon drawn <paramref name="size"/> points square (<c>.resizable().interpolation(.high).frame(...)</c>).</summary>
    public static Image Make(double size)
    {
        var image = new Image { Source = Bitmap, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }
}
