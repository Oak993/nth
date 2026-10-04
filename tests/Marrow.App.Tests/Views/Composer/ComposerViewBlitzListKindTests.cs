using System.Linq;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzListTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzRingTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzUsageTests;

namespace Marrow.App.Tests.Views.Composer;

// PR-COMPOSER-052, the visible observer's kind guard (found open by the adversarial review of this unit).
[Collection("Headless")]
public sealed class ComposerViewBlitzListKindTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:213-215 `if visible, suggestions.kind == .command { Task { await loadCommands() } }`
    public void B3_opening_the_file_list_starts_no_command_load_while_the_command_list_does()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var fs = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var ps = Presentation.Presenter.Override(presenter);
            var script = ProviderProbeStandIns.CopyFor("composer-review-file-list").WaitForRelease().SetExitCode(0)
                .SetStdout("{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[{\"name\":\"proof-late\",\"description\":\"Delayed fixture command\"}]}}}\n");
            var directory = h.Store.NewFolder("review-file-list");
            Touch(directory, "main.swift", "mail.txt");
            Indexed(h, directory);
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude);
                h.App.Providers.UpdateCommands(new([new() { Name = "cached", Detail = "Cached command" }]), ProviderKind.Claude, directory);
                ((HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!).Add(ProviderKind.Claude);
            });
            var (view, window, _, _) = OnScreen(h, h.Runtime, directory);
            var loads = (System.Collections.IDictionary)typeof(ProviderRegistry)
                .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;
            try
            {
                Pump(h, window);
                Assert.Empty(script.CommandListings());
                h.Main.Advance(TimeSpan.FromSeconds(601)); // the cached list is now stale: any load would launch the listing
                h.Main.OnMain(() => Type(view, "@ma")); // the file list opens
                Until(h, window, () => view.Suggestions.IsVisible && view.Suggestions.SuggestionKind == SuggestionState.Kind.File);
                for (var round = 0; round < 4; round++) Until(h, window, () => true);
                Pump(h, window);
                Assert.False(loads.Contains(key));
                Assert.Empty(script.CommandListings()); // :214 only the command list asks for a load
                // The control: the command list opening on the same stale cache does launch it.
                h.Main.OnMain(() => Type(view, ""));
                Until(h, window, () => !view.Suggestions.IsVisible);
                h.Main.OnMain(() => Type(view, "/"));
                Until(h, window, () => view.Suggestions.IsVisible && script.CommandListings().Count == 1);
            }
            finally
            {
                script.Release();
                Until(h, window, () => !loads.Contains(key));
                Close(h, window);
            }
        });
    }
}
