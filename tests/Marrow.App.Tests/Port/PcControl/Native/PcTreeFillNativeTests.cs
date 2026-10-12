// PORT-ONLY: Tactus native probe NP-2, Chromium and Electron tree fill (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4 item
// 11, 2.11). NATIVE: opens a local test page (a counter that ticks every 500 ms and 300 list rows) in a new Chrome and a
// new Edge window, and reads VS Code and Slack when the owner opened them first. For each window as it is, covered and
// minimized: how long UI Automation takes to hand over a filled tree (the first read wakes Chromium's accessibility
// mode), how many nodes it holds, and on the test page whether the counter in the tree keeps moving (staleness). Reading
// only; the windows it opened are closed. Runs only from the smoke script.
using System.ComponentModel;
using System.Diagnostics;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcTreeFillNativeTests
{
    private const string Title = "Tactus NP-2 page";

    private static string Page()
    {
        var path = Path.Combine(Path.GetTempPath(), "tactus-smoke", "np2.html");
        var rows = string.Concat(Enumerable.Range(1, 300).Select(i => "<li>row " + i + "</li>"));
        File.WriteAllText(path, "<!doctype html><title>" + Title + "</title><h1 id=t>tick 0</h1><ul>" + rows
            + "</ul><script>let n=0;setInterval(()=>{document.getElementById('t').textContent='tick '+(++n)},500)</script>");
        return new Uri(path).AbsoluteUri;
    }

    private static int Count(PcUiNode node) => 1 + node.Children.Sum(Count);

    private static int? Tick(PcUiNode node)
    {
        if (node.Name.StartsWith("tick ", StringComparison.Ordinal) && int.TryParse(node.Name.AsSpan(5), out var tick)) return tick;
        foreach (var child in node.Children)
            if (Tick(child) is { } found) return found;
        return null;
    }

    [Fact]
    public async Task NP2_Chromium_and_Electron_trees_fill_and_stay_fresh_when_covered_and_minimized()
    {
        using var rig = new PcSmokeRig("np2-tree-fill");
        using var reader = new UiaReader();
        var cover = await rig.Form("Tactus smoke cover");
        var page = Page();
        var windows = new List<(string Name, nint Handle, bool Opened)>();
        foreach (var (name, exe) in new[] { ("Chrome", "chrome.exe"), ("Edge", "msedge.exe") })
        {
            try
            {
                windows.Add((name, await rig.Launch(exe, "--new-window " + page, (_, title) => title.StartsWith(Title, StringComparison.Ordinal)), true));
            }
            catch (Exception error) when (error is Win32Exception or TimeoutException)
            {
                rig.Result(name + " not available: " + error.Message);
            }
        }
        foreach (var (name, exe) in new[] { ("VS Code", "code.exe"), ("Slack", "slack.exe") })
        {
            if (PcSmokeRig.WindowsOf(exe) is [var first, ..]) windows.Add((name, first.Handle, false));
            else rig.Result(name + " not open: skipped");
        }

        foreach (var (name, handle, opened) in windows)
        {
            foreach (var state in new[] { "as-is", "covered", "minimized" })
            {
                if (state == "covered") PcSmokeRig.Cover(handle, cover);
                if (state == "minimized") PcSmokeRig.Show(handle, minimized: true);
                await Task.Delay(1_000);

                var watch = Stopwatch.StartNew();
                PcUiNode? tree = null;
                var reads = 0;
                while (watch.ElapsedMilliseconds < 10_000)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    tree = await reader.Tree(handle, timeout.Token);
                    reads++;
                    if (tree is not null && (opened ? Tick(tree) is not null : Count(tree) > 50)) break;
                    await Task.Delay(100);
                }
                var filledMs = watch.ElapsedMilliseconds;
                var first = tree is null ? null : Tick(tree);
                await Task.Delay(3_000);
                using var again = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var later = await reader.Tree(handle, again.Token);
                var second = later is null ? null : Tick(later);

                var fresh = !opened ? "" : first is null || second is null ? "; counter not in the tree" : "; counter " + first + " then " + second + " after 3 s (6 expected: " + (second - first >= 4 ? "fresh" : "STALE") + ")";
                rig.Result(name + " " + state + ": " + (tree is null ? "no tree" : Count(tree) + " nodes") + " after " + filledMs + " ms in " + reads + " reads" + fresh);
            }
            PcSmokeRig.Show(handle, minimized: false);
            if (opened) PcSmokeRig.Close(handle);
        }

        PcSmokeRig.Close(cover);
        Assert.NotEmpty(windows);
    }
}
