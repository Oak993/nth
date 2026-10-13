using System.Runtime.CompilerServices;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20SplitterSnapshotTests(HeadlessSession headless)
{
    private static string SnapshotFile([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../fixtures/web-surfaces/w20/splitters-uia.txt"));

    private static string Describe(AutomationPeer peer)
    {
        var range = (IRangeValueProvider)peer;
        Assert.InRange(range.Value, range.Minimum, range.Maximum);
        return FormattableString.Invariant($"{peer.GetAutomationControlType()}|{peer.GetName()}|{peer.GetLocalizedControlType()}|{range.Minimum:0}/{range.Value:0}/{range.Maximum:0}|{((IValueProvider)peer).Value}|{peer.GetHelpText()}");
    }

    [Fact]
    public void Splitter_roles_orientation_ranges_and_collapsed_state_match_the_stored_snapshot() => headless.OnUiThread(() =>
    {
        var lines = new List<string>();
        foreach (var direction in new[] { SplitterOrientation.Vertical, SplitterOrientation.Horizontal })
        {
            using var rig = new SplitterRig(direction);
            var peer = ControlAutomationPeer.CreatePeerForElement(rig.Splitter);
            lines.Add(Describe(peer));
            rig.FocusByKeyboard();
            rig.Key(Key.Space);
            rig.Settle();
            lines.Add(Describe(peer));
        }
        Assert.Equal(File.ReadAllText(SnapshotFile()).Replace("\r\n", "\n", StringComparison.Ordinal), string.Join('\n', lines) + "\n");
    });
}
