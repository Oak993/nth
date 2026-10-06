using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class ManualContextPinTests
{
    [Theory]
    [InlineData(ProviderKind.Claude, false)]
    [InlineData(ProviderKind.Claude, true)]
    [InlineData(ProviderKind.Local, false)]
    [InlineData(ProviderKind.Local, true)]
    public void An_explicit_CLI_or_Local_selection_adopts_acknowledged_context_and_pins_only_that_thread(ProviderKind provider, bool alreadyPinned)
    {
        using var rig = new ManualContextPinRig(provider, alreadyPinned);
        rig.Rig.H.Main.Loop.Run(async () => Assert.True(await rig.Pin(), rig.Describe), AppModelHarness.Bound);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(1, rig.Rig.Session.StopCalls);
        Assert.Same(rig.Incoming, ThreadRuntimeReflection.Get<Marrow.Services.Providers.ProviderSession>(rig.Rig.Runtime, "session"));
        Assert.True(rig.Rig.Thread.IsPinned);
        var defaults = rig.Rig.App.Settings.Accounts.Get(rig.Target).Defaults;
        Assert.Equal(defaults.Model, rig.Rig.Thread.Model);
        Assert.Equal(defaults.Effort, rig.Rig.Thread.Effort);
        Assert.Equal(defaults.FastMode, rig.Rig.Thread.FastMode);
        Assert.False(string.IsNullOrWhiteSpace(rig.Rig.Thread.ProviderSessionID));
        rig.Rig.H.Main.Advance(TimeSpan.FromSeconds(5));
        typeof(AppModelHarness).GetMethod("FinishDiskWrites", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(rig.Rig.H, null);
        Assert.Equal(rig.Target, AppModelHarness.LoadedLibrary().Threads.Single(thread => thread.Id == rig.Rig.InitialThread.Id).ProviderAccountPinID);
        Assert.Equal(rig.Source, rig.Rig.App.Settings.Accounts.ForService(provider)[0].Id);
        Assert.True(rig.HasReceived);
        Assert.False(rig.Rig.App.Settings.AccountHandshakeEnabled);
        Assert.Empty(rig.Rig.Session.Inputs);
        rig.Rig.H.Main.OnMain(() => Assert.True(rig.Rig.App.TryPinThreadAccount(rig.Rig.InitialThread.Id, null)));
        Assert.Null(rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountID);
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Local)]
    public void A_manual_selection_prepares_in_private_and_waits_for_the_original_reply_to_finish(ProviderKind provider)
    {
        using var rig = new ManualContextPinRig(provider, true);
        rig.Rig.Send("Retain my running instruction.");
        Task<bool> selection = null!;
        rig.Rig.H.Main.OnMain(() => selection = rig.Pin());
        rig.Rig.H.Main.Loop.RunUntil(() => rig.HasReceived || selection.IsCompleted, AppModelHarness.Bound);
        Assert.True(rig.HasReceived, rig.Describe);
        Assert.False(selection.IsCompleted);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountPinID);
        var turn = Assert.Single(rig.Rig.Runtime.Turns).Id;
        rig.Rig.Complete();
        rig.Rig.H.Main.Loop.Run(async () => Assert.True(await selection), AppModelHarness.Bound);
        Assert.Equal(turn, Assert.Single(rig.Rig.Runtime.Turns).Id);
        Assert.Equal(TurnStatus.Completed, rig.Rig.Runtime.Turns[0].Status);
        Assert.Equal(rig.Target, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(1, rig.Rig.Session.StopCalls);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("pin")]
    [InlineData("defaults")]
    [InlineData("cancel")]
    public void Revocation_while_a_manual_selection_waits_preserves_the_source_and_releases_private_work(string change)
    {
        using var rig = new ManualContextPinRig(ProviderKind.Local, true);
        using var cancellation = new CancellationTokenSource();
        rig.Rig.Send("Retain my running instruction.");
        Task<bool> selection = null!;
        rig.Rig.H.Main.OnMain(() => selection = rig.Pin(ct: cancellation.Token));
        rig.Rig.H.Main.Loop.RunUntil(() => rig.HasReceived || selection.IsCompleted, AppModelHarness.Bound);
        Assert.True(rig.HasReceived, rig.Describe);
        rig.Rig.H.Main.OnMain(() =>
        {
            if (change == "off") rig.Rig.App.Settings.Accounts.SetEnabled(rig.Target, false);
            if (change == "pin") rig.Rig.App.UpdateThread(rig.Rig.InitialThread.Id, thread => thread with { ProviderAccountPinID = null });
            if (change == "defaults") rig.Rig.App.Settings.Accounts.SetDefaults(rig.Target, new() { Model = "removed-model" });
            if (change == "cancel") cancellation.Cancel();
        });
        rig.Rig.Complete();
        rig.Rig.H.Main.Loop.Run(async () =>
        {
            if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection);
            else Assert.False(await selection);
        }, AppModelHarness.Bound);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
        Assert.False(rig.Incoming!.IsRunning);
        Assert.False(rig.Rig.App.Settings.AccountHandshakeEnabled);
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Local)]
    public void Back_to_order_cancels_a_pending_manual_transfer_and_clears_only_the_account_pin(ProviderKind provider)
    {
        using var rig = new ManualContextPinRig(provider, true);
        rig.Rig.Send("Retain my running instruction.");
        Task<bool> selection = null!;
        rig.Rig.H.Main.OnMain(() => selection = rig.Pin());
        rig.Rig.H.Main.Loop.RunUntil(() => rig.HasReceived || selection.IsCompleted, AppModelHarness.Bound);
        Assert.True(rig.HasReceived, rig.Describe);
        var cleared = rig.Rig.H.Main.OnMain(() => rig.Rig.App.TryPinThreadAccount(rig.Rig.InitialThread.Id, null));
        // Release a rejected implementation too; the assertion below remains exact.
        if (!cleared) rig.Rig.Complete();
        Assert.True(cleared);
        rig.Rig.H.Main.Loop.Run(async () => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection), AppModelHarness.Bound);
        Assert.Null(rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.True(rig.Rig.Thread.IsPinned);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
        Assert.False(rig.Incoming!.IsRunning);
        rig.Rig.Complete();
    }

    [Fact]
    public void A_real_limit_event_cancels_manual_preparation_and_retains_the_sources_pinned_wait()
    {
        using var rig = new ManualContextPinRig(ProviderKind.Claude, true);
        rig.Rig.Send("Retain my running instruction.");
        Task<bool> selection = null!;
        rig.Rig.H.Main.OnMain(() => selection = rig.Pin());
        rig.Rig.H.Main.Loop.RunUntil(() => rig.HasReceived || selection.IsCompleted, AppModelHarness.Bound);
        Assert.True(rig.HasReceived, rig.Describe);
        rig.Rig.H.Main.OnMain(() => rig.Rig.Session.Emit(new Marrow.Services.Providers.ProviderEvent.UsageLimit(rig.Rig.H.Main.Time.GetUtcNow().AddMinutes(1))));
        rig.Rig.Complete(TurnStatus.Interrupted);
        rig.Rig.H.Main.Loop.Run(async () => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection), AppModelHarness.Bound);
        rig.Rig.H.Main.Loop.Run(() => rig.Rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
        Assert.Equal(HandshakePhase.Waiting, rig.Rig.App.AccountRouting.State(rig.Rig.InitialThread.Id)!.To);
        Assert.False(rig.Incoming!.IsRunning);
    }

    [Fact]
    public void Signing_out_during_CLI_tool_restoration_preserves_the_original_account_and_pin()
    {
        using var rig = new ManualContextPinRig(ProviderKind.Claude, true);
        RuntimeClaudePrimingProcess? process = null;
        using var transport = Marrow.Services.Port.Skills.Providers.ClaudeProviderRuntime.Process.Override((_, args, _, _) =>
        {
            process = new(args, "");
            process.OnPolicyRead = () => { if (process.PolicyReads == 5) rig.Rig.App.Settings.Accounts.MarkSignedIn(rig.Target, false); };
            return process;
        });
        rig.Rig.H.Main.Loop.Run(async () => Assert.False(await rig.Pin()), AppModelHarness.Bound);
        Assert.NotNull(process);
        Assert.True(process.Terminated);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(rig.Source, rig.Rig.Thread.ProviderAccountID);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Local)]
    public void Manual_context_selection_does_not_invent_a_Bring_choice(ProviderKind provider)
    {
        using var rig = new ManualContextPinRig(provider);
        rig.Rig.H.Main.Loop.Run(async () => Assert.False(await rig.Pin(null)), AppModelHarness.Bound);
        Assert.False(rig.HasReceived);
        Assert.Null(rig.Incoming);
        Assert.Null(rig.Rig.Thread.ProviderAccountPinID);
        Assert.Equal(0, rig.Rig.Session.StopCalls);
    }
}
