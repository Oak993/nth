using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The two provider loads a turn or a session start waits for: ThreadRuntime.swift:1283-1285 (loadCommands, B2 numbering
// :1283-1285) and :1439-1441 (the heads provider's catalogue). The provider's CLI is the offline probe stand-in
// (Fixtures/ProviderProbeStandIns.cs), reached only through ProviderRegistry.SetCLIUpdaterExecutableForTesting, which
// records every call it gets; a load that happened is a call in that record.
public sealed class ThreadRuntimeProviderLoadTests
{
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static IDisposable Executable(ProviderKind provider, ProviderProbeScript script) =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(candidate => candidate == provider ? FileUrl(script.ExecutablePath) : null);

    [Fact]
    [Trait("Req", "PR-RUNTIME-065")] // :1283-1285 a message that begins with a slash command loads the provider's command list first
    public void A_message_beginning_with_a_slash_command_loads_that_providers_command_list()
    {
        var script = ProviderProbeStandIns.CopyFor("slash-load");
        using var seam = Executable(ProviderKind.Claude, script);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);

        rig.Send("/review the diff");

        Assert.NotEmpty(script.CommandListings());
        Assert.Equal("/review the diff", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-065")] // :1283 a message with no slash command does no load
    public void A_message_with_no_slash_command_loads_no_command_list()
    {
        var script = ProviderProbeStandIns.CopyFor("no-slash-load");
        using var seam = Executable(ProviderKind.Claude, script);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);

        rig.Send("review the diff");

        Assert.Empty(script.CommandListings());
        Assert.Single(rig.Session.Inputs);
    }

    private static HydraPair CrossProviderPair() => new(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex };

    private static ThreadRuntimeTurnRig HydraLead(HydraPair pair) => new(ProviderKind.Claude,
        thread => thread with { HydraPairID = pair.Id },
        hydraOn: true,
        settings: app =>
        {
            app.Settings.HydraEnabled = true;
            app.Settings.AddHydraPair(pair);
        });

    private static void EnsureSession(ThreadRuntimeTurnRig rig)
    {
        Task task = null!;
        rig.H.Main.OnMain(() => task = ThreadRuntimeReflection.Call<Task>(rig.Runtime, "EnsureSession", rig.Directory, true)!);
        rig.H.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);
        task.GetAwaiter().GetResult();
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-024")] // :1439-1441 a pair whose heads run on another provider loads that provider's catalogue before the session is judged
    public void A_pair_whose_heads_run_on_another_provider_loads_that_providers_catalog_when_the_session_is_ensured()
    {
        var codex = ProviderProbeStandIns.CopyFor("heads-catalog");
        using var seam = Executable(ProviderKind.Codex, codex);
        var pair = CrossProviderPair();
        using var rig = HydraLead(pair);
        Assert.Empty(codex.RecordedCalls());

        EnsureSession(rig);

        Assert.NotEmpty(codex.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-024")] // :1439 `!= thread.provider`: heads on the lead's own provider load nothing extra
    public void A_pair_whose_heads_run_on_the_leads_own_provider_loads_no_other_catalog()
    {
        var codex = ProviderProbeStandIns.CopyFor("same-provider-heads");
        var claude = ProviderProbeStandIns.CopyFor("same-provider-lead");
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(candidate => candidate switch
        {
            ProviderKind.Codex => FileUrl(codex.ExecutablePath),
            ProviderKind.Claude => FileUrl(claude.ExecutablePath),
            _ => null,
        });
        var pair = new HydraPair(ProviderKind.Claude);
        using var rig = HydraLead(pair);

        EnsureSession(rig);

        Assert.Empty(codex.RecordedCalls());
        Assert.Empty(claude.RecordedCalls());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-024")] // :1439 `app.hydraIsOn(thread)`: with Hydra off for the chat nothing is loaded
    public void With_hydra_off_for_the_chat_the_heads_catalog_is_not_loaded()
    {
        var codex = ProviderProbeStandIns.CopyFor("hydra-off-heads");
        using var seam = Executable(ProviderKind.Codex, codex);
        var pair = CrossProviderPair();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude,
            thread => thread with { HydraPairID = pair.Id, HydraEnabled = false },
            settings: app => app.Settings.AddHydraPair(pair));

        EnsureSession(rig);

        Assert.Empty(codex.RecordedCalls());
    }
}
