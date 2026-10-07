using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Settings;

// B3 52bc792: AboutUpdateViews.swift:553-558 draws NSApp.applicationIconImage at 20 pt, high interpolation.
// OD-0112 / OD-0033: the approved Aurora bitmap from generate_app_icon_d.py; ED-402 pins the replacement hash.
[Collection("Headless")]
public sealed class AboutIdentityIconTests(HeadlessSession headless)
{
    private const string ApprovedSha256 = "2833C700A1BEC5DB954FD0EDC3045708BC1A821E0F8D4CB47958036408097008";

    private static byte[] Draw(Control image)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(20, 20), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext()) image.Render(context);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var decoded = SKBitmap.Decode(stream.ToArray());
        Assert.Contains(decoded.Pixels, pixel => pixel.Alpha > 0);
        return decoded.Bytes;
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-001")] // AboutUpdateViews.swift:551-571; approved identity deviation OD-0015 / OD-0033.
    public void The_actual_identity_pill_displays_the_approved_Marrow_resource_in_original_colors() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.4.2"));
        runtime.OnMain(() =>
        {
            using var resource = AssetLoader.Open(new Uri("avares://Marrow.App/Assets/icon.png"));
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            var approvedBytes = bytes.ToArray();
            // Bind the reference to the owner's existing artifact, not a filename or a replacement test logo.
            Assert.Equal(ApprovedSha256, Convert.ToHexString(SHA256.HashData(approvedBytes)));
            using var stream = new MemoryStream(approvedBytes, writable: false);
            using var approved = new Bitmap(stream);
            var reference = new Image { Source = approved, Width = 20, Height = 20, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(reference, BitmapInterpolationMode.HighQuality);
            reference.Measure(new Size(20, 20));
            reference.Arrange(new Rect(0, 0, 20, 20));

            var pill = new AboutIdentityPill();
            var window = new Avalonia.Controls.Window { Content = pill, Width = 200, Height = 60 };
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var padded = Assert.Single(pill.GetVisualDescendants().OfType<Border>(), b => b.Child is StackPanel);
                var row = Assert.IsType<StackPanel>(padded.Child);
                var displayed = Assert.IsAssignableFrom<Control>(row.Children[0]);
                // Render the real displayed control. The old SwiftImage also reaches this assertion,
                // so reverting either the original reference or only the new URI fails for wrong pixels.
                Assert.True(Draw(reference).SequenceEqual(Draw(displayed)),
                    "ABOUT-APPROVED-ICON: the identity pill is not drawing the approved Marrow bitmap.");
                var image = Assert.IsType<Image>(displayed);
                Assert.Equal(approved.PixelSize, Assert.IsType<Bitmap>(image.Source).PixelSize);
                Assert.Equal(new Size(20, 20), image.Bounds.Size);
                Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(image));
                Assert.False(image.Focusable);
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-001")] // AboutUpdateViews.swift:553 HStack(spacing: 6): the default alignment is .center
    public void The_version_text_sits_on_the_centre_line_of_the_20_point_icon_beside_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.4.2"));
        runtime.OnMain(() =>
        {
            var pill = new AboutIdentityPill();
            var window = new Avalonia.Controls.Window { Content = pill, Width = 200, Height = 60 };
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var row = Assert.IsType<StackPanel>(Assert.Single(pill.GetVisualDescendants().OfType<Border>(), b => b.Child is StackPanel).Child);
                var icon = Assert.IsAssignableFrom<Control>(row.Children[0]);
                var text = Assert.IsType<TextBlock>(row.Children[1]);
                // The text (about 15 points) is shorter than the icon (20): an HStack puts it on the icon's centre line, at its own
                // height; the StackPanel stretched it to 20 and drew it at the top.
                Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5);
                Assert.Equal(icon.Bounds.Center.Y, text.Bounds.Center.Y, 0.5);
            }
            finally { window.Close(); }
        });
    });
}
