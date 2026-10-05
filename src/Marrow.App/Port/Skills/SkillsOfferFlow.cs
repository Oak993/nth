// PORT-ONLY: SK-12 / DL-13, ONDEMAND 5.3-5.5; OD-0099, OD-0103. ED-NEW-codex-sk-12 records internal choices.
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Port.Skills.Providers;
namespace Marrow.App.Port.Skills;

public sealed class SkillsOfferFlow : MainActorModel, IToolRequestOffer
{
    private readonly ThreadRuntime runtime;
    private readonly SkillsCatalogue catalogue;
    private readonly Func<AddonsState> read;
    private readonly Action<AddonsState> write;
    private readonly ISkillsOfferDownload download;
    private readonly HashSet<string> offered = new(StringComparer.Ordinal);
    private readonly HashSet<string> modelRequested = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ApprovalRequest Request, long Epoch, string Item)> held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TaskCompletionSource<ToolRequestOutcome>>> waiters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> installed = new(StringComparer.Ordinal);
    private readonly HashSet<string> needsContinuation = new(StringComparer.Ordinal);
    private CancellationTokenSource? operation;
    private long revision;
    private OfferStage stage = OfferStage.Asked;
    private OfferCard? card;
    private OfferProgress? progress;
    private string? failure;
    private string Chat => runtime.ThreadID.ToString();

    public SkillsOfferFlow(ThreadRuntime runtime, SkillsCatalogue catalogue, Func<AddonsState> read, Action<AddonsState> write, ISkillsOfferDownload download)
    {
        this.runtime = runtime; this.catalogue = catalogue; this.read = read; this.write = write; this.download = download;
        runtime.AttachSkillsOffers(this);
    }
    public OfferStage Stage { get => Read(stage); private set => Set(ref stage, value); }
    public OfferCard? Card { get => Read(card); private set => Set(ref card, value); }
    public OfferProgress? Progress { get => Read(progress); private set => Set(ref progress, value); }
    public string? Failure { get => Read(failure); private set => Set(ref failure, value); }
    public bool Available => read().Items.Any(i => i.InstalledPin is not null && i.State is ItemState.Installed or ItemState.UpdateAvailable);
    private CatalogueItem? Item(string name) => catalogue.Items.FirstOrDefault(i => i.Id == name) ?? ProvidesIndex.Build(catalogue).Resolve(ProvideKind.Exe, name);
    public bool InCatalogue(string name) => Item(name) is not null;

    // Both provider orders occur: an approval can precede the normalized held event, or follow it.
    internal void BindApproval(ApprovalRequest request)
    {
        if (request.RequestKind != ApprovalRequest.Kind.Command) return;
        Observe(new CommandHeld(request.Title), request);
        if (!held.ContainsKey(request.Id) && request.Detail is { } detail && detail != request.Title)
            Observe(new CommandHeld(detail), request);
    }

    public void Observe(ToolEvent value, ApprovalRequest? held = null)
    {
        var context = new DetectionContext(catalogue, read(), Chat, offered);
        var detection = MissingToolDetector.Detect(context, value);
        var incoming = detection.Card ?? (detection.AttachTo.Count == 0 ? null : MissingToolDetector.BuildCard(context, detection.AttachTo));
        var fresh = false;
        if (Card is not null && Stage is OfferStage.Asked or OfferStage.Downloading or OfferStage.Checking or OfferStage.Failed)
        {
            if (incoming is { } additional)
            {
                var requested = Card.Requested.Concat(additional.Requested).Distinct(StringComparer.Ordinal).ToArray();
                Card = MissingToolDetector.BuildCard(context, requested);
            }
        }
        else if (incoming is { } next)
        {
            fresh = true;
            Card = next;
            Stage = OfferStage.Asked;
            Failure = null;
            Progress = null;
            needsContinuation.Clear();
            modelRequested.Clear();
            this.held.Clear();
        }
        if (Card is null || Stage is OfferStage.Continuing or OfferStage.Declined) return;
        foreach (var finding in detection.Findings.Where(f => Card.Requested.Contains(f.ItemId)))
        {
            offered.Add(finding.ItemId);
            if (finding.Signal is DetectedSignal.SkillNeed or DetectedSignal.FailedCommand) needsContinuation.Add(finding.ItemId);
            if (finding.Signal == DetectedSignal.ModelRequest) modelRequested.Add(finding.ItemId);
            if (finding.Signal != DetectedSignal.HeldCommand || value is not CommandHeld command) continue;
            var approval = held ?? runtime.Approvals.FirstOrDefault(a => a.RequestKind == ApprovalRequest.Kind.Command && (a.Detail == command.CommandLine || a.Title == command.CommandLine));
            if (approval is not null) this.held.TryAdd(approval.Id, (approval, runtime.SkillsSessionEpoch, finding.ItemId));
        }
        if (fresh && detection.Card is null && detection.AttachTo.Count != 0)
            _ = MainActor.Spawn(() => Begin(attaching: true));
    }

    public Task Accept() => Begin(attaching: false);

    private async Task Begin(bool attaching)
    {
        if (Card is null || Stage is not (OfferStage.Asked or OfferStage.Failed)) return;
        var current = Card;
        var items = current.Items.Where(id => !installed.ContainsKey(id)).Select(id => catalogue.Items.Single(i => i.Id == id)).ToArray();
        if (items.Any(i => ItemTerms.Required(i, read()))) return; // The terms step stays inside Asked.
        var reports = new List<Task>();
        var token = new CancellationTokenSource();
        operation = token;
        var attempt = ++revision;
        Failure = null;
        Stage = OfferStage.Downloading;
        try
        {
            Action<OfferProgress> report = value =>
            {
                // The downloader may report from a worker. Every observable change belongs to the main actor.
                void Apply()
                {
                    if (revision != attempt || token.IsCancellationRequested) return;
                    if (value.Stage is not (OfferStage.Downloading or OfferStage.Checking) || value.BytesDone < 0 || value.BytesTotal < value.BytesDone)
                        throw new InvalidDataException("Invalid offer progress.");
                    Progress = value;
                    Stage = value.Stage;
                }
                lock (reports) reports.Add(MainActor.Run(Apply));
            };
            var paths = await (attaching ? download.Attach(items, report, token.Token) : download.Install(items, report, token.Token));
            Task[] pendingReports;
            lock (reports) pendingReports = reports.ToArray();
            await Task.WhenAll(pendingReports);
            if (revision != attempt || token.IsCancellationRequested) return;
            foreach (var item in items)
                if (!paths.TryGetValue(item.Id, out var path) || !Path.IsPathFullyQualified(path))
                    throw new InvalidDataException("A verified install path is missing.");
            foreach (var item in items) installed[item.Id] = paths[item.Id];
            // A new need received during the download joins the card but still requires the user's yes.
            if (Card!.Items.Any(id => !installed.ContainsKey(id))) { Stage = OfferStage.Asked; return; }
            Stage = OfferStage.Done;
            Continue();
        }
        catch (OperationCanceledException)
        {
            if (revision == attempt) Stage = OfferStage.Asked;
        }
        catch (Exception error)
        {
            if (revision != attempt || token.IsCancellationRequested) return;
            Failure = error.Message; // SK-3/SK-4 supply the S1 failure; never paraphrase it.
            Stage = OfferStage.Failed;
        }
        finally
        {
            if (revision == attempt) revision++; // Late worker progress cannot re-open a finished card.
            if (ReferenceEquals(operation, token)) operation = null;
            token.Dispose();
        }
    }

    public void Cancel()
    {
        if (Stage is not (OfferStage.Downloading or OfferStage.Checking)) return;
        revision++;
        operation?.Cancel();
        Stage = OfferStage.Asked;
        Progress = null;
        Failure = null;
    }

    public void Decline()
    {
        if (Card is null || Stage is not (OfferStage.Asked or OfferStage.Failed)) return;
        var next = read();
        foreach (var id in Card.Requested) next = next.Decline(Chat, id);
        write(next); // Persist the decline before answering a provider.
        Stage = OfferStage.Declined;
        foreach (var binding in held.Values)
            runtime.ResolveSkillsApproval(binding.Request, false, DeclineWords(catalogue.Items.Single(i => i.Id == binding.Item)), binding.Epoch);
        held.Clear();
        foreach (var list in waiters.Values) foreach (var waiter in list) waiter.TrySetResult(ToolRequestOutcome.Declined);
        waiters.Clear();
    }

    private void Continue()
    {
        var heldItems = held.Values.Select(h => h.Item).ToHashSet(StringComparer.Ordinal);
        var messages = Card!.Requested.Where(id => needsContinuation.Contains(id) && !heldItems.Contains(id) && !modelRequested.Contains(id))
            .Select(id => ContinuationWords(catalogue.Items.Single(i => i.Id == id))).ToArray();
        foreach (var binding in held.Values) runtime.ResolveSkillsApproval(binding.Request, true, null, binding.Epoch);
        held.Clear();
        foreach (var (id, list) in waiters)
            foreach (var waiter in list) waiter.TrySetResult(ToolRequestOutcome.Installed(installed[id]));
        waiters.Clear();
        Stage = OfferStage.Continuing;
        if (messages.Length != 0) runtime.QueueSkillsContinuation(string.Join("\n", messages));
    }

    // S1 words, ONDEMAND 5.5: only the catalogue's title, pin and executable fill the slots.
    private string ContinuationWords(CatalogueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Pin)) throw new InvalidDataException("The continuation has no declared pin.");
        var executable = item.Provides.FirstOrDefault(p => p.Kind == ProvideKind.Exe)?.Name
            ?? item.Needs.Select(id => catalogue.Items.Single(i => i.Id == id)).SelectMany(i => i.Provides).FirstOrDefault(p => p.Kind == ProvideKind.Exe)?.Name
            ?? throw new InvalidDataException("The continuation has no declared executable.");
        return $"{item.Title} {item.Pin} is installed now (`{executable}` is on PATH). Please continue the task from where it stopped.";
    }
    private static string DeclineWords(CatalogueItem item) => $"{item.Title} isn't on this PC and the user chose not to download it now.";

    public async Task<ToolRequestOutcome> Ask(string name, string reason, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Item(name) is not { } item) return ToolRequestOutcome.Declined;
        if (installed.TryGetValue(item.Id, out var path)) return ToolRequestOutcome.Installed(path);
        Observe(new ToolRequested(name, reason));
        if (Card is null || !Card.Requested.Contains(item.Id) || Stage is OfferStage.Declined or OfferStage.Continuing) return ToolRequestOutcome.Declined;
        modelRequested.Add(item.Id);
        var waiter = new TaskCompletionSource<ToolRequestOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!waiters.TryGetValue(item.Id, out var list)) waiters[item.Id] = list = [];
        list.Add(waiter);
        try { return await waiter.Task.WaitAsync(cancellation); }
        finally { list.Remove(waiter); }
    }
}
