// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-5, ED-NEW-pcctl-9; DESIGN.md 3.1, 3.2). Program.Main's one
// Bind line for Tactus: the capture seams point at Windows.Graphics.Capture (made on first use, only where Windows has
// it) and the Skia JPEG encoder. Binding costs nothing while Tactus is off; nothing is captured until a tool asks.
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl;

public static class PcControlStartup
{
    public static void Bind()
    {
        PcCaptureSeams.Primary.SetProcessDefault(() => WgcWindowCapture.IsSupported ? WgcWindowCapture.Shared : null);
        PcCaptureSeams.Encoder.SetProcessDefault(() => JpegEncoder.Shared);
    }
}
