// PORT-ONLY: first-release feature unit SK-10 (DL-10 of porting/first-release/skills-ONDEMAND-PLAN.md section 7). The missing-tool detector of section 5.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Port.Skills.Detect;

/// <summary>
/// What the detector decides from. Everything it knows about the machine is here: the catalogue, the state (installed, found on this PC, downloading, declined per chat),
/// the chat, and the items already offered in this chat (a runtime fact the caller keeps). Nothing is probed.
/// </summary>
public sealed record DetectionContext(SkillsCatalogue Catalogue, AddonsState State, string ChatId, IReadOnlySet<string> OfferedInChat);

/// <summary>S1 to S4 of ONDEMAND-PLAN.md 5.1.</summary>
public enum DetectedSignal
{
    SkillNeed,
    HeldCommand,
    FailedCommand,
    ModelRequest,
}

public enum FindingVerdict
{
    /// <summary>Missing and nothing in the way: offer it.</summary>
    Offer,

    /// <summary>Its download is running: the offer attaches to that download's progress.</summary>
    AttachToDownload,

    /// <summary>A guard says no offer.</summary>
    Suppressed,
}

public enum SuppressReason
{
    InstalledAndHealthy,
    FoundOnPc,
    Removing,
    Declined,
    AlreadyOffered,
}

/// <summary>One missing catalogue item: which signal found it, the token it was found by (a program or module name, the message, a need id, or the model's own word), and the verdict.</summary>
public sealed record Finding(string ItemId, DetectedSignal Signal, string Token, FindingVerdict Verdict, SuppressReason? Reason);

/// <summary>
/// The one card for everything found missing in a batch (ONDEMAND-PLAN.md 5.3 "a second missing item joins the same card"; 5.4 "one download with its combined size"):
/// <see cref="Requested"/> are the items found missing, in the order found; <see cref="Items"/> everything to download for them, the needs they lack first and each
/// item once; <see cref="ApproxSizeMb"/> the plan's sizes added up, null when any is unknown.
/// </summary>
public sealed record OfferCard(IReadOnlyList<string> Requested, IReadOnlyList<string> Items, double? ApproxSizeMb);

public sealed record Detection(IReadOnlyList<Finding> Findings, OfferCard? Card, IReadOnlyList<string> AttachTo);

