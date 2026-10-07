using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Install;
using Marrow.Testing;
using System.Text;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class ProviderCompositionReleaseTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("copilot")]
    [InlineData("opencode")]
    [InlineData("opencode-go")]
    [InlineData("pi")]
    [InlineData("commandcode")]
    [InlineData("grok")]
    [InlineData("native")]
    [Trait("Feature", "SK-11")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_family_gets_managed_and_imported_skills_with_progressive_disclosure(string provider)
    {
        using var f = new GenerationFixture();
        using var home = new ForeignHome();
        var item = f.Add("O3", "pdf");
        var project = Path.Combine(home.Root, "project");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(f.Paths.Addons);
        PipelineRoutes.Prepare(f.Paths.StateFile, new(item.Store.Catalogue.Id, item.Store.Pin, item.Store.Folder, [], new(StringComparer.Ordinal)));
        AddonsStateStore.Save(f.Paths.StateFile, AddonsState.Restore([new("O3", ItemState.Installed, item.Store.Pin, null, null)], [], [], []));
        using var host = SkillsHost.Open(f.Paths.Root, new SkillsHostOptions
        {
            Catalogue = SkillsHostRig.Catalogue(item.Store.Catalogue), Protection = f.Protection, InstallLock = new FakeInstallLock(),
            Trust = (_, _, _) => new([item.Store], new Dictionary<(string, string), IReadOnlyList<string>>(), []),
        });
        var picked = home.Skill("picked-skills", "lesson", "lesson", "Imported fixture.");
        var foreign = Assert.Single(new ForeignSkillScanner(home.Home, [picked]).Scan().Skills);
        host.Imported.Import([foreign], ImportMode.Copy, _ => DuplicateChoice.Skip);
        var original = ForeignHome.Fingerprint(home.Home);
        using var generation = ProviderGenerationComposer.Prepare(host, provider, "release-test", project, home.Home, f.Budget, new Dictionary<string, string>());
        Assert.NotNull(generation);
        Assert.Equal(["lesson", "pdf"], generation.Skills.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.True(generation.Verify());
        Assert.DoesNotContain("BODY SENTINEL", generation.Catalogue, StringComparison.Ordinal);
        Assert.Contains("BODY SENTINEL", generation.Invoke("lesson"), StringComparison.Ordinal);
        Assert.Equal(original, ForeignHome.Fingerprint(home.Home));
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Pi)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.OpenRouter)]
    [Trait("Feature", "SK-11")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_production_factory_binds_the_generation_and_native_tool_offer_without_launching(ProviderKind provider)
    {
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        var generations = f.Open();
        using var generation = generations.Start(ProviderSkillsComposition.Family(provider), "factory", [item], f.Switches((item, true)), f.Budget);
        var offer = new BackendFixtureOffer();
        var configuration = new SessionConfiguration
        {
            Provider = provider, WorkingDirectory = SwiftURL.Create(new Uri(f.Paths.Root).AbsoluteUri)!, Environment = new ValueMap<string, string>([]),
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
            SkillGeneration = generation, SkillDataRoot = f.Paths.Root, SkillHome = Path.Combine(f.Paths.Root, "fake-home"), SkillToolOffer = offer,
        };
        var session = SkillSessionFactory.Create(configuration);
        if (session is OpenAICompatibleSession api)
        {
            Assert.NotNull(api.SkillTools);
            Assert.Contains("pdf", api.SkillTools.CatalogueBlock()!, StringComparison.Ordinal);
            Assert.DoesNotContain("BODY SENTINEL", api.SkillTools.CatalogueBlock()!, StringComparison.Ordinal);
            var body = await api.SkillTools.Execute("use_skill", JSONValue.FromDictionaryLiteral(("name", "pdf")));
            Assert.Contains("BODY SENTINEL", body.Output, StringComparison.Ordinal);
            var answer = await api.SkillTools.Execute("request_tool", JSONValue.FromDictionaryLiteral(("name", "qpdf"), ("reason", "Fixture reason.")));
            Assert.Equal("declined", answer.Output);
            Assert.Equal(1, offer.Calls);
        }
        else
        {
            Assert.IsAssignableFrom<ISkillSessionLifetime>(session);
            Assert.True(((ISkillSessionLifetime)session).SkillReadersExited.IsCompleted);
            var field = session.GetType().GetField("skills", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (session is CodexSession) Assert.Same(generation, Assert.IsType<CodexSkillWiring>(field!.GetValue(session)).Chat);
            if (session is PiSession pi) Assert.Equal(generation.Folder, pi.SkillLaunch!.GenerationFolder);
            if (session is CommandCodeSession command) Assert.Equal(generation.Folder, command.SkillLaunch!.GenerationFolder);
            if (provider == ProviderKind.Grok) Assert.Equal(generation.Folder, ((ACPSession)session).SkillLaunch!.GenerationFolder);
        }
        Assert.True(generation.Verify()); // constructing a session cannot release its owner's lease
    }

    [Fact]
    [Trait("Feature", "SK-11a")]
    [Trait("Rule", "R-TEST-4")]
    public void Protocol_EOF_and_Stop_do_not_release_a_Claudes_generation_before_the_fake_child_exits()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        var generations = f.Open();
        using var generation = generations.Start("claude", "exit", [item], f.Switches((item, true)), f.Budget);
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--plugin-dir"), [])));
        RecordingClaudeProcess? child = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => child = new(args, env) { DelayExit = true });
        var session = SkillSessionFactory.Create(new SessionConfiguration
        {
            Provider = ProviderKind.Claude, Executable = SwiftURL.Create("/offline/fixture-claude")!,
            WorkingDirectory = SwiftURL.Create(new Uri(f.Paths.Root).AbsoluteUri)!, Environment = new ValueMap<string, string>([]),
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
            SkillGeneration = generation, SkillDataRoot = f.Paths.Root,
        });
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        var exited = ((ISkillSessionLifetime)session).SkillReadersExited;
        Assert.False(exited.IsCompleted);
        child!.CloseOutput();
        runtime.OnMain(session.Stop);
        Assert.True(child.TerminateRequested);
        Assert.False(exited.IsCompleted);
        Assert.True(generation.Verify());
        child.CompleteExit();
        runtime.Loop.RunUntil(() => exited.IsCompleted && !generation.Verify(), TimeSpan.FromSeconds(5));
        Assert.True(exited.IsCompletedSuccessfully);
    }

}
