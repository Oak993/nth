// PORT-ONLY: web surfaces W16a (DESIGN.md 7.4 progressive renderer: "NthUiView (Marrow.UI) keeps a keyed tree. Each
// completed line adds or updates one node. A new node fades in (opacity 0 → 1, 6 DIP rise, 180 ms) and its container height
// springs open. Earlier nodes never move except by growth below them. Text streams into text nodes as it arrives. ... State
// changes recompute every let and re-render only nodes that read it, inside one frame. Staging like the best answers: text
// first, then the visual, then the controls, simply because that is the order the model writes; nth never waits for the
// end"; 12 "each node under 2 ms; a 300-node view never takes more than 4 ms of a frame; earlier nodes never shift";
// ED-NEW-web-w16a-1). No Swift counterpart.
//
// The live view a host puts in the thread: feed it the nth-ui text as it streams (Append), end it (Complete), and handle the
// effects it asks for (copy, open, export, send, context, call). ENGINEERING DECISIONS (ED-NEW-web-w16a-1): a text line still
// arriving shows only once it has been arriving for 250 ms, so a short line (an answer behind `when=`) never flashes before
// its attributes arrive, while a long paragraph still streams; the line's slot is kept when the line completes, so the text
// does not fade in twice. Containers close when a later line arrives outside them, which is when a form shows its submit
// button and a centred or spread row lets its children in.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Core.NthUi;

namespace Marrow.UI.NthUi;

/// <summary>A live answer drawn natively from streamed nth-ui text.</summary>
public sealed class NthUiView : Decorator
{
    /// <summary>How long a text line must have been arriving before it shows unfinished.</summary>
    public const double PendingDelaySeconds = .25;

    private readonly NthUiParser parser;
    private readonly NthUiMotion motion = new();
    private readonly NthUiContext context;
    private readonly NthUiStackView root;
    private readonly Dictionary<string, NthUiNodeView> views = new(StringComparer.Ordinal);
    private readonly List<NthUiNodeView> open = [];
    private readonly PendingWatch watch;
    private PendingLine? pending;
    private NthUiNodeView? bodyPending;

