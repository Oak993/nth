// PORT-ONLY: T11 step 2. The model behind the pill editor: one pill's name and steps (account, model, effort, fast mode, Handshake point), "Same for every step / Set each step on its own", the loop line and the earlier-account rule.
// A step starts as "Same as the account"; a change is saved with the pill and a reset clears it (NOTES "Effort and fast mode"). A pill keeps at least two steps. It reads and writes PillStore only.
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using HandshakePoint = Marrow.Core.Accounts.HandshakePoint;

namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed class PillEditorModel
{
    private readonly PillStore pills;
    private readonly AccountStore accounts;
    private readonly Func<Account, bool> connected;
    private readonly Func<ProviderKind, string?, string> modelName;
    private readonly Guid id;
    private HandshakePoint lastSame;
    private Pill? last;

    /// <param name="modelName">The words for a model id of a service (the registry's chip name); the page passes the app's.</param>
    public PillEditorModel(PillStore pills, AccountStore accounts, Func<Account, bool> connected, Func<ProviderKind, string?, string> modelName, Guid id)
    {
        this.pills = pills;
        this.accounts = accounts;
        this.connected = connected;
        this.modelName = modelName;
        this.id = id;
        lastSame = Pill.SameHandshake ?? HandshakePoint.Smart;
        pills.Changed += (_, change) => { if (change.PillId == id) Changed?.Invoke(); };
    }

    public event Action? Changed;

    public Guid PillId => id;

    /// <summary>The pill; once it is deleted, the last state it had (the editor is closing).</summary>
    public Pill Pill
    {
        get
        {
            if (pills.Find(id) is { } found) return last = found;
            return last ?? throw new KeyNotFoundException("That pill was deleted.");
        }
    }

    public bool Exists => pills.Find(id) is not null;

    public string Name => Pill.Name;

    public PillRenameResult Rename(string name) => PillsSettingsModel.Rename(pills, id, name);

    public string CountText => MultiAccountWords.StepCount(Pill.Steps.Count, Pill.Steps.Select(s => PillsSettingsModel.Find(accounts, s.AccountId)?.Service).Distinct().Count());

    public string LoopLine => MultiAccountWords.LoopLine;

    public IReadOnlyList<StepMark> Marks => PillsSettingsModel.StepMarks(Pill, accounts);

    public IReadOnlyList<StepView> Steps
    {
        get
        {
            var pill = Pill;
            var marks = PillsSettingsModel.StepMarks(pill, accounts);
            var locked = pill.HandshakeMode == PillHandshakeMode.SameForEvery;
            return [.. pill.Steps.Select((step, i) =>
            {
                var account = PillsSettingsModel.Find(accounts, step.AccountId);
                var service = account?.Service;
                var defaults = account?.Defaults;
                var model = step.Model ?? defaults?.Model;
                var point = locked ? pill.SameHandshake ?? HandshakePoint.Smart : step.HandshakeAt ?? HandshakePoint.Smart;
                return new StepView(i, i + 1, step.AccountId, marks[i].AccountName, service, service is { } s ? ServiceNames.Title(s) : "", marks[i].Skipped, marks[i].NewService,
                    step.Model is null && step.Effort is null && step.Fast is null, service is { } sv ? modelName(sv, model) : "", model, step.Effort ?? defaults?.Effort, step.Fast ?? defaults?.FastMode ?? false,
                    point, locked, service == ProviderKind.Local, Marrow.Core.Port.MultiAccount.Capsule.AccountMono.Of(marks[i].AccountName, account?.CustomName is not null), Marrow.Core.Port.MultiAccount.Capsule.ComposerCapsuleModel.MarkHue(service ?? ProviderKind.Claude, service is { } hueService ? accounts.ForService(hueService).ToList().FindIndex(a => a.Id == account?.Id) : 0));
            })];
        }
    }

    public IReadOnlyList<AvailableStep> Available
    {
        get
        {
            var taken = Pill.Steps.Select(s => s.AccountId).ToHashSet(StringComparer.Ordinal);
            return [.. accounts.Accounts.Where(a => connected(a) && !taken.Contains(a.Id.ToString("D")))
                .OrderBy(a => (int)a.Service).ThenBy(a => a.Priority)
                .Select(a => new AvailableStep(a.Id.ToString("D"), (a.CustomName ?? accounts.DefaultName(a.Id)) + " · " + (a.Defaults.Model is { } m ? modelName(a.Service, m) : ServiceNames.Title(a.Service)), a.Service))];
        }
    }

    public void AddStep(string accountId) => pills.AddStep(id, new PillStep(accountId));

    /// <summary>Removes a step; false (and nothing changes) when the pill would be left with fewer than two.</summary>
    public bool RemoveStep(int index)
    {
        if (Pill.Steps.Count <= 2) return false;
        pills.RemoveStep(id, index);
        return true;
    }

    public void MoveStep(int from, int to) => pills.MoveStep(id, from, to);

    public IReadOnlyList<MenuEntry> MenuFor(int index)
    {
        var count = Pill.Steps.Count;
        return
        [
            new("up", MultiAccountWords.MoveUp, "Alt+↑", Disabled: index == 0), new("top", MultiAccountWords.MoveToTop, Disabled: index == 0),
            new("down", MultiAccountWords.MoveDown, "Alt+↓", Disabled: index == count - 1),
        ];
    }

    public void Do(int index, string key)
    {
        switch (key)
        {
            case "up" when index > 0: MoveStep(index, index - 1); break;
            case "top" when index > 0: MoveStep(index, 0); break;
            case "down" when index < Pill.Steps.Count - 1: MoveStep(index, index + 1); break;
        }
    }

    public void SetStepSettings(int index, StepSettings settings) => pills.UpdateStep(id, index, step => step with { Model = settings.Model, Effort = settings.Effort, Fast = settings.Fast });

    public void ResetStepSettings(int index) => pills.UpdateStep(id, index, step => step with { Model = null, Effort = null, Fast = null });

    public void SetStepHandshake(int index, HandshakePoint point) => pills.UpdateStep(id, index, step => step with { HandshakeAt = point });

    // ---- Handshake point
    public PillHandshakeMode Mode => Pill.HandshakeMode;

    public HandshakePoint Same => Pill.SameHandshake ?? lastSame;

    public string HandshakeDescription => Mode == PillHandshakeMode.SameForEvery ? MultiAccountWords.SameDescription : MultiAccountWords.EachDescription;

    public void SetSame(HandshakePoint point)
    {
        lastSame = point;
        pills.SetHandshakeMode(id, PillHandshakeMode.SameForEvery, point);
    }

    /// <summary>Switching to each step on its own gives every step without a point of its own the pill's one point; switching back keeps what the steps hold.</summary>
    public void SetMode(PillHandshakeMode mode)
    {
        var pill = Pill;
        if (pill.HandshakeMode == mode) return;
        if (mode == PillHandshakeMode.SameForEvery)
        {
            pills.SetHandshakeMode(id, mode, lastSame);
            return;
        }
        lastSame = pill.SameHandshake ?? lastSame;
        pills.SetHandshakeMode(id, mode);
        for (var i = 0; i < pill.Steps.Count; i++)
            if (pill.Steps[i].HandshakeAt is null) pills.UpdateStep(id, i, step => step with { HandshakeAt = lastSame });
    }

    // ---- When an earlier account is back
    public SettingRow EarlierRow
    {
        get
        {
            var value = Pill.EarlierAccount;
            var label = value switch { DecisionValues.GoBack => MultiAccountWords.EarlierGo, DecisionValues.Stay => MultiAccountWords.EarlierStay, _ => MultiAccountWords.EarlierSettings };
            var description = value switch { DecisionValues.GoBack => MultiAccountWords.EarlierGoDescription, DecisionValues.Stay => MultiAccountWords.EarlierStayDescription, _ => MultiAccountWords.EarlierSettingsDescription };
            return new SettingRow("earlier", MultiAccountWords.EarlierTitle, SettingKind.Menu, label, description, true, false, false, false,
                [new("settings", MultiAccountWords.EarlierSettings, true, value is null), new(DecisionValues.GoBack, MultiAccountWords.EarlierGo, false, value == DecisionValues.GoBack),
                 new(DecisionValues.Stay, MultiAccountWords.EarlierStay, false, value == DecisionValues.Stay)], null);
        }
    }

    public void ChooseEarlier(string value) => pills.SetEarlierAccount(id, value == "settings" ? null : value);
}
