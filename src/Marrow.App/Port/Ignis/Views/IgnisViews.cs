// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views; work/ignis/PLAN.md unit U4). The binding point of the Ignis views: what
// the composer, the timeline and its notice rows ask, and the one call (Bind) that hangs the running controller on the U0
// seams. Unbound (tests of other views, a build without the controller), every hook keeps the view it had: the composer its
// ContextMeter, the timeline every item, a notice row the plain NoticeRow.
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Port.Ignis.Views;

/// <summary>What the Ignis views observe that the controller does not raise: the ping filter's revision (Show pings) and the notice's holder.</summary>
internal sealed class IgnisViewState : MainActorModel
{
    public static IgnisViewState Shared { get; } = new();

    private int pingsRevision;

    /// <summary>Bumped whenever "Show keep-warm pings" (or the notice) may have changed what the timeline shows; the timeline reads it so its pass runs again.</summary>
    public int PingsRevision { get => Read(pingsRevision); set => Set(ref pingsRevision, value); }

    /// <summary>The event item that carries the first-time notice in this process (shown once: one line holds it until Got it).</summary>
    public string? NoticeHolder { get; set; }
}

public static class IgnisViews
{
    /// <summary>
    /// ENGINEERING DECISION (ED-NEW-ignis-views): the providers whose chats get the Kindle capsule in place of the ContextMeter: Claude Code and
    /// Codex (hidden pings, compact) and the OpenAI-compatible API-key providers (silent keep-warm, exact tidy-up). Every other provider keeps
    /// the ContextMeter: Ignis has no cache numbers or keep-warm route for it.
    /// </summary>
    public static IReadOnlySet<ProviderKind> SupportedProviders { get; } = new HashSet<ProviderKind>
    {
        ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.OpenRouter,
    };

    public static bool Supports(ProviderKind provider) => SupportedProviders.Contains(provider);

    /// <summary>
    /// Hangs <paramref name="controller"/> on the U0 view seams for the whole process (Program.Main, unit U3, after the controller is made):
    /// the composer slot hosts the Kindle capsule, the timeline hides "ignis:ping:" items unless pings are shown, and "ignis:event:" notices
    /// draw as quiet event lines. Settings changes reach the timeline through <see cref="IgnisViewState.PingsRevision"/>.
    /// </summary>
    public static void Bind(IIgnisController controller)
    {
        IgnisSeams.Controller.SetProcessDefault(() => controller);
        IgnisSeams.ComposerSlot.SetProcessDefault(thread => new KindleCapsule(controller, thread));
        IgnisSeams.TimelineFilter.SetProcessDefault(item => IgnisSeams.ShowsItem(item, controller.Settings.Value.ShowPings));
        IgnisSeams.EventLine.SetProcessDefault(item => IgnisEventLine.For(controller, item));
        controller.Settings.Changed += BumpPings;
    }

    /// <summary>
    /// The production binding (IgnisRuntimeStartup): the view seams ask <paramref name="running"/> for the controller each
    /// time, since it is made on first use once the app model exists; until then every hook keeps its fallback. The
    /// controller calls <see cref="Attach"/> once it is made, so Show pings reaches the timeline.
    /// </summary>
    public static void Bind(Func<IIgnisController?> running)
    {
        IgnisSeams.ComposerSlot.SetProcessDefault(thread => running() is { } controller ? new KindleCapsule(controller, thread) : null);
        IgnisSeams.TimelineFilter.SetProcessDefault(item => running() is not { } controller || IgnisSeams.ShowsItem(item, controller.Settings.Value.ShowPings));
        IgnisSeams.EventLine.SetProcessDefault(item => running() is { } controller ? IgnisEventLine.For(controller, item) : null);
    }

    /// <summary>The running controller's settings changes re-run the timeline's ping filter.</summary>
    public static void Attach(IIgnisController controller) => controller.Settings.Changed += BumpPings;

    /// <summary>The same binding for one async flow (tests); disposing it restores what was there.</summary>
    public static IDisposable Override(IIgnisController controller)
    {
        var scopes = new List<IDisposable>
        {
            IgnisSeams.Controller.Override(() => controller),
            IgnisSeams.ComposerSlot.Override(thread => new KindleCapsule(controller, thread)),
            IgnisSeams.TimelineFilter.Override(item => IgnisSeams.ShowsItem(item, controller.Settings.Value.ShowPings)),
            IgnisSeams.EventLine.Override(item => IgnisEventLine.For(controller, item)),
        };
        controller.Settings.Changed += BumpPings;
        return new Scope(() =>
        {
            controller.Settings.Changed -= BumpPings;
            for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
            IgnisViewState.Shared.NoticeHolder = null;
        });
    }

    private static void BumpPings()
    {
        if (MainActor.IsIsolated) IgnisViewState.Shared.PingsRevision++;
        else MainActor.Post(() => IgnisViewState.Shared.PingsRevision++);
    }

    /// <summary>
    /// ComposerView's hook (where the ContextMeter sits): the Kindle capsule for a supported provider's chat when Ignis is bound; null keeps
    /// the ContextMeter (the fallback).
    /// </summary>
    internal static Control? ComposerOffer(Guid thread, ProviderKind provider) =>
        Supports(provider) ? IgnisSeams.ComposerSlot.Current(thread) as Control : null;

    /// <summary>
    /// ThreadTimeline's hook: the entries the timeline shows. Hidden keep-warm pings are left out unless "Show keep-warm pings" is on: the
    /// ping's own item and the rest of its turn (the reply, its spend and work rows, by TurnID; the ping item's id carries the turn id),
    /// but never a quiet event line. The list itself is returned (no copy) when nothing is left out, so the block cache sees the same entries.
    /// </summary>
    internal static IReadOnlyList<TimelineEntry> Visible(IReadOnlyList<TimelineEntry> entries)
    {
        _ = IgnisViewState.Shared.PingsRevision; // observed: Show pings re-runs the pass
        var filter = IgnisSeams.TimelineFilter.Current;
        HashSet<Guid>? hiddenTurns = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var item = entries[i].Item;
            if (!IgnisEvent.IsPingItem(item.Id) || filter(item)) continue;
            if (PingTurn(item) is { } turn) (hiddenTurns ??= []).Add(turn);
        }
        List<TimelineEntry>? kept = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var item = entries[i].Item;
            var shows = IgnisEvent.IsPingItem(item.Id)
                ? filter(item)
                : IgnisEvent.IsEventItem(item.Id) || hiddenTurns is null || item.TurnID is not { } turn || !hiddenTurns.Contains(turn);
            if (shows) kept?.Add(entries[i]);
            else if (kept is null)
            {
                kept = new List<TimelineEntry>(entries.Count);
                for (var j = 0; j < i; j++) kept.Add(entries[j]);
            }
        }
        return kept is null ? entries : kept;
    }

    /// <summary>The turn a ping item belongs to: its TurnID, else the turn id its id carries ("ignis:ping:" + turn id).</summary>
    private static Guid? PingTurn(TimelineItem item) =>
        item.TurnID ?? (Guid.TryParse(item.Id.AsSpan(IgnisEvent.PingPrefix.Length), out var turn) ? turn : null);

    /// <summary>NoticeRow's hook: the quiet event line for an "ignis:event:" notice; null keeps the plain notice.</summary>
    internal static Control? EventLineFor(TimelineItem item) =>
        IgnisEvent.IsEventItem(item.Id) ? IgnisSeams.EventLine.Current(item) as Control : null;

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? pending = dispose;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
