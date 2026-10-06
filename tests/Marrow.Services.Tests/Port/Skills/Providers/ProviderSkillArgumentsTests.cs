using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e / WU-10 argv rewriting.
public sealed class ProviderSkillArgumentsTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "Marrow Code");
    private static string View(string provider, string chat = "chat-1") => Path.Combine(Data, "skills", "views", provider, chat);
    private static ProviderSkillLaunch Launch(string provider, params string[] plugins) => new(View(provider), Data, plugins);

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Pi_appends_skill_and_never_changes_the_input()
    {
        string[] live = ["--mode", "rpc", "-e", "x", "--no-approve"];
        var result = ProviderSkillArguments.Pi(live, Launch("pi"));
        Assert.Equal([.. live, "--skill", View("pi")], result);
        Assert.Equal(5, live.Length);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void CommandCode_appends_skill_last_after_resume()
    {
        string[] turn = ["-p", "--yolo", "--resume", "s1"];
        Assert.Equal([.. turn, "--skill", View("commandcode")], ProviderSkillArguments.CommandCode(turn, Launch("commandcode")));
    }

    [Theory, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    [InlineData("--permission-mode", "default", "agent", "stdio")]
    [InlineData("agent", "--always-approve", "stdio")]
    public void Grok_puts_plugin_dir_before_the_agent_subcommand(params string[] argv)
    {
        var result = ProviderSkillArguments.Grok(argv, Launch("grok"));
        var at = result.IndexOf("--plugin-dir");
        Assert.Equal(View("grok"), result[at + 1]);
        Assert.True(at + 2 == result.IndexOf("agent"));
        Assert.Equal(argv, result.Where((_, i) => i < at || i >= at + 2).ToArray());
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Grok_passes_each_generation_plugin_folder_as_its_own_plugin_dir()
    {
        var extra = Path.Combine(View("grok"), "plugins", "x");
        var result = ProviderSkillArguments.Grok(["agent", "stdio"], Launch("grok", extra));
        Assert.Equal(["--plugin-dir", View("grok"), "--plugin-dir", extra, "agent", "stdio"], result);
    }

    [Theory, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    [InlineData("grok", "claude")]
    [InlineData("pi", "grok")]
    [InlineData("commandcode", "pi")]
    public void A_generation_folder_under_another_providers_views_fails_closed(string wiring, string folderProvider)
    {
        var launch = Launch(folderProvider);
        Assert.Throws<ArgumentException>(() => wiring switch
        {
            "grok" => ProviderSkillArguments.Grok(["agent", "stdio"], launch),
            "pi" => ProviderSkillArguments.Pi(["-e"], launch),
            _ => ProviderSkillArguments.CommandCode(["-p"], launch),
        });
    }

    [Theory, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    [InlineData("relative/views/grok/c")]
    [InlineData("")]
    public void A_relative_or_empty_folder_fails_closed(string folder)
    {
        Assert.Throws<ArgumentException>(() => ProviderSkillArguments.Grok(["agent"], new ProviderSkillLaunch(folder, Data)));
    }
}
