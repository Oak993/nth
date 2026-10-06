using System.Text;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeOffTransferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_final_context_refresh_retains_the_receipts_readback_mode(bool readBack)
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.DeepseekAPIKeyInput = "offline-off-receipt");
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), ProviderKind.Deepseek.Id, "Destination"));
            incoming.SendScript = input =>
            {
                incoming.Emit(new ProviderEvent.MessageCompleted("receipt", "Recorded."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), readBack, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
            var refresh = incoming.Inputs[1].Text;
            Assert.Equal(readBack, refresh.Contains("Reply with the files, open tasks, and latest user instructions you understood.", StringComparison.Ordinal));
            if (!readBack)
            {
                Assert.Equal(2, incoming.Inputs.Count);
                Assert.All(incoming.Inputs, input => Assert.Contains("Acknowledge the context and wait for the user's next turn.", input.Text, StringComparison.Ordinal));
            }
            Assert.Equal(destination.Id, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        }, AppModelHarness.Bound);
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        Assert.Equal(1, rig.Session.StopCalls);
    }

    [Theory]
    [InlineData(ProviderKind.Claude, false)]
    [InlineData(ProviderKind.Claude, true)]
    [InlineData(ProviderKind.Local, false)]
    [InlineData(ProviderKind.Local, true)]
    public void An_explicit_manual_pin_obeys_the_actual_Handshake_choice(ProviderKind provider, bool enabled)
    {
        using var rig = new ManualContextPinRig(provider);
        rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.AccountHandshakeEnabled = enabled);
        rig.Rig.H.Main.Loop.Run(async () => Assert.True(await rig.Pin(), rig.Describe), AppModelHarness.Bound);
        var prompts = provider == ProviderKind.Claude
            ? rig.Process!.Sent.Where(value => value["type"]?.JSONValueString == "user").Select(value => value.CompactString).ToArray()
            : rig.Http.Received.Select(value => Encoding.UTF8.GetString(value.Body)).ToArray();
        Assert.NotEmpty(prompts);
        Assert.Equal(enabled, prompts[0].Contains("Reply with the files, open tasks, and latest user instructions you understood.", StringComparison.Ordinal));
        if (!enabled)
        {
            Assert.Equal(2, prompts.Length);
            Assert.All(prompts, prompt => Assert.DoesNotContain("These recorded facts were missing from your read-back.", prompt, StringComparison.Ordinal));
        }
        Assert.Equal(enabled, rig.Rig.App.Settings.AccountHandshakeEnabled);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(1, rig.Rig.Session.StopCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changing_the_Handshake_choice_while_manual_preparation_waits_invalidates_its_receipt(bool enabled)
    {
        using var rig = new ManualContextPinRig(ProviderKind.Local, true);
        rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.AccountHandshakeEnabled = enabled);
        rig.Rig.Send("Keep this running instruction.");
        Task<bool> selection = null!;
        rig.Rig.H.Main.OnMain(() => selection = rig.Pin());
        rig.Rig.H.Main.Loop.RunUntil(() => rig.HasReceived || selection.IsCompleted, AppModelHarness.Bound);
        Assert.True(rig.HasReceived, rig.Describe);
        rig.Rig.H.Main.OnMain(() => rig.Rig.App.Settings.AccountHandshakeEnabled = !enabled);
        rig.Rig.Complete();
        rig.Rig.H.Main.Loop.Run(async () => Assert.False(await selection), AppModelHarness.Bound);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
        Assert.False(rig.Incoming!.IsRunning);
    }
}
