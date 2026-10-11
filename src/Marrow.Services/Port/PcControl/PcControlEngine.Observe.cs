// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-5, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.2,
// 2.3, 2.10, 2.11, 2.12). Seeing: pc_list_apps, pc_launch_app, pc_observe and pc_zoom. A look is the window's tree first
// (a diff against the last look, from the warm cache when nothing changed); a screenshot only when asked, or when the tree
// is weak, on the model's own grid with element numbers drawn on it, plus full-resolution detail crops where targets are
// small or crowded and native tiles for a weak tree. A locked PC gives the tree only.
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    /// <summary>A tree with fewer actionable elements than this is weak: auto adds a screenshot.</summary>
    public const int WeakTree = 5;

    /// <summary>A target smaller than this on the screenshot gets a full-resolution crop (DESIGN.md 2.12).</summary>
    public const int SmallTargetPx = 16;

    /// <summary>More actionable elements than this in a 200 x 200 screenshot area get a full-resolution crop.</summary>
    public const int Crowded = 12;

    /// <summary>The most detail crops one look carries.</summary>
    public const int MaxDetails = 4;

    // Windows whose content is drawn, not built from controls: auto always adds a screenshot.
    private static readonly HashSet<string> canvasClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TscShellContainerClass", "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW30", "Valve001",
    };

    // ----- pc_list_apps, pc_launch_app -----

    private PcResult ListApps(bool installed)
    {
        var text = new StringBuilder();
        foreach (var monitor in parts.Catalog.Monitors())
        {
            text.Append("screen ").Append(Count(monitor.Index)).Append(": ").Append(Count(monitor.Bounds.Width)).Append('x').Append(Count(monitor.Bounds.Height))
                .Append(" at ").Append(Count(monitor.Bounds.X)).Append(',').Append(Count(monitor.Bounds.Y))
                .Append(", scale ").Append((monitor.Dpi / 96.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(monitor.Primary ? ", primary" : "").Append('\n');
        }
        var settings = parts.Settings();
        foreach (var app in parts.Catalog.Windows().Where(w => !parts.Own.Owns(w.Handle)).GroupBy(w => w.App.Key))
        {
            var first = app.First();
            var verdict = PcPolicy.Classify(first.App, settings.UserDeniedApps);
            string approved;
            if (verdict.Denied) approved = "blocked (" + verdict.Reason + ")";
            else if (PcPolicy.IsElevated(parts.Security.Process(first.ProcessId))) approved = "blocked (runs with admin rights)";
            else approved = Granted(first.App);
            text.Append("app ").Append(AppName(first)).Append(" exe ").Append(first.ExeName);
            if (first.Aumid is { Length: > 0 } aumid) text.Append(" app_id ").Append(PcWindowLine.Quote(aumid));
            text.Append(" approved: ").Append(approved).Append('\n');
            foreach (var window in app)
            {
                // A fresh list is the model's new view: a handle Windows reused is known again from here on.
                lock (gate) known[window.Handle] = window;
                text.Append("  ").Append(PcWindowRef.Format(window.Handle)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(window.Title, 120)))
                    .Append(" screen ").Append(Count(window.Monitor));
                if (window.Minimized) text.Append(", minimized");
                if (PcPolicy.IsRunDialog(window.ExeName, window.ClassName, window.Title)) text.Append(", blocked: the Run dialog");
                if (parts.Ownership.Holder(window.Handle, caller.Thread) is { } holder) text.Append(", in use by ").Append(holder);
                text.Append('\n');
            }
        }
        if (installed)
        {
            text.Append("installed apps (start one with pc_launch_app app_id):\n");
            foreach (var app in parts.Launcher.Installed().Take(400))
                text.Append("  ").Append(PcWindowLine.Quote(TreeFormatter.Clean(app.Name, 80))).Append(" app_id ").Append(PcWindowLine.Quote(app.AppId)).Append('\n');
        }
        return PcResult.Success(body: text.ToString().TrimEnd('\n'));
    }

    // none, chat or always: what the user allowed for this app (seeing or acting).
    private string Granted(PcAppIdentity app)
    {
        if (caller.Head is null && (parts.Approver.Standing(app, PcCapability.Act) == PcApproval.Always || parts.Approver.Standing(app, PcCapability.See) == PcApproval.Always))
            return "always";
        lock (gate)
        {
            foreach (var capability in new[] { PcCapability.Act, PcCapability.See })
            {
                if (approvals.TryGetValue((app.Key, capability), out var asked) && asked.Answer.IsCompletedSuccessfully && asked.Answer.Result is PcApproval.Chat or PcApproval.Always)
                    return "chat";
            }
        }
        return "none";
    }

    private async Task<PcResult> Launch(JsonObject args, PcCall call)
    {
        var id = PcArgs.Str(args, "app_id");
        var path = PcArgs.Str(args, "path");
        if ((id is null) == (path is null)) throw new PcArgsException("give exactly one of app_id and path");
        var place = PcArgs.Str(args, "place") ?? "auto";
        if (place is not ("auto" or "desk" or "workspace")) throw new PcArgsException("place must be auto, desk or workspace");
        if (place == "workspace")
            return PcResult.Fail(PcErrorCode.NotInWorkspace, "Tactus's separate workspace is not part of this version, so nothing was started");
        var target = (id ?? path)!.Trim();
        var app = path is not null ? new PcAppIdentity(target) : target.Contains('!', StringComparison.Ordinal) ? new PcAppIdentity("", target) : new PcAppIdentity(target);
        call.Audit.App = app.ExeName.Length > 0 ? app.ExeName : app.Aumid;
        var verdict = PcPolicy.Classify(app, parts.Settings().UserDeniedApps);
        if (verdict.Denied) return PcResult.Fail(PcErrorCode.DeniedApp, verdict.Reason ?? "this app is not allowed");
        if (parts.Security.SecureDesktop()) return PcResult.Fail(PcErrorCode.SecureDesktop, "a Windows security prompt is open");
        if (!parts.StopKey.Arm())
            return PcResult.Fail(PcErrorCode.NotSupported, "the stop key " + parts.StopKey.Chord + " is taken by another app, so Tactus cannot start. Ask the user to pick another stop key in Settings, Tactus");
        call.Timings.Mark("guard");
        var name = app.ExeName.Length > 0 ? Path.GetFileNameWithoutExtension(app.ExeName) : target;
        PcApproval approval;
        using (call.Wait.Waiting(PcWaitReason.Approval)) approval = await Approve(app, name, PcCapability.Act, call.Token).ConfigureAwait(false);
        call.Timings.Mark("approve");
        if (approval is not (PcApproval.Chat or PcApproval.Always)) return PcResult.Fail(PcErrorCode.NotApproved, "the user did not allow Tactus to start this app");
        var before = parts.Catalog.Windows().Select(w => w.Handle).ToHashSet();
        parts.Feed.Watch(PcChangeFeed.Desktop, 0);
        var launch = parts.Launcher.Launch(target);
        call.Timings.Mark("launch");
        if (!launch.Started) return PcResult.Fail(PcErrorCode.NotSupported, launch.Problem ?? "Windows could not start it");
        // The app's first window, woken by "a window was shown" events, at most 10 s.
        var deadline = parts.Clock.NowMs + 10_000;
        List<PcWindowInfo> fresh;
        while (true)
        {
            var version = parts.Feed.Version(PcChangeFeed.Desktop);
            fresh = parts.Catalog.Windows().Where(w => !before.Contains(w.Handle) && !parts.Own.Owns(w.Handle)
                && (launch.ProcessId is { } pid ? w.ProcessId == pid || w.App.Key == app.Key : w.App.Key == app.Key)).ToList();
            var left = deadline - parts.Clock.NowMs;
            if (fresh.Count > 0 || left <= 0) break;
            await parts.Feed.Changed(PcChangeFeed.Desktop, version, (int)left, call.Token).ConfigureAwait(false);
        }
        call.Timings.Mark("wait");
        if (fresh.Count == 0) return PcResult.Success(body: "started; no window yet. Call pc_list_apps in a moment.");
        var text = new StringBuilder("started. new windows:");
        foreach (var window in fresh)
        {
            Remember(window);
            text.Append("\n  ").Append(PcWindowRef.Format(window.Handle)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(window.Title, 120)));
        }
        return PcResult.Success(body: text.ToString());
    }

    // ----- pc_observe, pc_zoom -----

    private async Task<PcResult> Observe(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var wantTree = PcArgs.Bool(args, "tree", true);
        var mode = PcArgs.Str(args, "screenshot") ?? "auto";
        if (mode is not ("auto" or "yes" or "no")) throw new PcArgsException("screenshot must be auto, yes or no");
        var marks = PcArgs.Bool(args, "marks", true);
        var region = PcArgs.Region(args);
        var query = PcArgs.Str(args, "query");
        var full = PcArgs.Bool(args, "full", false);
        var maxLines = Math.Clamp(PcArgs.Int(args, "max_lines") ?? TreeFormatter.DefaultMaxLines, 20, 2000);
        var previous = Latest(info.Handle);
        var tree = await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
        call.Timings.Mark("read");
        if (tree is null) return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
        var version = CachedVersion(info.Handle);
        var notes = new List<string>();
        var weak = Actionable(tree) < WeakTree || canvasClasses.Contains(info.ClassName);
        PcShot? shot = null;
        PcImage? image = null;
        var details = new List<PcCrop>();
        if (region is not null || mode == "yes" || (mode == "auto" && (!wantTree || weak)))
        {
            var why = NoPixels(info);
            if (why is not null)
            {
                if (region is not null || !wantTree) return PcResult.Fail(PcErrorCode.CaptureFailed, why);
                notes.Add(why);
            }
            else if (region is { } r)
            {
                if (LatestShot(info.Handle)?.Shot is not { } last) throw new PcArgsException("region needs an earlier screenshot of this window; call pc_observe with screenshot yes first");
                var grab = await parts.Capture!.Grab(info.Handle, info.Frame, call.Token).ConfigureAwait(false);
                call.Timings.Mark("capture");
                if (grab is not { Frame: var frame }) return PcResult.Fail(PcErrorCode.CaptureFailed, "the window could not be captured");
                var crop = parts.Capture.Native(frame, ToFrame(last.Grid.Scale, r), Grid());
                details.Add(crop);
                notes.Add("region " + Describe(r) + " of the last screenshot at up to full resolution (" + Count(crop.Width) + "x" + Count(crop.Height) + ")");
            }
            else
            {
                shot = await parts.Capture!.Shoot(info.Handle, info.Frame, Grid(), marks ? Marks(info, tree) : [], call.Token).ConfigureAwait(false);
                call.Timings.Mark("capture");
                if (shot is null)
                {
                    if (!wantTree) return PcResult.Fail(PcErrorCode.CaptureFailed, "the window could not be captured");
                    notes.Add("no screenshot: the window could not be captured");
                }
                else
                {
                    details.AddRange(Detail(info, tree, shot, weak && mode == "auto", notes));
                    call.Timings.Mark("detail");
                }
            }
        }
        var observation = Record(info, tree, version, shot);
        var text = new StringBuilder();
        if (wantTree)
        {
            var diff = full || previous is null || query is not null ? null : TreeDiff.Compute(previous.Tree, tree, previous.Id);
            text.Append(diff ?? tree.Render(query, maxLines));
            if (tree.Focused is { } focused) text.Append("\nfocused: [").Append(Count(focused)).Append(']');
            var selected = tree.Selected.Take(10).Select(l => "[" + Count(l.Index) + "]").ToList();
            if (selected.Count > 0) text.Append("\nselected: ").Append(string.Join(' ', selected));
        }
        if (shot is not null)
        {
            image = new PcImage(shot.Jpeg, shot.Grid.Width, shot.Grid.Height, observation.Id);
            if (marks) text.Append("\nthe screenshot shows element numbers in small labels");
        }
        foreach (var note in notes) text.Append('\n').Append(note);
        call.Timings.Mark("observe");
        var result = PcResult.Success(Line(info, observation), text.ToString().TrimStart('\n'), image);
        return details.Count == 0 ? result : result with { Details = details.Select(d => new PcImage(d.Jpeg, d.Width, d.Height, observation.Id)).ToList() };
    }

    // Why no pixels can be had now (the tree still works); null when a screenshot can be taken.
    private string? NoPixels(PcWindowInfo info)
    {
        if (!caller.Vision) return "no screenshot: this model does not take images";
        if (parts.Security.Locked()) return "screen locked: tree only";
        if (info.Minimized) return "no screenshot: the window is minimized (the tree still works)";
        if (parts.Capture is null) return "no screenshot: capture is not available";
        return null;
    }

    private static int Actionable(PcTree tree) => tree.Elements.Count(e => e.Key != 1 && PcElementMatch.Actionable(e.Value));

    // Element numbers drawn on the model's copy (set-of-mark), in frame pixels.
    private static List<PcMark> Marks(PcWindowInfo info, PcTree tree) =>
        Targets(tree).Select(t => new PcMark(t.Index, t.Node.Bounds.Offset(-info.Frame.X, -info.Frame.Y)))
            .Where(m => m.Rect.Right > 0 && m.Rect.Bottom > 0 && m.Rect.X < info.Frame.Width && m.Rect.Y < info.Frame.Height)
            .ToList();

    private static IEnumerable<(int Index, PcUiNode Node)> Targets(PcTree tree) =>
        tree.Lines.Where(l => l.Index != 1).Select(l => (l.Index, Node: tree.Elements[l.Index]))
            .Where(t => PcElementMatch.Actionable(t.Node) && !t.Node.Bounds.IsEmpty && !t.Node.Has(PcStates.Offscreen));

    /// <summary>
    /// The full-resolution crops a screenshot needs (DESIGN.md 2.12): where targets would be under 16 px on it, where more
    /// than 12 crowd into 200 x 200 of it, and (for a weak tree in auto) the whole window as native tiles.
    /// </summary>
    private List<PcCrop> Detail(PcWindowInfo info, PcTree tree, PcShot shot, bool weakAuto, List<string> notes)
    {
        var crops = new List<PcCrop>();
        var scale = shot.Grid.Scale;
        if (scale >= 1 || parts.Capture is null) return crops;
        var frame = shot.Frame;
        var profile = Grid();
        var targets = Targets(tree).Select(t => (t.Index, Rect: t.Node.Bounds.Offset(-info.Frame.X, -info.Frame.Y))).Where(t => !PcFrames.Clip(frame, t.Rect).IsEmpty).ToList();
        var small = targets.Where(t => Math.Min(t.Rect.Width, t.Rect.Height) * scale < SmallTargetPx).ToList();
        if (small.Count > 0)
        {
            var area = Pad(PcRect.Union(small.Select(t => t.Rect)), 24);
            crops.AddRange(parts.Capture.Tiles(frame, area, profile));
            notes.Add("small on the screenshot: " + string.Join(' ', small.Take(12).Select(t => "[" + Count(t.Index) + "]")) + "; detail crops at full resolution follow");
        }
        var side = (int)Math.Ceiling(200 / scale);
        var centres = targets.Select(t => t.Rect.Center).ToList();
        foreach (var centre in centres)
        {
            var square = new PcRect(centre.X - (side / 2), centre.Y - (side / 2), side, side);
            if (centres.Count(c => square.Contains(c)) <= Crowded) continue;
            if (crops.Any(c => c.Rect.Contains(centre))) break;
            crops.AddRange(parts.Capture.Tiles(frame, square, profile));
            notes.Add("crowded area " + Describe(square, scale) + " of the screenshot; a detail crop at full resolution follows");
            break;
        }
        if (weakAuto && crops.Count == 0)
        {
            crops.AddRange(parts.Capture.Tiles(frame, new PcRect(0, 0, frame.Width, frame.Height), profile));
            notes.Add("the tree has little to act on: the window follows as " + Count(crops.Count) + " tile(s) at full resolution");
        }
        return crops.Take(MaxDetails).ToList();
    }

    private static PcRect Pad(PcRect rect, int by) => new(rect.X - by, rect.Y - by, rect.Width + (2 * by), rect.Height + (2 * by));

    // A screenshot region (model pixels) as frame pixels.
    private static PcRect ToFrame(double scale, (double X, double Y, double Width, double Height) region) =>
        new((int)Math.Floor(region.X / scale), (int)Math.Floor(region.Y / scale), (int)Math.Ceiling(region.Width / scale), (int)Math.Ceiling(region.Height / scale));

    private static string Describe((double X, double Y, double Width, double Height) r) =>
        "[" + string.Join(", ", new[] { r.X, r.Y, r.Width, r.Height }.Select(v => Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";

    private static string Describe(PcRect frameRect, double scale) =>
        Describe((frameRect.X * scale, frameRect.Y * scale, frameRect.Width * scale, frameRect.Height * scale));

    private Task<PcResult> Zoom(PcWindowInfo info, PcObservation observation, JsonObject args, PcCall call)
    {
        if (!caller.Vision) return Task.FromResult(PcResult.Fail(PcErrorCode.NotSupported, "this model does not take images"));
        if (observation.Shot is not { } shot || parts.Capture is null) return Task.FromResult(PcResult.Fail(PcErrorCode.StaleObs, observation.Id + " has no screenshot Tactus still holds"));
        var region = PcArgs.Region(args) ?? throw new PcArgsException("region must be [x, y, width, height]");
        var crop = PcFrames.Clip(shot.Frame, ToFrame(shot.Grid.Scale, region));
        if (crop.IsEmpty) throw new PcArgsException("region is outside the screenshot");
        var zoomed = parts.Capture.Native(shot.Frame, crop, Grid());
        call.Timings.Mark("zoom");
        return Task.FromResult(PcResult.Success(Line(info, observation), "zoom of " + observation.Id + " " + Describe(region) + " at " + Count(zoomed.Width) + "x" + Count(zoomed.Height),
            new PcImage(zoomed.Jpeg, zoomed.Width, zoomed.Height, observation.Id)));
    }
}
