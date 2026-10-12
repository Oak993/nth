using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, "Screens, headless", unit U2 (ED-NEW-inst-u2-1). Every screen in
// every state, in the dark, light and contrast themes, at 100, 150 and 225 % text size, with the long non-ASCII user name
// and a OneDrive path: no line is cut off or leaves the window, nothing overlaps, the primary button keeps at least 16 DIP
// of room above it, and each one draws (at 100, 150 and 200 % display scale for 100 % text). With NTH_SETUP_RENDERS set to
// a folder the renders are saved there as PNG files, for comparing with the canvas.
[Collection("SetupHeadless")]
public sealed class ScreenRenderTests(SetupHeadlessSession headless)
{
    private const string OneDrive = @"C:\Users\Ana-María Østergård-Fernández\OneDrive - Contoso Ltd\Programs\nth";

    public static readonly string[] States =
    [
        "welcome", "welcome-no-desktop", "welcome-signed", "welcome-unsigned", "options", "options-off", "options-onedrive", "options-light-wallpaper",
        "installing", "installing-slow",
        "ready-created", "ready-off", "ready-name-taken", "ready-failed", "ready-hidden", "ready-updated", "ready-wallpaper",
        "update-ask", "update-ask-open", "update-busy", "update-busy-one", "update-waiting", "update-updating",
        "up-to-date", "newer-installed",
        "problem-safe", "problem-left", "problem-changed", "problem-details", "problem-blocked", "problem-other", "problem-signature",
        "uninstall", "uninstall-wipe", "uninstall-removing", "uninstall-removed",
    ];

    public static TheoryData<string, double> Looks => new()
    {
        { "dark", 1.0 }, { "dark", 1.5 }, { "dark", 2.25 },
        { "light", 1.0 }, { "light", 1.5 }, { "light", 2.25 },
        { "contrast", 1.0 }, { "contrast", 1.5 }, { "contrast", 2.25 },
    };

    [Theory]
    [MemberData(nameof(Looks))]
    [Trait("Decision", "OD-0072")]
    public void Every_screen_and_state_fits_without_clipping_or_overlap_and_draws(string look, double textScale)
    {
        headless.OnUiThread(() =>
        {
            var folder = Environment.GetEnvironmentVariable("NTH_SETUP_RENDERS");
            double[] scales = textScale == 1.0 ? [1.0, 1.5, 2.0] : [1.0];
            var problems = new List<string>();
            foreach (var state in States)
            {
                var rig = new WindowRig(Look(look, textScale));
                var screen = Arrange(rig, state);
                foreach (var problem in Check(rig, screen)) problems.Add($"{state} ({look}, text {textScale:P0}): {problem}");
                foreach (var scale in scales)
                {
                    var name = FormattableString.Invariant($"u2-{state}-{look}-s{scale * 100:0}-t{textScale * 100:0}.png");
                    using var bitmap = Render(rig.Window, scale);
                    if (!Draws(bitmap)) problems.Add($"{state} ({look}, text {textScale:P0}, scale {scale:P0}): the render is blank");
                    if (folder is { Length: > 0 })
                    {
                        Directory.CreateDirectory(folder);
                        bitmap.Save(Path.Combine(folder, name), PngBitmapEncoderOptions.Default);
                    }
                }
                rig.Window.CloseNow();
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_long_user_name_and_the_OneDrive_path_are_shown_whole()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var welcome = (WelcomeScreen)Arrange(rig, "welcome");
            Assert.Contains(WindowRig.Root, Words(welcome.InstallsLine), StringComparison.Ordinal);
            rig.Window.CloseNow();

            var other = new WindowRig(Look("dark", 2.25));
            var options = (OptionsScreen)Arrange(other, "options-onedrive");
            Assert.Equal(OneDrive, options.PathBlock.Text);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, options.PathBlock.TextWrapping);
            Assert.True(options.PathBlock.Bounds.Height >= options.PathBlock.DesiredSize.Height - options.PathBlock.Margin.Top - options.PathBlock.Margin.Bottom - .5);
            other.Window.CloseNow();
        });
    }

    private static SystemLook Look(string look, double textScale) => look switch
    {
        "light" => WindowRig.Light with { TextScale = textScale },
        "contrast" => WindowRig.Contrast with { TextScale = textScale },
        _ => SystemLook.Default with { TextScale = textScale },
    };

