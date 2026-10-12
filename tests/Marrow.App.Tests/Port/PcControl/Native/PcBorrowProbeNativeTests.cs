// PORT-ONLY: Tactus native probe NP-4, borrow timing and restore (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.7 steps 5
// and 6, 5.4 item 11, critiques C2 and C9). NATIVE: with the owner's focus in a desk window of the smoke's own, tries each
// activation way (a: an injected click in the target, b: an injected click on its caption then UI Automation focus, c:
// SetForegroundWindow from nth) and the restore way (SetForegroundWindow back to the desk, then UI Automation focus on its
// text box) on Notepad, Calculator, Explorer and Paint, timing each, with the foreground-lock timeout and each window's DPI
// noted. A pair that passes every try is written to %TEMP%\tactus-smoke\np4-proofs.json in the capability store's import
// format (rung R4, per exe, version and framework, ED-NEW-pcctl-45); only a development build imports it, once at start,
// where nothing was learned yet; the app learns the same ways by itself. It also times the hooks' callback with and without forced
// garbage collection. The script runs it once with the apps on the 100 % monitor and once on the 150 % one. Hands off the
// mouse and keyboard while it runs.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
[Trait("Decision", "ED-NEW-pcctl-45")]
public sealed class PcBorrowProbeNativeTests
{
    private const int Tries = 3;
    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const ushort VK_F24 = 0x87;

