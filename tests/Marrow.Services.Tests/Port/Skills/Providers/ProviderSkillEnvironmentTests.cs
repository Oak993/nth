using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e / DL-11 and DL-6.
public sealed class ProviderSkillEnvironmentTests
{
    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Without_a_launch_the_very_same_map_comes_back()
    {
        var env = new ValueMap<string, string>(new Dictionary<string, string> { ["PATH"] = "a", ["X"] = "1" });
        Assert.Same(env, ProviderSkillEnvironment.Apply(env, null));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void With_a_launch_only_PATH_changes_and_gets_addons_bin_before_WindowsApps()
    {
        var data = SkillTapFixtures.TempFolder("env");
        var sep = Path.PathSeparator;
        var env = new ValueMap<string, string>(new Dictionary<string, string> { ["PATH"] = "one" + sep + "/u/Microsoft/WindowsApps" + sep + "two", ["X"] = "1", ["Y"] = "2" });
        var child = ProviderSkillEnvironment.Apply(env, new ProviderSkillLaunch(Path.Combine(data, "skills", "views", "pi", "c"), data));
        Assert.Equal("one" + sep + Path.Combine(data, "addons", "bin") + sep + "/u/Microsoft/WindowsApps" + sep + "two", child["PATH"]);
        Assert.Equal(env.Where(p => p.Key != "PATH").OrderBy(p => p.Key).ToArray(), child.Where(p => p.Key != "PATH").OrderBy(p => p.Key).ToArray());
        Assert.Equal("one" + sep + "/u/Microsoft/WindowsApps" + sep + "two", env["PATH"]);
    }
}
