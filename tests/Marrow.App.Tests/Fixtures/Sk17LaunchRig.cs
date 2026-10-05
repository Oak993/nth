using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.App.Tests.Fixtures;

public sealed class Sk17LaunchRig : IDisposable
{
    private readonly List<IDisposable> scopes = [];
    private readonly GenerationItem item;
    private readonly SkillGenerations generations;
    public Sk17LaunchRig(ProviderKind provider = ProviderKind.Antigravity, string? resume = null)
    {
        var assembly = typeof(ThreadRuntime).Assembly;
        var factoryType = assembly.GetType("Marrow.App.Runtime.CliSessionFactory");
        Assert.NotNull(factoryType);
        var skillsType = assembly.GetType("Marrow.App.Port.Skills.AntigravitySkills");
        Assert.NotNull(skillsType);
        RuntimeRig = new(provider, configure: t => t with { ProviderSessionID = resume }, install: false);
        Files = new();
        item = Files.Add("O3", "pdf");
        generations = Files.Open();
        scopes.Add(ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.Create("marrow-native-path:C:/sk17-fake/cli")!));
        var factory = (Ambient<Func<SessionConfiguration, ProviderSession>>)factoryType.GetProperty("Current", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        scopes.Add(factory.Override(c =>
        {
            if (ConstructionFailure is { } failure) throw failure;
            var fake = new Sk17FakeSession(c) { StartFailure = StartFailureOnce && Sessions.Count > 0 ? null : StartFailure };
            Sessions.Add(fake);
            return fake;
        }));
        var prepare = (Ambient<Func<string, string, Task<Generation>>>)skillsType.GetProperty("Prepare", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        scopes.Add(prepare.Override((chat, directory) =>
        {
            PreparedDirectories.Add(directory);
            var generation = generations.Start("agy", chat, [item], Files.Switches((item, true)), Files.Budget);
            Leases.Add(generation);
            return Task.FromResult(generation);
        }));
    }
    public ThreadRuntimeTurnRig RuntimeRig { get; }
    public GenerationFixture Files { get; }
    public List<Sk17FakeSession> Sessions { get; } = [];
    public List<Generation> Leases { get; } = [];
    public List<string> PreparedDirectories { get; } = [];
    public Exception? StartFailure { get; set; }
    public bool StartFailureOnce { get; set; }
    public Exception? ConstructionFailure { get; set; }
    public ProviderSession Ensure(bool allowNew = true)
    {
        var task = RuntimeRig.H.Main.OnMain(() => ThreadRuntimeReflection.Call<Task<ProviderSession>>(RuntimeRig.Runtime, "EnsureSession", RuntimeRig.Directory, allowNew)!);
        return RuntimeRig.H.Main.Loop.Run(() => task, AppModelHarness.Bound);
    }
    public void Stop() => RuntimeRig.H.Main.OnMain(() => RuntimeRig.Runtime.StopSession());
    public void Dispose()
    {
        Stop();
        foreach (var scope in scopes.AsEnumerable().Reverse()) scope.Dispose();
        foreach (var lease in Leases) lease.Dispose();
        Files.Dispose();
        RuntimeRig.Dispose();
    }
}
