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

    /// <param name="settings">The rules and switches (the store's reserves.json holds them).</param>
    /// <param name="bars">The accounts' bars now (ReserveGuard.Bars); empty before any poll.</param>
    /// <param name="focus">The account the owner is looking at, for the one question; null: the first.</param>
    public ReservesModel(ReserveSettings settings, Func<IReadOnlyList<ReserveAccountBar>> bars, Func<string?>? focus = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.bars = bars ?? throw new ArgumentNullException(nameof(bars));
        this.focus = focus ?? (() => null);
        settings.Changed += Raise;
    }

    /// <summary>The model of a running guard: its rules, its bars, and the account the owner is looking at (null: the first).</summary>
    public static ReservesModel For(ReserveGuard guard, Func<string?>? focus = null) =>
        new((guard ?? throw new ArgumentNullException(nameof(guard))).Settings, guard.Bars, focus);

    /// <summary>The words typed so far.</summary>
    public string Draft { get; set; } = "";

    /// <summary>The one question a rule that did not name its accounts is waiting on, or null.</summary>
    public ReserveRuleQuestion? Question { get; private set; }

    /// <summary>Why the last words could not be made a rule, or null.</summary>
    public string? Problem { get; private set; }

    public IReadOnlyList<ReserveRule> Rules => settings.Rules;

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

    public void SetHoldReserves(bool value) => settings.SetHoldReserves(value);

    public void SetAlwaysAskBeforeStarting(bool value) => settings.SetAlwaysAskBeforeStarting(value);

    public void Dispose() => settings.Changed -= Raise;
}
