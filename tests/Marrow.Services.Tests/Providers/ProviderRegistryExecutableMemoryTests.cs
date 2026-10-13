using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-2): ProviderRegistry.Executable(for:) and Environment(for:) share one search for a provider's CLI. Real files in a scratch folder, the
// manual clock; the answers a caller on the UI thread gets are the ones it got before, only without the search repeated.
public sealed class ProviderRegistryExecutableMemoryTests
{
    private static ProviderRegistry MakeRegistry(TestRuntime runtime, FakeProviderSettings settings)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return new ProviderRegistry(settings);
    }

    private static string Write(StorageScratchFolder scratch, string name)
    {
        Directory.CreateDirectory(scratch.Root);
        var path = Path.Combine(scratch.Root, name).Replace('\\', '/');
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-094")] // ProviderRegistry.swift:219-226: the answers are the same; ED-NEW-perf-ui-thread-2 remembers them
    public void A_found_cli_is_remembered_and_an_earlier_candidate_that_appears_is_seen_only_when_the_memory_runs_out()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var settings = new FakeProviderSettings();
        var tool = Path.Combine(scratch.Root, "tool").Replace('\\', '/'); // no extension: PATHEXT order picks .exe before .cmd
        settings.Paths[ProviderKind.Claude] = tool;
        var registry = MakeRegistry(runtime, settings);
        var cmd = Write(scratch, "tool.cmd");

        Assert.Equal(cmd, registry.Executable(ProviderKind.Claude)!.Path, ignoreCase: true);
        var exe = Write(scratch, "tool.exe");
        Assert.Equal(cmd, registry.Executable(ProviderKind.Claude)!.Path, ignoreCase: true); // remembered: no new search for the same inputs
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(exe, registry.Executable(ProviderKind.Claude)!.Path, ignoreCase: true); // after 30 s the search is run again and finds the earlier one
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-094")]
    public void A_cli_that_is_deleted_is_not_found_and_one_that_comes_back_is_found_at_once()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var settings = new FakeProviderSettings();
        var path = Write(scratch, "myclaude.cmd");
        settings.Paths[ProviderKind.Claude] = path;
        var registry = MakeRegistry(runtime, settings);

        Assert.Equal(path, registry.Executable(ProviderKind.Claude)!.Path);
        File.Delete(path);
        Assert.Null(registry.Executable(ProviderKind.Claude)); // the memory is trusted only while the file is there
        Write(scratch, "myclaude.cmd");
        Assert.Equal(path, registry.Executable(ProviderKind.Claude)!.Path); // a miss is not remembered
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-094")]
    public void A_changed_custom_path_is_another_search()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var settings = new FakeProviderSettings();
        var one = Write(scratch, "one.cmd");
        var two = Write(scratch, "two.cmd");
        var registry = MakeRegistry(runtime, settings);

        settings.Paths[ProviderKind.Claude] = one;
        Assert.Equal(one, registry.Executable(ProviderKind.Claude)!.Path);
        settings.Paths[ProviderKind.Claude] = two;
        Assert.Equal(two, registry.Executable(ProviderKind.Claude)!.Path);
        settings.Paths[ProviderKind.Claude] = Path.Combine(scratch.Root, "missing.cmd").Replace('\\', '/');
        Assert.Null(registry.Executable(ProviderKind.Claude));
        settings.Paths[ProviderKind.Claude] = one;
        Assert.Equal(one, registry.Executable(ProviderKind.Claude)!.Path);
    }
}
