// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.2 "The new assistant introduces itself", the approved canvas "Create an Astur"; unit AS-16; ED-NEW-dextra-as16-3, -4). No Swift twin.
// The words of the profile card and of a new Astur's first message: the lines the decision card lists for a draft (name, look, area, duties,
// connections, budget, what it does alone and what it asks), the plain-words budget, and the introduction prompt its home thread starts with. The
// introduction asks for nothing outward: until the owner answers it proposes, and does not act. Pure.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class AsturWords
{
    /// <summary>"Up to 15% of any account a day" (and "and up to $5 a day on an API key").</summary>
    public static string Budget(AsturBudget budget)
    {
        var percent = budget.DailyPercent.ToString("0.##", CultureInfo.InvariantCulture);
        var text = $"Up to {percent}% of any account a day";
        if (budget.DailyDollars is { } dollars) text += $", and up to ${dollars.ToString("0.##", CultureInfo.InvariantCulture)} a day on an API key";
        return text + ".";
    }

    /// <summary>The words for what a connection is, so its kind is never shown by an icon or a colour alone.</summary>
    public static string Kind(WantKind kind) => kind switch
    {
        WantKind.Site => "Site",
        WantKind.Folder => "Folder",
        WantKind.Login => "Sign-in",
        WantKind.Connector => "Connector",
        _ => "Limit",
    };

    /// <summary>One connection as the card lists it: its kind, the text, and "you sign in" when it needs one.</summary>
    public static string Want(AsturWant want) => $"{Kind(want.Kind)}: {want.Text}" + (want.NeedsLogin && !want.Text.Contains("sign", StringComparison.OrdinalIgnoreCase) ? " · you sign in" : "");

    /// <summary>The "Never" line: what the owner made stricter than asking, in words ("sending", "paying"). Empty when nothing is.</summary>
    public static string Never(AsturDraft draft)
    {
        var never = draft.ClassRules.Where(r => r.Value == ClassDefault.Never).Select(r => r.Key).OrderBy(c => (int)c).Select(ClassWord).ToList();
        return never.Count == 0 ? "" : string.Join(", ", never);
    }

    private static string ClassWord(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Outward => "sending",
        ActionClass.Money => "paying",
        ActionClass.Login => "signing in",
        ActionClass.Commitment => "booking or agreeing",
        ActionClass.ConnectorWrite => "writing through connectors",
        ActionClass.WriteElsewhere => "writing outside its folder",
        ActionClass.Destructive => "deleting",
        ActionClass.Install => "installing",
        ActionClass.PersonalDataOut => "sending personal data",
        ActionClass.Run => "running commands",
        ActionClass.ReadWeb => "reading the web",
        ActionClass.ReadLocal => "reading files",
        ActionClass.Work => "changing files",
        ActionClass.Agents => "starting agents",
        _ => actionClass.ToString(),
    };

    /// <summary>
    /// The inputs the owner's decision card lists for a draft (4.2: the exact inputs, so what he approves is what is made). The same words
    /// are on the profile sheet; the card is bound to them.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Inputs(AsturDraft draft)
    {
        var d = draft.Normalized();
        var lines = new List<(string, string)>
        {
            ("Name", d.Name),
            ("Look", d.Shape is { } shape ? AsturShapes.Title(shape) : "None yet"),
            ("Area", d.Area),
        };
        if (d.Aliases.Count > 0) lines.Add(("Also answers to", string.Join(", ", d.Aliases)));
        if (d.Style.Length > 0) lines.Add(("How it writes", d.Style));
        var duties = d.KeptDuties.Select(x => x.Text).ToList();
        lines.Add(("Standing duties", duties.Count == 0 ? "None" : string.Join("; ", duties)));
        lines.Add(("Connections", d.Wants.Count == 0 ? "None" : string.Join("; ", d.Wants.Select(Want))));
        lines.Add(("Budget", Budget(d.Budget)));
        lines.Add(("On its own", d.OnItsOwn));
        lines.Add(("Asks you", d.AsksYou));
        if (Never(d) is { Length: > 0 } never) lines.Add(("Never", never));
        return lines;
    }

    /// <summary>
    /// What a new Astur's home thread is told first (9.2): who it is, what it looks after, to introduce itself and propose its first tasks, and to do
    /// nothing outward until the owner answers. Its standing duties and the connections it wants are proposals, not running things.
    /// </summary>
    public static string Introduction(AssistantProfile astur)
    {
        ArgumentNullException.ThrowIfNull(astur);
        var lines = new List<string>
        {
            $"You are {astur.Name}, a new {AssistantNames.Astur} of {AssistantNames.Assistant}. The owner has just created you.",
            $"Your area: {astur.Area}",
        };
        if (astur.Style.Length > 0) lines.Add($"How you write: {astur.Style}");
        var duties = astur.Duties.Where(d => d.Keep).Select(d => d.Text).ToList();
        if (duties.Count > 0) lines.Add("You were set up with these standing duties. They are proposals only: nothing runs on a schedule until the owner turns it on: " + string.Join("; ", duties) + ".");
        if (astur.Wants.Count > 0) lines.Add("You will need these connections. The owner connects them and signs in himself: " + string.Join("; ", astur.Wants.Select(Want)) + ".");
        lines.Add("Introduce yourself to the owner in two or three sentences, then propose your first three tasks as a short list.");
        lines.Add("Until the owner answers, do nothing outward: do not send, post, buy, book, sign in, install or delete anything, and start no mission. Ask first.");
        return string.Join('\n', lines);
    }
}
