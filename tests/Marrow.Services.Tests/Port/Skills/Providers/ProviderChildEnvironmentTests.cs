// SK-11f / DL-11 (skills-ONDEMAND-PLAN.md 3.6 and 4.2, ED-309): every provider child carries addons\bin on its PATH by the DL-6 rule, and nothing else in its
// environment changes.
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class ProviderChildEnvironmentTests
{
    private static string NewRoot() => Path.Combine(TestDataDirectory.Read()!, "sk11f-env-" + Guid.NewGuid().ToString("N"));

    private static Dictionary<string, string> Sample(string path) => new(StringComparer.Ordinal)
    {
        ["PATH"] = path, ["HOME"] = "/home/someone", ["DEEPSEEK_API_KEY"] = "sk-not-real", ["EMPTY"] = "",
    };

    [Fact, Trait("Decision", "ED-309"), Trait("Rule", "R-TEST-9")]
    public void The_bin_folder_goes_in_before_the_first_WindowsApps_entry_and_no_other_variable_changes()
    {
        var root = NewRoot();
        var apps = "/home/u/AppData/Local/Microsoft/WindowsApps";
        var original = string.Join(Path.PathSeparator, "/usr/bin", "/opt/tools", apps, "/last");
        var env = Sample(original);
        var child = ProviderChildEnvironment.Apply(env, root);

        var bin = Path.Combine(root, "addons", "bin");
        Assert.Equal(string.Join(Path.PathSeparator, "/usr/bin", "/opt/tools", bin, apps, "/last"), child["PATH"]);
        Assert.Equal(env.Where(p => p.Key != "PATH").OrderBy(p => p.Key), child.Where(p => p.Key != "PATH").OrderBy(p => p.Key));
        Assert.Equal(env.Count, child.Count);
        Assert.Equal(original, env["PATH"]); // the caller's dictionary is not touched
        Assert.True(Directory.Exists(bin), "the folder is on PATH even while it is empty");
        Assert.Empty(Directory.GetFileSystemEntries(bin));
    }

    [Fact, Trait("Decision", "ED-309"), Trait("Rule", "R-TEST-9")]
    public void Without_a_WindowsApps_entry_the_bin_folder_is_appended_and_without_a_PATH_it_is_the_PATH()
    {
        var root = NewRoot();
        var bin = Path.Combine(root, "addons", "bin");
        Assert.Equal("/usr/bin" + Path.PathSeparator + bin, ProviderChildEnvironment.Apply(Sample("/usr/bin"), root)["PATH"]);
        var none = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOME"] = "/h" };
        Assert.Equal(bin, ProviderChildEnvironment.Apply(none, root)["PATH"]);
    }

    [Fact, Trait("Decision", "ED-309"), Trait("Rule", "R-TEST-9")]
    public void The_app_overload_uses_the_bound_data_root()
    {
        using var scratch = new StorageScratchFolder();
        var child = ProviderChildEnvironment.Apply(Sample("/usr/bin"));
        Assert.Equal("/usr/bin" + Path.PathSeparator + Path.Combine(scratch.Root, "addons", "bin"), child["PATH"]);
        Assert.Equal(ShimPath.ForChild(Sample("/usr/bin"), scratch.Root)["PATH"], child["PATH"]);
    }

    [Fact, Trait("Rule", "R-TEST-10")]
    public void With_no_data_root_bound_the_environment_is_returned_untouched_and_no_folder_is_made()
    {
        var env = Sample("/usr/bin");
        Assert.Same(env, ProviderChildEnvironment.Apply(env));
        Assert.Equal("/usr/bin", env["PATH"]);
    }
}
