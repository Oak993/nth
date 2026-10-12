// PORT-ONLY: Tactus's separate workspace probes (unit P18, debug builds only, never shipped; ED-NEW-pcws-11; DESIGN.md 2.13
// NP-6 and NP-7). Run by hand, never a test: `Marrow.exe --tactus-overlay-spike np6-workspace` or `np7-cloaked`, steps in
// scripts/pc-control-overlay-spike.ps1, after the owner turned child sessions on himself (NP-5). With child sessions off it
// says so and starts nothing; it never changes a Windows setting. It runs the workspace exactly as nth would (ChildSessionHost
// on ChildSessionDesktop's seams: the cloaked view, the current-user pipe, this same exe as the agent), with the preview
// gate lifted for itself only; nth's own host keeps WorkspacePreview.Released. It starts Notepad inside the workspace and
// types a numbered marker into it every 3 s (8 rounds) with the agent's own engine, which owns the workspace's real mouse
// and keyboard, then reads the text back.
// - np6-workspace (NP-6): the view is shown ("Watch workspace") while the owner types on his own desk; RESULT: whether the
//   agent started and in how long, markers typed and read back, and how often the desk's front window changed (the owner
//   says whether any change was not his own, and whether his typing arrived whole).
// - np7-cloaked (NP-7): the view stays cloaked; after each marker the agent takes a screenshot inside the workspace, and
//   RESULT also counts the screenshots that differ from the one before (the session keeps rendering while hidden).
// Approvals and confirmations the agent asks for are answered yes for this probe (it only touches its own Notepad).
#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>NP-6 and NP-7: the separate workspace started for real, typed into while shown or cloaked.</summary>
internal sealed class WorkspaceProbe(SpikeLog log, SpikeOptions options, bool cloaked) : SpikeProbe(log, options), IDisposable
{
    public const string Shown = "np6-workspace";
    public const string Cloaked = "np7-cloaked";
    public const string Notepad = "C:\\Windows\\System32\\notepad.exe";
    public const int Rounds = 8;
    private const int RoundMs = 3_000;
    private const int FindWindowMs = 15_000;
    private const int FindPollMs = 500;

    private readonly CancellationTokenSource stop = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private ChildSessionHost? host;
    private bool ready;
    private long agentMs = -1;
    private int typed;
    private int readBack;
    private int frames;
    private int changedFrames;
    private int deskChanges;
    private string? problem;

    private string Name => cloaked ? Cloaked : Shown;

    private string Marker(int round) => (cloaked ? "np7-" : "np6-") + round.ToString(CultureInfo.InvariantCulture);

