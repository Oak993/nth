using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.App.Tests.Port.Skills;

// RR-15: production ThreadRuntime, offline sessions, explicit fake protection; no CLI, account or network.
[Trait("Rule", "R-TEST-4")]
public sealed class BackendProviderLaunchTests
{
    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Pi)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Grok)]
    [Trait("Feature", "SK-11")]
    public void Resumed_runtime_binds_the_generation_reuses_it_and_waits_for_exit_before_releasing(ProviderKind provider)
    {
        using var rig = new ThreadRuntimeTurnRig(provider, configure: t => t with { ProviderSessionID = "prior-chat" }, install: false);
        using var files = new GenerationFixture();
        var item = files.Add("O3", "pdf");
        var generations = files.Open();
        var leases = new List<Generation>();
        var sessions = new List<Sk17FakeSession>();
        var assembly = typeof(ThreadRuntime).Assembly;
        var preparation = (Ambient<Func<ProviderKind, string, string, IReadOnlyDictionary<string, string>, Task<Generation?>>>)assembly
            .GetType("Marrow.App.Port.Skills.ProviderSkills")!.GetProperty("Prepare")!.GetValue(null)!;
        var factory = (Ambient<Func<SessionConfiguration, ProviderSession>>)assembly.GetType("Marrow.App.Runtime.CliSessionFactory")!
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.Create("marrow-native-path:C:/fixture/cli")!);
        using var prepare = preparation.Override((family, chat, directory, environment) =>
        {
            Assert.Equal(provider, family);
            Assert.Equal(rig.Directory, directory);
            var generation = generations.Start(ProviderSkillsComposition.Family(family), chat, [item], files.Switches((item, true)), files.Budget);
            leases.Add(generation);
            return Task.FromResult<Generation?>(generation);
        });
        using var create = factory.Override(configuration =>
        {
            var session = new Sk17FakeSession(configuration);
            sessions.Add(session);
            return session;
        });
        Task<ProviderSession> Begin() => rig.H.Main.OnMain(() => ThreadRuntimeReflection.Call<Task<ProviderSession>>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        ProviderSession Ensure() { var task = Begin(); return rig.H.Main.Loop.Run(() => task, AppModelHarness.Bound); }
        try
        {
            var first = Assert.IsType<Sk17FakeSession>(Ensure());
            var lease = Assert.Single(leases);
            Assert.Same(lease, first.Configuration.SkillGeneration);
            Assert.Equal("prior-chat", first.Configuration.ResumeID);
            Assert.True(lease.Verify());
            Assert.Same(first, Ensure());
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            first.SkillReadersExited = exited.Task;
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
            Assert.True(lease.Verify());
            var next = Begin();
            Assert.False(next.IsCompleted);
            Assert.Single(leases);
            exited.SetResult();
            var second = rig.H.Main.Loop.Run(() => next, AppModelHarness.Bound);
            Assert.NotSame(first, second);
            Assert.False(lease.Verify());
            Assert.True(leases[1].Verify());
            Assert.Equal(2, sessions.Count);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
            foreach (var lease in leases) lease.Dispose();
        }
    }
    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.OpenRouter)]
    [Trait("Feature", "SK-11f")]
    public void Native_runtime_binds_disclosure_and_invocation_to_its_owned_generation_without_sending_a_turn(ProviderKind provider)
    {
        using var rig = new ThreadRuntimeTurnRig(provider, configure: t => t with { ProviderSessionID = "native-prior-chat" }, install: false);
        rig.H.Main.OnMain(() => rig.App.Settings.SetAPIKeyInput("sk-offline-fixture-key", provider));
        using var files = new GenerationFixture();
        var item = files.Add("O3", "pdf");
        var generations = files.Open();
        Generation? lease = null;
        var preparation = (Ambient<Func<ProviderKind, string, string, IReadOnlyDictionary<string, string>, Task<Generation?>>>)typeof(ThreadRuntime).Assembly
            .GetType("Marrow.App.Port.Skills.ProviderSkills")!.GetProperty("Prepare")!.GetValue(null)!;
        using var prepare = preparation.Override((family, chat, directory, environment) =>
        {
            Assert.Equal(provider, family);
            lease = generations.Start("native", chat, [item], files.Switches((item, true)), files.Budget);
            return Task.FromResult<Generation?>(lease);
        });
        try
        {
            var task = rig.H.Main.OnMain(() => ThreadRuntimeReflection.Call<Task<ProviderSession>>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
            var session = Assert.IsAssignableFrom<OpenAICompatibleSession>(rig.H.Main.Loop.Run(() => task, AppModelHarness.Bound));
            Assert.NotNull(session.SkillTools);
            Assert.Contains("pdf", session.SkillTools.CatalogueBlock()!, StringComparison.Ordinal);
            Assert.DoesNotContain("BODY SENTINEL", session.SkillTools.CatalogueBlock()!, StringComparison.Ordinal);
            var invocation = session.SkillTools.Execute("use_skill", JSONValue.FromDictionaryLiteral(("name", "pdf")));
            var result = rig.H.Main.Loop.Run(() => invocation, AppModelHarness.Bound);
            Assert.Contains("BODY SENTINEL", result.Output, StringComparison.Ordinal);
            Assert.True(lease!.Verify());
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
            Assert.False(lease.Verify());
        }
        finally { rig.H.Main.OnMain(() => rig.Runtime.StopSession()); lease?.Dispose(); }
    }

}
