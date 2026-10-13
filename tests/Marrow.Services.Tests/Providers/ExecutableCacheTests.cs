using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-2): the memory of where a provider's CLI was found. The search it saves is the PATH x PATHEXT walk Executable(for:) and
// Environment(for:) ran on the UI thread for every call; a counting search stands in for it, the clock is the manual one, and the files are real ones in a scratch folder.
public sealed class ExecutableCacheTests
{
    private const string Search = "PATHEXT|PATH|HOME";

    private static string Write(StorageScratchFolder scratch, string name = "claude.exe")
    {
        Directory.CreateDirectory(scratch.Root);
        var path = Path.Combine(scratch.Root, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    [Trait("Decision", "ED-127")] // the resolver it sits in front of (ProviderRegistry.ResolveExecutable); ED-NEW-perf-ui-thread-2
    public void A_found_cli_is_not_searched_for_again_while_the_file_is_there()
    {
        using var scratch = new StorageScratchFolder();
        var found = Write(scratch);
        var cache = new ExecutableCache();
        var searches = 0;
        string? Find() { searches++; return found; }

        Assert.Equal(found, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        Assert.Equal(found, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        Assert.Equal(found, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        Assert.Equal(1, searches);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_file_that_has_gone_is_searched_for_again_and_a_search_that_finds_nothing_is_not_remembered()
    {
        using var scratch = new StorageScratchFolder();
        var first = Write(scratch, "claude.exe");
        var second = Write(scratch, "claude.cmd");
        var cache = new ExecutableCache();
        var searches = 0;
        string? answer = first;
        string? Find() { searches++; return answer; }

        Assert.Equal(first, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        File.Delete(first);
        answer = second;
        Assert.Equal(second, cache.Resolve(ProviderKind.Claude, "", Search, Find)); // the remembered file is gone: another search
        Assert.Equal(2, searches);

        File.Delete(second);
        answer = null;
        Assert.Null(cache.Resolve(ProviderKind.Claude, "", Search, Find));
        Assert.Null(cache.Resolve(ProviderKind.Claude, "", Search, Find)); // a miss is searched for every time, so an install is seen at once
        Assert.Equal(4, searches);
        Assert.Equal(0, cache.Count);

        answer = Write(scratch, "claude.bat");
        Assert.Equal(answer, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        Assert.Equal(5, searches);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_remembered_path_is_trusted_for_thirty_seconds_and_then_searched_for_again()
    {
        using var scratch = new StorageScratchFolder();
        var found = Write(scratch);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var cache = new ExecutableCache();
        var searches = 0;
        string? Find() { searches++; return found; }

        cache.Resolve(ProviderKind.Claude, "", Search, Find);
        clock.Advance(TimeSpan.FromSeconds(29));
        cache.Resolve(ProviderKind.Claude, "", Search, Find);
        Assert.Equal(1, searches);
        clock.Advance(TimeSpan.FromSeconds(2));
        cache.Resolve(ProviderKind.Claude, "", Search, Find);
        Assert.Equal(2, searches);
        Assert.Equal(TimeSpan.FromSeconds(30), ExecutableCache.Lifetime);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void The_provider_the_custom_path_and_the_search_are_each_part_of_the_key()
    {
        using var scratch = new StorageScratchFolder();
        var found = Write(scratch);
        var cache = new ExecutableCache();
        var searches = 0;
        string? Find() { searches++; return found; }

        cache.Resolve(ProviderKind.Claude, "", Search, Find);
        cache.Resolve(ProviderKind.Codex, "", Search, Find);                    // another provider
        cache.Resolve(ProviderKind.Claude, "C:/mine/claude.exe", Search, Find); // another custom path
        cache.Resolve(ProviderKind.Claude, "", Search + "2", Find);             // another PATH
        Assert.Equal(4, searches);
        cache.Resolve(ProviderKind.Claude, "", Search, Find);
        cache.Resolve(ProviderKind.Codex, "", Search, Find);
        cache.Resolve(ProviderKind.Claude, "C:/mine/claude.exe", Search, Find);
        cache.Resolve(ProviderKind.Claude, "", Search + "2", Find);
        Assert.Equal(4, searches);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_refresh_asks_the_disk_even_when_the_memory_is_fresh_and_leaves_its_answer()
    {
        using var scratch = new StorageScratchFolder();
        var older = Write(scratch, "claude.cmd");
        var newer = Write(scratch, "claude.exe");
        var cache = new ExecutableCache();
        var searches = 0;
        string? answer = older;
        string? Find() { searches++; return answer; }

        Assert.Equal(older, cache.Resolve(ProviderKind.Claude, "", Search, Find));
        answer = newer;
        Assert.Equal(older, cache.Resolve(ProviderKind.Claude, "", Search, Find));                 // within its time the memory answers
        Assert.Equal(newer, cache.Resolve(ProviderKind.Claude, "", Search, Find, refresh: true));  // the probe's way: the disk answers
        Assert.Equal(newer, cache.Resolve(ProviderKind.Claude, "", Search, Find));                 // and the UI thread's callers see it
        Assert.Equal(2, searches);
        answer = null;
        Assert.Null(cache.Resolve(ProviderKind.Claude, "", Search, Find, refresh: true));          // a probe that finds nothing forgets it
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void However_often_the_search_changes_the_table_stays_under_its_cap()
    {
        using var scratch = new StorageScratchFolder();
        var found = Write(scratch);
        var cache = new ExecutableCache();
        for (var i = 0; i < ExecutableCache.Capacity * 5; i++)
        {
            cache.Resolve(ProviderKind.Claude, "", Search + i, () => found);
            Assert.True(cache.Count <= ExecutableCache.Capacity, "the table held " + cache.Count + " entries after " + (i + 1) + " searches");
        }
        Assert.True(cache.Count > 0);
    }
}
