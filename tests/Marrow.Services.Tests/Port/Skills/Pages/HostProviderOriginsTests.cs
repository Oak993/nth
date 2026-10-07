using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Pages;

public sealed class HostProviderOriginsTests
{
    [Fact]
    [Trait("Feature", "SK-16")]
    [Trait("Rule", "R-TEST-4")]
    public void The_default_library_backend_reads_the_same_hosts_provider_observations()
    {
        using var f = new GenerationFixture();
        using var home = new ForeignHome();
        var boundary = (Marrow.Core.Port.Ambient<string>)typeof(LoginEnvironment).GetProperty("HomeForTesting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        using var homeScope = boundary.Override(home.Home);
        home.Skill(".claude/skills/synced", "pdf", "pdf", "Account fixture.");
        using var host = SkillsHost.Open(f.Paths.Root, new SkillsHostOptions
        {
            Catalogue = SkillsHostRig.Catalogue(), Protection = f.Protection, InstallLock = new FakeInstallLock(),
        });
        var backend = new HostLibraryBackend(host, host.Catalogue);
        Assert.Empty(backend.Providers());
        host.ProviderSkills.Observe("claude", "Claude", home.Root, []);
        var skill = Assert.Single(Assert.Single(backend.Providers()).Skills);
        Assert.Equal("pdf", skill.Name);
        Assert.True(skill.AccountProvided);
        Assert.Empty(host.Pipeline.State.Items);
    }
}
