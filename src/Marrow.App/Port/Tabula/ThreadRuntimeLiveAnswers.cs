// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3, 7.4; ED-NEW-web-w24-5). One chat's runtime as the live-answer tools see
// it: every event of the chat's session passes through the live-answer flow on its way to the timeline (a fenced nth-ui or nth-html
// block, or a call of ui_show, ui_update or html_widget, becomes a live-answer row that grows as it arrives), the changes the tools
// record are applied to the rows, and the rows are found again by view id, after a restart too. ThreadRuntime.cs holds only the one
// hook the artifact fallback already has.
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Web;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private LiveAnswerFlow? liveFlow;
    private IDisposable? liveSubscription;

    /// <summary>The events the timeline handles for events the artifact fallback has already read: the live-answer flow's.</summary>
    private IReadOnlyList<ProviderEvent> LiveEvents(ArtifactHost host, IReadOnlyList<ProviderEvent> events)
    {
        if (!host.Live.Enabled) return events;
        var flow = LiveFlowFor(host);
        if (events.Count == 1) return flow.Filter(events[0]);
        return [.. events.SelectMany(flow.Filter)];
    }

    private LiveAnswerFlow LiveFlowFor(ArtifactHost host)
    {
        if (liveFlow is not null) return liveFlow;
        liveFlow = new LiveAnswerFlow(ThreadID, host.Live, Thread?.Provider == ProviderKind.Local, new LiveEntryLookup
        {
            EntryOf = LiveEntryOf,
            Current = LiveViewOf,
        });
        // The tools record on their own threads; the chat hears of it here and draws it on the main actor. A chat that is gone hears nothing.
        var weak = new WeakReference<ThreadRuntime>(this);
        liveSubscription = host.Live.Subscribe(ThreadID, change =>
        {
            if (!weak.TryGetTarget(out var runtime)) return;
            _ = MainActor.Run(() => runtime.ApplyLiveChange(change));
        });
        return liveFlow;
    }

    private void ApplyLiveChange(LiveViewChange change)
    {
        if (liveFlow is null) return;
        foreach (var @event in liveFlow.Apply(change)) Handle(@event);
    }

    /// <summary>For tests: an event as the chat's session delivers it, through the artifact fallback and the live-answer flow.</summary>
    internal void DeliverSessionEvent(ProviderEvent @event)
    {
        foreach (var shown in ArtifactEvents(@event)) Handle(shown);
    }

    /// <summary>The id of the row showing the view with <paramref name="viewId"/> (the newest that is not superseded), or null.</summary>
    private string? LiveEntryOf(string viewId)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (LiveViewIn(Entries[i]) is { Superseded: false } view && string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase)) return Entries[i].Id;
        }
        return null;
    }

    private ToolLiveView? LiveViewOf(string entryId) => entryIndex.TryGetValue(entryId, out var entry) ? LiveViewIn(entry) : null;

    /// <summary>The live view a row holds, or null for any other row.</summary>
    public static ToolLiveView? LiveViewIn(TimelineEntry entry) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Tool { Value.Payload.LiveView: { } view } ? view : null;

    /// <summary>For the tools: the view with <paramref name="viewId"/> as this chat's timeline holds it (a view shown before the app started).</summary>
    public LiveViewRecord? FindLiveView(string viewId)
    {
        var entry = LiveEntryOf(viewId);
        return entry is not null && LiveViewOf(entry) is { Problem: null } view
            ? new LiveViewRecord(view.Id, view.Kind, view.Text, view.Title, view.Version, view.Lite, view.Height)
            : null;
    }

    /// <summary>The reader changed a control of a live view: its states are kept on the row, so the view reopens as they left it.</summary>
    public void SetLiveViewState(string entryId, string? state)
    {
        if (LiveViewOf(entryId) is not { } view || string.Equals(view.State, state, StringComparison.Ordinal)) return;
        if (state is { Length: > ToolLiveView.MaxStateLength }) return;
        ApplyToolUpdate(entryId, new ToolUpdate { Payload = new ToolPayload { LiveView = view with { State = state } } });
    }

    private readonly Dictionary<string, string> liveContext = new(StringComparer.Ordinal);

    /// <summary>An effect of a live view (the context action): a value the reader's view holds rides silently with their next message.</summary>
    public void AddLiveContext(string key, string json)
    {
        if (key.Length == 0) return;
        liveContext[key] = json.Length > ToolLiveView.MaxStateLength ? json[..ToolLiveView.MaxStateLength] : json;
    }

    /// <summary>The context the reader's views are carrying, as a block for the model ahead of their next message; null when there is none. Taking it empties it.</summary>
    private string? TakeLiveContext()
    {
        if (liveContext.Count == 0) return null;
        var lines = liveContext.Select(pair => "\"" + pair.Key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\": " + pair.Value).ToList();
        liveContext.Clear();
        return "The reader's live view carries this with their message (set by their own controls):\n{" + string.Join(", ", lines) + "}";
    }

    /// <summary>An effect of a live view: the reader's action sends a message of their own.</summary>
    public void SendLiveText(string text)
    {
        if (FoundationText.Trim(text, includeNewlines: true).Length == 0) return;
        _ = MainActor.Spawn(() => StartTurn(text, []));
    }
}
