using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Common;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W6 (owner quality bar: compare with the frames). The Side chat pane painted by the real renderer in a headless
// window, light and dark: it must paint (not blank, not flat) in each state, and with W6_PANE_DUMP set to a folder the frames are written
// there for review against the approved canvas.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class SideChatPaneFrameTests(HeadlessSession headless)
{
    private static void Dump(SideChatPaneRig rig, string name)
    {
        rig.Settle();
        var (_, bytes, _) = DictationProbe.Frame(rig.Window);
        var distinct = new HashSet<int>();
        for (var at = 0; at + 3 < bytes.Length; at += 4 * 97) distinct.Add(bytes[at] << 16 | bytes[at + 1] << 8 | bytes[at + 2]);
        Assert.True(distinct.Count > 8, name + ": the frame is flat");
        if (Environment.GetEnvironmentVariable("W6_PANE_DUMP") is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = rig.Window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static void Converse(SideChatPaneRig rig)
    {
        rig.H.Main.OnMain(() => rig.Pane!.Composer.Field.Text = "What is the main thread doing right now?");
        rig.Pump();
        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(rig.Pane!.Composer.SendButton));
        rig.Until(() => rig.Launches.Count == 1 && rig.Launches[0].Session.Inputs.Count == 1, "the question");
        var session = rig.Launches[0].Session;
        rig.H.Main.OnMain(() =>
        {
            session.Emit(new ProviderEvent.MessageCompleted("a1", "It just edited `src/Hero.css` and is waiting for the preview to reload. Two of three todos are done. Next it checks the tablet size."));
            session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        rig.Until(() => rig.Model.ExistingRuntime(rig.Side!.Id)!.Phase == RuntimePhase.Idle, "the answer");
        rig.Settle();
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void The_pane_paints_empty_with_a_conversation_and_with_Can_act(string theme) => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig(width: 1100, height: 760);
        rig.Window.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        rig.Open();
        Dump(rig, $"side-{theme}-empty");
        Converse(rig);
        Dump(rig, $"side-{theme}-answered");
        rig.H.Main.OnMain(() => rig.Model.SetSideChatCanAct(rig.ThreadId, true));
        rig.Settle();
        Dump(rig, $"side-{theme}-can-act");
    });
}
