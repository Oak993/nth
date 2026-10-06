// PORT-ONLY: SK-17. Real session Start paths launch compiled offline stand-ins; never an account or model.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Collection(PlanLimitsProcessCollection.Name)]
[Trait("Feature", "SK-17"), Trait("Rule", "R-TEST-4")]
public sealed class SkillOverlaySessionTests
{
    private static SwiftURL Url(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/'))!;
    internal static SessionConfiguration Config(ProviderKind provider, string executable, string directory, Generation? skills, string? resume = null,
        ValueMap<string, string>? environment = null)
    {
        var config = new SessionConfiguration
        {
            Provider = provider, Executable = Url(executable), WorkingDirectory = Url(directory),
            Environment = environment ?? ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build, ResumeID = resume,
        };
        var property = typeof(SessionConfiguration).GetProperty("SkillGeneration");
        Assert.NotNull(property);
        property.SetValue(config, skills);
        return config;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("prior-conversation")]
    public void Antigravity_new_and_resumed_sessions_add_the_verified_generation_as_a_second_root(string? resume)
    {
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("agy", "chat", [item], f.Switches((item, true)), f.Budget);
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("sk17");
        script.SetInit("{\"event\":\"init\",\"conversation_id\":\"conv\"}");
        var config = Config(ProviderKind.Antigravity, script.ExecutablePath, f.Paths.Root, g, resume, new([script.EnvironmentEntry]));
        var session = new AntigravitySession(config);
        try
        {
            Assert.Equal("conv", runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));
            var args = Assert.Single(script.Calls());
            Assert.Equal(2, args.Count(a => a == "--add-dir"));
            Assert.Equal(new[] { f.Paths.Root.Replace('\\', '/'), g.Folder }, args.Where((a, i) => i > 0 && args[i - 1] == "--add-dir"));
            Assert.DoesNotContain("--dangerously-skip-permissions", args);
            if (resume is not null) Assert.Contains(resume, args);
        }
        finally { session.Stop(); }
    }

    [Fact]
    public void Cursor_MCP_attachment_preserves_the_original_CLI_arguments()
    {
        using var storage = new StorageScratchFolder(); // ACP's MCP setup reads Storage.Locations, even with no configured servers.
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("sk17-cursor");
        var session = new ACPSession(Config(ProviderKind.Cursor, script.ExecutablePath, f.Paths.Root, g));
        try
        {
            Assert.Equal("s1", runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));
            Assert.Equal(1, script.Launches);
            Assert.Equal(new[] { "acp" }, script.Arguments(0));
        }
        finally { session.Stop(); }
    }

    [Fact]
    public void Cursor_without_a_generation_does_not_probe_or_change_its_arguments()
    {
        using var storage = new StorageScratchFolder();
        using var f = new GenerationFixture();
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("sk17-cursor-off");
        var session = new ACPSession(Config(ProviderKind.Cursor, script.ExecutablePath, f.Paths.Root, null));
        try
        {
            Assert.Equal("s1", runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));
            Assert.Equal(1, script.Launches);
            Assert.Equal(new[] { "acp" }, script.Arguments(0));
        }
        finally { session.Stop(); }
    }

    [Fact]
    public void A_damaged_generation_is_refused_before_starting_the_cli()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("agy", "chat", [item], f.Switches((item, true)), f.Budget);
        f.Trees.Unlock(g.Folder);
        File.AppendAllText(Path.Combine(Assert.Single(g.Skills).Folder, "SKILL.md"), "damage");
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("sk17-damaged");
        var session = new AntigravitySession(Config(ProviderKind.Antigravity, script.ExecutablePath, f.Paths.Root, g, environment: new([script.EnvironmentEntry])));
        Assert.Throws<IOException>(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));
        Assert.Empty(script.Calls());
    }
}
