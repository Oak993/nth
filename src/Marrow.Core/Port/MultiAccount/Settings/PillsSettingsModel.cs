// PORT-ONLY: T11 step 2 (OD-0114, OD-0117). The model behind Settings > Pills: the list of pills, New pill, Duplicate / Rename / Delete, "Use for new threads". The page is always in the sidebar; with fewer than two accounts connected
// it is disabled under a plain explanation (OD-0117) and everything enables when a second account is connected. It reads and writes PillStore and AccountStore only.
using System.Globalization;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Port.Accounts;

namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed class PillsSettingsModel
{
    private readonly PillStore pills;
    private readonly AccountStore accounts;
    private readonly Func<Account, bool> connected;

    public PillsSettingsModel(PillStore pills, AccountStore accounts, Func<Account, bool> connected)
    {
        this.pills = pills;
        this.accounts = accounts;
        this.connected = connected;
        pills.Changed += (_, _) => Changed?.Invoke();
    }

    public event Action? Changed;

    public void Refresh() => Changed?.Invoke();

    /// <summary>Two or more accounts are connected (of one service or of several).</summary>
    public bool Enabled => accounts.Accounts.Count(connected) >= 2;

    public string WhatText => MultiAccountWords.DisabledWhat;

    public string NeedText => MultiAccountWords.DisabledNeed;

    public IReadOnlyList<PillRowView> Rows => [.. pills.Pills.Select(RowOf)];

    internal PillRowView RowOf(Pill pill)
    {
        var marks = StepMarks(pill, accounts);
        var services = marks.Select(m => m.Service).Distinct().Count();
        return new PillRowView(pill.Id, pill.Name, string.Join(" → ", marks.Select(m => m.AccountName)), MultiAccountWords.PillSteps(pill.Steps.Count, services), marks);
    }

    internal static IReadOnlyList<StepMark> StepMarks(Pill pill, AccountStore accounts)
    {
        var marks = new List<StepMark>();
        foreach (var step in pill.Steps)
        {
            var account = Find(accounts, step.AccountId);
            var service = account?.Service;
            marks.Add(new StepMark(account is null ? "Unknown account" : account.CustomName ?? accounts.DefaultName(account.Id), service, account is null || !account.Enabled, marks.Count > 0 && marks[^1].Service != service));
        }
        return marks;
    }

    internal static Account? Find(AccountStore accounts, string id) => Guid.TryParse(id, out var guid) ? accounts.Accounts.FirstOrDefault(a => a.Id == guid) : null;

    /// <summary>A new pill: "New pill" (numbered when taken), the first two usable accounts in order, one Smart Handshake for every step (the page's default). Null while the page is disabled.</summary>
    public Pill? Create()
    {
        if (!Enabled) return null;
        var name = MultiAccountWords.DefaultPillName;
        for (var n = 2; pills.NameProblem(name) != PillNameProblem.None; n++) name = MultiAccountWords.DefaultPillName + " " + n.ToString(CultureInfo.InvariantCulture);
        // the service with the most connected accounts first (the page's pill starts on two accounts of one service), then the others in the sidebar's order
        var weight = accounts.Accounts.Where(connected).GroupBy(a => a.Service).ToDictionary(g => g.Key, g => g.Count());
        var usable = accounts.Accounts.Where(a => connected(a) && a.Enabled).OrderByDescending(a => weight[a.Service]).ThenBy(a => (int)a.Service).ThenBy(a => a.Priority).Take(2);
        var pill = pills.Create(name, usable.Select(a => new PillStep(a.Id.ToString("D"))));
        pills.SetHandshakeMode(pill.Id, PillHandshakeMode.SameForEvery, Marrow.Core.Accounts.HandshakePoint.Smart);
        return pills.Find(pill.Id);
    }

    public Pill? Duplicate(Guid id) => pills.Find(id) is null ? null : pills.Duplicate(id);

    public bool Delete(Guid id) => pills.Delete(id);

    public PillRenameResult Rename(Guid id, string name) => Rename(pills, id, name);

    internal static PillRenameResult Rename(PillStore pills, Guid id, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return PillRenameResult.Empty;
        if (new StringInfo(trimmed).LengthInTextElements > 24) return PillRenameResult.TooLong;
        if (pills.Find(id)?.Name == trimmed) return PillRenameResult.Ok;
        if (pills.NameProblem(trimmed, id) == PillNameProblem.Taken) return PillRenameResult.Taken;
        pills.Rename(id, trimmed);
        return PillRenameResult.Ok;
    }

    /// <summary>"Use for new threads": None, or one of the pills.</summary>
    public SettingRow UseRow
    {
        get
        {
            var chosen = pills.UseForNewThreads is { } id ? pills.Find(id) : null;
            var options = new List<MenuOption> { new("none", MultiAccountWords.UseNone, true, chosen is null) };
            options.AddRange(pills.Pills.Select(p => new MenuOption(p.Id.ToString("D"), p.Name, false, p.Id == chosen?.Id)));
            return new SettingRow("use", MultiAccountWords.UseForNew, SettingKind.Menu, chosen?.Name ?? MultiAccountWords.UseNone,
                chosen is null ? MultiAccountWords.UseNoneDescription : MultiAccountWords.UsePillDescription, true, false, false, false, options, null);
        }
    }

    public void ChooseUse(string value) => pills.SetUseForNewThreads(Guid.TryParse(value, out var id) ? id : null);
}
