using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-3")]
public sealed class WebW20BrowserSnapshotTests(HeadlessSession headless)
{
    private static string SnapshotFile([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../../fixtures/web-surfaces/w20/browser-uia.txt"));

    [Fact]
    public async Task The_trust_strip_and_prompt_match_the_stored_snapshot_and_F6_Escape_return_focus()
    {
        Task<bool>? decision = null;
        headless.OnUiThread(() =>
        {
            using var visus = new VisusRig();
            using var rig = new PaneWorkspaceRig(contents: kind => kind == PaneKind.Browser ? VisusPane.Create(visus.Session, null) : new Marrow.App.Views.Panes.PaneContent(new Border()), popupLayers: true);
            rig.Workspace.Open(PaneKind.Browser);
            rig.Settle();
            var body = (VisusPaneBody)rig.Card(PaneKind.Browser).Content.Body;
            visus.Session.Go("https://a.test/");
            var tab = visus.Session.Selected!;
            VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
            rig.Layout();
            body.Toolbar.Address.Box.Focus(NavigationMethod.Tab);
            var opener = rig.Focused;
            var request = VisusRig.Live(tab).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true);
            rig.Layout();
            var prompt = (VisusPrompt)body.Prompt!;
            Assert.True(ReferenceEquals(opener, rig.Focused), "appearing prompt must retain the opener focus");
            var lines = new List<string>();
            void Describe(Control control)
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(control);
                lines.Add($"{peer.GetAutomationControlType()}|{peer.GetName()}");
            }
            Describe(body.Toolbar.Trust);
            // WebPolicy.IsDevUrl recognises .test: this secure fixture also announces that it is a project preview.
            Describe(prompt);
            lines.Add($"Live|{AutomationProperties.GetLiveSetting(prompt)}");
            foreach (var button in prompt.Buttons) Describe(button);
            var expected = File.ReadAllText(SnapshotFile()).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = string.Join('\n', lines) + "\n";
            Assert.True(string.Equals(expected, actual, StringComparison.Ordinal), "UIA snapshot: " + actual.Replace('\n', '|'));
            rig.Chord(Key.F6, PhysicalKey.F6);
            Assert.True(ReferenceEquals(prompt.Lead, rig.Focused), "F6 must enter the prompt's text");
            rig.Key(Key.Enter);
            Assert.False(request.Decision.IsCompleted);
            rig.Key(Key.Escape);
            Assert.True(request.Decision.IsCompletedSuccessfully);
            decision = request.Decision;
            Assert.True(ReferenceEquals(opener, rig.Focused), "Escape must restore the opener focus");
        });
        Assert.False(await decision!);
    }
}
