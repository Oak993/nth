using System.Text.Json;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class ConfiguredProviderCompositionTests
{
    [Theory]
    [InlineData("copilot", false)]
    [InlineData("commandcode", false)]
    [InlineData("commandcode", true)]
    [Trait("Feature", "SK-11")]
    [Trait("Rule", "R-TEST-4")]
    public void Configured_user_skill_roots_win_without_writing_the_users_configuration(string provider, bool projectSettings)
    {
        using var f = new GenerationFixture();
        using var home = new ForeignHome();
        var item = f.Add("O3", "pdf");
        var project = Path.Combine(home.Home, "project");
        Directory.CreateDirectory(project);
        PipelineRoutes.Prepare(f.Paths.StateFile, new(item.Store.Catalogue.Id, item.Store.Pin, item.Store.Folder, [], new(StringComparer.Ordinal)));
        AddonsStateStore.Save(f.Paths.StateFile, AddonsState.Restore([new("O3", ItemState.Installed, item.Store.Pin, null, null)], [], [], []));
        using var host = SkillsHost.Open(f.Paths.Root, new SkillsHostOptions
        {
            Catalogue = SkillsHostRig.Catalogue(item.Store.Catalogue), Protection = f.Protection, InstallLock = new FakeInstallLock(),
            Trust = (_, _, _) => new([item.Store], new Dictionary<(string, string), IReadOnlyList<string>>(), []),
        });
        var environment = new Dictionary<string, string>();
        if (provider == "copilot")
        {
            var external = Path.Combine(home.Home, "alternative-copilot");
            home.Skill("alternative-copilot/skills", "pdf", "pdf", "User configured fixture.");
            environment["COPILOT_HOME"] = external;
        }
        else
        {
            var external = home.Skill("registered-skills", "pdf", "pdf", "User registered fixture.");
            home.Raw(projectSettings ? "project/.commandcode/settings.local.json" : ".commandcode/settings.json", JsonSerializer.Serialize(new { skills = new[] { external } }));
        }
        var before = ForeignHome.Fingerprint(home.Home);
        using var generation = ProviderGenerationComposer.Prepare(host, provider, "configured", project, home.Home, f.Budget, environment);
        Assert.NotNull(generation);
        Assert.Empty(generation.Skills);
        Assert.True(generation.Verify());
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home));
    }
}
