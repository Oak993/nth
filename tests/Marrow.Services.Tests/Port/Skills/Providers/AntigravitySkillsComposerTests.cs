using System.Reflection;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-17"), Trait("Rule", "R-TEST-4")]
public sealed class AntigravitySkillsComposerTests
{
    private static MethodInfo PrepareMethod()
    {
        var type = typeof(SkillsHost).Assembly.GetType("Marrow.Services.Port.Skills.Providers.AntigravitySkillsComposer");
        Assert.NotNull(type);
        return type.GetMethod("Prepare")!;
    }
    [Fact]
    public void The_host_composes_installed_pins_current_switches_defaults_and_user_clashes()
    {
        var prepare = PrepareMethod();
        using var f = new GenerationFixture();
        var pdf = f.Add("O3", "pdf");
        var design = f.Add("A1", "frontend-design");
        var named = f.Add("O1", "screenshot");
        var off = f.Add("O2", "aspnet-core");
        var items = new[] { pdf, design, named, off };
        Directory.CreateDirectory(f.Paths.Addons);
        foreach (var item in items)
            PipelineRoutes.Prepare(f.Paths.StateFile, new(item.Store.Catalogue.Id, item.Store.Pin, item.Store.Folder, [], []));
        AddonsStateStore.Save(f.Paths.StateFile, AddonsState.Restore(items.Select(i => new ItemRecord(i.Store.Catalogue.Id, ItemState.Installed, i.Store.Pin, null, null)), [], [], []));
        var catalogue = SkillsHostRig.Catalogue(items.Select(i => i.Store.Catalogue).ToArray());
        using var host = SkillsHost.Open(f.Paths.Root, new SkillsHostOptions
        {
            Catalogue = catalogue, Protection = f.Protection, InstallLock = new FakeInstallLock(),
            Trust = (_, _, _) => new(items.Select(i => i.Store).ToArray(), new Dictionary<(string, string), IReadOnlyList<string>>(), []),
        });
        f.Switches((named, true)).Save(f.Paths.EnabledFile);
        var home = Path.Combine(f.Paths.Root, "fake-home");
        var project = Path.Combine(f.Paths.Root, "fake-project");
        var user = Path.Combine(project, ".agents", "skills", "frontend-design");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "SKILL.md"), "---\nname: frontend-design\ndescription: User description kept intact\n---\nUSER BODY\n");
        using var g = (Generation)prepare.Invoke(null, [host, "chat", project, home, f.Budget])!;
        Assert.Equal(new[] { "pdf", "screenshot" }, g.Skills.Select(s => s.Name).Order().ToArray());
        Assert.True(File.Exists(Path.Combine(g.Folder, ".agents", "skills", "pdf", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(g.Folder, ".agents", "skills", "screenshot")));
        Assert.False(Directory.Exists(Path.Combine(g.Folder, ".agents", "skills", "frontend-design")));
        Assert.Contains("USER BODY", File.ReadAllText(Path.Combine(user, "SKILL.md")), StringComparison.Ordinal);
        Assert.True(g.Verify());
        g.Dispose();
        f.Switches((pdf, false), (named, false)).Save(f.Paths.EnabledFile);
        using var next = (Generation)prepare.Invoke(null, [host, "chat", project, home, f.Budget])!;
        Assert.Empty(next.Skills);
    }
    [Fact]
    public void A_fresh_host_still_builds_a_verified_empty_generation_without_writing_provider_files()
    {
        var prepare = PrepareMethod();
        using var f = new GenerationFixture();
        using var host = SkillsHost.Open(f.Paths.Root, new SkillsHostOptions
        {
            Catalogue = SkillsHostRig.Catalogue(), Protection = f.Protection, InstallLock = new FakeInstallLock(),
        });
        var home = Path.Combine(f.Paths.Root, "fake-home");
        var project = Path.Combine(f.Paths.Root, "fake-project");
        Directory.CreateDirectory(project);
        using var g = (Generation)prepare.Invoke(null, [host, "empty", project, home, f.Budget])!;
        Assert.True(g.Verify());
        Assert.Empty(g.Skills);
        Assert.False(Directory.Exists(home));
        Assert.False(File.Exists(f.Paths.EnabledFile));
    }
}
