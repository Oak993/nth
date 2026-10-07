using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Port.Accounts;

// OD-0116, ED-439: the context an account without a standby is handed survives an app restart before the first turn (it is kept in the thread's own folder, like the
// rest of the handoff state) and is delivered once.
[Trait("Rule", "R-TEST-4")]
public sealed class DeferredHandoffPersistenceTests
{
    private static string FilePath(ThreadRuntimeTurnRig rig) =>
        Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id), "deferred-handoff.txt");

    private static Guid SwitchDeferred(ThreadRuntimeTurnRig rig, ProviderKind provider, ThreadRuntimeScriptedSession? warm = null)
    {
        Guid account = default;
        rig.H.Main.Loop.Run(async () =>
        {
            account = rig.App.Settings.Accounts.ForService(provider)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(account);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, _) => new(configuration, warm ?? throw new InvalidOperationException("never launched")));
            var target = new SwitchTarget(new(account.ToString("D"), provider.Id, provider.DisplayName));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        return account;
    }

    // What a restart does to the runtime: the in-memory copy is gone and nothing has been checked yet.
    private static void Restart(ThreadRuntimeTurnRig rig)
    {
        ThreadRuntimeReflection.Set(rig.Runtime, "deferredHandoff", null);
        ThreadRuntimeReflection.Set(rig.Runtime, "deferredHandoffChecked", false);
    }

    [Fact]
    public void The_held_context_is_written_to_the_thread_folder_survives_a_restart_and_is_delivered_once()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var account = SwitchDeferred(rig, ProviderKind.Commandcode);
        var path = FilePath(rig);
        Assert.True(File.Exists(path));
        Assert.StartsWith(account.ToString("D") + "\n", File.ReadAllText(path), StringComparison.Ordinal);
        Restart(rig);
        Assert.False(rig.Runtime.HasDeferredHandoff);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.Install(incoming);
        rig.Send("the first message after the restart");
        var first = Assert.Single(incoming.Inputs).Text;
        Assert.Contains("[Marrow Handshake]", first, StringComparison.Ordinal);
        Assert.Contains("the first message after the restart", first, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        rig.Complete();
        rig.Send("second");
        Assert.DoesNotContain("[Marrow Handshake]", incoming.Inputs[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warm_adoption_keeps_the_context_in_the_same_file()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var warm = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-session") };
        SwitchDeferred(rig, ProviderKind.Copilot, warm);
        Assert.True(File.Exists(FilePath(rig)));
        Restart(rig);
        rig.Send("next");
        Assert.Contains("[Marrow Handshake]", Assert.Single(warm.Inputs).Text, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath(rig)));
    }

    [Fact]
    public void A_context_saved_for_another_account_is_never_delivered()
    {
        using var rig = new ThreadRuntimeTurnRig();
        SwitchDeferred(rig, ProviderKind.Commandcode);
        File.WriteAllText(FilePath(rig), Guid.NewGuid().ToString("D") + "\nSTALE-CONTEXT-OF-ANOTHER-ACCOUNT");
        Restart(rig);
        var incoming = new ThreadRuntimeScriptedSession();
        rig.Install(incoming);
        rig.Send("hello");
        Assert.DoesNotContain("STALE-CONTEXT", Assert.Single(incoming.Inputs).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_thread_that_never_held_a_context_reads_no_file_and_sends_its_message_plain()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("plain");
        Assert.Equal("plain", Assert.Single(rig.Session.Inputs).Text);
        Assert.False(File.Exists(FilePath(rig)));
    }
}
