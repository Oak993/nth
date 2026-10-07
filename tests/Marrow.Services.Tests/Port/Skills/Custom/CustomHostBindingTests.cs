using System.Text;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Feature", "SK-2")]
[Trait("Rule", "R-TEST-4")]
[Collection("Dev data root env")]
public sealed class CustomHostBindingTests
{
    [Fact]
    public async Task Committed_custom_commands_join_child_PATH_after_existing_PC_tools_and_without_changing_parent()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var fake = new CustomSourceFake { Files = new() { ["fixture.exe"] = [77, 90] } };
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), SkillsHostRig.Catalogue()) with { CustomFiles = fake, CustomGitHub = fake });
            var candidate = Assert.Single(await host.CustomSources.Check(new(CustomAddKind.Tools, CustomAddSource.Folder, "fixture", "Fixture", "fixture"), CancellationToken.None));
            var item = await host.CustomSources.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
            var parent = new Dictionary<string, string> { ["PATH"] = "existing-pc-tool", ["OTHER"] = "unchanged" };
            var child = ProviderCustomCommands.ForChild(parent, root);
            var bin = Path.GetDirectoryName(Assert.Single(host.CustomSources.Commands()).Entry)!;
            Assert.Contains(bin, child["PATH"].Split(Path.PathSeparator));
            Assert.Equal("existing-pc-tool", child["PATH"].Split(Path.PathSeparator)[0]);
            Assert.Equal("existing-pc-tool", parent["PATH"]);
            Assert.Equal("unchanged", child["OTHER"]);
            host.CustomSources.SetEnabled(item.Id, false);
            Assert.DoesNotContain(bin, ProviderCustomCommands.ForChild(parent, root)["PATH"].Split(Path.PathSeparator));
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("grok", true)]
    [InlineData("native", false)]
    [InlineData("codex", false)]
    public async Task Custom_plugin_skills_and_hooks_are_composed_into_verified_provider_generations(string provider, bool loadsPlugins)
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            var fake = new CustomSourceFake { Files = new() {
                [".claude-plugin/plugin.json"] = Encoding.UTF8.GetBytes("{\"name\":\"fixture-plugin\",\"description\":\"Fixture plugin.\"}"),
                ["hooks/hooks.json"] = Encoding.UTF8.GetBytes("{}"),
                ["skills/fixture/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: fixture\ndescription: Fixture skill.\n---\nInstructions"),
                ["skills/fixture/resources/file.txt"] = [1, 2, 3]
            } };
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), SkillsHostRig.Catalogue()) with { CustomFiles = fake, CustomGitHub = fake });
            var candidate = Assert.Single(await host.CustomSources.Check(new(CustomAddKind.Plugins, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None));
            var own = await host.CustomSources.Add(candidate, new CustomSourceProgress(), CancellationToken.None);
            using var generation = host.Generations.Start(provider, "fixture-chat", [], SkillsHostRig.Switches(), new(10000));
            var skill = Assert.Single(generation.Skills);
            Assert.Equal("fixture", skill.Name);
            Assert.True(generation.Verify());
            if (loadsPlugins)
            {
                var plugin = Assert.Single(generation.PluginFolders);
                Assert.True(File.Exists(Path.Combine(generation.Folder, plugin, "hooks", "hooks.json")));
                Assert.True(File.Exists(Path.Combine(generation.Folder, plugin, ".claude-plugin", "plugin.json")));
            }
            else Assert.Empty(generation.PluginFolders);
            host.CustomSources.Remove(own.Id);
            Assert.True(generation.Verify()); // an already leased generation owns its bytes
            Assert.Empty(host.CustomSources.Snapshot());
        }
        finally { SkillsHostRig.Delete(root); }
    }
}
