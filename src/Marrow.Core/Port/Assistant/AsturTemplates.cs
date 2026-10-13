// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.2 "From a template"; the approved canvas "Asturs and the org chart"; unit AS-16; ED-NEW-dextra-as16-3). No Swift twin.
// The six templates of 9.2 (Channel, Store, Project maintainer, Research desk, Inbox with drafts only, Finance watcher read-only),
// each with the sentence, area, duties, connections, budget and rules the canvas shows for it, and the plain-words test that picks
// one from a sentence ("Make an assistant that runs my online store"). A template only fills a card; the owner edits any field and
// taps Create, and nothing it names runs until then. Pure.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public sealed record AsturTemplate(string Id, string Title, string Sentence, string Name, string Area)
{
    public ValueList<DutyDraft> Duties { get; init; } = ValueList<DutyDraft>.Empty;

    public ValueList<AsturWant> Wants { get; init; } = ValueList<AsturWant>.Empty;

    public AsturBudget Budget { get; init; } = new(15);

    /// <summary>What the owner made stricter than 12.1's default for this kind of Astur ("send never", "no logins").</summary>
    public ValueMap<ActionClass, ClassDefault> ClassRules { get; init; } = ValueMap<ActionClass, ClassDefault>.Empty;

    /// <summary>The card's "On its own" line.</summary>
    public string OnItsOwn { get; init; } = AsturTemplates.DefaultOnItsOwn;

    /// <summary>The card's "Asks you" line.</summary>
    public string AsksYou { get; init; } = AsturTemplates.DefaultAsksYou;
}

public static partial class AsturTemplates
{
    public const string DefaultOnItsOwn = "Its own folder, research (read-only), drafts, its missions within budget.";

    public const string DefaultAsksYou = "sign-ins, uploads, posting, anything sent.";

