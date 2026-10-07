using Avalonia;
using Marrow.UI.Port.Settings;
namespace Marrow.App.Tests.Fixtures;
internal static class CudaBarTestMeasure
{
    public static double MeasureForTest(this VoiceProgressBar bar) { bar.Measure(new Size(300, 100)); return bar.DesiredSize.Height; }
}
