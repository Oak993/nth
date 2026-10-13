using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20AutomationSnapshotTests(HeadlessSession headless)
{
    private static string SnapshotFile([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../../fixtures/web-surfaces/w20/panes-uia.txt"));

    [Fact]
    public void Pane_landmarks_and_collapsed_rail_buttons_match_the_stored_accessibility_snapshot() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(state: PaneWorkspaceState.Default with { TerminalPrefersGrid = true });
        var lines = new List<string>();
        foreach (var kind in Enum.GetValues<PaneKind>().Where(kind => kind != PaneKind.Chat))
        {
            rig.Workspace.Open(kind);
            rig.Settle();
            var card = rig.Card(kind);
            lines.Add($"{AutomationProperties.GetLandmarkType(card)}|{ControlAutomationPeer.CreatePeerForElement(card).GetName()}");
            rig.Workspace.Close(kind);
            rig.Settle();
        }
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Settle();
        rig.Workspace.Collapse(PaneGutterKeys.Chat);
        rig.Settle();
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        lines.Add($"{AutomationProperties.GetLandmarkType(rail)}|{ControlAutomationPeer.CreatePeerForElement(rail).GetName()}");
        foreach (var button in rail.IconButtons.Append(rail.Chevron))
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(button);
            lines.Add($"{peer.GetAutomationControlType()}|{peer.GetName()}");
        }
        Assert.Equal(File.ReadAllText(SnapshotFile()).Replace("\r\n", "\n", StringComparison.Ordinal), string.Join('\n', lines) + "\n");
    });
}
