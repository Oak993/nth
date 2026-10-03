using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// ED-238 evidence (unit/effort-title-fidelity: PR-COMPOSER-111's morphing title, OD-0094's drift): renders the effort card's
/// title through a schedule of effort changes as raw frames, for the side-by-sides with the reference video (study folder
/// E:\Droppy\work\reference-video\study\effort-title-2026-10-03, tools/probe.sh renders, tools/rawconv.py turns the frames into
/// PNGs with their video times). A schedule is "start;ms:effort;ms:effort", for example "high;0:xhigh;215:max": the slider's
/// effort at the start, then each change at its millisecond, written inside withAnimation(.snappy(duration: 0.2)) as the
/// slider writes it (EffortSlider.swift:631). Every step a frame t{ms:0000}.raw is written as the renderer locks it (rows of
/// RowBytes, 4 bytes a pixel), and raw.txt holds the width, height, row bytes, pixel format and alpha format.
/// </summary>
internal static class EffortTitleFramesRig
{
    /// <summary>One study: the folder the frames go to, the schedule, the step and end in ms, the pair card (a Hydra pair's
    /// leader, Codex) or a single Claude card, and the title's colours as a left-to-right gradient ("#RRGGBB,...") or none.</summary>
    internal sealed record Study(string Directory, string Schedule, int StepMs, int EndMs, bool Pair, string? Gradient);

    /// <summary>What was rendered: the times of the frames (ms), the frame's size and row bytes, and the title's bounds in the
    /// window's pixels at the end.</summary>
    internal sealed record Frames(IReadOnlyList<int> Times, int Width, int Height, int RowBytes, PixelRect Title);

    /// <summary>The study the environment asks for (MARROW_STUDY_DIR, MARROW_STUDY_SCHEDULE, and optionally MARROW_STUDY_STEP,
    /// MARROW_STUDY_END, MARROW_STUDY_PAIR=1 and MARROW_STUDY_GRADIENT), or null when it asks for none.</summary>
    internal static Study? FromEnvironment()
    {
        var dir = Environment.GetEnvironmentVariable("MARROW_STUDY_DIR");
        var schedule = Environment.GetEnvironmentVariable("MARROW_STUDY_SCHEDULE");
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(schedule)) return null;
        var step = int.TryParse(Environment.GetEnvironmentVariable("MARROW_STUDY_STEP"), CultureInfo.InvariantCulture, out var s) ? s : 5;
        var end = int.TryParse(Environment.GetEnvironmentVariable("MARROW_STUDY_END"), CultureInfo.InvariantCulture, out var e) ? e : 700;
        var pair = Environment.GetEnvironmentVariable("MARROW_STUDY_PAIR") == "1";
        var gradient = Environment.GetEnvironmentVariable("MARROW_STUDY_GRADIENT") is { Length: > 0 } list ? list : null;
        return new Study(dir, schedule, step, end, pair, gradient);
    }

    /// <summary>Renders the study's frames into its folder, on the UI thread (inside HeadlessSession.OnUiThread).</summary>
    internal static Frames Render(Study study)
    {
        Directory.CreateDirectory(study.Directory);
        var parts = study.Schedule.Split(';');
        var changes = parts.Skip(1).Select(p => p.Split(':')).Select(p => (Ms: int.Parse(p[0], CultureInfo.InvariantCulture), Effort: p[1])).ToList();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = parts[0] };
        using var harness = study.Pair ? new AppModelHarness(withRuntime: false) : null;
        var pairLook = study.Pair ? new EffortPairLook(new HydraPair(ProviderKind.Codex), new AppModel().Providers, null) : null;
        var card = new EffortSliderCard(study.Pair ? "Lead" : "Sonnet", study.Pair ? ProviderKind.Codex : ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, pairLook, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 140, Background = Brushes.Black, Content = card, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = card.GetVisualDescendants().OfType<MorphingTextBlock>().First();
            // Which ancestors clip the title (a letter carried past the chevron must not be cut): written beside the frames.
            var chain = new StringBuilder();
            for (Visual? v = title; v is not null; v = v.GetVisualParent())
                chain.AppendLine(CultureInfo.InvariantCulture, $"{v.GetType().FullName} clip={v.ClipToBounds} geo={(v.Clip is null ? "-" : "set")} bounds={v.Bounds}");
            File.WriteAllText(Path.Combine(study.Directory, "chain.txt"), chain.ToString());
            LinearGradientBrush? gradient = null;
            if (study.Gradient is { } list)
            {
                var colours = list.Split(',').Select(Color.Parse).ToArray();
                gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative) };
                for (var i = 0; i < colours.Length; i++) gradient.GradientStops.Add(new GradientStop(colours[i], i / (double)(colours.Length - 1)));
                title.Foreground = gradient;
            }
            var times = new List<int>();
            var (width, height, rowBytes) = (0, 0, 0);
            var now = 0;
            var next = 0;
            for (var ms = 0; ms <= study.EndMs; ms += study.StepMs)
            {
                while (next < changes.Count && changes[next].Ms <= ms)
                {
                    var advance = changes[next].Ms - now;
                    if (advance > 0) { clock.Advance(TimeSpan.FromMilliseconds(advance)); frames.Advance(TimeSpan.FromMilliseconds(advance)); now += advance; }
                    var value = changes[next].Effort;
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = value);
                    if (gradient is not null) title.Foreground = gradient;
                    Dispatcher.UIThread.RunJobs();
                    next++;
                }
                var step = ms - now;
                if (step > 0) { clock.Advance(TimeSpan.FromMilliseconds(step)); frames.Advance(TimeSpan.FromMilliseconds(step)); now = ms; }
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var buffer = frame.Lock();
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                File.WriteAllBytes(Path.Combine(study.Directory, $"t{ms:0000}.raw"), bytes);
                File.WriteAllText(Path.Combine(study.Directory, "raw.txt"), $"{buffer.Size.Width} {buffer.Size.Height} {buffer.RowBytes} {buffer.Format} {buffer.AlphaFormat}\n");
                (width, height, rowBytes) = (buffer.Size.Width, buffer.Size.Height, buffer.RowBytes);
                times.Add(ms);
            }
            var scale = window.RenderScaling;
            var box = title.TranslatePoint(new Point(0, 0), window) ?? throw new Xunit.Sdk.XunitException("the title is not in the window");
            var titleRect = new PixelRect(PixelPoint.FromPoint(box, scale), PixelSize.FromSize(title.Bounds.Size, scale));
            return new Frames(times, width, height, rowBytes, titleRect);
        }
        finally { window.Close(); }
    }

    /// <summary>The pixels of <paramref name="rect"/> in the frame rendered at <paramref name="ms"/>, row by row.</summary>
    internal static byte[] Crop(string directory, int ms, Frames frames, PixelRect rect)
    {
        var all = File.ReadAllBytes(Path.Combine(directory, $"t{ms:0000}.raw"));
        var crop = new byte[rect.Width * 4 * rect.Height];
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(all, (rect.Y + y) * frames.RowBytes + rect.X * 4, crop, y * rect.Width * 4, rect.Width * 4);
        return crop;
    }
}
