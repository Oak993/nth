// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.2; the approved canvas "Create an Astur"; unit AS-16; ED-NEW-dextra-as16-15). No Swift twin.
// What the profile sheet shows and does, apart from how it looks: the card of a new Astur (drafted from a sentence, from a template, or by Dextra on a
// decision card) or of an existing one being edited. Every field is the owner's to change; the problems the team's rules find are listed as he types, and
// Create is there only when there are none. Create hands the finished draft to the host (the controller's CreateFromCard, CreateByOwner or UpdateAstur),
// which checks it again, because the team may have changed since: what it finds comes back as the same list.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

public enum ProfileSheetMode
{
    /// <summary>The owner adds an Astur himself: a sentence or a template starts the card.</summary>
    Add,
    /// <summary>Dextra drafted the card (create_assistant) and the owner reviews it: Create answers the decision card.</summary>
    Review,
    /// <summary>The owner edits an Astur that exists.</summary>
    Edit,
}

/// <summary>One of the six looks in the picker: whether another Astur has it, and whether it is this card's.</summary>
public sealed record ShapeChoice(AsturShape Shape, string Title, string? TakenBy, bool Selected)
{
    public bool Available => TakenBy is null;
}

public sealed class ProfileSheetModel
{
    private readonly Func<IReadOnlyList<AssistantProfile>> team;
    private readonly Func<AsturDraft, Task<IReadOnlyList<string>>> create;
    private readonly Action cancel;
    private readonly string? editing;
    private IReadOnlyList<string> returned = [];
    private string? inputProblem;

    /// <param name="team">The team as it is now (read at every check: it may change while the sheet is open).</param>
    /// <param name="create">What Create does with the finished draft; it returns what is still wrong with it, or empty when it was made or saved.</param>
    /// <param name="cancel">What Cancel and Escape do (the host closes the sheet and puts the focus back where it came from).</param>
    /// <param name="draft">The card to start from (Review and Edit); null for Add.</param>
    /// <param name="editing">The Astur being edited (Edit), so its own name and look do not clash with themselves.</param>
    /// <param name="reserveLine">The standing reserve rule in words, shown under the budget ("Keeps 30% on Claude 20x (your standing rule).").</param>
    public ProfileSheetModel(ProfileSheetMode mode, Func<IReadOnlyList<AssistantProfile>> team, Func<AsturDraft, Task<IReadOnlyList<string>>> create, Action cancel,
        AsturDraft? draft = null, string? editing = null, string reserveLine = "")
    {
        Mode = mode;
        this.team = team ?? throw new ArgumentNullException(nameof(team));
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        this.editing = editing;
        ReserveLine = reserveLine;
        Draft = draft;
        if (mode != ProfileSheetMode.Add && draft is null) throw new ArgumentException("A card to review or edit needs its draft.", nameof(draft));
    }

    public ProfileSheetMode Mode { get; }

    public string ReserveLine { get; }

    /// <summary>The card, once there is one. In Add it appears when a sentence or a template starts it.</summary>
    public AsturDraft? Draft { get; private set; }

    /// <summary>The sentence in the box ("Make an assistant for my third channel...").</summary>
    public string Sentence { get; set; } = "";

    public bool Busy { get; private set; }

    /// <summary>The sheet did its work (Create or Save went through): the host closes it.</summary>
    public bool Done { get; private set; }

    /// <summary>A line the screen reader hears: what the last tap did.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Fires when the shape of the sheet changed (a card appeared, a template was used, a look or a duty was picked): the view is built again.</summary>
    public event Action? Changed;

    /// <summary>Fires when only the words under the fields changed (the problems, the status, whether Create is there): the fields are left alone, so the caret stays.</summary>
    public event Action? Edited;

    // ---- the words ----

    public string Title => Mode switch
    {
        ProfileSheetMode.Add => "Add an " + AssistantNames.Astur,
        ProfileSheetMode.Review => "New " + AssistantNames.Astur,
        _ => "Edit " + (Draft?.Name is { Length: > 0 } name ? name : AssistantNames.Astur),
    };

    public string Subtitle => Mode switch
    {
        ProfileSheetMode.Add => "Say what it should look after, or start from a template. You see its card first, and nothing is made until you create it.",
        ProfileSheetMode.Review => AssistantNames.Assistant + " drafted this profile. Nothing is made until you create it; change any field first.",
        _ => "Changes apply from the next thing it does.",
    };

    public string CreateTitle => Mode == ProfileSheetMode.Edit ? "Save" : "Create";

    public const string CancelTitle = "Cancel";

    public const string DraftTitle = "Draft it";

    public const string Isolation = "Own browser profile and folder. Logins never shared.";

    public static IReadOnlyList<AsturTemplate> Templates => AsturTemplates.All;

    // ---- what is wrong, and whether Create is there ----

