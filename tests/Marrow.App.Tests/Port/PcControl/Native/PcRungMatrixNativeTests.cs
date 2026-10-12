// PORT-ONLY: Tactus native probe NP-1, the rung matrix (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4 item 11, 2.6). NATIVE:
// calls the real backends directly, not through the engine, so each way is tried on its own: UI Automation Invoke and
// SetValue (R1), EM_REPLACESEL, WM_CHAR and a posted click (R2), on Notepad, Calculator and Explorer (opened here) and on
// Word, Excel, Chrome, Edge, VS Code and Slack when the owner has opened them first (each with a blank document, a new
// tab, a scratch file or their own DM: the probe types short marker text and clicks the text field, presses no Enter and
// sends nothing; Calculator's "One" and the browsers' and Explorer's Refresh are the only buttons it invokes). Each app is
// tried as it is, covered by a window of the smoke's own, then minimized; foreground, focus and cursor are recorded
// before and after every way. Only RESULT lines decide; nothing asserts per app. Runs only from the smoke script.
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcRungMatrixNativeTests
{
    private static readonly PcMatrixApp[] Apps =
    [
        new("Notepad", "notepad.exe", "notepad.exe", "Text editor", null, (cls, _) => cls == "Notepad"),
        new("Calculator", "applicationframehost.exe", "calc.exe", null, "One", (_, title) => title == "Calculator"),
        new("Explorer", "explorer.exe", "explorer.exe", "Search", "Refresh", (cls, _) => cls == "CabinetWClass"),
        new("Word", "winword.exe", null, null, null),
        new("Excel", "excel.exe", null, "Formula Bar", null),
        new("Chrome", "chrome.exe", null, "Address and search bar", "Reload"),
        new("Edge", "msedge.exe", null, "Address and search bar", "Refresh"),
        new("VS Code", "code.exe", null, null, null),
        new("Slack", "slack.exe", null, "Message", null),
    ];

    private static PcUiNode? Find(PcUiNode node, Func<PcUiNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static bool IsText(PcUiNode node) => node.Role is "edit" or "document";

    [Fact]
    public async Task NP1_Each_rung_way_is_tried_on_each_app_as_it_is_covered_and_minimized()
    {
        using var rig = new PcSmokeRig("np1-rungs");
        using var reader = new UiaReader();
        var messages = new MessageSender();
        var cover = await rig.Form("Tactus smoke cover");
        var tried = 0;

        foreach (var app in Apps)
        {
            nint handle;
            if (app.Opens is { } file)
            {
                handle = await rig.Launch(file, app.Name == "Explorer" ? "\"" + Path.GetTempPath() + "\"" : null, app.IsWindow!);
            }
            else if (PcSmokeRig.WindowsOf(app.Exe) is [var first, ..])
            {
                handle = first.Handle;
            }
            else
            {
                rig.Result(app.Name + " not open: skipped");
                continue;
            }

            foreach (var state in new[] { "as-is", "covered", "minimized" })
            {
                if (state == "covered") PcSmokeRig.Cover(handle, cover);
                if (state == "minimized") PcSmokeRig.Show(handle, minimized: true);
                await Task.Delay(700);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var tree = await reader.Tree(handle, timeout.Token);
                if (tree is null)
                {
                    rig.Result(app.Name + " " + state + ": no UI Automation tree");
                    continue;
                }
                var edit = Find(tree, n => IsText(n) && (app.EditName is null || n.Name.Contains(app.EditName, StringComparison.OrdinalIgnoreCase)));
                var button = app.InvokeName is { } invokeName ? Find(tree, n => n.Role is "button" or "split button" && string.Equals(n.Name, invokeName, StringComparison.OrdinalIgnoreCase)) : null;
                var control = edit is { NativeWindow: not 0 } ? edit.NativeWindow : messages.FocusControl(handle);

                async Task Try(string way, Func<Task<string>> run, string? marker)
                {
                    var before = PcSmokeRig.Snapshot();
                    string outcome;
                    try
                    {
                        outcome = await run();
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException or OperationCanceledException)
                    {
                        outcome = "threw " + error.GetType().Name;
                    }
                    await Task.Delay(400);
                    var after = PcSmokeRig.Snapshot();
                    var landed = "";
                    if (marker is not null && edit is not null)
                    {
                        using var read = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var text = await reader.ReadText(handle, edit.RuntimeId, read.Token) ?? (await reader.Element(handle, edit.RuntimeId, read.Token))?.Value ?? "";
                        landed = text.Contains(marker, StringComparison.Ordinal) ? "; text landed" : "; text NOT seen";
                    }
                    tried++;
                    rig.Result(app.Name + " " + state + " " + way + ": " + outcome + landed
                        + "; foreground " + (before.Foreground == after.Foreground ? "kept" : "CHANGED")
                        + ", focus " + (before.Focus == after.Focus ? "kept" : "CHANGED")
                        + ", cursor " + (before.X == after.X && before.Y == after.Y ? "kept" : "MOVED")
                        + " (before " + PcSmokeRig.Describe(before) + ", after " + PcSmokeRig.Describe(after) + ")");
                }

                if (button is null) rig.Result(app.Name + " " + state + " invoke: no safe button");
                else await Try("invoke (R1)", async () => Outcome(await reader.Perform(handle, button.RuntimeId, new PcElementCommand(PcElementAction.Invoke), CancellationToken.None)), null);
                if (edit is null)
                {
                    rig.Result(app.Name + " " + state + ": no text field found; the text ways are skipped");
                    continue;
                }
                await Try("set_value (R1)", async () => Outcome(await reader.Perform(handle, edit.RuntimeId, new PcElementCommand(PcElementAction.SetValue) { Value = "np1 setvalue " + state }, CancellationToken.None)), "np1 setvalue " + state);
                await Try("em_replacesel (R2)", () => Task.FromResult(messages.ReplaceSelection(control, " np1 replacesel " + state, false) ? "sent" : "refused"), "np1 replacesel " + state);
                await Try("wm_char (R2)", () => Task.FromResult(messages.Chars(control, " np1 wmchar " + state) ? "sent" : "refused"), "np1 wmchar " + state);
                await Try("posted_click (R2)", () => Task.FromResult(messages.Mouse(control, edit.Bounds.Center, PcMouseButton.Left, 1) ? "sent" : "refused"), null);
            }
            PcSmokeRig.Show(handle, minimized: false);
            if (app.Opens is not null && app.Name != "Notepad") PcSmokeRig.Close(handle);
        }

        PcSmokeRig.Close(cover);
        rig.Result(tried + " ways tried");
        Assert.True(tried > 0);
    }

    private static string Outcome(PcUiaOutcome outcome) => outcome.Done ? "done" : "error " + outcome.Error + " (" + outcome.Reason + ")";
}