    public static IReadOnlyList<AsturTemplate> All { get; } =
    [
        new("channel", "Channel", "Make an assistant for my third channel that handles everything for it", "Channel",
            "A channel: scripts, voiceovers, edits, uploads and its numbers.")
        {
            Duties = Duties("Watch the numbers, weekdays 09:00", "Keep a backlog of ideas from comments", "Prepare the next script every Monday"),
            Wants = WantList(
                new(WantKind.Site, "YouTube Studio · read · you sign in once", NeedsLogin: true),
                new(WantKind.Folder, "Its own folder · read/write"),
                new(WantKind.Login, "Voice site · sign-in asks you", NeedsLogin: true)
            ),
        },
        new("store", "Store", "Make an assistant that runs my online store", "Store",
            "Online store: listings, prices, stock, invoices and reviews.")
        {
            Duties = Duties("Price watch on 5 competitors, every 6 h", "File invoices from the inbox folder", "Weekly numbers on Friday"),
            Wants = WantList(
                new(WantKind.Site, "Store dashboard · read · you sign in once", NeedsLogin: true),
                new(WantKind.Folder, "Its own folder · read/write"),
                new(WantKind.Limit, "Payments · never (purchases are off)")
            ),
            ClassRules = Rules((ActionClass.Money, ClassDefault.Never)),
        },
        new("project", "Project maintainer", "Make an assistant that maintains the shop app", "Maintainer",
            "A project's repository: dependencies, tests, small fixes, releases.")
        {
            Duties = Duties("Dependency audit, weekly", "Run the tests on every push", "Triage new issues daily"),
            Wants = WantList(
                new(WantKind.Folder, "The project folder you pick · access folder (git)"),
                new(WantKind.Connector, "GitHub · read issues; PRs ask you")
            ),
            OnItsOwn = "Its own folder, the project's own build and tests, small fixes in its branch.",
            AsksYou = "sign-ins, pushes, releases, anything sent.",
        },
        new("research", "Research desk", "Make a research desk that answers deep questions with sources", "Desk",
            "Research with sources and a fact-check pass.")
        {
            Duties = Duties("Weekly brief on your topics", "Watch 4 news sources for your keywords"),
            Wants = WantList(new(WantKind.Site, "Web · read-only"), new(WantKind.Folder, "Its own folder for reports")),
            Budget = new(10),
            ClassRules = Rules((ActionClass.Login, ClassDefault.Never), (ActionClass.ConnectorWrite, ClassDefault.Never)),
            AsksYou = "anything sent or posted. It signs in nowhere.",
        },
        new("inbox", "Inbox (drafts only)", "Make an inbox assistant that drafts replies in my voice", "Inbox",
            "Email and messages: triage and drafts in your voice. Never sends.")
        {
            Duties = Duties("Morning triage at 07:30", "Flag replies older than 2 days"),
            Wants = WantList(
                new(WantKind.Connector, "Gmail · read and draft · send never", NeedsLogin: true),
                new(WantKind.Connector, "Calendar · read", NeedsLogin: true)
            ),
            Budget = new(5),
            ClassRules = Rules((ActionClass.Outward, ClassDefault.Never)),
            OnItsOwn = "Reading mail, sorting it, and drafts in your voice. It never sends.",
            AsksYou = "sign-ins, and anything that would be sent: it does not send.",
        },
        new("finance", "Finance watcher (read-only)", "Make a finance watcher that only reads", "Ledger",
            "Budget sheet and bank exports: read-only checks and monthly summary.")
        {
            Duties = Duties("Monthly summary on the 1st", "Alert on unusual spend"),
            Wants = WantList(new(WantKind.Folder, "A folder you pick · read-only"), new(WantKind.Limit, "No sends, no payments, no logins")),
            Budget = new(5),
            ClassRules = Rules(
                (ActionClass.Outward, ClassDefault.Never), (ActionClass.Commitment, ClassDefault.Never), (ActionClass.Money, ClassDefault.Never),
                (ActionClass.Login, ClassDefault.Never), (ActionClass.ConnectorWrite, ClassDefault.Never), (ActionClass.WriteElsewhere, ClassDefault.Never)),
            OnItsOwn = "Reading the folder you pick and writing its summaries in its own folder.",
            AsksYou = "anything outside that folder. It sends, pays and signs in nowhere.",
        },
    ];

    public static AsturTemplate? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The template that fits a sentence or an area, by plain words (the canvas's own test, with whole words): finance, inbox and research
    /// first, then a project's code, then a store, then a channel. Null when nothing fits: the card is then a blank one.
    /// </summary>
    public static AsturTemplate? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var id = Finance().IsMatch(text) ? "finance"
            : Inbox().IsMatch(text) ? "inbox"
            : Research().IsMatch(text) ? "research"
            : Project().IsMatch(text) ? "project"
            : Store().IsMatch(text) ? "store"
            : Channel().IsMatch(text) ? "channel"
            : null;
        return Find(id);
    }

    private static ValueList<DutyDraft> Duties(params string[] lines) => new(lines.Select(line => new DutyDraft(line)));

    private static ValueList<AsturWant> WantList(params AsturWant[] wants) => new(wants);

    private static ValueMap<ActionClass, ClassDefault> Rules(params (ActionClass Class, ClassDefault Rule)[] rules) =>
        new(rules.Select(r => new KeyValuePair<ActionClass, ClassDefault>(r.Class, r.Rule)));

    [GeneratedRegex(@"\b(financ\w*|budget|bank|ledger|accounting|bookkeep\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Finance();

    [GeneratedRegex(@"\b(inbox|e-?mails?|mail|messages|replies)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Inbox();

    [GeneratedRegex(@"\b(research\w*|sources|fact-?check\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Research();

    [GeneratedRegex(@"\b(app|apps|repo|repos|repository|code|codebase|maintain\w*|dependenc\w+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Project();

    [GeneratedRegex(@"\b(store|stores|shop|shops|shopify|etsy|listings|inventory)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Store();

    [GeneratedRegex(@"\b(channel|channels|youtube|videos?|podcasts?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Channel();
}
