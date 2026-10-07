// PORT-ONLY: T11 step 2 (OD-0114..OD-0117). The model behind Settings > Models for several accounts: the accounts of each connected service (rename, order, per-account on/off, the Handshake point, sign out), "When an account runs
// out" with its indented rows, and the Handshake group. It reads and writes the foundations (AccountStore, AccountDecisionStore, HandshakeSwitch) and holds no state of its own; the view draws it and calls it. Automatic switching starts
// unset (OD-0116): nothing is written until the user chooses.
using System.Globalization;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using HandshakePoint = Marrow.Core.Port.Accounts.HandshakePoint;

namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed class AccountsSettingsModel
{
    private static readonly IReadOnlyList<string> RunKeys = ["run", "mid", "back", "other", "bring", "handed"];
    private static readonly IReadOnlyList<string> HandshakeKeys = ["hs", "hsWhen"];
    private readonly AccountStore accounts;
    private readonly AccountDecisionStore decisions;
    private readonly HandshakeSwitch handshake;
    private readonly Func<Account, bool> connected;
    private readonly Func<bool> pillsExist;
    private readonly IAccountActions actions;
    private readonly Func<Guid, AccountUsageLine?> usage;
    private readonly Func<Guid, double?> credits;
    private readonly TimeProvider clock;
    private readonly TimeZoneInfo zone;

    public AccountsSettingsModel(AccountStore accounts, AccountDecisionStore decisions, HandshakeSwitch handshake, Func<Account, bool> connected, Func<bool> pillsExist,
        IAccountActions actions, Func<Guid, AccountUsageLine?>? usage = null, TimeProvider? clock = null, TimeZoneInfo? zone = null, Func<Guid, double?>? credits = null)
    {
        this.accounts = accounts;
        this.decisions = decisions;
        this.handshake = handshake;
        this.connected = connected;
        this.pillsExist = pillsExist;
        this.actions = actions;
        this.usage = usage ?? (_ => null);
        this.credits = credits ?? (_ => null);
        this.clock = clock ?? TimeProvider.System;
        this.zone = zone ?? TimeZoneInfo.Local;
        decisions.Changed += () => Changed?.Invoke();
    }

    /// <summary>Something this page shows changed: draw again.</summary>
    public event Action? Changed;

    public void Refresh() => Changed?.Invoke();

    // ---------------------------------------------------------------- Accounts
    public IReadOnlyList<ServiceBlockView> Blocks => [.. SwiftEnum<ProviderKind>.AllCases.Where(s => s != ProviderKind.Local).Select(Block).Where(b => b is not null).Select(b => b!)];

    /// <summary>The Local block: always there, so the first local server can be added; its rows are the local models found (each with no limit).</summary>
    public ServiceBlockView LocalBlock => Block(ProviderKind.Local)!;

    private IReadOnlyList<Account> Connected(ProviderKind service) => [.. accounts.ForService(service).Where(connected)];

    private ServiceBlockView? Block(ProviderKind service)
    {
        var list = Connected(service);
        var local = service == ProviderKind.Local;
        if (list.Count == 0 && !local) return null;
        var keys = service.IsAPIKeyBased;
        var several = local ? list.Count > 0 : list.Count > 1;
        var rows = several ? list.Select((a, i) => Row(a, i + 1)).ToArray() : [];
        var first = list.Count > 0 ? Row(list[0], 1) : null;
        string summary;
        if (local) summary = list.Count == 0 ? MultiAccountWords.LocalNone : MultiAccountWords.LocalCount(list.Count);
        else if (keys)
        {
            var left = list.Select(a => credits(a.Id)).ToList();
            summary = MultiAccountWords.Keys(list.Count, left.All(c => c is not null) ? left.Sum(c => c!.Value) : null);
        }
        else summary = several ? MultiAccountWords.Several(list.Count) : MultiAccountWords.One(list[0].Plan);
        return new ServiceBlockView(service, local ? MultiAccountWords.LocalTitle : ServiceNames.Title(service), summary, several,
            AccountCapabilities.SupportsMultipleAccounts(service) && !local && !keys, rows, several || first is null ? null : first.WeekFraction, several || first is null ? "" : first.UsageText,
            ServerBox: local, ActionLabel: keys ? MultiAccountWords.KeysAndOrder : null);
    }

    private AccountRowView Row(Account account, int number)
    {
        var line = usage(account.Id);
        var name = account.CustomName ?? accounts.DefaultName(account.Id);
        var plan = MultiAccountWords.PlanText(account.Plan);
        var point = account.Defaults.HandshakePoint;
        var week = line?.Week;
        var out5 = line is { FiveHour: >= 1 } || line is { Week: >= 1 };
        string subtitle;
        if (!account.Enabled) subtitle = plan + " · " + MultiAccountWords.OffNotUsed;
        else
        {
            subtitle = plan;
            if (point is not null && point != accounts.GeneralHandshakePoint) subtitle += " · " + MultiAccountWords.HandshakeText(PointLabel(point).ToLowerInvariant());
            if (account.CustomName is not null) subtitle += " · " + MultiAccountWords.CustomName;
            if (out5 && (line?.FiveHourReset ?? line?.WeekReset) is { } until) subtitle += " · " + MultiAccountWords.OutUntil(ResetText(until));
        }
        var usageText = account.Service == ProviderKind.Local || line is { NoLimit: true } ? MultiAccountWords.NoLimit : line is null ? ""
            : week is { } w && line.WeekReset is { } reset ? MultiAccountWords.WeekText((int)Math.Round(w * 100, MidpointRounding.AwayFromZero), ResetText(reset)) : "";
        var label = point is not null ? PointLabel(point) : DefaultLabel() + " · " + MultiAccountWords.UsesDefault;
        return new AccountRowView(account.Id, number, name, plan, subtitle, account.Enabled, label, point is null, week, usageText, week >= 0.8, line?.NoFiveHour ?? false);
    }

    private static string PointLabel(HandshakePoint point) => point.Percentage is { } percent ? MultiAccountWords.WhenAt(percent) : MultiAccountWords.WhenSmart;

    private string DefaultLabel() => accounts.GeneralHandshakePoint.Percentage is { } percent ? "at " + percent + "%" : "automatically";

    private string ResetText(DateTimeOffset reset)
    {
        var local = TimeZoneInfo.ConvertTime(reset, zone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
        return local.ToString(local.Date == today ? "h:mm tt" : "ddd h:mm tt", CultureInfo.InvariantCulture);
    }

    public bool Rename(Guid id, string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (name is not null && new StringInfo(name).LengthInTextElements > 24) return false;
        if (name is not null && name == accounts.DefaultName(id)) name = null;
        accounts.Rename(id, name);
        Changed?.Invoke();
        return true;
    }

    public void SetEnabled(Guid id, bool on)
    {
        accounts.SetEnabled(id, on);
        Changed?.Invoke();
    }

    /// <summary>Moves the account to place <paramref name="index"/> among the connected accounts of its service; accounts not connected keep their places.</summary>
    public void MoveTo(Guid id, int index)
    {
        var service = accounts.Get(id).Service;
        var all = accounts.ForService(service).Select(a => a.Id).ToList();
        var shown = Connected(service).Select(a => a.Id).ToList();
        if (!shown.Remove(id)) return;
        shown.Insert(Math.Clamp(index, 0, shown.Count), id);
        var slots = all.Select((x, i) => (x, i)).Where(t => shown.Contains(t.x)).Select(t => t.i).ToList();
        for (var n = 0; n < slots.Count; n++) all[slots[n]] = shown[n];
        accounts.Reorder(service, all);
        Changed?.Invoke();
    }

    public void SetHandshake(Guid id, HandshakePoint? point)
    {
        accounts.SetDefaults(id, accounts.Get(id).Defaults with { HandshakePoint = point });
        Changed?.Invoke();
    }

    public IReadOnlyList<MenuEntry> Menu(Guid id)
    {
        var account = accounts.Get(id);
        var list = Connected(account.Service);
        var index = list.Select(a => a.Id).ToList().IndexOf(id);
        var point = account.Defaults.HandshakePoint;
        return
        [
            new("rename", MultiAccountWords.Rename), MenuEntry.Line,
            new("up", MultiAccountWords.MoveUp, "Alt+↑", Disabled: index <= 0), new("top", MultiAccountWords.MoveToTop, Disabled: index <= 0),
            new("down", MultiAccountWords.MoveDown, "Alt+↓", Disabled: index < 0 || index >= list.Count - 1), MenuEntry.Line,
            .. account.Service == ProviderKind.Local ? Array.Empty<MenuEntry>() : [new("handshake", MultiAccountWords.HandshakeAt, point is null ? MultiAccountWords.LikeAll : PointLabel(point).Replace("At ", "", StringComparison.Ordinal)), MenuEntry.Line],
            new("signout", MultiAccountWords.SignOutOfThis, Disabled: list.Count < 2, Quiet: true),
        ];
    }

    /// <summary>The move entries of <see cref="Menu"/> (up, top, down).</summary>
    public void Do(Guid id, string key)
    {
        var list = Connected(accounts.Get(id).Service).Select(a => a.Id).ToList();
        var index = list.IndexOf(id);
        if (index < 0) return;
        switch (key)
        {
            case "up" when index > 0: MoveTo(id, index - 1); break;
            case "top" when index > 0: MoveTo(id, 0); break;
            case "down" when index < list.Count - 1: MoveTo(id, index + 1); break;
        }
    }

    public IReadOnlyList<MenuEntry> BlockMenu(ProviderKind service)
    {
        var list = Connected(service);
        var canAdd = AccountCapabilities.SupportsMultipleAccounts(service) && service != ProviderKind.Local && !service.IsAPIKeyBased;
        if (list.Count < 2)
        {
            var one = new List<MenuEntry> { new("usage", MultiAccountWords.UsageDetails), new("signout", MultiAccountWords.SignOut, Disabled: true), MenuEntry.Line };
            if (canAdd) one.Add(new("add", MultiAccountWords.AddAnother, Quiet: true));
            return one;
        }
        var many = new List<MenuEntry>();
        if (canAdd) many.Add(new("add", MultiAccountWords.AddAnother));
        many.Add(new("usage", MultiAccountWords.UsageDetails));
        return many;
    }

    /// <summary>The Handshake menu of one account: like all accounts, Automatically (with the plan's own start), then the five points.</summary>
    public IReadOnlyList<MenuEntry> HandshakeMenu(Guid id)
    {
        var account = accounts.Get(id);
        var point = account.Defaults.HandshakePoint;
        var menu = new List<MenuEntry>
        {
            new("like", MultiAccountWords.LikeAllAccounts(DefaultLabel()), Checked: point is null), MenuEntry.Line,
            new("smart", MultiAccountWords.WhenSmart, HandshakeStops.NearText(account.Plan), Checked: point is { Percentage: null }),
        };
        menu.AddRange(HandshakeStops.MenuPoints.Select(n => new MenuEntry(n.ToString(CultureInfo.InvariantCulture), MultiAccountWords.WhenAt(n), Checked: point?.Percentage == n)));
        return menu;
    }

    /// <summary>The example sheet behind "Show an example": the next account in the order (the second account of the first service that has two), the way the next account is brought, the Handshake switch.</summary>
    public HandedOverExample Example()
    {
        var next = SwiftEnum<ProviderKind>.AllCases.Select(Connected).FirstOrDefault(list => list.Count > 1) is { } pair
            ? pair[1].CustomName ?? accounts.DefaultName(pair[1].Id) : MultiAccountWords.HandedNext;
        return HandedOverExample.For(decisions.GetSetting(DecisionKind.Bring), next, handshake.Enabled);
    }

    public async Task<string?> AddAsync(ProviderKind service)
    {
        var message = await actions.AddAccountAsync(service);
        Changed?.Invoke();
        return message;
    }

    /// <summary>Looks for a local server at <paramref name="address"/> and says what it found, in the words of the page.</summary>
    public async Task<string> ConnectLocalAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return MultiAccountWords.LocalBadAddress;
        var result = await actions.ConnectLocalAsync(address.Trim());
        Changed?.Invoke();
        return result.Outcome switch
        {
            LocalConnectOutcome.Found => MultiAccountWords.LocalFoundOn(result.Where, result.Models, result.Kind),
            LocalConnectOutcome.NotFound => MultiAccountWords.LocalNotFound(result.Where),
            _ => MultiAccountWords.LocalBadAddress,
        };
    }

    public async Task SignOutAsync(Guid id)
    {
        await actions.SignOutAsync(id);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- When an account runs out, Handshake
    /// <summary>"When an account runs out" and the Handshake group appear once a service has two accounts or a pill exists; with one account per service Models looks as today.</summary>
    public bool ShowRunOut => pillsExist() || SwiftEnum<ProviderKind>.AllCases.Any(s => Connected(s).Count > 1);

    public IReadOnlyList<SettingRow> RunRows => [.. RunKeys.Select(Row)];

    public IReadOnlyList<SettingRow> HandshakeRows => [.. HandshakeKeys.Select(Row)];

    public SettingRow Row(string key)
    {
        var run = decisions.GetSetting(DecisionKind.OnLimit);
        var go = run == DecisionValues.Switch;
        switch (key)
        {
            case "run":
                return Menu(key, MultiAccountWords.RunTitle, decisions.GetSetting(DecisionKind.OnLimit), null, MultiAccountWords.RunNone,
                    [(DecisionValues.Switch, MultiAccountWords.RunGo, false), (DecisionValues.Ask, MultiAccountWords.RunAsk, false), (DecisionValues.Wait, MultiAccountWords.RunWait, false)],
                    v => v switch { DecisionValues.Switch => MultiAccountWords.RunGoDescription, DecisionValues.Ask => MultiAccountWords.RunAskDescription, DecisionValues.Wait => MultiAccountWords.RunWaitDescription, _ => MultiAccountWords.RunNoneDescription },
                    true, false);
            case "mid":
                return Menu(key, MultiAccountWords.MidTitle, decisions.GetSetting(DecisionKind.MidReply), DecisionValues.Finish, "",
                    [(DecisionValues.Finish, MultiAccountWords.MidFinish, true), (DecisionValues.Stop, MultiAccountWords.MidStop, false)],
                    v => v == DecisionValues.Stop ? MultiAccountWords.MidStopDescription : MultiAccountWords.MidFinishDescription, go, true);
            case "back":
                return Menu(key, MultiAccountWords.BackTitle, decisions.GetSetting(DecisionKind.EarlierAccount), DecisionValues.GoBack, "",
                    [(DecisionValues.GoBack, MultiAccountWords.BackGo, true), (DecisionValues.Stay, MultiAccountWords.BackStay, false)],
                    v => v == DecisionValues.Stay ? MultiAccountWords.BackStayDescription : MultiAccountWords.BackGoDescription, go, true);
            case "other":
                return Menu(key, MultiAccountWords.OtherTitle, decisions.GetSetting(DecisionKind.OtherService), DecisionValues.Ask, "",
                    [(DecisionValues.Ask, MultiAccountWords.OtherAsk, true), (DecisionValues.Switch, MultiAccountWords.OtherSwitch, false), (DecisionValues.Wait, MultiAccountWords.OtherWait, false)],
                    v => v switch { DecisionValues.Switch => MultiAccountWords.OtherSwitchDescription, DecisionValues.Wait => MultiAccountWords.OtherWaitDescription, _ => MultiAccountWords.OtherAskDescription },
                    go || run == DecisionValues.Ask, true);
            case "bring":
                return Menu(key, MultiAccountWords.BringTitle, decisions.GetSetting(DecisionKind.Bring), DecisionValues.MarrowSummary, "",
                    [(DecisionValues.MarrowSummary, MultiAccountWords.BringCompact, true), (DecisionValues.Full, MultiAccountWords.BringFull, false), (DecisionValues.AiSummary, MultiAccountWords.BringAi, false)],
                    v => v switch { DecisionValues.Full => MultiAccountWords.BringFullDescription, DecisionValues.AiSummary => MultiAccountWords.BringAiDescription, _ => MultiAccountWords.BringCompactDescription },
                    go, true);
            case "handed":
                return new SettingRow(key, MultiAccountWords.HandedTitle, SettingKind.Button, "", MultiAccountWords.HandedDescription, go, true, false, false, [], MultiAccountWords.HandedButton);
            case "hs":
                return new SettingRow(key, MultiAccountWords.UseHandshake, SettingKind.Switch, "", handshake.Enabled ? MultiAccountWords.UseHandshakeOn : MultiAccountWords.UseHandshakeOff, true, false, false, handshake.Enabled, [], null);
            case "hsWhen":
                var general = accounts.GeneralHandshakePoint;
                var options = new List<MenuOption> { new("smart", MultiAccountWords.WhenSmart, true, general.Percentage is null) };
                options.AddRange(HandshakeStops.MenuPoints.Select(n => new MenuOption(n.ToString(CultureInfo.InvariantCulture), MultiAccountWords.WhenAt(n), false, general.Percentage == n)));
                return new SettingRow(key, MultiAccountWords.WhenToStart, SettingKind.Menu, general.Percentage is { } at ? MultiAccountWords.WhenAt(at) : MultiAccountWords.WhenSmart,
                    general.Percentage is { } p ? MultiAccountWords.WhenAtDescription(p) : MultiAccountWords.WhenSmartDescription, handshake.Enabled, true, false, false, options, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Not a row of this page.");
        }
    }

    private static SettingRow Menu(string key, string title, string? stored, string? fallback, string none, (string Value, string Label, bool Recommended)[] options,
        Func<string?, string> describe, bool visible, bool indented)
    {
        var current = stored ?? fallback;
        var chosen = options.FirstOrDefault(o => o.Value == current);
        return new SettingRow(key, title, SettingKind.Menu, chosen.Label ?? none, describe(current), visible, indented, stored is null && fallback is null, false,
            [.. options.Select(o => new MenuOption(o.Value, o.Label, o.Recommended, o.Value == current))], null);
    }

    public void Choose(string key, string value)
    {
        switch (key)
        {
            case "run": decisions.SetSetting(DecisionKind.OnLimit, value); break;
            case "mid": decisions.SetSetting(DecisionKind.MidReply, value); break;
            case "back": decisions.SetSetting(DecisionKind.EarlierAccount, value); break;
            case "other": decisions.SetSetting(DecisionKind.OtherService, value); break;
            case "bring": decisions.SetSetting(DecisionKind.Bring, value); break;
            case "hsWhen":
                accounts.GeneralHandshakePoint = value == "smart" ? HandshakePoint.Smart : new HandshakePoint(int.Parse(value, CultureInfo.InvariantCulture));
                Changed?.Invoke();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Not a menu row of this page.");
        }
    }

    public void SetSwitch(string key, bool on)
    {
        if (key != "hs") throw new ArgumentOutOfRangeException(nameof(key), key, "Not a switch row of this page.");
        handshake.Enabled = on;
        Changed?.Invoke();
    }
}
