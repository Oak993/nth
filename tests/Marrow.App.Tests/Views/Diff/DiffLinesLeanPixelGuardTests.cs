using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Services.Git;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorSamples;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Diff;

// ED-NEW-lean-17: a file's diff rows look exactly as they did before the lean pass (one shared font per face, one brush per
// line kind instead of a throwaway control per brush). Three sample diffs, each in the Light and the Dark appearance, are held
// two ways, both recorded on the unchanged code: the SHA-256 of every rendered frame's pixels, and the SHA-256 of every
// visible control's type, place, size, text, font, foreground and background (the same frames without the rasteriser, so a
// font or driver difference on another machine still has a guard to read).
[Collection("Headless")]
public sealed class DiffLinesLeanPixelGuardTests(HeadlessSession headless)
{
    private const string ScenePixels = "1DAB5A0183CB9A0908D7D6FB21C8DF0292AB0BD3EFA958A41A936EB8D5E582D0";
    private const string SceneTree = "633BA4E40B62F2C2D5D49771DD1E2C1AB97043163A2DEE7747FD201F28DE2B58";

    [Fact]
    [Trait("Decision", "ED-NEW-lean-17")]
    public void Three_sample_diffs_render_exactly_as_before_in_both_appearances() => headless.OnUiThread(() =>
    {
        using var pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var tree = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var (file, numbers) in Samples())
            {
                Shot(new DiffLinesView(file, numbers), theme, pixels, tree);
                count++;
            }
        var p = Convert.ToHexString(pixels.GetHashAndReset());
        var t = Convert.ToHexString(tree.GetHashAndReset());
        if (Environment.GetEnvironmentVariable("MARROW_LEAN_LOG") is { Length: > 0 } path)
            File.AppendAllText(path, $"L17 pixel guard frames={count} pixels={p} tree={t}" + Environment.NewLine);
        Assert.Equal(6, count);
        Assert.True(t == SceneTree, $"tree: {t} (recorded {SceneTree}); pixels: {p}");
        Assert.True(p == ScenePixels, $"pixels: {p} (recorded {ScenePixels}); tree: {t}");
    });

    private static IEnumerable<(DiffFile File, bool Numbers)> Samples()
    {
        // One: two hunks with line numbers, every line kind, an empty line, a short tinted run between context.
        yield return (FileOf(
            Hunk("@@ -1,6 +1,7 @@",
                Line(DiffLine.Kind.Context, "import Foundation", 1, 1),
                Line(DiffLine.Kind.Context, "", 2, 2),
                Line(DiffLine.Kind.Deletion, "let old = 1", 3, null),
                Line(DiffLine.Kind.Addition, "let new = 2", null, 3),
                Line(DiffLine.Kind.Addition, "let more = 3", null, 4),
                Line(DiffLine.Kind.Context, "print(new)", 4, 5),
                Line(DiffLine.Kind.Note, "\\ No newline at end of file")),
            Hunk("@@ -40,3 +41,2 @@",
                Line(DiffLine.Kind.Context, "func tail() {", 40, 41),
                Line(DiffLine.Kind.Deletion, "    return", 41, null),
                Line(DiffLine.Kind.Context, "}", 42, 42))), true);
        // Two: no line numbers (the timeline's edit card), a tinted run longer than one 40-line chunk.
        var run = new List<DiffLine> { Line(DiffLine.Kind.Context, "header line") };
        for (var i = 1; i <= 46; i++) run.Add(Line(i % 3 == 0 ? DiffLine.Kind.Deletion : DiffLine.Kind.Addition, "row " + i));
        run.Add(Line(DiffLine.Kind.Context, "footer line"));
        yield return (FileOf(Hunk("@@ -1,20 +1,40 @@", run.ToArray())), false);
        // Three: a line that wraps, deletions only, and more than 200 lines (the "Show all" capsule).
        var many = new List<DiffLine> { Line(DiffLine.Kind.Deletion, "let sentence = \"" + string.Concat(Enumerable.Repeat("a long line that wraps across the row ", 4)) + "\"", 1, null) };
        for (var i = 2; i <= 210; i++) many.Add(Line(DiffLine.Kind.Deletion, "removed " + i, i, null));
        yield return (FileOf(Hunk("@@ -1,210 +0,0 @@", many.ToArray())), true);
    }

    private static void Shot(DiffLinesView view, ThemeVariant theme, IncrementalHash pixels, IncrementalHash tree)
    {
        var host = new Border { Background = new SolidColorBrush(theme == ThemeVariant.Dark ? Color.FromRgb(30, 30, 30) : Color.FromRgb(255, 255, 255)), Child = view };
        var window = new AvaloniaWindow { Content = host, Width = 460, Height = 900, RequestedThemeVariant = theme };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var buffer = frame.Lock();
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            pixels.AppendData(SHA256.HashData(bytes));
            var lines = new StringBuilder();
            void Walk(Visual visual)
            {
                foreach (var child in visual.GetVisualChildren())
                {
                    if (!child.IsVisible || child.Opacity == 0) continue;
                    if (child is Control control)
                    {
                        var origin = control.TranslatePoint(default, window);
                        lines.Append(control.GetType().Name).Append('|')
                            .Append(origin is { } o ? FormattableString.Invariant($"{o.X:0.##},{o.Y:0.##}") : "-").Append('|')
                            .Append(FormattableString.Invariant($"{control.Bounds.Width:0.##}x{control.Bounds.Height:0.##}|"));
                        if (control is TextBlock text)
                            lines.Append(text.Text).Append('|').Append(text.FontFamily.ToString()).Append('|')
                                .Append(FormattableString.Invariant($"{text.FontSize}|")).Append(Paint(text.Foreground)).Append('|').Append(Paint(text.Background));
                        if (control is Border border)
                            lines.Append(Paint(border.Background)).Append('|').Append(border.CornerRadius.ToString());
                        lines.Append('\n');
                    }
                    Walk(child);
                }
            }
            Walk(view);
            tree.AppendData(Encoding.UTF8.GetBytes(lines.ToString()));
        }
        finally { window.Close(); }
    }

    private static string Paint(IBrush? brush) => brush switch
    {
        null => "-",
        ISolidColorBrush solid => solid.Color.ToString() + "@" + solid.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => brush.GetType().Name,
    };
}
