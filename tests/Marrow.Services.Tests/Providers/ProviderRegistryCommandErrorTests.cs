using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), loadCommands :762-784 and
// updateCommands :740-751.
//
// Test-first proof for PR-PROVREG-218: a failed command load stores the provider error's own description under the
// same key, leaves the previously cached list alone, and a later successful load clears the error. The failure is
// the one Claude's handshake raises for a reply with no commands array (ClaudeSession.swift:203-205), produced by
// the compiled Claude stand-in (R-TEST-8) reached through a custom binary path (R-TEST-10).
public sealed class ProviderRegistryCommandErrorTests
{
    private const string Good =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{\"models\":[],\"commands\":[\"deploy\"]}}}";

    private const string NoCommands =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{\"models\":[]}}}";

    private static string Folder()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-command-error", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-218")] // ProviderRegistry.swift:776-779 (catch stores error.localizedDescription under the key; commands untouched), :750 (a success clears the error)
    public async Task a_failed_load_stores_the_error_text_under_the_same_key_keeps_the_stale_list_and_a_later_success_clears_it()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("command-error");
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = script.ExecutablePath.Replace('\\', '/');
        var registry = new ProviderRegistry(settings);
        var directory = Folder();
        var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);

        script.SetResponse(Good);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directory, force: true)));
        Assert.Equal(["deploy"], registry.Commands[key].Select(command => command.Name));
        Assert.DoesNotContain(key, registry.CommandErrors.Keys);

        script.SetResponse(NoCommands);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directory, force: true)));
        Assert.Equal("Claude could not load its commands.", registry.CommandErrors[key]); // exactly the error's description
        Assert.Equal([key], registry.CommandErrors.Keys); // under the same key as the list
        Assert.Equal(["deploy"], registry.Commands[key].Select(command => command.Name)); // the stale list survives

        script.SetResponse(Good);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directory, force: true)));
        Assert.DoesNotContain(key, registry.CommandErrors.Keys); // a successful load clears the error
        Assert.Equal(["deploy"], registry.Commands[key].Select(command => command.Name));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-218")] // ProviderRegistry.swift:855-858 (catch stores error.localizedDescription: a CLI that cannot even be launched is an error too)
    [Trait("Req", "PR-COMPOSER-050")] // ComposerView.swift:245-248, :269-273 (the composer shows the stored text and a Retry row); ProviderRegistry.swift:781-783 (the in-flight entry is removed, so Retry is not stuck behind a failed load)
    public async Task a_load_that_cannot_launch_the_CLI_stores_the_launch_error_and_clears_the_in_flight_entry()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("command-error-launch");
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = script.ExecutablePath.Replace('\\', '/');
        var registry = new ProviderRegistry(settings);
        var missing = Path.Combine(Folder(), "folder-that-is-gone");
        var key = ProviderRegistry.CommandKey(ProviderKind.Claude, missing);
        Assert.False(Directory.Exists(missing));

        // The text Foundation would give for this launch failure is the failure's own description on Windows:
        // the Message of the very exception the same launch raises when the handshake is run directly (a locale
        // proof: the expected text is never typed into the test).
        var launchFailure = await Task.Run(() => runtime.Loop.Run(async () =>
        {
            try
            {
                await ClaudeSession.Handshake(registry.Executable(ProviderKind.Claude)!,
                    SwiftURL.Create("marrow-native-path:" + missing.Replace('\\', '/'))!, registry.Environment(ProviderKind.Claude));
                return null;
            }
            catch (Exception error) { return error; }
        }));
        Assert.NotNull(launchFailure);
        Assert.NotEqual("", launchFailure!.Message);
        Assert.Empty(script.Calls()); // nothing was ever started: it is a launch failure, not a bad reply

        // Act: the failure must be stored, never thrown out of the load (an exception from the catch block
        // would escape the non-throwing main task and end the process).
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, missing, force: true)));

        Assert.Equal(launchFailure.Message, registry.CommandErrors[key]);
        Assert.Equal([key], registry.CommandErrors.Keys);
        Assert.DoesNotContain(key, registry.Commands.Keys); // no list is invented
        Assert.Empty(InFlightCommandLoads(registry)); // the entry is gone, so the next caller starts a load of its own

        // Not stuck: once the folder exists the next forced load runs, lists, and clears the error.
        Directory.CreateDirectory(missing);
        script.SetResponse(Good);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, missing, force: true)));
        Assert.Equal(["deploy"], registry.Commands[key].Select(command => command.Name));
        Assert.DoesNotContain(key, registry.CommandErrors.Keys);
        Assert.Empty(InFlightCommandLoads(registry));
    }

    // The private in-flight map (ProviderRegistry.swift:781-783 commandLoads): read through reflection because the
    // Swift property is private and the C# keeps it so.
    private static System.Collections.ICollection InFlightCommandLoads(ProviderRegistry registry) =>
        (System.Collections.ICollection)typeof(ProviderRegistry)
            .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(registry)!;

    [Fact]
    [Trait("Req", "PR-PROVREG-218")] // ProviderRegistry.swift:776-779 (a first load that fails stores only the error: no list is invented)
    public async Task a_first_load_that_fails_stores_the_error_and_no_list()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("command-error-first");
        script.SetResponse(NoCommands);
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = script.ExecutablePath.Replace('\\', '/');
        var registry = new ProviderRegistry(settings);
        var directory = Folder();
        var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);

        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directory, force: true)));

        Assert.Equal("Claude could not load its commands.", registry.CommandErrors[key]);
        Assert.DoesNotContain(key, registry.Commands.Keys);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // ProviderRegistry.swift:819-830 (a 33rd key evicts the least recently fetched entry, with its error entry)
    [Trait("Req", "PR-PROVREG-214")] // ProviderRegistry.swift:822-826 (the eviction also removes the oldest key's error)
    public async Task the_entry_evicted_for_a_thirty_third_key_takes_its_error_entry_with_it()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("command-error-evicted");
        script.SetResponse(NoCommands);
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = script.ExecutablePath.Replace('\\', '/');
        var registry = new ProviderRegistry(settings);
        var root = Folder();
        var directories = Enumerable.Range(0, 33).Select(index => Directory.CreateDirectory(Path.Combine(root, "d" + index)).FullName).ToArray();
        for (var index = 0; index < 32; index++)
        {
            runtime.Advance(TimeSpan.FromSeconds(1)); // distinct, increasing fetch times: the first key is the oldest
            var directory = directories[index];
            var name = "c" + index;
            runtime.Loop.Run(() => MainActor.Run(() => registry.UpdateCommands(
                new ValueList<SlashCommand>([new SlashCommand { Name = name, Detail = "" }]), ProviderKind.Claude, directory)));
        }
        var oldest = ProviderRegistry.CommandKey(ProviderKind.Claude, directories[0]);
        var survivor = ProviderRegistry.CommandKey(ProviderKind.Claude, directories[1]);

        // The oldest key fails to reload: its error is stored beside its stale list (PR-PROVREG-218), and the fetch time stays what it was.
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCommands(ProviderKind.Claude, directories[0], force: true)));
        Assert.Equal("Claude could not load its commands.", registry.CommandErrors[oldest]);
        Assert.Equal(32, registry.Commands.Count);

        // A 33rd key evicts the oldest entry, and with it the error that was stored under that key.
        runtime.Advance(TimeSpan.FromSeconds(1));
        runtime.Loop.Run(() => MainActor.Run(() => registry.UpdateCommands(
            new ValueList<SlashCommand>([new SlashCommand { Name = "new", Detail = "" }]), ProviderKind.Claude, directories[32])));
        Assert.Equal(32, registry.Commands.Count);
        Assert.False(registry.Commands.ContainsKey(oldest));
        Assert.False(registry.CommandErrors.ContainsKey(oldest)); // the error entry went with it
        Assert.True(registry.Commands.ContainsKey(survivor));
        Assert.Empty(registry.CommandErrors);
    }
}