    private static async Task<bool> Until(Func<bool> done, int ms = 600)
    {
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            if (done()) return true;
            await Task.Delay(10);
        }
        return done();
    }

    [Fact]
    public async Task NP4_Each_activation_and_restore_way_is_timed_per_app_and_the_proven_pairs_are_written()
    {
        using var rig = new PcSmokeRig("np4-borrow");
        using var reader = new UiaReader();
        var host = new RealInputHost(new SystemPcClock(), reader.FocusControl);
        var messages = new MessageSender();
        var lockMs = 0u;
        SystemParametersInfoW(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref lockMs, 0);
        rig.Note("foreground lock timeout " + lockMs + " ms");

        var desk = await rig.Form("Tactus smoke desk", "$t = New-Object System.Windows.Forms.TextBox; $t.Multiline = $true; $t.Dock = 'Fill'; $f.Controls.Add($t);");
        rig.Result("click into the \"Tactus smoke desk\" text box now; the probe starts in 8 s; then hands off");
        await Task.Delay(8_000);
        var (userWindow, userFocus) = host.Focus();
        rig.Note("user window " + PcWindowRef.Format(userWindow) + (userWindow == desk ? " (the desk)" : " (NOT the desk)") + ", focus " + PcWindowRef.Format(userFocus));

        var targets = new List<(string Exe, nint Handle)>
        {
            ("notepad.exe", await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad")),
            ("calculatorapp.exe", await rig.Launch("calc.exe", null, (_, title) => title == "Calculator")),
            ("explorer.exe", await rig.Launch("explorer.exe", "\"" + Path.GetTempPath() + "\"", (cls, _) => cls == "CabinetWClass")),
            ("mspaint.exe", await rig.Launch("mspaint.exe", null, (_, title) => title.EndsWith("Paint", StringComparison.Ordinal))),
        };
        await Task.Delay(1_500);
        host.Restore(userWindow, userFocus);

        var proofs = new JsonArray();
        var restoreFails = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (exe, handle) in targets)
        {
            var target = await TargetOf(reader, handle);
            rig.Note(exe + " proofs are for " + target.Exe + " " + target.Version + ", framework " + target.Framework);
            var bounds = PcSmokeRig.Bounds(handle);
            var dpi = GetDpiForWindow(handle);
            foreach (var (method, label) in new[] { (PcBorrowProofs.Pointer, "a pointer"), (PcBorrowProofs.Caption, "b caption"), (PcBorrowProofs.Foreground, "c foreground") })
            {
                var passes = 0;
                var times = new List<long>();
                for (var attempt = 0; attempt < Tries; attempt++)
                {
                    var watch = Stopwatch.StartNew();
                    switch (method)
                    {
                        case PcBorrowProofs.Pointer:
                            host.Send(PcInputPlan.Click(bounds.Center, host.Desktop(), PcMouseButton.Left, 1, [], host.ScanCode), 0);
                            break;
                        case PcBorrowProofs.Caption when host.Caption(handle) is { } caption:
                        {
                            host.Send(PcInputPlan.Click(caption, host.Desktop(), PcMouseButton.Left, 1, [], host.ScanCode), 0);
                            var control = messages.FocusControl(handle);
                            if (control != 0) reader.FocusControl(control);
                            break;
                        }
                        case PcBorrowProofs.Caption:
                            rig.Note(exe + " has no caption point on screen");
                            break;
                        default:
                            host.SetForeground(handle);
                            break;
                    }
                    var activated = await Until(() => host.Focus().Foreground == handle);
                    var activateMs = watch.ElapsedMilliseconds;
                    watch.Restart();
                    host.Restore(userWindow, userFocus);
                    var restored = await Until(() => host.Focus() == (userWindow, userFocus));
                    var restoreMs = watch.ElapsedMilliseconds;
                    if (!restored) restoreFails[exe] = restoreFails.GetValueOrDefault(exe) + 1;
                    if (activated && restored) passes++;
                    times.Add(activateMs + restoreMs);
                    rig.Note(exe + " " + label + " try " + (attempt + 1) + ": activate " + (activated ? "ok" : "FAILED") + " in " + activateMs + " ms, restore " + (restored ? "ok" : "FAILED") + " in " + restoreMs + " ms");
                    await Task.Delay(300);
                }
                rig.Result(exe + " " + label + " + restore: " + passes + "/" + Tries + " passed, " + string.Join("/", times) + " ms; dpi " + dpi + ", lock timeout " + lockMs + " ms");
                if (passes == Tries) proofs.Add(Proof(target, method));
            }
            if (!restoreFails.ContainsKey(exe)) proofs.Add(Proof(target, PcBorrowProofs.Restore));
            else rig.Result(exe + " restore failed " + restoreFails[exe] + " times: activation unproven for it");
        }
        var file = Path.Combine(Path.GetTempPath(), "tactus-smoke", "np4-proofs.json");
        File.WriteAllText(file, proofs.ToJsonString());
        rig.Result(proofs.Count + " proofs written to " + file);

        await HookLatency(rig, host);
        foreach (var (_, handle) in targets) PcSmokeRig.Close(handle);
        PcSmokeRig.Close(desk);
    }

    // The hooks' callback latency (critique C2): an F24 press, untagged so the monitor takes it as the user's, timed from
    // SendInput to the monitor's snapshot showing it, 20 times quietly and 20 times while another thread forces full
    // collections.
    private static async Task HookLatency(PcSmokeRig rig, RealInputHost host)
    {
        var input = (rig.Composition.Assembly ?? throw new InvalidOperationException("Tactus is off.")).Input;
        using var keep = input.Keep();
        await Task.Delay(500);
        foreach (var forced in new[] { false, true })
        {
            using var stop = new CancellationTokenSource();
            var hammer = forced ? Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    _ = new byte[8 << 20];
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                }
            }) : Task.CompletedTask;
            var samples = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var before = input.Snapshot().LastKeyMs;
                var watch = Stopwatch.StartNew();
                host.Send([PcInputEvent.Key(VK_F24, host.ScanCode(VK_F24), 0), PcInputEvent.Key(VK_F24, host.ScanCode(VK_F24), PcInputPlan.KEY_UP)], 0);
                var seen = await Until(() => input.Snapshot().LastKeyMs != before, 500);
                samples.Add(seen ? watch.Elapsed.TotalMilliseconds : double.NaN);
                await Task.Delay(50);
            }
            await stop.CancelAsync();
            await hammer;
            var seenSamples = samples.Where(double.IsFinite).Order().ToList();
            rig.Result("hook latency " + (forced ? "with forced GC" : "quiet") + ": " + seenSamples.Count + "/20 seen"
                + (seenSamples.Count > 0 ? ", p50 " + seenSamples[seenSamples.Count / 2].ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + " ms, max " + seenSamples[^1].ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms" : ""));
        }
    }

    // The target's identity as the borrow rung keys it: the exe and file version, and the framework of its root element.
    private static async Task<PcProofTarget> TargetOf(UiaReader reader, nint handle)
    {
        var window = new WindowCatalog().Window(handle) ?? throw new InvalidOperationException("the target window is gone");
        var root = await reader.Tree(handle, CancellationToken.None);
        return PcProofTarget.Of(window, PcFrameworkGate.Of(window, root, null));
    }

    private static JsonObject Proof(PcProofTarget target, string method) => new()
    {
        ["exe"] = target.Exe,
        ["version"] = target.Version,
        ["rung"] = (int)PcRung.R4,
        ["method"] = method,
        ["control"] = target.Framework.ToString(),
        ["state"] = nameof(PcLearned.Passed),
        ["at"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref uint value, uint winIni);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
