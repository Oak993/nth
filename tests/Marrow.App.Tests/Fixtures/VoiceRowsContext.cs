using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Tests.Fixtures;

// Each headless dispatch recreates its font manager. Prime the embedded release faces in this dispatch, as
// HeadlessSession does in its constructor's separate dispatch; script an unlisted display language for the copy oracle.
internal sealed class VoiceRowsContext : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
    private readonly IDisposable platform = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    internal VoiceRowsContext()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fi-FI");
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold })
        {
            new TextBlock { Text = "Listening… · Windows’", FontFamily = new FontFamily("fonts:Inter#Inter"), FontWeight = weight }.Measure(Size.Infinity);
        }
    }

    public void Dispose()
    {
        // Drain the closed rig's queued layout while this dispatch still owns live font services.
        // Headless ResetForUnitTests runs after its font manager has been disposed.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        CultureInfo.CurrentUICulture = previous;
        platform.Dispose();
    }
}