    public NthUiView(NthUiProfile profile = NthUiProfile.Full, CultureInfo? culture = null)
    {
        parser = new NthUiParser(profile);
        Runtime = new NthUiRuntime(parser.Document, culture);
        context = new NthUiContext(this, motion);
        watch = new PendingWatch(this);
        var spec = NthUiSchema.Resolve("stack", null)!;
        root = new NthUiStackView(context, new NthUiNode { Kind = "stack", Spec = spec, Line = -1, Source = "" }, NthUiTokens.CardGap);
        Child = root.Control;
        AutomationProperties.SetName(this, NthUiStrings.LiveAnswer);
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);
    }

    /// <summary>A view of a whole text shown at once, as a saved answer is reopened: every line placed in its end state with
    /// no arrival motion; the reader's own changes (a tab, a filter) still move.</summary>
    public static NthUiView Show(string text, NthUiProfile profile = NthUiProfile.Full, CultureInfo? culture = null, string? savedState = null)
    {
        var view = new NthUiView(profile, culture) { Animates = false };
        view.Append(text);
        view.Complete();
        if (savedState is not null) view.LoadState(savedState);
        view.Animates = true;
        return view;
    }

    public NthUiDocument Document => parser.Document;

    public NthUiRuntime Runtime { get; }

    /// <summary>False shows every node in its end state at once.</summary>
    public bool Animates
    {
        get => motion.Enabled;
        set => motion.Enabled = value;
    }

    /// <summary>Whether anything in the view is still moving.</summary>
    public bool IsMoving => motion.IsAnimating;

    /// <summary>Loads an https or artifact image the host allows (the first image from a domain asks, DESIGN 7.2); null
    /// leaves a placeholder with the image's description. data: images are decoded here.</summary>
    public Func<string, CancellationToken, Task<IImage?>>? ImageLoader { get; set; }

    /// <summary>An action asked for something outside the view.</summary>
    public event EventHandler<NthUiEffectEventArgs>? EffectRequested;

    /// <summary>PORT-ONLY (W24, ED-NEW-web-w24-6): a state changed (the reader moved a control, an action ran, or saved states were loaded), so a host that keeps the reader's choices can save them.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Adds streamed text: each completed line is placed at once.</summary>
    public void Append(string chunk)
    {
        Apply(parser.Feed(chunk));
        UpdatePending();
    }

    /// <summary>Ends the stream: the last line counts, every container closes.</summary>
    public void Complete()
    {
        Apply(parser.Complete());
        UpdatePending();
        for (var i = open.Count - 1; i >= 0; i--) open[i].Close();
        open.Clear();
    }

    /// <summary>The control drawing the node with <paramref name="key"/> (a part's owner for a part it draws).</summary>
    public Control? ControlFor(string key) => views.TryGetValue(key, out var view) ? view.Control : null;

    /// <summary>The view of the node with <paramref name="key"/>.</summary>
    public NthUiNodeView? ViewFor(string key) => views.GetValueOrDefault(key);

    /// <summary>The slot of the node with <paramref name="key"/>, when it has one of its own.</summary>
    public NthUiSlot? SlotFor(string key) => views.TryGetValue(key, out var view) && view.Node.Key == key ? view.Slot : null;

    /// <summary>The states as JSON, to reopen the view as the reader left it.</summary>
    public string SaveState() => Runtime.SaveState();

    /// <summary>Restores saved states and redraws what reads them.</summary>
    public void LoadState(string json) => Refresh(Runtime.LoadState(json));

    /// <summary>The node with <paramref name="key"/> (the nearest card for `export png`) as a PNG on the thread's colour, or
    /// null when it is not shown.</summary>
    public byte[]? ExportPng(string key, double scale = 2)
    {
        if (ControlFor(key) is not { } control || control.Bounds.Width < 1 || control.Bounds.Height < 1) return null;
        var size = new PixelSize((int)Math.Ceiling(control.Bounds.Width * scale), (int)Math.Ceiling(control.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        Visual target = SlotFor(key) is { } slot ? slot : control;
        // A card's fill is see-through and text drawn over nothing loses its edges, so the export paints the thread first and
        // draws the card over it through a visual brush, as the thread shows it.
        using (var context = bitmap.CreateDrawingContext(true))
        {
            var area = new Rect(target.Bounds.Size);
            context.FillRectangle(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(NthUiTokens.Backdrop.Resolve(ActualThemeVariant)), new Rect(0, 0, size.Width / scale, size.Height / scale));
            context.FillRectangle(new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, SourceRect = new RelativeRect(area, RelativeUnit.Absolute), DestinationRect = new RelativeRect(area, RelativeUnit.Absolute) }, area);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    // ---- placing nodes ---------------------------------------------------------------------------------------------------

    private void Apply(IReadOnlyList<NthUiChange> changes)
    {
        if (changes.Count == 0) return;
        var batch = changes.ToArray();
        var fresh = Runtime.Sync();
        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in batch)
        {
            if (change.Kind == NthUiChangeKind.BodyChanged)
            {
                if (views.TryGetValue(change.Node.Key, out var owner)) owner.BodyChanged();
                continue;
            }
            Add(change.Node);
            created.Add(change.Node.Key);
        }
        if (fresh.Count > 0) Refresh(fresh.Where(k => !created.Contains(k)).ToArray());
    }

    private void Add(NthUiNode node)
    {
        CloseOutside(node);
        var parent = node.Parent is null ? root : views.GetValueOrDefault(node.Parent.Key);
        if (parent is null || (node.Parent is not null && parent.Node.Key != node.Parent.Key)) return; // under a part its owner draws
        var view = parent.Add(node);
        if (view is null) return;
        views[node.Key] = view;
        open.Add(view);
    }

    private void CloseOutside(NthUiNode node)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (var p = node.Parent; p is not null; p = p.Parent) ancestors.Add(p.Key);
        while (open.Count > 0 && !ancestors.Contains(open[^1].Node.Key))
        {
            var last = open[^1];
            open.RemoveAt(open.Count - 1);
            last.Close();
        }
    }

    internal void Register(string key, NthUiNodeView owner) => views.TryAdd(key, owner);

    // ---- state ---------------------------------------------------------------------------------------------------------

    internal void Refresh(IEnumerable<string> keys)
    {
        var list = keys as IReadOnlyCollection<string> ?? keys.ToArray();
        if (list.Count == 0) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
        var done = new HashSet<NthUiNodeView>();
        foreach (var key in list)
        {
            if (views.TryGetValue(key, out var view) && done.Add(view)) view.Refresh();
        }
        foreach (var key in list)
        {
            if (!views.TryGetValue(key, out var view) || view.Node.Key != key || view.Slot is null || view.Node.Arg("when") is null) continue;
            view.Slot.SetShown(Runtime.IsVisible(view.Node));
        }
    }

    internal void Run(NthUiNode node)
    {
        var result = Runtime.Run(node);
        Refresh(result.Changed);
        foreach (var effect in result.Effects) Raise(effect);
    }

    internal void Raise(NthUiEffect effect)
    {
        var args = new NthUiEffectEventArgs(effect);
        EffectRequested?.Invoke(this, args);
        if (!args.Handled && effect.Kind == NthUiEffectKind.Copy) _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(effect.Text);
    }

    // ---- the line still arriving -----------------------------------------------------------------------------------------

    private sealed class PendingLine(NthUiNodeView host, string kind, double since)
    {
        public NthUiNodeView Host { get; } = host;
        public string Kind { get; } = kind;
        public double Since { get; } = since;
        public string Text { get; set; } = "";
        public IReadOnlySet<string> Flags { get; set; } = new HashSet<string>();
        public NthUiPendingTextView? View { get; set; }
        public NthUiSlot? Slot { get; set; }
    }

    private sealed class PendingWatch(NthUiView owner) : INthUiAnimated
    {
        public bool Step(double now) => owner.ShowPendingIfDue(now);
    }

    private void UpdatePending()
    {
        var line = parser.Pending;
        if (line is { Kind: "body", Parent: { } bodyOwner } && views.TryGetValue(bodyOwner.Key, out var ownerView) && ownerView.Node.Key == bodyOwner.Key)
        {
            if (bodyPending is not null && !ReferenceEquals(bodyPending, ownerView)) bodyPending.ShowPending(null);
            bodyPending = ownerView;
            ownerView.ShowPending(line.Text);
        }
        else if (bodyPending is not null)
        {
            bodyPending.ShowPending(null);
            bodyPending = null;
        }
        if (line is null || line.Kind == "body")
        {
            DropPending();
            return;
        }
        var text = CleanPending(line.Kind, line.Text, out var flags);
        var host = text is null ? null : HostFor(line.Parent);
        if (text is null || host is null)
        {
            DropPending();
            return;
        }
        if (pending is not null && (!ReferenceEquals(pending.Host, host) || pending.Kind != line.Kind)) DropPending();
        pending ??= new PendingLine(host, line.Kind, NthUiMotion.Now);
        pending.Text = text;
        pending.Flags = flags;
        if (pending.View is not null) pending.View.Show(text, flags);
        else if (ShowPendingIfDue(NthUiMotion.Now)) motion.Start(watch);
    }

    private bool ShowPendingIfDue(double now)
    {
        if (pending is null || pending.View is not null) return false;
        if (now - pending.Since < PendingDelaySeconds) return true;
        var view = new NthUiPendingTextView(context, pending.Kind);
        view.Show(pending.Text, pending.Flags);
        var slot = new NthUiSlot(motion) { Mode = NthUiSlotMode.Follow, Surface = view.Surface, Child = view.Control };
        pending.View = view;
        pending.Slot = slot;
        pending.Host.Insert(slot, true);
        return false;
    }

    private void DropPending()
    {
        if (pending?.Slot is { } slot && slot.Parent is Panel panel) panel.Children.Remove(slot);
        pending = null;
    }

    /// <summary>The slot already showing <paramref name="view"/>'s line while it arrived, when it is in <paramref name="host"/>.</summary>
    internal NthUiSlot? TakePending(NthUiNodeView view, NthUiNodeView host)
    {
        if (pending is not { Slot: { } slot } line || !ReferenceEquals(line.Host, host) || line.Kind != view.Node.Kind) return null;
        pending = null;
        return slot;
    }

    private NthUiNodeView? HostFor(NthUiNode? parent)
    {
        for (var node = parent; node is not null; node = node.Parent)
        {
            if (!NthUiSchema.TakesText(node.Spec.Children)) continue;
            return views.TryGetValue(node.Key, out var view) && view.Node.Key == node.Key && view.HostsPending ? view : null;
        }
        return root;
    }

    /// <summary>The text of an unfinished text, heading or callout line worth showing: its leading flags read, a quoted text
    /// unquoted; null for a tail expression or nothing yet.</summary>
    internal static string? CleanPending(string kind, string text, out IReadOnlySet<string> flags)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        flags = found;
        var rest = text.TrimStart();
        if (kind == "text")
        {
            while (true)
            {
                var space = rest.IndexOf(' ', StringComparison.Ordinal);
                var word = space < 0 ? rest : rest[..space];
                if (word is not ("muted" or "strong" or "small")) break;
                found.Add(word);
                rest = space < 0 ? "" : rest[(space + 1)..].TrimStart();
            }
        }
        if (rest.Length == 0 || rest[0] == '=') return null;
        if (rest[0] is '"' or '\'') rest = Unquote(rest);
        rest = rest.TrimEnd();
        return rest.Length == 0 ? null : rest;
    }

    private static string Unquote(string text)
    {
        var quote = text[0];
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[++i];
                builder.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => ' ', _ => next });
                continue;
            }
            if (c == quote) break;
            builder.Append(c);
        }
        return builder.ToString();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        motion.Resume();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        motion.Pause();
    }
}
