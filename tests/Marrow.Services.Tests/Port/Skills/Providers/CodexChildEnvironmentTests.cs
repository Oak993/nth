// PORT-ONLY: SK-11b / DL-11 offline acceptance. The DL-6 PATH rule reaches Codex's app-server child, and nothing else in its environment changes.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexChildEnvironmentTests
{
    private static string Scratch() => Path.Combine(Marrow.Testing.TestDataDirectory.Read() ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder (R-TEST-9)."), "CodexChildEnvironment", Guid.NewGuid().ToString("N"));

    private static ValueMap<string, string> Env(string path) => new(new Dictionary<string, string>
    {
        ["PATH"] = path, ["HOME"] = "/home/x", ["CODEX_HOME"] = "/home/x/.codex", ["ZED"] = "1",
    });

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void Nothing_is_bound_so_the_environment_is_returned_untouched_and_no_folder_is_made()
    {
        var env = Env("/usr/bin" + Path.PathSeparator + "/bin");
        using (CodexChildEnvironment.Root.Override(new CodexChildRoot(() => null)))
        {
            Assert.Same(env, CodexChildEnvironment.Apply(env));
        }
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void With_a_data_root_only_PATH_changes_and_addons_bin_is_made_even_when_empty()
    {
        var root = Scratch();
        var bin = Path.Combine(root, "addons", "bin");
        var windowsApps = Path.Combine("C:", "Users", "x", "AppData", "Local", "Microsoft", "WindowsApps");
        var path = string.Join(Path.PathSeparator, "/usr/bin", windowsApps, "/bin");
        var env = Env(path);
        using var _ = CodexChildEnvironment.Root.Override(new CodexChildRoot(() => root));
        var child = CodexChildEnvironment.Apply(env);
        Assert.True(Directory.Exists(bin));
        Assert.Equal(string.Join(Path.PathSeparator, "/usr/bin", bin, windowsApps, "/bin"), child["PATH"]);
        foreach (var (key, value) in env.Where(p => p.Key != "PATH")) Assert.Equal(value, child[key]);
        Assert.Equal(env.Keys.OrderBy(k => k, StringComparer.Ordinal), child.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(env.Select(p => p.Key), child.Select(p => p.Key)); // the same entries in the same order
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void Without_a_WindowsApps_entry_the_folder_goes_last_and_the_users_entries_keep_their_order()
    {
        var root = Scratch();
        var env = Env("/a" + Path.PathSeparator + "/b");
        using var _ = CodexChildEnvironment.Root.Override(new CodexChildRoot(() => root));
        Assert.Equal(string.Join(Path.PathSeparator, "/a", "/b", Path.Combine(root, "addons", "bin")), CodexChildEnvironment.Apply(env)["PATH"]);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void The_originator_override_variable_is_never_added()
    {
        var root = Scratch();
        using var _ = CodexChildEnvironment.Root.Override(new CodexChildRoot(() => root));
        var child = CodexChildEnvironment.Apply(Env("/a"));
        Assert.DoesNotContain(child.Keys, k => k.Contains("ORIGINATOR", StringComparison.OrdinalIgnoreCase));
        var here = Path.GetDirectoryName(CodexWiringFixture.FixturePath("x"))!;
        var source = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", "..", "..", "src", "Marrow.Services"));
        var offenders = Directory.EnumerateFiles(Path.Combine(source, "Providers"), "Codex*.cs").Concat(Directory.EnumerateFiles(Path.Combine(source, "Port", "Skills", "Providers"), "Codex*.cs"))
            .Where(f => File.ReadAllText(f).Contains("CODEX_INTERNAL_ORIGINATOR_OVERRIDE", StringComparison.Ordinal) && !f.EndsWith("CodexChildEnvironment.cs", StringComparison.Ordinal)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void The_default_root_is_the_bound_storage_root_and_an_unbound_storage_leaves_the_environment_alone()
    {
        var env = Env("/a");
        Assert.Same(env, CodexChildEnvironment.Apply(env)); // Storage.Locations is unbound here: fail closed, no exception, no change.
        using var storage = new StorageScratchFolder();
        var child = CodexChildEnvironment.Apply(env);
        Assert.Equal(string.Join(Path.PathSeparator, "/a", Path.Combine(storage.Root, "addons", "bin")), child["PATH"]);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void A_PATH_spelled_Path_is_found_the_way_the_environment_block_treats_names()
    {
        var root = Scratch();
        var env = new ValueMap<string, string>(new Dictionary<string, string> { ["Path"] = "/a", ["Q"] = "1" });
        using var _ = CodexChildEnvironment.Root.Override(new CodexChildRoot(() => root));
        var child = CodexChildEnvironment.Apply(env);
        Assert.Equal(OperatingSystem.IsWindows() ? 2 : 3, child.Count);
        if (OperatingSystem.IsWindows()) Assert.Contains(Path.Combine(root, "addons", "bin"), child["Path"], StringComparison.Ordinal);
    }
}
