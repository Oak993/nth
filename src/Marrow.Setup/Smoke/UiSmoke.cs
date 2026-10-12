using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;

namespace Marrow.Setup.Smoke;

/// <summary>What <c>--ui-smoke</c> was asked for: where to write its report.</summary>
public sealed record UiSmokeOptions(string ReportPath);

/// <summary>One smoke check's outcome.</summary>
public sealed record SmokeCheck(string Name, bool Passed, string Detail);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 5, 8a and 10, "Startup time"; unit U1, ED-NEW-inst-u1-1). <c>--ui-smoke [report]</c>
/// shows the real window, records the first frame's time, checks on the live window what only Windows can answer (the
/// caption's hit-testing through WM_NCHITTEST, that nothing maximises it, Close pausing against WM_SYSCOMMAND and
/// WM_QUERYENDSESSION with the shutdown reason Windows holds, a screen change settling, the signature check), writes a
/// report (default <c>%TEMP%\nth-setup-ui-smoke.txt</c>) and exits 0 when every check passed, else 1. It cannot install:
/// its session has no install, it takes no single-instance lock, and it never starts Setup.exe or Update.exe. Units U2 and
/// U5 add the ShellLinks round trip in a temp folder and the wallpaper read here (PLAN.md 8a); U2's wallpaper read is in.
/// </summary>
public static class UiSmoke
{
    public const string Flag = "--ui-smoke";
    public const string DefaultReportName = "nth-setup-ui-smoke.txt";