public static class MissingToolDetector
{
    public static Detection Detect(DetectionContext context, ToolEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);
        return Detect(context, [toolEvent]);
    }

    public static Detection Detect(DetectionContext context, IEnumerable<ToolEvent> events)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Catalogue);
        ArgumentNullException.ThrowIfNull(context.State);
        ArgumentNullException.ThrowIfNull(context.OfferedInChat);
        if (string.IsNullOrWhiteSpace(context.ChatId)) throw new ArgumentException("A chat id is required.", nameof(context));
        ArgumentNullException.ThrowIfNull(events);

        var index = ProvidesIndex.Build(context.Catalogue);
        var found = new List<(string Item, DetectedSignal Signal, string Token)>();
        void Add(CatalogueItem? item, DetectedSignal signal, string token)
        {
            if (item is not null && !found.Any(f => f.Item == item.Id)) found.Add((item.Id, signal, token));
        }
        CatalogueItem? Program(string name) => index.Resolve(ProvideKind.Exe, name);
        CatalogueItem? PythonModuleOrProgram(string name) => index.Resolve(ProvideKind.PyModule, name) ?? index.Resolve(ProvideKind.Exe, name);

        foreach (var toolEvent in events)
        {
            switch (toolEvent)
            {
                case null:
                    throw new ArgumentException("An event is null.", nameof(events));
                case CommandHeld held:
                    foreach (var target in CommandTokenizer.Targets(held.CommandLine))
                    {
                        Add(Program(target.Program), DetectedSignal.HeldCommand, target.Program);
                        if (target.Module is not null) Add(PythonModuleOrProgram(target.Module), DetectedSignal.HeldCommand, target.Module);
                    }
                    break;
                case CommandFinished finished:
                    if (!FailurePatterns.Failed(finished)) break;
                    var named = FailurePatterns.NamedMissingPrograms(finished);
                    foreach (var token in CommandTokenizer.OrderedTokens(finished.CommandLine))
                        if (named.Contains(token)) Add(Program(token), DetectedSignal.FailedCommand, token);
                    foreach (var module in FailurePatterns.PythonModules(finished.Output)) Add(PythonModuleOrProgram(module), DetectedSignal.FailedCommand, module);
                    foreach (var module in FailurePatterns.NodeModules(finished.Output)) Add(index.Resolve(ProvideKind.NodeModule, module), DetectedSignal.FailedCommand, module);
                    if (FailurePatterns.PlaywrightMissingBrowser(finished.Output))
                        Add(index.Resolve(ProvideKind.Message, FailurePatterns.PlaywrightMessage), DetectedSignal.FailedCommand, FailurePatterns.PlaywrightMessage);
                    break;
                case SkillUsed used:
                    foreach (var need in SkillNeeds(context, used)) Add(context.Catalogue.Items.FirstOrDefault(i => i.Id == need), DetectedSignal.SkillNeed, need);
                    break;
                case ToolRequested requested:
                    if (string.IsNullOrWhiteSpace(requested.Name)) break;
                    Add(context.Catalogue.Items.FirstOrDefault(i => i.Id == requested.Name) ?? Program(requested.Name), DetectedSignal.ModelRequest, requested.Name);
                    break;
            }
        }

        var findings = found.Select(f =>
        {
            var (verdict, reason) = Guard(context, f.Item);
            return new Finding(f.Item, f.Signal, f.Token, verdict, reason);
        }).ToList();
        var requestedItems = findings.Where(f => f.Verdict == FindingVerdict.Offer).Select(f => f.ItemId).ToList();
        return new Detection(findings, requestedItems.Count == 0 ? null : BuildCard(context, requestedItems), findings.Where(f => f.Verdict == FindingVerdict.AttachToDownload).Select(f => f.ItemId).ToList());
    }

    // The per-skill needs the catalogue lists for the skill used. An unnamed set is the one installed set that has the skill; none or several gives nothing (no guess).
    private static IReadOnlyList<string> SkillNeeds(DetectionContext context, SkillUsed used)
    {
        if (string.IsNullOrWhiteSpace(used.Skill)) return [];
        IEnumerable<CatalogueItem> sets = used.ItemId is not null
            ? context.Catalogue.Items.Where(i => i.Id == used.ItemId)
            : context.Catalogue.Items.Where(i => context.State.Get(i.Id).InstalledPin is not null);
        var owners = sets.Where(i => i.Skills.Any(s => s.Folder == used.Skill)).ToList();
        return owners.Count == 1 ? owners[0].Skills.First(s => s.Folder == used.Skill).Needs : [];
    }

    private static bool Healthy(ItemRecord record) =>
        record.State is ItemState.Installed or ItemState.UpdateAvailable
        || (record.State is ItemState.Queued or ItemState.Downloading or ItemState.Checking or ItemState.Failed && record.InstalledPin is not null && record.TargetPin != record.InstalledPin);

    private static bool InFlight(ItemRecord record) => record.State is ItemState.Queued or ItemState.Downloading or ItemState.Checking;

    // ONDEMAND-PLAN.md 5.3 Guards. The order when several apply is an ENGINEERING DECISION (ED-296).
    private static (FindingVerdict, SuppressReason?) Guard(DetectionContext context, string id)
    {
        var record = context.State.Get(id);
        if (Healthy(record)) return (FindingVerdict.Suppressed, SuppressReason.InstalledAndHealthy);
        if (context.State.IsFoundOnPc(id)) return (FindingVerdict.Suppressed, SuppressReason.FoundOnPc);
        if (record.State == ItemState.Removing) return (FindingVerdict.Suppressed, SuppressReason.Removing);
        if (InFlight(record)) return (FindingVerdict.AttachToDownload, null);
        if (context.State.IsDeclined(context.ChatId, id)) return (FindingVerdict.Suppressed, SuppressReason.Declined);
        if (context.OfferedInChat.Contains(id)) return (FindingVerdict.Suppressed, SuppressReason.AlreadyOffered);
        return (FindingVerdict.Offer, null);
    }

    // SK-12 also uses this dependency order when attaching to an already confirmed download.
    public static OfferCard BuildCard(DetectionContext context, IReadOnlyList<string> requested)
    {
        var byId = context.Catalogue.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var items = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Available(string id)
        {
            var record = context.State.Get(id);
            return Healthy(record) || InFlight(record) || context.State.IsFoundOnPc(id);
        }
        void Visit(string id)
        {
            if (!visited.Add(id)) return;
            foreach (var need in byId[id].Needs)
                if (byId.ContainsKey(need) && !Available(need)) Visit(need);
            items.Add(id);
        }
        foreach (var id in requested) Visit(id);
        var sizes = items.Select(i => byId[i].ApproxSizeMb).ToList();
        return new OfferCard(requested, items, sizes.Any(s => s is null) ? null : sizes.Sum(s => s!.Value));
    }
}
