// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.2 "Creating one"; the approved canvas "Create an Astur"; unit AS-16; ED-NEW-dextra-as16-3, -4). No Swift twin.
// The profile card before it is an Astur: a suggested name, the area, proposed duties, the connections it needs (flagging which need a
// login), a budget, and what it does alone and what it asks. It comes from a sentence ("Make an assistant for my second channel that
// handles everything for it") or from a template; the owner edits any field and taps Create. Nothing on a draft acts: it is text until
// the owner creates it. Pure.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>An Astur's profile card, as it is being drafted and edited.</summary>
public sealed record AsturDraft
{
    public string Name { get; init; } = "";

    /// <summary>The node shape the owner picked; null: the first one no Astur has.</summary>
    public AsturShape? Shape { get; init; }

    /// <summary>What it owns, in one line.</summary>
    public string Area { get; init; } = "";

    /// <summary>The template it started from, or null.</summary>
    public string? Template { get; init; }

    public ValueList<DutyDraft> Duties { get; init; } = ValueList<DutyDraft>.Empty;

    public ValueList<AsturWant> Wants { get; init; } = ValueList<AsturWant>.Empty;

    public AsturBudget Budget { get; init; } = new(15);

    /// <summary>Classes the owner made stricter than the defaults for this Astur; never looser than Dextra's.</summary>
    public ValueMap<ActionClass, ClassDefault> ClassRules { get; init; } = ValueMap<ActionClass, ClassDefault>.Empty;

    public string OnItsOwn { get; init; } = "";

    public string AsksYou { get; init; } = "";

    /// <summary>How it writes, one line.</summary>
    public string Style { get; init; } = "";

    /// <summary>Other names it answers to, for "@" and voice.</summary>
    public ValueList<string> Aliases { get; init; } = ValueList<string>.Empty;

    public string? Voice { get; init; }

    public AsturModel? Model { get; init; }
    public ValueList<string> PillOrder { get; init; } = ValueList<string>.Empty;

    /// <summary>The draft with every text a clean one-line text and the lists trimmed of empty lines. It changes no meaning.</summary>
    public AsturDraft Normalized() => this with
    {
        Name = MemoryRules.Clean(Name),
        Area = MemoryRules.Clean(Area),
        OnItsOwn = MemoryRules.Clean(OnItsOwn),
        AsksYou = MemoryRules.Clean(AsksYou),
        Style = MemoryRules.Clean(Style),
        Voice = MemoryRules.Clean(Voice) is { Length: > 0 } voice ? voice : null,
        Duties = new ValueList<DutyDraft>(Duties.Select(d => d with { Text = MemoryRules.Clean(d.Text) }).Where(d => d.Text.Length > 0)),
        Wants = new ValueList<AsturWant>(Wants.Select(w => w with { Text = MemoryRules.Clean(w.Text) }).Where(w => w.Text.Length > 0)),
        Aliases = new ValueList<string>(Aliases.Select(MemoryRules.Clean).Where(a => a.Length > 0)),
        PillOrder = new ValueList<string>(PillOrder.Select(MemoryRules.Clean).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Take(32)),
    };

    /// <summary>The standing duties the owner kept.</summary>
    public IEnumerable<DutyDraft> KeptDuties => Duties.Where(d => d.Keep);
}

/// <summary>What a model said when it drafted an Astur (the create_assistant tool's arguments), all optional here.</summary>
public sealed record AsturHints(string? Name = null, string? Area = null, string? Purpose = null, AsturShape? Shape = null);

public static class AsturDrafter
{
    /// <summary>
    /// A card from a sentence and what a model said. The template that fits the words fills the duties, connections, budget and rules; with
    /// none, the card is a blank one with the sentence as its area. The name is the model's, else the template's, made unique among the team.
    /// </summary>
    public static AsturDraft FromSentence(string? sentence, AsturHints? hints, IReadOnlyList<AssistantProfile> team)
    {
        hints ??= new AsturHints();
        var words = string.Join(' ', new[] { sentence, hints.Area, hints.Purpose, hints.Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var template = AsturTemplates.Detect(words);
        var draft = template is null ? Blank(team) : FromTemplate(template, team);
        var name = MemoryRules.Clean(hints.Name);
        var area = MemoryRules.Clean(hints.Purpose);
        if (area.Length == 0 && template is null) area = MemoryRules.Clean(hints.Area);
        if (area.Length == 0 && template is null) area = MemoryRules.Clean(sentence);
        return draft with
        {
            Name = name.Length > 0 ? name : draft.Name,
            Area = area.Length > 0 ? Clip(area, AsturTeamRules.AreaMax) : draft.Area,
            Shape = hints.Shape ?? draft.Shape,
        };
    }

    /// <summary>A card from a template (9.2), named uniquely among the team. An unknown template id gives a blank card.</summary>
    public static AsturDraft FromTemplate(string? id, IReadOnlyList<AssistantProfile> team) =>
        AsturTemplates.Find(id) is { } template ? FromTemplate(template, team) : Blank(team);

    private static AsturDraft FromTemplate(AsturTemplate template, IReadOnlyList<AssistantProfile> team) => new()
    {
        Name = AsturTeamRules.UniqueName(template.Name, team),
        Shape = AsturShapes.FirstFree(AsturTeamRules.ActiveAsturs(team).Select(a => a.Shape).OfType<AsturShape>()),
        Area = template.Area,
        Template = template.Id,
        Duties = template.Duties,
        Wants = template.Wants,
        Budget = template.Budget,
        ClassRules = template.ClassRules,
        OnItsOwn = template.OnItsOwn,
        AsksYou = template.AsksYou,
    };

    /// <summary>A card with nothing on it but a free shape, a name that is free, and the safe default of asking for sign-ins and anything sent.</summary>
    public static AsturDraft Blank(IReadOnlyList<AssistantProfile> team) => new()
    {
        Name = AsturTeamRules.UniqueName("New Astur", team),
        Shape = AsturShapes.FirstFree(AsturTeamRules.ActiveAsturs(team).Select(a => a.Shape).OfType<AsturShape>()),
        Wants = new ValueList<AsturWant>([new AsturWant(WantKind.Folder, "Its own folder · read/write")]),
        Budget = new(10),
        OnItsOwn = AsturTemplates.DefaultOnItsOwn,
        AsksYou = AsturTemplates.DefaultAsksYou,
    };

    /// <summary>The text cut at a word end to at most <paramref name="max"/> characters, with an ellipsis when cut.</summary>
    public static string Clip(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max - 1);
        return text[..(cut > max / 2 ? cut : max - 1)].TrimEnd(' ', ',', ';', ':', '.') + "…";
    }
}