    /// <summary>What stops the card being made, in the order the fields show: the team's rules, then anything the host found, then a number that was not one.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            if (Draft is null) return [];
            var found = new List<string>();
            if (inputProblem is not null) found.Add(inputProblem);
            found.AddRange(AsturTeamRules.Problems(Draft, team(), editing));
            foreach (var line in returned) if (!found.Contains(line)) found.Add(line);
            return found;
        }
    }

    public bool CanCreate => Draft is not null && !Busy && !Done && Problems.Count == 0;

    // ---- the shape picker ----

    public IReadOnlyList<ShapeChoice> Shapes
    {
        get
        {
            var others = AsturTeamRules.ActiveAsturs(team()).Where(p => p.Id != editing).ToList();
            return [.. AsturShapes.All.Select(shape => new ShapeChoice(shape, AsturShapes.Title(shape), others.FirstOrDefault(p => p.Shape == shape)?.Name, Draft?.Shape == shape))];
        }
    }

    // ---- starting the card ----

    /// <summary>"Draft it": the card from the sentence (the template its words fit, else a blank card with the sentence as its area).</summary>
    public void Draw()
    {
        if (Mode != ProfileSheetMode.Add) return;
        Start(AsturDrafter.FromSentence(Sentence, null, team()), AsturTemplates.Detect(Sentence) is { } found ? $"Drafted from your words: the {found.Title} template." : "Drafted a blank card from your words.");
    }

    /// <summary>A template button: the card it fills. The sentence box takes the template's sentence, as the canvas does.</summary>
    public void UseTemplate(string id)
    {
        if (Mode != ProfileSheetMode.Add || AsturTemplates.Find(id) is not { } template) return;
        Sentence = template.Sentence;
        Start(AsturDrafter.FromTemplate(id, team()), $"The {template.Title} template.");
    }

    private void Start(AsturDraft draft, string status)
    {
        Draft = draft;
        Status = status;
        returned = [];
        inputProblem = null;
        Changed?.Invoke();
    }

    // ---- editing the card ----

    /// <summary>A field changed by typing: the words under the fields are refreshed and the fields stay as they are.</summary>
    public void Edit(Func<AsturDraft, AsturDraft> change)
    {
        if (Draft is null) return;
        Draft = change(Draft);
        returned = [];
        Edited?.Invoke();
    }

    public void SetName(string name) => Edit(d => d with { Name = name });

    public void SetArea(string area) => Edit(d => d with { Area = area });

    public void SetStyle(string style) => Edit(d => d with { Style = style });

    /// <summary>The look: only one no other Astur has.</summary>
    public void SetShape(AsturShape shape)
    {
        if (Draft is null || Shapes.FirstOrDefault(s => s.Shape == shape) is not { Available: true }) return;
        Draft = Draft with { Shape = shape };
        returned = [];
        Status = AsturShapes.Title(shape) + " picked.";
        Changed?.Invoke();
    }

    /// <summary>Ticks or unticks a standing duty (the ones kept become duties when the scheduler is here).</summary>
    public void ToggleDuty(int index)
    {
        if (Draft is null || index < 0 || index >= Draft.Duties.Count) return;
        var duty = Draft.Duties[index];
        Draft = Draft with { Duties = Draft.Duties.SetItem(index, duty with { Keep = !duty.Keep }) };
        Status = duty.Keep ? "Not kept: " + duty.Text : "Kept: " + duty.Text;
        returned = [];
        Changed?.Invoke();
    }

    /// <summary>The budget boxes: the percent of any account a day, and the dollars a day on an API key (empty: none). A box that is not a number keeps the old value and says so.</summary>
    public void SetBudget(string percentText, string dollarsText)
    {
        if (Draft is null) return;
        inputProblem = null;
        var budget = Draft.Budget;
        if (double.TryParse(percentText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) budget = budget with { DailyPercent = percent };
        else inputProblem = "Type the daily percent as a number, like 15.";
        var dollars = dollarsText.Trim();
        if (dollars.Length == 0) budget = budget with { DailyDollars = null };
        else if (decimal.TryParse(dollars, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) budget = budget with { DailyDollars = amount };
        else inputProblem ??= "Type the daily dollars as a number, like 5, or leave it empty.";
        Edit(d => d with { Budget = budget });
    }

    // ---- Create and Cancel ----

    /// <summary>The Create (or Save) button. False, and nothing handed over, while the card has problems; else what the host answers.</summary>
    public async Task<bool> Create()
    {
        if (!CanCreate) return false;
        Busy = true;
        Edited?.Invoke();
        IReadOnlyList<string> answer;
        try { answer = await create(Draft!).ConfigureAwait(true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            answer = [$"It could not be saved: {error.Message}"];
        }
        Busy = false;
        returned = answer;
        if (answer.Count == 0)
        {
            Done = true;
            Status = (Mode == ProfileSheetMode.Edit ? "Saved " : "Created ") + Draft!.Normalized().Name + ".";
        }
        Edited?.Invoke();
        return answer.Count == 0;
    }

    public void Cancel() => cancel();
}
