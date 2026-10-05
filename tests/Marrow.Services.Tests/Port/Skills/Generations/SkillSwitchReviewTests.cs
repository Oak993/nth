using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Generations;

public sealed class SkillSwitchReviewTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void A_plugin_and_its_folder_default_off_and_an_explicit_folder_choice_wins()
    {
        Assert.False(SkillSwitches.Empty.PluginEnabled("plugin"));
        Assert.False(SkillSwitches.Empty.PluginEnabled("plugin", "folder"));
        var on = SkillSwitches.Empty.WithPlugin("plugin", true);
        Assert.True(on.PluginEnabled("plugin", "folder"));
        Assert.False(on.WithPlugin("plugin/folder", false).PluginEnabled("plugin", "folder"));
        Assert.True(SkillSwitches.Empty.WithPlugin("plugin/folder", true).PluginEnabled("plugin", "folder"));
    }
}
