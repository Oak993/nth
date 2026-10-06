using System.ComponentModel;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Runtime;

public sealed partial class MultiAccountRouting
{
    private readonly Dictionary<Guid, CancellationTokenSource> manualSelections = [];
    private event Action<Guid>? ManualSafePoint;
    internal bool ManualSelectionPending(Guid thread) => manualSelections.ContainsKey(thread);
    internal void CancelManualSelection(Guid thread)
    {
        if (manualSelections.TryGetValue(thread, out var current)) current.Cancel();
    }

    internal Task<bool> PinManualAsync(ThreadRuntime runtime, Guid destination, Bring bring, CancellationToken ct)
    {
        var work = PinManualOwnedAsync(runtime, destination, bring, ct);
        TrackOperation(work);
        return work;
    }

    private async Task<bool> PinManualOwnedAsync(ThreadRuntime runtime, Guid destination, Bring bring, CancellationToken ct)
    {
        MainActor.AssumeIsolated();
        var thread = runtime.ThreadID;
        if (manualSelections.ContainsKey(thread) || app.ApiKeys.AwaitingChoice(thread)
            || routes.TryGetValue(thread, out var route) && route.Machine.Phase != HandshakePhase.Idle
            || !runtime.TryHandshakeAccountId(out var source) || app.Thread(thread) is not { } original) return false;
        var row = app.Settings.Accounts.Accounts.FirstOrDefault(row => row.Id == destination);
        if (row is not { Enabled: true } || row.Service != original.Provider || !app.IsAccountConnected(row) || !app.CanUseAccount(destination)) return false;
        var epoch = runtime.HandshakeStamp.Epoch;
        var readBackEnabled = app.Settings.AccountHandshakeEnabled;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        manualSelections.Add(thread, cancellation);
        bool StillOwns() => !cancellation.IsCancellationRequested && manualSelections.GetValueOrDefault(thread) == cancellation
            && app.Settings.AccountHandshakeEnabled == readBackEnabled
            && !app.ApiKeys.AwaitingChoice(thread) && app.Thread(thread) is { } current
            && current.ProviderAccountPinID == original.ProviderAccountPinID && current.Provider == original.Provider
            && runtime.TryHandshakeAccountId(out var active) && active == source && runtime.HandshakeStamp.Epoch == epoch;
        try
        {
            var summaries = new HandshakeSummaryAdapter((id, token) => runtime.BuildHandshakeSourceAsync(token));
            var host = new ThreadAccountHost(app, runtime, app.Settings.Pills, summaries, manualSelection: StillOwns) { CanCommit = StillOwns };
            var target = new SwitchTarget(new(destination.ToString("D"), row.Service.Id, row.CustomName ?? app.Settings.Accounts.DefaultName(destination)));
            await using var standby = await host.StartStandbyAsync(target, cancellation.Token);
            var summary = await summaries.BuildAsync(thread, cancellation.Token);
            string? briefing = null;
            if (bring == Bring.AiSummary)
            {
                if (app.CanUseAccount(source))
                {
                    try { briefing = await host.RequestBriefingAsync(summary, cancellation.Token); }
                    catch (Exception error) when (error is not OperationCanceledException && SwiftTry.IsThrownError(error)) { }
                }
                if (string.IsNullOrWhiteSpace(briefing)) briefing = await host.RequestIncomingBriefingAsync(target, summary, cancellation.Token);
            }
            var readBack = await standby.ReceiveAsync(new(bring, summary, briefing), readBackEnabled, cancellation.Token);
            if (readBackEnabled)
            {
                var gaps = summaries.FindGaps(summary, readBack ?? "");
                if (gaps.Count > 0) await standby.FillGapsAsync(gaps, cancellation.Token);
            }
            await WaitForManualSafePointAsync(runtime, cancellation.Token);
            if (!StillOwns()) return false;
            await host.SwitchAsync(standby, target, resumeReply: false, cancellation.Token);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException && SwiftTry.IsThrownError(error)) { return false; }
        finally
        {
            if (manualSelections.GetValueOrDefault(thread) == cancellation) manualSelections.Remove(thread);
            AtSafePoint(thread);
        }
    }

    private async Task WaitForManualSafePointAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (runtime.IsAccountSwitchSafe) return;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (runtime.IsAccountSwitchSafe) ready.TrySetResult();
        }
        void Settled(Guid thread) { if (thread == runtime.ThreadID && runtime.IsAccountSwitchSafe) ready.TrySetResult(); }
        runtime.PropertyChanged += Changed;
        ManualSafePoint += Settled;
        try
        {
            using var canceled = ct.Register(() => ready.TrySetCanceled(ct));
            if (runtime.IsAccountSwitchSafe) ready.TrySetResult();
            await ready.Task;
        }
        finally { runtime.PropertyChanged -= Changed; ManualSafePoint -= Settled; }
    }
}
