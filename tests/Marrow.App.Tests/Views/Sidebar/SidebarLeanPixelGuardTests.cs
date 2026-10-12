using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// ED-NEW-lean-15: the sidebar looks and moves exactly as it did before the lean pass (skipped identical list passes, children indexed once,
// the trailing overlay kept while its inputs stand, one shared LayoutUpdated subscriber, per-id drag cells, one key space, the minute label
// kept). One scripted scene, captured at 26 fixed timestamps: the list at rest, a hover and a second hover with their crossfades, a drag
// that swaps a neighbour and springs home, the pointer leaving, a search and its clearing. It is held two ways, both recorded on the
// unchanged code: the SHA-256 of every rendered frame's pixels, and the SHA-256 of every visible control's type, place, size, opacity,
// transform and text (the same frames without the rasteriser, so a font or driver difference on another machine still has a guard to read).
// ED-NEW-sidebarguard-1: the tree hash was re-recorded once, after the selection glide of ED-NEW-session-hotkeys joined the list's cell
// (a SelectionGlide and its Canvas, drawn in no state of this scene); the pixel hash is the one first recorded and has not moved.
[Collection("Headless")]
public sealed class SidebarLeanPixelGuardTests(HeadlessSession headless)
{
    private const string ScenePixels = "BEE8082646528548DAB010DFF1166F6DB082B34B11D0CB227573D5350BF0BD4F";
    private const string SceneTree = "9CADEAB5A2B8A4E331ACC22E7BD7EB61159A28319287B458F480CC8F6CD873ED";

    [Fact]
    [Trait("Decision", "ED-NEW-lean-15")]
    [Trait("Decision", "ED-NEW-sidebarguard-1")]
    public void The_list_hover_drag_and_search_render_exactly_as_before() => headless.OnUiThread(() =>
    {
        var (pixels, tree, count) = Capture();
        if (Environment.GetEnvironmentVariable("MARROW_LEAN_LOG") is { Length: > 0 } path)
            File.AppendAllText(path, $"L15 pixel guard frames={count} pixels={pixels} tree={tree}" + Environment.NewLine);
        Assert.Equal(26, count);
        Assert.True(tree == SceneTree, $"tree: {tree} (recorded {SceneTree}); pixels: {pixels}");
        Assert.True(pixels == ScenePixels, $"pixels: {pixels} (recorded {ScenePixels}); tree: {tree}");
    });

    private static ChatThread At(Project project, string title, DateTimeOffset at) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = at, UpdatedAt = at };

    private static (string Pixels, string Tree, int Count) Capture()
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var tree = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        try
        {
            // Ages on the half hour, so the minute hand's own tick (60 s) can never change a label during the scene.
            var now = harness.Main.OnMain(() => MinuteClock.Shared.Now);
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var threads = new List<ChatThread>();
            for (var i = 0; i < 6; i++) threads.Add(At(alpha, "Alpha task " + i, now.AddMinutes(-90 - 60 * i)) with { IsPinned = i == 2 });
            for (var i = 0; i < 4; i++) threads.Add(At(beta, "Beta task " + i, now.AddMinutes(-450 - 60 * i)) with { IsSettled = i == 3 });
            var model = Model(harness, [alpha, beta], threads);
            SidebarView view = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false;
                var host = new Border { Background = new SolidColorBrush(Color.FromRgb(236, 236, 232)) };
                AppEnvironment.SetAppModel(host, model);
                view = new SidebarView(false, null);
                host.Child = view;
                window = new AvaloniaWindow { Content = host, Width = 280, Height = 640, RequestedThemeVariant = ThemeVariant.Light };
                window.Show();
                window.UpdateLayout();
            });
            void Tick(int ms)
            {
                harness.Main.AdvanceMilliseconds(ms);
                harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            }
            void Shot()
            {
                harness.Main.OnMain(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                    using var buffer = frame.Lock();
                    var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                    System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                    pixels.AppendData(SHA256.HashData(bytes));
                    var lines = new StringBuilder();
                    // What can be seen: a hidden or fully transparent control and everything under it is left out (a row that has faded out
                    // leaves the tree on its own schedule, which the pixels never show).
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
                                    .Append(FormattableString.Invariant($"{control.Bounds.Width:0.##}x{control.Bounds.Height:0.##}|{control.Opacity:0.####}|"))
                                    .Append(control.RenderTransform?.Value.ToString() ?? "").Append('|')
                                    .Append((control as TextBlock)?.Text ?? "").Append('\n');
                            }
                            Walk(child);
                        }
                    }
                    Walk(view);
                    if (Environment.GetEnvironmentVariable("MARROW_LEAN_TREE_DUMP") is { Length: > 0 } dump) File.WriteAllText(Path.Combine(dump, $"tree-{count:00}.txt"), lines.ToString());
                    tree.AppendData(Encoding.UTF8.GetBytes(lines.ToString()));
                    count++;
                });
            }
            Point On(string title) => harness.Main.OnMain(() =>
            {
                var row = LogicalQuery.SelfAndDescendants<SidebarThreadRow>(view)
                    .Single(candidate => LogicalQuery.SelfAndDescendants<Button>(candidate).Any(button => AutomationProperties.GetName(button) == title));
                return row.TranslatePoint(new Point(60, 12), window)!.Value;
            });
            void Pointer(Action<AvaloniaWindow> act) => harness.Main.OnMain(() => act(window));
            void Type(string text) => harness.Main.OnMain(() => SearchBox(view).Text = text);
            try
            {
                Tick(1600); Shot(); // at rest
                var second = On("Alpha task 1");
                Pointer(w => w.MouseMove(second));
                Tick(0); Shot(); Tick(50); Shot(); Tick(50); Shot(); Tick(100); Shot(); Tick(400); Shot(); // the hover's crossfade in
                var third = On("Alpha task 3");
                Pointer(w => w.MouseMove(third));
                Tick(0); Shot(); Tick(60); Shot(); Tick(60); Shot(); Tick(400); Shot(); // one row out, the next in
                var grab = On("Alpha task 4");
                Pointer(w => w.MouseDown(grab, MouseButton.Left));
                Pointer(w => w.MouseMove(new Point(grab.X, grab.Y + 5), RawInputModifiers.LeftMouseButton)); Tick(16); Shot();
                Pointer(w => w.MouseMove(new Point(grab.X, grab.Y + 12), RawInputModifiers.LeftMouseButton)); Tick(16); Shot();
                Pointer(w => w.MouseMove(new Point(grab.X, grab.Y + 20), RawInputModifiers.LeftMouseButton)); Tick(16); Shot(); // past the next row's centre: a swap
                Pointer(w => w.MouseUp(new Point(grab.X, grab.Y + 20), MouseButton.Left));
                Tick(0); Shot(); Tick(80); Shot(); Tick(120); Shot(); Tick(600); Shot(); // the spring home
                Pointer(w => w.MouseMove(new Point(140, 620)));
                Tick(0); Shot(); Tick(400); Shot(); // the pointer leaves the rows
                Type("beta");
                Tick(0); Shot(); Tick(100); Shot(); Tick(600); Shot(); // the search results
                Type("");
                Tick(0); Shot(); Tick(150); Shot(); Tick(300); Shot(); Tick(1600); Shot(); // the list comes back
            }
            finally { harness.Main.OnMain(() => window.Close()); }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        return (Convert.ToHexString(pixels.GetHashAndReset()), Convert.ToHexString(tree.GetHashAndReset()), count);
    }
}
