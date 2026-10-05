using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.App.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Optional, exact-frame layer captures for locating V-T133 defects. Never changes the acceptance image or detector.
internal static class VoicePillDiagnostics
{
    public static void Capture(VoicePillScene scene, string tag)
    {
        if (Environment.GetEnvironmentVariable("VT12_DUMP") is not { } directory) return;
        var pill = VoicePillProbe.Pill(scene.View);
        var orb = VoicePillProbe.Slot(scene.View).Orb;
        var rim = pill.GetVisualDescendants().OfType<VoicePillRim>().Single();
        Rect Bounds(Control control) => new(control.TranslatePoint(default, scene.Window)!.Value, control.Bounds.Size);
        File.WriteAllText(Path.Combine(directory, "geometry-" + tag + ".txt"),
            $"scale={scene.Window.RenderScaling}\npill={Bounds(pill)}\ncross={Bounds(pill.Cross)}\ncrossClip={pill.Cross.ClipToBounds}, {pill.Cross.Clip}\norb={Bounds(orb)}\n");
        void Save(string layer)
        {
            foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
            for (var i = 0; i < 3; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
#pragma warning disable CS0618
            using var frame = scene.Window.CaptureRenderedFrame();
#pragma warning restore CS0618
            frame!.Save(Path.Combine(directory, "layer-" + layer + "-" + tag + ".png"), new PngBitmapEncoderOptions());
        }
        Save("whole");
        foreach (var (name, control) in new (string, Control)[] { ("rim", rim), ("cross", pill.Cross), ("body", pill.Body), ("orb", orb), ("pill", pill) })
        {
            var shown = control.IsVisible;
            try { control.IsVisible = false; Save("without-" + name); }
            finally { control.IsVisible = shown; Save("restored-" + name); }
        }
    }
}
