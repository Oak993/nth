using Avalonia.Controls;
using Avalonia.Headless;

namespace Marrow.App.Tests.Fixtures;

internal static class VoiceEvidence
{
    internal static void Capture(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("MARROW_RR11_EVIDENCE_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        using var bitmap = window.CaptureRenderedFrame();
        bitmap?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