    private static CanvasScreen Arrange(WindowRig rig, string state)
    {
        SetupSession session;
        var after = new List<Action<CanvasScreen>>();
        switch (state)
        {
            case "welcome":
            case "welcome-no-desktop":
            case "welcome-signed":
            case "welcome-unsigned":
                session = rig.Session(SetupScreen.Welcome);
                session.SizeMegabytes = 420;
                if (state == "welcome-no-desktop") session.SetDesktopShortcut(false);
                // unit U10: the canvas's Signed and Unsigned test build toggle (the line under the maker)
                if (state == "welcome-signed") session.SignatureChecked(new SignatureResult(SignatureState.Signed, "oak993", 0));
                if (state == "welcome-unsigned") session.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, unchecked((int)0x800B0100)));
                break;
            case "options":
            case "options-off":
            case "options-onedrive":
            case "options-light-wallpaper":
                session = rig.Session(SetupScreen.Options);
                if (state == "options-off") session.SetDesktopShortcut(false);
                if (state == "options-onedrive") session.SetInstallRoot(OneDrive);
                if (state == "options-light-wallpaper") session.SetWallpaper(Wallpaper(236, 238, 242));
                break;
            case "installing":
            case "installing-slow":
                session = rig.Session(SetupScreen.Installing);
                session.SizeMegabytes = 420;
                after.Add(s =>
                {
                    var installing = (InstallingScreen)s;
                    installing.Steps.Advance(1);
                    installing.Steps.Rows[1].SetDetail(SetupStrings.ProgressBytes(168, 420));
                    installing.Bar.SetTarget(40);
                    if (state == "installing-slow") installing.ShowSlow(true);
                });
                break;
            case "update-ask":
            case "update-ask-open":
            case "update-busy":
            case "update-busy-one":
            case "update-waiting":
            case "update-updating":
                session = rig.Session(SetupScreen.Update, installed: "1.0.0");
                session.ReleaseNotes = ["Threads open faster on large projects", "Voice input understands more accents", "A calmer composer with fewer pop-ups"];
                session.SizeMegabytes = 420;
                after.Add(s =>
                {
                    var update = (UpdateScreen)s;
                    switch (state)
                    {
                        case "update-ask-open": update.NthOpen = true; update.ShowAsk(); break;
                        case "update-busy": update.ShowBusy([("Refactor the sign-in module", 4), ("Write the release notes", 1)]); break;
                        case "update-busy-one": update.ShowBusy([("Refactor the sign-in module", 12)]); break;
                        case "update-waiting": update.ShowWaiting(2); break;
                        case "update-updating":
                            update.ShowUpdating(UpdateRun.Open);
                            update.Steps.Advance(1);
                            update.Steps.Rows[1].SetDetail(SetupStrings.ProgressBytes(210, 420));
                            update.Bar.SetTarget(55);
                            break;
                    }
                });
                break;
            case "up-to-date":
                session = rig.Session(SetupScreen.UpToDate, installed: "1.1.0");
                break;
            case "newer-installed":
                session = rig.Session(SetupScreen.NewerInstalled, installed: "1.2.0");
                break;
            case "problem-safe":
            case "problem-left":
            case "problem-changed":
            case "problem-details":
            case "problem-blocked":
            case "problem-other":
            case "problem-signature":
                session = rig.Session(SetupScreen.Problem);
                var reason = state switch
                {
                    "problem-left" => SetupStrings.ProblemInUse,
                    "problem-changed" => SetupStrings.ProblemChanged,
                    "problem-blocked" => SetupStrings.ProblemBlocked, // unit U10: the rest of the canvas's reasons
                    "problem-other" => SetupStrings.ProblemOther,
                    "problem-signature" => SetupStrings.ProblemSignature,
                    _ => SetupStrings.ProblemSpace("C:", 420, 112),
                };
                session.Fail(reason, safe: state != "problem-left", "step: copying · Setup.exe exit 1\n[12:04:31] Velopack Setup 1.2.161\n[12:04:33] copying files to " + WindowRig.Root + "\n[12:04:34] error 0x80070020: the file is in use");
                if (state == "problem-details") after.Add(s => ((ProblemScreen)s).DetailsLink.Invoke());
                break;
            case "uninstall":
            case "uninstall-wipe":
            case "uninstall-removing":
            case "uninstall-removed":
                session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
                after.Add(s =>
                {
                    var uninstall = (UninstallScreen)s;
                    if (state != "uninstall") uninstall.Switch.Toggle();
                    if (state is "uninstall-removing" or "uninstall-removed") uninstall.ShowRemoving();
                    if (state == "uninstall-removed") uninstall.ShowRemoved();
                });
                break;
            default:
                session = rig.Session(SetupScreen.Ready);
                session.SetResult(state switch
                {
                    "ready-off" => new InstallResult(DesktopShortcutOutcome.Off),
                    "ready-name-taken" => new InstallResult(DesktopShortcutOutcome.NameTaken),
                    "ready-failed" => InstallResult.FailedWith(unchecked((int)0x80070005)),
                    "ready-hidden" => new InstallResult(DesktopShortcutOutcome.Created, IconsHidden: true),
                    _ => new InstallResult(DesktopShortcutOutcome.Created),
                }, updated: state == "ready-updated");
                if (state == "ready-wallpaper") session.SetWallpaper(Wallpaper(28, 64, 110));
                break;
        }
        session.Start();
        rig.Window.Show();
        rig.Settle();
        Run(rig, 1.5);
        var screen = (CanvasScreen)session.Current!;
        foreach (var step in after)
        {
            step(screen);
            Run(rig, 1.5);
        }
        Run(rig, 3);
        // the window takes the height the screen asked for (the platform posts the resize)
        rig.Settle();
        Run(rig, .5);
        rig.Settle();
        return screen;
    }

    private static void Run(WindowRig rig, double seconds) => rig.Frames.Run(seconds, () => rig.Window.UpdateLayout(), .05);

    private static WallpaperScene Wallpaper(byte r, byte g, byte b)
    {
        var monitor = new WallpaperRect(0, 0, 2560, 1600);
        return new WallpaperScene(WallpaperOrigin.Wallpaper, FakeWallpaperSource.Solid(320, 200, r, g, b), WallpaperPosition.Fill, default, monitor, monitor);
    }

    internal static IEnumerable<string> Check(WindowRig rig, CanvasScreen screen)
    {
        var window = rig.Window;
        var client = new Rect(window.ClientSize);
        var column = screen.Column;
        if (column.DesiredSize.Height > column.Bounds.Height + .5)
            yield return $"the screen needs {column.DesiredSize.Height:F1} DIP but has {column.Bounds.Height:F1}";

        foreach (var text in screen.View.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!Seen(text, screen.View)) continue;
            var box = Box(text, window);
            if (box is not { } r) continue;
            var words = Words(text);
            if (words.Length == 0) continue;
            // text in a scrolling box of the screen (Problem's log) may run past the box when the box scrolls to it; the box itself must fit
            var scroller = text.FindAncestorOfType<ScrollViewer>();
            if (scroller is not null && screen.View.IsVisualAncestorOf(scroller) && Box(scroller, window) is { } frame)
            {
                if (frame.X < client.X - .5 || frame.Y < client.Y - .5 || frame.Right > client.Right + .5 || frame.Bottom > client.Bottom + .5)
                    yield return $"the scrolling box of \"{words}\" leaves the window ({frame})";
                if (r.Height > frame.Height + .5 && !(scroller.Extent.Height > scroller.Viewport.Height && scroller.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled))
                    yield return $"\"{words}\" runs past its box, which does not scroll ({r} in {frame})";
                if (r.Width > frame.Width + .5) yield return $"\"{words}\" is wider than its box ({r} in {frame})";
            }
            else if (r.X < client.X - .5 || r.Y < client.Y - .5 || r.Right > client.Right + .5 || r.Bottom > client.Bottom + .5)
                yield return $"\"{words}\" leaves the window ({r})";
            var needed = text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom;
            if (needed > text.Bounds.Height + .5) yield return $"\"{words}\" is cut off ({text.Bounds.Height:F1} of {needed:F1} DIP)";
            var wide = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
            if (text.TextWrapping == Avalonia.Media.TextWrapping.NoWrap && wide > text.Bounds.Width + .5)
                yield return $"\"{words}\" is cut off at the side ({text.Bounds.Width:F1} of {wide:F1} DIP)";
        }

        var blocks = new List<(Control Control, Rect Box)>();
        foreach (var child in column.Children)
        {
            if (child is Grow || ScreenColumn.GetIsGhost(child) || !Seen(child, screen.View) || child.Bounds.Height <= 0) continue;
            if (Box(child, window) is { } r) blocks.Add((child, r));
        }
        for (var i = 0; i < blocks.Count; i++)
            for (var j = i + 1; j < blocks.Count; j++)
            {
                var overlap = blocks[i].Box.Intersect(blocks[j].Box);
                if (overlap.Width > .5 && overlap.Height > .5)
                    yield return $"{Name(blocks[i].Control)} overlaps {Name(blocks[j].Control)} ({blocks[i].Box} and {blocks[j].Box}; {Chain(blocks[i].Control, window)} | {Chain(blocks[j].Control, window)})";
            }

        var rows = column.Children.OfType<ButtonRow>().Where(r => Seen(r, screen.View)).ToList();
        foreach (var row in rows)
        {
            var buttons = row.Children.OfType<SetupButton>().Where(b => Seen(b, screen.View)).ToList();
            if (buttons.Count == 0) continue;
            var top = buttons.Min(b => Box(b, window)!.Value.Top);
            var index = column.Children.IndexOf(row);
            var above = column.Children.Take(index).Where(c => c is not Grow and not HoldLine && !ScreenColumn.GetIsGhost(c) && Seen(c, screen.View) && c.Bounds.Height > 0).LastOrDefault();
            if (above is not null && Box(above, window) is { } a)
            {
                var gap = top - a.Bottom;
                if (gap < CanvasScreen.ButtonGap - .5) yield return $"only {gap:F1} DIP above the buttons (under {Name(above)})";
            }
            foreach (var button in buttons)
                if (Box(button, window) is { } b && (b.Right > client.Right + .5 || b.Bottom > client.Bottom + .5 || b.X < -.5))
                    yield return $"{button.Label} leaves the window ({b})";
            for (var i = 0; i < buttons.Count; i++)
                for (var j = i + 1; j < buttons.Count; j++)
                {
                    var overlap = Box(buttons[i], window)!.Value.Intersect(Box(buttons[j], window)!.Value);
                    if (overlap.Width > .5 && overlap.Height > .5) yield return $"{buttons[i].Label} overlaps {buttons[j].Label}";
                }
        }
    }

    private static string Chain(Control element, Visual root)
    {
        var parts = new List<string>();
        for (Visual? v = element; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            var t = v.RenderTransform?.Value;
            parts.Add(v.GetType().Name + "@" + v.Bounds.ToString() + (t is { } m && !m.IsIdentity ? FormattableString.Invariant($" t({m.M31:F1},{m.M32:F1})") : "") + (v is Avalonia.Layout.Layoutable l ? " d" + l.DesiredSize.ToString() : ""));
        }
        return string.Join(" < ", parts);
    }

    private static bool Seen(Visual visual, Visual root)
    {
        if (!visual.IsEffectivelyVisible) return false;
        double opacity = 1;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
        {
            opacity *= v.Opacity;
            if (ReferenceEquals(v, root)) break;
        }
        return opacity > .01;
    }

    private static Rect? Box(Visual visual, Visual window)
    {
        var tl = visual.TranslatePoint(default, window);
        var br = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), window);
        return tl is { } a && br is { } b ? new Rect(a, b).Normalize() : null;
    }

    private static string Words(TextBlock text) =>
        text.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.Select(i => i switch
            {
                Avalonia.Controls.Documents.Run run => run.Text,
                Avalonia.Controls.Documents.InlineUIContainer { Child: LinkButton link } => link.Words,
                _ => "",
            }))
            : text.Text ?? "";

    private static string Name(Control control) => control switch
    {
        TextBlock t => "\"" + Words(t) + "\"",
        _ => control.GetType().Name,
    };

    internal static RenderTargetBitmap Render(Window window, double scale)
    {
        var size = window.ClientSize;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        return bitmap;
    }

    internal static bool Draws(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var bytes = stride * size.Height;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), buffer, bytes, stride);
            var pixels = new byte[bytes];
            Marshal.Copy(buffer, pixels, 0, bytes);
            var first = BitConverter.ToInt32(pixels, (size.Height / 2 * size.Width + 2) * 4);
            var different = 0;
            for (var i = 0; i < bytes; i += 4 * 7)
                if (BitConverter.ToInt32(pixels, i) != first) different++;
            return different > bytes / (4 * 7) / 100;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
