using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Accessibility;

[Collection("Headless")]
public sealed class A11yAnnouncementTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "VT-14")]
    public void Voice_progress_announces_changed_whole_percent_only_while_downloading() => headless.OnUiThread(() =>
    {
        var column = new VoiceStateColumn();
        var window = new Avalonia.Controls.Window { Content = column, Width = 220, Height = 100 };
        try
        {
            window.Show();
            var percent = A11yReview.Tree(column).OfType<TextBlock>().Single(t => t.Text == "0%");
            var peer = ControlAutomationPeer.CreatePeerForElement(percent)!;
            var names = new List<string?>();
            peer.PropertyChanged += (_, e) => { if (e.Property == AutomationElementIdentifiers.NameProperty) names.Add(e.NewValue as string); };
            column.SetState(VoiceModelState.Downloading, .37);
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            Assert.Equal("37%", peer.GetName());
            column.SetState(VoiceModelState.Downloading, .379);
            column.SetState(VoiceModelState.Downloading, .38);
            Assert.Equal(new[] { "37%", "38%" }, names);
            column.SetState(VoiceModelState.InUse);
            Assert.Equal(AutomationLiveSetting.Off, peer.GetLiveSetting());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "UE-4")]
    public void Updates_keep_one_current_live_peer_across_status_and_percentage_changes() => headless.OnUiThread(() =>
    {
        var status = new UpdatesStatusCell(RowStatus.Current, "Up to date");
        var window = new Avalonia.Controls.Window { Content = status, Width = 240, Height = 100 };
        try
        {
            window.Show();
            var peer = ControlAutomationPeer.CreatePeerForElement(status)!;
            Assert.NotNull(peer);
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            Assert.Equal("Up to date", peer.GetName());
            var names = new List<string?>();
            peer.PropertyChanged += (_, e) => { if (e.Property == AutomationElementIdentifiers.NameProperty) names.Add(e.NewValue as string); };
            status.SetStatus(RowStatus.Downloading, "Downloading 37%");
            status.SetStatus(RowStatus.Downloading, "Downloading 38%");
            status.SetStatus(RowStatus.Downloading, "Downloading 38%");
            status.SetStatus(RowStatus.Current, "Up to date");
            Assert.Equal(new[] { "Downloading 37%", "Downloading 38%", "Up to date" }, names);
            Assert.Equal("Up to date", peer.GetName());
            Assert.Same(peer, ControlAutomationPeer.CreatePeerForElement(status));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "GR-2")]
    public void Getting_ready_announces_step_state_and_progress_without_rewording_visible_labels() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        var row = rig.Sheet.Rows[0];
        var peer = ControlAutomationPeer.CreatePeerForElement(row.Text)!;
        Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
        Assert.Equal(row.Label + ": Waiting", peer.GetName());
        var names = new List<string?>();
        peer.PropertyChanged += (_, e) => { if (e.Property == AutomationElementIdentifiers.NameProperty) names.Add(e.NewValue as string); };
        rig.Sheet.Report(new FirstRunProgress(rig.Plan[0].Step, 0, rig.Plan.Count, FirstRunStepState.Running, 37, 100, TimeSpan.Zero));
        rig.Sheet.Report(new FirstRunProgress(rig.Plan[0].Step, 0, rig.Plan.Count, FirstRunStepState.Running, 38, 100, TimeSpan.Zero));
        rig.Sheet.Report(new FirstRunProgress(rig.Plan[0].Step, 0, rig.Plan.Count, FirstRunStepState.Running, 38, 100, TimeSpan.Zero));
        rig.Sheet.Report(new FirstRunProgress(rig.Plan[0].Step, 0, rig.Plan.Count, FirstRunStepState.Done, 100, 100, TimeSpan.Zero));
        Assert.Equal(new[] { row.Label + ": Running 37%", row.Label + ": Running 38%", row.Label + ": Done" }, names);
        Assert.Equal(row.Label, ((TextBlock)row.Text).Text);
    });
}
