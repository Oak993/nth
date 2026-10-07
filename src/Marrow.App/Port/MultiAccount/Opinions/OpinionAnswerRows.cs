// PORT-ONLY: MA-3b-7, MA-3b-11 timeline seam. Stable per-round rows subscribe only while attached; W binds job-0 ThreadPortSeams.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.App.Views.Chat;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionAnswerRows : StackPanel
{
    private readonly Guid thread;
    private readonly IOpinionsPort port;
    private readonly Func<IReadOnlyList<ThreadAccount>> accounts;
    private readonly Func<OpinionContinuation> continuation;
    private readonly double above;
    private readonly Func<Guid, Guid, string?> errors;
    private readonly Func<Guid, IReadOnlyList<ThreadAccount>?>? snapshots;
    private readonly Dictionary<Guid, OpinionRoundView> kept = [];
    private bool attached;
    internal IReadOnlyList<OpinionRoundView> Rounds => port.Rounds(thread).Where(r => kept.ContainsKey(r.Id)).Select(r => kept[r.Id]).ToArray();
    internal event Action? Refreshed;
    internal OpinionAnswerRows(Guid thread, IOpinionsPort port, Func<IReadOnlyList<ThreadAccount>> accounts, Func<OpinionContinuation> continuation, double above, Func<Guid, Guid, string?>? errors = null, Func<Guid, IReadOnlyList<ThreadAccount>?>? snapshots = null)
    {
        this.thread = thread; this.port = port; this.accounts = accounts; this.continuation = continuation; this.above = above; Spacing = 12;
        this.errors = errors ?? ((_, _) => null);
        this.snapshots = snapshots;
        AttachedToVisualTree += (_, _) => { attached = true; port.Changed += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => { attached = false; port.Changed -= Changed; };
    }
    internal static OpinionAnswerRows Append(Panel body, Guid thread, IOpinionsPort port, Func<IReadOnlyList<ThreadAccount>> accounts, Func<OpinionContinuation> continuation, double above)
    {
        var host = new OpinionAnswerRows(thread, port, accounts, continuation, above); body.Children.Add(host); return host;
    }
    internal static bool Append(Panel body, ThreadRuntime runtime, double above, Func<IReadOnlyList<(DisplayBlock Block, Control Row)>> visibleRows)
    {
        var seams = ThreadPortSeams.Offer.Current; if (seams.Opinions is not { } port) return false;
        if (OpinionTimelineSeams.Offer.Current(runtime.ThreadID) is not { } context) return false;
        IReadOnlyList<ThreadAccount> Accounts() => SwiftEnum<ProviderKind>.AllCases.SelectMany(service => seams.Account(runtime.ThreadID, service)?.Accounts ?? []).DistinctBy(a => a.Id).ToArray();
        OpinionContinuation Continuation()
        {
            var thread = runtime.Thread; var rows = Accounts();
            var active = thread is null ? null : seams.Account(runtime.ThreadID, thread.Provider);
            var name = rows.FirstOrDefault(a => a.Id == active?.ActiveAccountId)?.Name ?? rows.FirstOrDefault(a => a.Id == thread?.ProviderAccountID)?.Name ?? thread?.Provider.DisplayName ?? "";
            var saved = thread?.PillRelay;
            var relay = saved is not null && runtime.App is { } app ? PillRelayResolver.Resolve(app.Settings.Pills.Pills, saved) : null;
            return new(name, relay?.Pill?.Name, relay?.StepIndex is { } step ? step + 1 : null);
        }
        var host = new OpinionAnswerRows(runtime.ThreadID, port, Accounts, Continuation, 0, context.HeadError, context.AccountsAtAsk);
        _ = new OpinionTimelinePlacement(host, body, visibleRows, context); body.Children.Add(host); return true;
    }
    private void Changed(Guid changed)
    {
        if (changed != thread) return;
        if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(() => { if (attached) Refresh(); });
    }
    private void Refresh()
    {
        var rounds = port.Rounds(thread);
        foreach (var stale in kept.Keys.Where(id => !rounds.Any(r => r.Id == id)).ToArray()) { Children.Remove(kept[stale]); kept.Remove(stale); }
        foreach (var round in rounds)
        {
            if (kept.TryGetValue(round.Id, out var row)) row.Apply(round);
            else { row = new(round, port, snapshots?.Invoke(round.Id) ?? accounts(), continuation, id => errors(round.Id, id)); kept.Add(round.Id, row); Children.Add(row); row.Enter(); }
        }
        Margin = new Thickness(0, Children.Count == 0 ? 0 : above, 0, 0);
        Refreshed?.Invoke();
    }
}
