// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1, 9.2, 12.1; unit AS-16; ED-NEW-dextra-as16-4). No Swift twin.
// What a profile card must be before it can become an Astur: a name that is free and cannot be mistaken for a spoken command, a shape no
// other Astur has, at most six Asturs, a budget that means something, bounded lines, and rules that are never looser than Dextra's
// (an Astur can never hold a grant or a freedom its parent lacks). Each problem is a plain sentence for the card. Pure.
using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static class AsturTeamRules
{
    public const int NameMax = 40;
    public const int AreaMax = 600;
    public const int StyleMax = 300;
    public const int LineMax = 200;
    public const int DutiesMax = 8;
    public const int WantsMax = 8;
    public const int AliasesMax = 6;
    public const double DollarsMax = 100_000;

    /// <summary>Words a name may not be: they open or end a spoken request, or name something else (routing would take the name for the word).</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        "dextra", "astur", "asturs", "all", "everyone", "everything", "nobody", "stop", "ask", "tell", "hey", "hi", "hello", "ok", "okay", "yo",
    };

    private static readonly char[] Forbidden = ['@', ',', ':', ';', '"', '<', '>', '\\', '/', '|', '`'];

    /// <summary>The Asturs that count: every profile with a parent that the owner has not retired.</summary>
    public static IEnumerable<AssistantProfile> ActiveAsturs(IEnumerable<AssistantProfile> team) => team.Where(p => !p.IsRoot && !p.Retired);

    /// <summary>The name made free: the same name when nobody has it, else "Name 2", "Name 3"... (the canvas's "Store 2").</summary>
    public static string UniqueName(string name, IEnumerable<AssistantProfile> team)
    {
        var taken = team.Where(p => !p.Retired).SelectMany(p => new[] { p.Name }.Concat(p.Aliases)).Select(AsturRouting.Key).ToHashSet(StringComparer.Ordinal);
        var key = AsturRouting.Key(name);
        if (key.Length > 0 && !taken.Contains(key) && !Kept.Contains(key)) return name;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = name + " " + n.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(AsturRouting.Key(candidate))) return candidate;
        }
        return name;
    }

    /// <summary>
    /// Why the draft cannot be created yet, one sentence each, in the order the card shows its fields; empty when it can. <paramref name="editing"/>
    /// is the Astur being edited, which does not collide with itself and does not count against the six.
    /// </summary>
    public static IReadOnlyList<string> Problems(AsturDraft draft, IReadOnlyList<AssistantProfile> team, string? editing = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(team);
        var d = draft.Normalized();
        var problems = new List<string>();
        var others = team.Where(p => !p.Retired && p.Id != editing).ToList();
        var root = team.FirstOrDefault(p => p.IsRoot);
        if (root is null) problems.Add(AssistantNames.Assistant + "'s own profile is missing, so there is nothing for an " + AssistantNames.Astur + " to belong to.");

        if (d.Name.Length == 0) problems.Add("Give it a name.");
        else
        {
            var key = AsturRouting.Key(d.Name);
            if (d.Name.Length > NameMax) problems.Add($"A name holds at most {NameMax} characters.");
            else if (key.Length == 0 || d.Name.IndexOfAny(Forbidden) >= 0) problems.Add("A name is letters and numbers, with spaces; it has no @ , : ; or similar marks.");
            else if (Kept.Contains(key)) problems.Add($"\"{d.Name}\" is a word nth listens for; pick another name.");
            else if (others.FirstOrDefault(p => Names(p).Contains(key)) is { } clash) problems.Add($"{clash.Name} already answers to \"{d.Name}\".");
        }
        if (d.Aliases.Count > AliasesMax) problems.Add($"At most {AliasesMax} other names.");
        var aliasKeys = new HashSet<string>(StringComparer.Ordinal);
        var ownKey = AsturRouting.Key(d.Name);
        foreach (var alias in d.Aliases.Take(AliasesMax))
        {
            var key = AsturRouting.Key(alias);
            if (key.Length == 0 || alias.Length > NameMax || alias.IndexOfAny(Forbidden) >= 0 || Kept.Contains(key)) problems.Add($"\"{alias}\" cannot be another name.");
            else if (key == ownKey || !aliasKeys.Add(key)) problems.Add($"\"{alias}\" is listed twice.");
            else if (others.FirstOrDefault(p => Names(p).Contains(key)) is { } clash) problems.Add($"{clash.Name} already answers to \"{alias}\".");
        }

        var asturs = others.Where(p => !p.IsRoot).ToList();
        if (asturs.Count >= AsturShapes.MostAsturs) problems.Add($"There can be at most {AsturShapes.MostAsturs} {AssistantNames.Asturs}. Retire one first.");
        if (d.Shape is { } shape)
        {
            if (asturs.FirstOrDefault(p => p.Shape == shape) is { } holder) problems.Add($"{AsturShapes.Title(shape)} belongs to {holder.Name}; pick another look.");
        }
        else if (asturs.Count < AsturShapes.MostAsturs) problems.Add("Pick a look.");

        if (d.Area.Length == 0) problems.Add("Say what it looks after.");
        else if (d.Area.Length > AreaMax) problems.Add($"What it looks after holds at most {AreaMax} characters.");
        if (d.Style.Length > StyleMax) problems.Add($"How it writes holds at most {StyleMax} characters.");
        if (d.Voice is { Length: > NameMax }) problems.Add("A voice name is too long.");
        if (d.Duties.Count > DutiesMax) problems.Add($"At most {DutiesMax} standing duties.");
        if (d.Duties.Any(x => x.Text.Length > LineMax)) problems.Add($"A duty holds at most {LineMax} characters.");
        if (d.Wants.Count > WantsMax) problems.Add($"At most {WantsMax} connections.");
        if (d.Wants.Any(x => x.Text.Length > LineMax)) problems.Add($"A connection holds at most {LineMax} characters.");
        if (d.OnItsOwn.Length > LineMax * 2 || d.AsksYou.Length > LineMax * 2) problems.Add($"The lines about what it does alone and what it asks hold at most {LineMax * 2} characters.");

        if (!double.IsFinite(d.Budget.DailyPercent) || d.Budget.DailyPercent < 1 || d.Budget.DailyPercent > 100)
            problems.Add("Its daily budget is between 1% and 100% of any account.");
        if (d.Budget.DailyDollars is { } dollars && (dollars <= 0 || dollars > (decimal)DollarsMax)) problems.Add($"Its daily dollar limit is more than $0 and at most ${DollarsMax.ToString("N0", CultureInfo.InvariantCulture)}.");

        if (root is not null)
        {
            var candidate = ToProfile(d, "candidate", root.Id, DateTimeOffset.UnixEpoch);
            if (AssistantChain.LooserThanParent(candidate, AssistantChain.Of(root.Id, team)) is { } looser) problems.Add(looser);
        }
        return problems;
    }

    /// <summary>
    /// The profile an accepted draft becomes. The caller has no problems to report. Its parent is Dextra; its id, home thread and the
    /// time are the caller's. Text is the clean text, shapes and rules as the owner left them.
    /// </summary>
    public static AssistantProfile ToProfile(AsturDraft draft, string id, string parentId, DateTimeOffset created, Guid? homeThread = null)
    {
        var d = draft.Normalized();
        return new AssistantProfile(id, d.Name)
        {
            ParentId = parentId,
            Shape = d.Shape ?? AsturShape.Squircle,
            Area = d.Area,
            Aliases = new ValueList<string>(d.Aliases),
            ClassRules = d.ClassRules,
            Style = d.Style,
            Voice = d.Voice,
            Model = d.Model,
            Duties = d.Duties,
            Wants = d.Wants,
            Budget = d.Budget,
            Template = d.Template,
            Created = created,
            HomeThread = homeThread,
        };
    }

    /// <summary>The draft an existing Astur's card shows when the owner edits it.</summary>
    public static AsturDraft ToDraft(AssistantProfile profile) => new()
    {
        Name = profile.Name,
        Shape = profile.Shape,
        Area = profile.Area,
        Template = profile.Template,
        Duties = profile.Duties,
        Wants = profile.Wants,
        Budget = profile.Budget ?? new AsturBudget(15),
        ClassRules = profile.ClassRules,
        OnItsOwn = AsturTemplates.Find(profile.Template)?.OnItsOwn ?? AsturTemplates.DefaultOnItsOwn,
        AsksYou = AsturTemplates.Find(profile.Template)?.AsksYou ?? AsturTemplates.DefaultAsksYou,
        Style = profile.Style,
        Aliases = profile.Aliases,
        Voice = profile.Voice,
        Model = profile.Model,
    };

    /// <summary>
    /// The assistant a reference names, among those not retired: its id, or its name or another name it answers to (compared by letters,
    /// without case or accents). Null when it names none, or more than one: a name is never guessed.
    /// </summary>
    public static AssistantProfile? Find(IEnumerable<AssistantProfile> team, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var active = team.Where(p => !p.Retired).ToList();
        if (active.FirstOrDefault(p => string.Equals(p.Id, reference.Trim(), StringComparison.Ordinal)) is { } byId) return byId;
        var key = AsturRouting.Key(reference);
        if (key.Length == 0) return null;
        var named = active.Where(p => Names(p).Contains(key)).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>Every name an assistant answers to, as keys.</summary>
    public static HashSet<string> Names(AssistantProfile profile) =>
        new[] { profile.Name }.Concat(profile.Aliases).Select(AsturRouting.Key).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
}