    /// <summary>The smoke options when <c>--ui-smoke</c> is among the arguments (before any <c>--</c>), else null.</summary>
    public static UiSmokeOptions? Read(IReadOnlyList<string> arguments, string? tempFolder = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == "--") break;
            if (!string.Equals(arguments[i], Flag, StringComparison.Ordinal)) continue;
            var next = i + 1 < arguments.Count ? arguments[i + 1] : null;
            var report = next is { Length: > 0 } && !next.StartsWith('-') ? Path.GetFullPath(next) : Path.Combine(tempFolder ?? Path.GetTempPath(), DefaultReportName);
            return new UiSmokeOptions(report);
        }
        return null;
    }

    /// <summary>The report: one line per check, then the summary line.</summary>
    public static string Report(IReadOnlyList<SmokeCheck> checks, IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(facts);
        var text = new StringBuilder();
        foreach (var (name, value) in facts) text.Append(CultureInfo.InvariantCulture, $"INFO {name}: {value}\n");
        foreach (var check in checks) text.Append(CultureInfo.InvariantCulture, $"{(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Detail}\n");
        var failed = checks.Count(c => !c.Passed);
        text.Append(CultureInfo.InvariantCulture, $"{(failed == 0 ? "PASSED" : "FAILED")} {checks.Count - failed}/{checks.Count}\n");
        return text.ToString();
    }

    /// <summary>Runs the checks on the shown window, writes the report and closes the window with the smoke's exit code.</summary>
    public static async Task RunAsync(SetupSession session, UiSmokeOptions options, long startedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        var window = session.Window;
        var checks = new List<SmokeCheck>();
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await NextFrame(window);
            facts["first-frame-ms-since-main"] = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            facts["first-frame-ms-since-process-start"] = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            Describe(window, handle, facts);
            if (handle == 0)
            {
                checks.Add(new SmokeCheck("window-handle", false, "the window has no native handle"));
            }
            else
            {
                checks.AddRange(HitTests(window, handle));
                checks.Add(NoMaximise(window, handle));
                checks.Add(await ScreenChange(session));
                checks.AddRange(await ClosePausing(session, handle));
            }
            var signature = await SignatureCheck.VerifySelfAsync();
            session.SignatureChecked(signature);
            checks.Add(new SmokeCheck("signature", signature.State != SignatureState.Unknown, $"{signature.State} {signature.Publisher} (0x{signature.Status:X8})"));
            checks.Add(await WallpaperRead(window));
        }
        catch (Exception error)
        {
            checks.Add(new SmokeCheck("smoke", false, error.GetType().Name + ": " + error.Message));
        }
        var report = Report(checks, facts);
        try
        {
            File.WriteAllText(options.ReportPath, report);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            checks.Add(new SmokeCheck("report", false, error.Message));
        }
        session.Finish(checks.All(c => c.Passed) ? 0 : 1);
    }

    /// <summary>The wallpaper chain through the real <c>IDesktopWallpaper</c> (unit U2): the AOT-compiled COM v-table is exercised here.</summary>
    private static async Task<SmokeCheck> WallpaperRead(SetupWindow window)
    {
        try
        {
            var scene = await WallpaperLoader.ReadAsync(window);
            var picture = Support.WallpaperPreview.Compose(scene, 352, 220);
            return new SmokeCheck("wallpaper", true, string.Create(CultureInfo.InvariantCulture, $"{scene.Origin} {scene.Position} {scene.Monitor.Width}x{scene.Monitor.Height}") + (picture is null ? "" : " ink-labels " + picture.InkLabels));
        }
        catch (Exception error)
        {
            return new SmokeCheck("wallpaper", false, error.GetType().Name + ": " + error.Message);
        }
    }

    private static void Describe(SetupWindow window, nint handle, IDictionary<string, string> facts)
    {
        var look = window.Look;
        facts["animations"] = look.Animations.ToString();
        facts["high-contrast"] = look.HighContrast.ToString();
        facts["dark"] = look.Dark.ToString();
        facts["text-scale"] = look.TextScale.ToString(CultureInfo.InvariantCulture);
        facts["windows-build"] = look.WindowsBuild.ToString(CultureInfo.InvariantCulture);
        facts["transparency"] = window.ActualTransparencyLevel.ToString();
        facts["render-scaling"] = window.RenderScaling.ToString(CultureInfo.InvariantCulture);
        facts["rendering-mode"] = SetupApp.RenderingModeName();
        facts["client-size"] = string.Create(CultureInfo.InvariantCulture, $"{window.ClientSize.Width:F0}x{window.ClientSize.Height:F0}");
        if (handle == 0) return;
        facts["style"] = "0x" + ((long)Native.GetWindowLongPtr(handle, Native.GWL_STYLE)).ToString("X8", CultureInfo.InvariantCulture);
        if (Native.DwmGetWindowAttribute(handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, out var corners, sizeof(int)) == 0) facts["dwm-corners"] = corners.ToString(CultureInfo.InvariantCulture);
        if (Native.DwmGetWindowAttribute(handle, Native.DWMWA_SYSTEMBACKDROP_TYPE, out var backdrop, sizeof(int)) == 0) facts["dwm-backdrop"] = backdrop.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>WM_NCHITTEST at the caption's parts, and a sweep of the caption band that never finds HTMAXBUTTON.</summary>
    private static IEnumerable<SmokeCheck> HitTests(SetupWindow window, nint handle)
    {
        var width = window.ClientSize.Width;
        var middle = CaptionBar.BandHeight / 2;
        (string Name, Point At, int Expected)[] cases =
        [
            ("hit-caption", new Point(width / 2, middle), Native.HTCAPTION),
            ("hit-close", new Point(width - CaptionBar.ButtonWidth / 2, middle), Native.HTCLOSE),
            ("hit-minimise", new Point(width - CaptionBar.ButtonWidth * 1.5, middle), Native.HTMINBUTTON),
            ("hit-body", new Point(width / 2, CaptionBar.BandHeight + 60), Native.HTCLIENT),
        ];
        foreach (var (name, at, expected) in cases)
        {
            var hit = HitTest(window, handle, at);
            yield return new SmokeCheck(name, hit == expected, $"at {at.X:F0},{at.Y:F0} got {hit}, expected {expected}");
        }
        var maximise = 0;
        for (var x = 1.0; x < width; x += 4)
        {
            if (HitTest(window, handle, new Point(x, middle)) == Native.HTMAXBUTTON) maximise++;
        }
        yield return new SmokeCheck("hit-no-maximise", maximise == 0, $"{maximise} caption points answered HTMAXBUTTON");
    }

    private static int HitTest(SetupWindow window, nint handle, Point client)
    {
        var screen = window.PointToScreen(client);
        return (int)Native.SendMessage(handle, Native.WM_NCHITTEST, 0, SetupWindowHooks.Pack(screen.X, screen.Y));
    }

    private static SmokeCheck NoMaximise(SetupWindow window, nint handle)
    {
        _ = Native.SendMessage(handle, Native.WM_SYSCOMMAND, Native.SC_MAXIMIZE, 0);
        return new SmokeCheck("no-maximise", window.WindowState == WindowState.Normal, $"after SC_MAXIMIZE the window is {window.WindowState}");
    }

    private static async Task<SmokeCheck> ScreenChange(SetupSession session)
    {
        var from = session.Current?.View;
        session.Go(SetupScreen.Installing);
        var frames = await Settle(session.Window);
        var arrived = session.Current?.View;
        (double Opacity, double Offset) pose = arrived is null ? (0, 0) : session.Window.Body.Pose(arrived);
        var gone = from is null || !session.Window.Body.Children.Contains(from);
        var passed = arrived is not null && pose.Opacity == 1 && pose.Offset == 0 && gone;
        return new SmokeCheck("screen-change", passed, $"settled in {frames} frames; opacity {pose.Opacity:F2}, offset {pose.Offset:F2}, previous removed {gone}");
    }

    private static async Task<IReadOnlyList<SmokeCheck>> ClosePausing(SetupSession session, nint handle)
    {
        var window = session.Window;
        var checks = new List<SmokeCheck>();
        session.PauseClose();
        await Settle(window);
        var refusals = window.Guard.Refusals;
        _ = Native.SendMessage(handle, Native.WM_SYSCOMMAND, Native.SC_CLOSE, 0);
        checks.Add(new SmokeCheck("close-paused", window.IsVisible && window.Guard.Refusals == refusals + 1, $"open {window.IsVisible}, refusals {window.Guard.Refusals - refusals}"));
        var reason = WindowShutdownBlocker.Query(handle);
        checks.Add(new SmokeCheck("shutdown-reason", reason == SetupSession.ShutdownReason(SetupScreen.Installing), $"Windows holds \"{reason}\""));
        var answer = Native.SendMessage(handle, Native.WM_QUERYENDSESSION, 0, 0);
        checks.Add(new SmokeCheck("query-end-session", answer == 0, $"WM_QUERYENDSESSION answered {answer}"));
        var line = session.Current?.Hold;
        checks.Add(new SmokeCheck("hold-line", line is { IsWanted: true } && line.Words == SetupSession.HoldWords(SetupScreen.Installing), $"\"{line?.Words}\""));
        session.ResumeClose();
        var after = WindowShutdownBlocker.Query(handle);
        checks.Add(new SmokeCheck("shutdown-released", string.IsNullOrEmpty(after), $"after resuming Windows holds \"{after}\""));
        return checks;
    }

    private static Task NextFrame(TopLevel topLevel)
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() => topLevel.RequestAnimationFrame(_ => frame.TrySetResult()));
        return frame.Task;
    }

    /// <summary>Waits frame by frame until every motion has come to rest (at most 3 s).</summary>
    private static async Task<int> Settle(SetupWindow window)
    {
        var frames = 0;
        var watch = Stopwatch.StartNew();
        do
        {
            await NextFrame(window);
            frames++;
        }
        while (!window.Animator.IsIdle && watch.Elapsed < TimeSpan.FromSeconds(3));
        return frames;
    }
}
