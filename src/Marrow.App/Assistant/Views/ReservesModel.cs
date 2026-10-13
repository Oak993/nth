// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.7; the approved canvas "Standing rules"; unit AS-13; ED-NEW-dextra-as13-10). No Swift twin.
// What the Reserves view shows and does, apart from how it draws: the owner's standing rules, the line he types a new one in
// (read into a rule, or one short question, or the reason it could not be read), the two switches, and the accounts' bars.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

public sealed class ReservesModel : IDisposable
{
    private readonly ReserveSettings settings;
    private readonly Func<IReadOnlyList<ReserveAccountBar>> bars;
    private readonly Func<string?> focus;
    private readonly Func<IReadOnlyList<CapAssistant>> assistants;

    /// <param name="settings">The rules and switches (the store's reserves.json holds them).</param>
    /// <param name="bars">The accounts' bars now (ReserveGuard.Bars); empty before any poll.</param>
    /// <param name="focus">The account the owner is looking at, for the one question; null: the first.</param>
    /// <param name="assistants">The assistants a cap may name ("Channel A: at most 15% of any account a day"); null: none.</param>
    public ReservesModel(ReserveSettings settings, Func<IReadOnlyList<ReserveAccountBar>> bars, Func<string?>? focus = null, Func<IReadOnlyList<CapAssistant>>? assistants = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.bars = bars ?? throw new ArgumentNullException(nameof(bars));
        this.focus = focus ?? (() => null);
        this.assistants = assistants ?? (() => []);
        settings.Changed += Raise;
    }

    /// <summary>The model of a running guard: its rules, its bars, and the account the owner is looking at (null: the first).</summary>
    public static ReservesModel For(ReserveGuard guard, Func<string?>? focus = null, Func<IReadOnlyList<CapAssistant>>? assistants = null) =>
        new((guard ?? throw new ArgumentNullException(nameof(guard))).Settings, guard.Bars, focus, assistants);

    /// <summary>The words typed so far.</summary>
    public string Draft { get; set; } = "";

    /// <summary>The one question a rule that did not name its accounts is waiting on, or null.</summary>
    public ReserveRuleQuestion? Question { get; private set; }

    /// <summary>Why the last words could not be made a rule, or null.</summary>
    public string? Problem { get; private set; }

    public IReadOnlyList<ReserveRule> Rules => settings.Rules;

    /// <summary>The standing caps (AS-15, 13.2): they are listed with the rules, since the owner says them the same way.</summary>
    public IReadOnlyList<CostCap> Caps => settings.Caps;

    /// <summary>The name the owner knows an assistant by; the id when it is not known.</summary>
    public string AssistantName(string id) => assistants().FirstOrDefault(a => a.Id == id)?.Name ?? id;

    public IReadOnlyList<ReserveAccountBar> Bars => bars();

    public bool HoldReserves => settings.HoldReserves;

    public bool AlwaysAskBeforeStarting => settings.AlwaysAskBeforeStarting;

    /// <summary>The rules came from a newer version of nth: they show, and nothing changes them.</summary>
    public bool ReadOnly => settings.ReadOnly;

    /// <summary>Raised when the rules, the switches or the question changed (the view draws again).</summary>
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    /// <summary>The names the owner gave his accounts, from the accounts the guard has read.</summary>
    private IReadOnlyList<string> AccountNames() => [.. bars().Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Reads the typed words as a standing rule (11.7: "Set in Settings > Reserves, by text").</summary>
    public void Submit()
    {
        Question = null;
        Problem = null;
        // Words about a cap ("at most 15% of any account a day") are a cap, not a reserve (AS-15).
        if (CostCapText.LooksLikeCap(Draft))
        {
            var cap = CostCapText.Read(Draft, assistants(), ApiKeyNames());
            if (cap.Problem is { } why) Problem = why;
            else if (cap.Cap is { } made) AddCap(made);
            Raise();
            return;
        }
        var reading = ReserveRuleText.Read(Draft, ReserveLifetime.Standing, AccountNames(), focus());
        if (reading.Question is { } question) Question = question;
        else if (reading.Problem is { } problem) Problem = problem;
        else if (reading.Rule is { } rule) Add(rule);
        Raise();
    }

    /// <summary>The answer to the one question: every account, or only the one it named.</summary>
    public void AnswerQuestion(bool everywhere)
    {
        if (Question is not { } question) return;
        Question = null;
        Add(everywhere ? question.Everywhere : question.OnlyOne);
        Raise();
    }

    // The accounts that are API keys: a key's windows are money, so its cap is in dollars.
    private IReadOnlyList<string> ApiKeyNames() => [.. bars().Where(b => IsApiKey(b.Key)).Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool IsApiKey(string accountKey) =>
        Marrow.Core.Port.SwiftEnum<Marrow.Core.Models.ProviderKind>.FromRawValue(accountKey.Split(':')[0]) is { } kind && SwarmPlaces.Of(kind) == SwarmPlace.Api;

    private void AddCap(CostCap cap)
    {
        if (settings.AddCap(cap) is { } refused) Problem = refused;
        else Draft = "";
    }

    private void Add(ReserveRule rule)
    {
        if (settings.Add(rule) is { } refused) Problem = refused;
        else Draft = "";
    }

    /// <summary>Drops the typed words, the question and the problem (Escape).</summary>
    public void Cancel()
    {
        Draft = "";
        Question = null;
        Problem = null;
        Raise();
    }

    public bool Remove(string id) => settings.Remove(id);

    public bool RemoveCap(string id) => settings.RemoveCap(id);

    public void SetHoldReserves(bool value) => settings.SetHoldReserves(value);

    public void SetAlwaysAskBeforeStarting(bool value) => settings.SetAlwaysAskBeforeStarting(value);

    public void Dispose() => settings.Changed -= Raise;
}