    public override void Open()
    {
        ready = new ChildSessionReadiness().Enabled() == true;
        Log.Line($"child sessions on: {ready} (this probe never turns them on; the owner does in NP-5)");
        if (!ready)
        {
            problem = "child sessions are off: run NP-5 first";
            return;
        }
        var seams = ChildSessionDesktop.Seams();
        host = new ChildSessionHost(new ChildSessionSeams
        {
            Readiness = seams.Readiness,
            Surface = seams.Surface,
            Accept = seams.Accept,
            Executable = seams.Executable,
            Log = line => Log.Line("host " + line),
            // the preview gate lifted for this hand-run probe only
            Released = true,
        });
        _ = Run(stop.Token).ContinueWith(t =>
        {
            problem = t.Exception!.InnerException?.GetType().Name ?? "failed";
            Log.Line("probe failed: " + problem);
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public override void Finish()
    {
        stop.Cancel();
        var rendering = cloaked ? $" frames={changedFrames}/{Math.Max(0, frames - 1)}" : "";
        Log.Line($"RESULT {Name} childSessions={ready} started={agentMs >= 0} agentMs={agentMs} typed={typed}/{Rounds} readBack={readBack}/{Rounds}{rendering} "
            + $"deskFrontChanges={deskChanges}" + (problem is null ? "" : $" problem=\"{problem}\""));
        Dispose();
    }

    public void Dispose()
    {
        host?.Dispose();
        host = null;
        stop.Dispose();
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        var workspace = host!;
        var started = clock.ElapsedMilliseconds;
        var launched = await workspace.Launch(Notepad, cancellationToken).ConfigureAwait(false);
        if (!launched.Started)
        {
            problem = launched.Problem;
            Log.Line("launch: " + problem);
            return;
        }
        agentMs = clock.ElapsedMilliseconds - started;
        if (await Find(workspace, launched, cancellationToken).ConfigureAwait(false) is not { } notepad)
        {
            problem = "no Notepad window in the workspace within 15 s";
            Log.Line(problem);
            return;
        }
        var id = WorkspaceRefs.Format(notepad.Info.Handle);
        Log.Line($"agent up and Notepad open after {agentMs} ms: {id}, elevated={notepad.Elevated}");
        if (!cloaked) workspace.Watch();
        var windowing = new WorkspaceWindowing();
        var front = windowing.Foreground();
        byte[]? last = null;
        for (var round = 1; round <= Rounds; round++)
        {
            var result = await Call(workspace, PcToolSchemas.Type, new JsonObject { ["window"] = id, ["text"] = Marker(round) + " " }, cancellationToken).ConfigureAwait(false);
            if (result.Ok) typed++;
            Log.Line($"round {round}: type {(result.Ok ? "ok" : result.Error?.ToString() ?? "failed")}");
            if (cloaked)
            {
                var look = await Call(workspace, PcToolSchemas.Observe, new JsonObject { ["window"] = id, ["screenshot"] = "yes", ["marks"] = false }, cancellationToken).ConfigureAwait(false);
                if (look.Image is { } image)
                {
                    frames++;
                    var hash = SHA256.HashData(image.Jpeg);
                    if (last is not null && !hash.AsSpan().SequenceEqual(last)) changedFrames++;
                    last = hash;
                }
            }
            var now = windowing.Foreground();
            if (now != front)
            {
                deskChanges++;
                front = now;
            }
            await Task.Delay(RoundMs, cancellationToken).ConfigureAwait(false);
        }
        var read = await Call(workspace, PcToolSchemas.Read, new JsonObject { ["window"] = id }, cancellationToken).ConfigureAwait(false);
        var text = read.Render();
        for (var round = 1; round <= Rounds; round++)
        {
            if (text.Contains(Marker(round), StringComparison.Ordinal)) readBack++;
        }
        Log.Line($"read back {readBack}/{Rounds} markers");
        if (cloaked) workspace.Watch();
    }

    private async Task<WorkspaceWindow?> Find(ChildSessionHost workspace, WorkspaceLaunch launched, CancellationToken cancellationToken)
    {
        if (launched.Windows.Count > 0) return launched.Windows[0];
        for (var waited = 0; waited < FindWindowMs; waited += FindPollMs)
        {
            var windows = await workspace.Windows(cancellationToken).ConfigureAwait(false);
            if (windows.FirstOrDefault(w => string.Equals(w.Info.ExeName, "notepad.exe", StringComparison.OrdinalIgnoreCase)) is { } window) return window;
            await Task.Delay(FindPollMs, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private Task<PcResult> Call(ChildSessionHost workspace, string tool, JsonObject args, CancellationToken cancellationToken) =>
        workspace.Call(new WorkspaceCall("tactus-probe", tool, args) { Settings = PcSettings.Default with { Enabled = true } }, new ProbeAsks(Log), cancellationToken);
}

/// <summary>The probe's answers to the agent's asks: yes, logged (it only touches its own Notepad in the workspace).</summary>
internal sealed class ProbeAsks(SpikeLog log) : IWorkspaceAsks
{
    public Task<WorkspaceApproval> Approve(PcAppIdentity app, string appName, PcCapability capability, CancellationToken cancellationToken)
    {
        log.Line($"approve {appName} {capability}: yes, for this chat");
        return Task.FromResult(new WorkspaceApproval(PcApproval.Chat, null));
    }

    public Task<WorkspaceConfirm> Confirm(string word, string elementName, string appName, CancellationToken cancellationToken)
    {
        log.Line($"confirm {word} in {appName}: yes");
        return Task.FromResult(new WorkspaceConfirm(true, null));
    }
}
#endif
