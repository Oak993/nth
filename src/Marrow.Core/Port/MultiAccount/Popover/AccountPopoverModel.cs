// PORT-ONLY: MA-3b-1, AccountPanel multi-account.html :2674-2797; OD-0115/0116/0117/0119.
using System.Globalization;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Core.Port.MultiAccount.Popover;

public sealed class AccountPopoverModel : IDisposable
{
    private readonly IThreadAccountPort port;
    private readonly AccountStore store;
    private readonly Func<ProviderKind, IReadOnlyList<ModelOption>> models;
    private readonly Func<int> connectedCount;
    private readonly Func<Guid, string?> localServer;
    private readonly TimeZoneInfo zone;
    public AccountPopoverModel(IThreadAccountPort port, AccountStore store, Func<ProviderKind, IReadOnlyList<ModelOption>>? models = null,
        Func<int>? connectedCount = null, Func<Guid, string?>? localServer = null, TimeZoneInfo? zone = null)
    {
        this.port = port; this.store = store; this.models = models ?? (_ => []);
        this.connectedCount = connectedCount ?? (() => port.Accounts.Count); this.localServer = localServer ?? (_ => null);
        this.zone = zone ?? TimeZoneInfo.Local;
        port.Changed += Refresh; store.Changed += Refresh;
    }
    public event Action? Changed;
    private void Refresh() => Changed?.Invoke();
    public void Dispose() { port.Changed -= Refresh; store.Changed -= Refresh; }
    public string Header => ServiceNames.Title(port.Service) + (port.Service == ProviderKind.Local ? " models" : " accounts");
    public string OrderHint => "Drag to set the order";
    public string Subtitle => "Priority order: row 1 starts new threads";
    public bool CanCreatePill => connectedCount() >= 2;
    public string CreatePillText => "Create a pill";
    public IReadOnlyList<AccountPopoverRow> Rows => port.Accounts.Select(Row).ToArray();
    private AccountPopoverRow Row(ThreadAccount a)
    {
        var point = a.Defaults.HandshakePoint ?? store.GeneralHandshakePoint;
        var handshake = a.NoLimit ? "Never runs out" : point.Percentage is { } percent ? "At " + percent.ToString(CultureInfo.InvariantCulture) + "%" : "Automatically";
        var reset = a.NoFiveHour ? a.Usage?.WeekReset : a.Usage?.FiveHourReset;
        return new(a, a.Id == port.ActiveAccountId, a.Id == port.PinnedAccountId, !a.Enabled ? "Off" : a.IsOut ? "Skipped" : null,
            !a.Enabled || a.IsOut, !a.Enabled ? "" : a.NoLimit ? localServer(a.Id) ?? "" : reset is { } time ? "resets " + Reset(time) : "",
            PopoverUsageBar.Of(a.NoLimit || a.NoFiveHour ? null : a.Usage?.FiveHour, a.IsOut),
            PopoverUsageBar.Of(a.NoLimit ? null : a.Usage?.Week), a.NoFiveHour ? "No 5-hour limit" : null,
            a.NoLimit ? "No limit · Stays on this PC" : null, AccountConfigText.Of(a.Defaults, models(a.Service)), handshake,
            "Handshake point for " + a.Name + ": " + handshake + (a.Defaults.HandshakePoint is null && !a.NoLimit ? ", like all accounts" : ""));
    }
    private string Reset(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
    public string Footer
    {
        get
        {
            var names = port.Accounts.Where(a => a.Enabled).Select(a => a.Name).ToArray();
            var first = names.FirstOrDefault() ?? "none";
            if (port.Service == ProviderKind.Local) return "New threads start on " + first + (names.Length > 1 ? " · then " + string.Join(", ", names.Skip(1)) : "") + " · local models never run out";
            return "New threads start on " + first + (names.Length < 2 ? "" : " · when it runs out, " + (names.Length == 2 ? names[1] : string.Join(", ", names.Skip(1).SkipLast(1)) + ", then " + names[^1]));
        }
    }
    public string? PinnedText => port.Accounts.FirstOrDefault(a => a.Id == port.PinnedAccountId) is { } a ? "This thread: " + a.Name + " · Back to order" : null;
    public string RememberText => port.ThreadChoices.Count == 0 ? "This thread follows Settings" : "This thread remembers: " + string.Join(" · ", port.ThreadChoices.OrderBy(c => c.Kind).Select(Label)) + " · Use Settings";
    private static string Label(Choice c) => (c.Kind, c.Value) switch
    {
        (DecisionKind.OnLimit, DecisionValues.Switch) => "Switch automatically",
        (DecisionKind.OnLimit, DecisionValues.Wait) => "Wait for reset",
        (DecisionKind.OtherService, DecisionValues.Switch) => "Switch to another service",
        (DecisionKind.OtherService, DecisionValues.Wait) => "Wait",
        (DecisionKind.MidReply, DecisionValues.Finish) => "Finish the reply on the next account",
        (DecisionKind.MidReply, DecisionValues.Stop) => "Stop, use the next one from my next message",
        (DecisionKind.Bring, DecisionValues.Full) => "Full conversation",
        (DecisionKind.Bring, DecisionValues.MarrowSummary) => "Marrow’s summary",
        (DecisionKind.Bring, DecisionValues.AiSummary) => "AI summary",
        (DecisionKind.EarlierAccount, DecisionValues.GoBack) => "Go back to it",
        (DecisionKind.EarlierAccount, DecisionValues.Stay) => "Stay until this one runs out",
        (_, DecisionValues.Ask) => "Ask me",
        _ => throw new ArgumentException("Unknown thread choice."),
    };
    public Task<PinResult> PinAsync(Guid id, Bring? bring = null, CancellationToken ct = default) => port.PinAsync(id, bring, ct);
    public void BackToOrder() => port.BackToOrder();
    public void UseSettings() => port.UseSettings();
    public void Reorder(IReadOnlyList<Guid> ids)
    {
        var shown = port.Accounts.Select(a => a.Id).ToHashSet();
        if (ids.Count != shown.Count || ids.Distinct().Count() != ids.Count || !shown.SetEquals(ids)) throw new ArgumentException("Every shown account must occur once.", nameof(ids));
        var next = store.ForService(port.Service).Select(a => a.Id).ToArray();
        var index = 0;
        for (var n = 0; n < next.Length; n++) if (shown.Contains(next[n])) next[n] = ids[index++];
        store.Reorder(port.Service, next);
    }
    private void Move(Guid id, int delta)
    {
        var ids = port.Accounts.Select(a => a.Id).ToList(); var index = ids.IndexOf(id); var target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count) return;
        ids.RemoveAt(index); ids.Insert(target, id); Reorder(ids);
    }
    public void MoveUp(Guid id) => Move(id, -1);
    public void MoveDown(Guid id) => Move(id, 1);
}
