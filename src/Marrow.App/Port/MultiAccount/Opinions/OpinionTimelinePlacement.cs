// PORT-ONLY: MA-3b-11 seam -> per-message placement. The existing user/assistant row, menu, and selectable text remain the same controls.
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Chat;
using Marrow.App.Runtime;
using Marrow.Core.Models;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionTimelinePlacement
{
    private readonly OpinionAnswerRows host;
    private readonly Panel body;
    private readonly Func<IReadOnlyList<(DisplayBlock Block, Control Row)>> visibleRows;
    private readonly OpinionTimelineContext context;
    private readonly Dictionary<Control, Decorator> anchors = [];
    private readonly Dictionary<Guid, Control> bylines = [];
    private readonly HashSet<Control> arrived = [];
    private bool moving;
    internal OpinionTimelinePlacement(OpinionAnswerRows host, Panel body, Func<IReadOnlyList<(DisplayBlock Block, Control Row)>> visibleRows, OpinionTimelineContext context)
    {
        this.host = host; this.body = body; this.visibleRows = visibleRows; this.context = context;
        host.AttachedToVisualTree += (_, _) => { body.LayoutUpdated += Layout; host.Refreshed += Refresh; Refresh(); };
        host.DetachedFromVisualTree += (_, _) => { body.LayoutUpdated -= Layout; host.Refreshed -= Refresh; };
    }
    private void Layout(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        if (moving) return; moving = true;
        try
        {
            var entries = new Dictionary<string, Decorator>(StringComparer.Ordinal);
            foreach (var (block, view) in visibleRows())
            {
                IReadOnlyList<TimelineEntry> items = block switch
                {
                    DisplayBlock.Turn turn => turn.Entries,
                    DisplayBlock.Group { Value: TimelineGroup.Single single } => [single.Value],
                    _ => [],
                };
                var users = view.GetSelfAndVisualDescendants().OfType<UserMessageRow>().ToArray();
                var assistants = view.GetSelfAndVisualDescendants().OfType<AssistantMessageRow>().ToArray();
                Index(TimelineEntry.Kind.User, users); Index(TimelineEntry.Kind.Assistant, assistants);
                void Index(TimelineEntry.Kind kind, IReadOnlyList<Decorator> views)
                {
                    var source = items.Where(item => item.TimelineEntryKind == kind).ToArray();
                    // A folded or filtered detail has no matching row: leave it unmounted until it is visible.
                    if (source.Length != views.Count) return;
                    for (var i = 0; i < source.Length; i++) entries[source[i].Id] = views[i];
                }
            }
            var wanted = new HashSet<Control>();
            foreach (var round in host.Rounds)
            {
                wanted.Add(round);
                if (context.PromptEntry(round.RoundId) is { } prompt && entries.TryGetValue(prompt, out var user)) Dock(round, user);
                else Undock(round);
                if (round.ClosingNote is not { } note) continue;
                wanted.Add(note);
                if (!bylines.TryGetValue(round.RoundId, out var byline))
                {
                    byline = new OpinionCombinedByline(context.PostedByline?.Invoke(round.RoundId) ?? round.CombinedByline, round.Cards.FirstOrDefault()?.AccountId, round.SourceAccount); bylines.Add(round.RoundId, byline);
                }
                if (byline is OpinionCombinedByline words) words.Apply(context.PostedByline?.Invoke(round.RoundId) ?? round.CombinedByline);
                wanted.Add(byline);
                if (context.CombinedEntry(round.RoundId) is { } combined && entries.TryGetValue(combined, out var assistant)) { Dock(byline, assistant); Dock(note, assistant); }
                else { Undock(byline); Undock(note); }
            }
            foreach (var stale in anchors.Keys.Where(control => !wanted.Contains(control)).ToArray()) Undock(stale);
        }
        finally { moving = false; }
    }
    private void Dock(Control control, Decorator anchor)
    {
        if (anchors.TryGetValue(control, out var previous) && ReferenceEquals(previous, anchor) && anchor.Child is MessageStack current && current.Children.Contains(control)) return;
        Undock(control);
        if (control.Parent is Panel panel) panel.Children.Remove(control);
        if (anchor.Child is not MessageStack stack)
        {
            var original = anchor.Child; anchor.Child = null; stack = new MessageStack(original); anchor.Child = stack;
        }
        stack.Children.Add(control); anchors[control] = anchor;
        if (arrived.Add(control))
        {
            if (control is OpinionRoundView round) round.Enter();
            else if (control is not OpinionCombinedByline) new OpinionArrival(control).Enter();
        }
    }
    private void Undock(Control control)
    {
        if (control.Parent is MessageStack stack)
        {
            stack.Children.Remove(control);
            if (stack.Children.Count == 1 && stack.Parent is Decorator owner) { var original = stack.Original; stack.Children.Clear(); owner.Child = null; owner.Child = original; }
        }
        else if (control.Parent is Panel panel) panel.Children.Remove(control);
        anchors.Remove(control);
    }
    private sealed class MessageStack : StackPanel
    {
        internal Control? Original { get; }
        internal MessageStack(Control? original) { Original = original; Spacing = 12; if (original is not null) Children.Add(original); }
    }
}
