// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.1, 7.4, 7.5; ED-NEW-web-w24-6). A live answer in the thread: the row the timeline
// shows in place of a tool step when a model answers with ui_show, a fenced nth-ui block, html_widget or a fenced nth-html block. It
// reads the entry's ToolLiveView and draws it: an nth-ui view natively in glass (NthUiView, building line by line while the tokens
// arrive, replaced with a crossfade when the model shows it again, its states kept), an HTML widget as a calm card that opens it in
// Tabula until the inline widget host of W19 takes its place (LiveAnswerHtml.Host), a note where a newer view replaced this one, and a
// calm line where the call failed. The reader's own states are saved on the row, so the view reopens as they left it.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.NthUi;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

/// <summary>How the timeline tells a live-answer row from a tool step.</summary>
public static class LiveAnswerRows
{
    /// <summary>True for a tool entry that holds a live answer: it is drawn as the view, outside the work steps.</summary>
    public static bool IsLive(TimelineEntry entry) =>
        entry.TimelineEntryKind == TimelineEntry.Kind.Tool && ThreadRuntime.LiveViewIn(entry) is not null;
}

/// <summary>Where the live-answer rows send what a reader does in a view (links); W10 points links at Visus through the reader's link setting.</summary>
public static class LiveAnswerEffects
{
    /// <summary>Opens a web address from a view. True when it was handed on.</summary>
    public static Ambient<Func<string, bool>> OpenLink { get; } = new(url => SwiftURL.Create(url) is { } address && Marrow.Services.Port.Workspace.Open.Current(address));
}

/// <summary>Lets the inline widget host (W19) draw an HTML live answer in the thread; null (the default) draws the card that opens it in Tabula.</summary>
public static class LiveAnswerHtml
{
    public static Ambient<Func<ToolLiveView, ThreadRuntime, Control?>> Host { get; } = new(static (_, _) => null);
}

public sealed class LiveAnswerRow : Decorator
{
    /// <summary>How long after the last change the reader's states are saved on the row.</summary>
    public const double SaveDelaySeconds = .5;

    private readonly TimelineEntry entry;
    private readonly ThreadRuntime runtime;
    private NthUiView? view;
    private Control? htmlHost;
    private string shownText = "";
    private int shownVersion;
    private bool shownLite;
    private bool completed;
    private bool loading;
    private Avalonia.Threading.DispatcherTimer? saveTimer;

    public LiveAnswerRow(TimelineEntry entry, ThreadRuntime runtime)
    {
        this.entry = entry;
        this.runtime = runtime;
        DetachedFromVisualTree += (_, _) => FlushState();
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);
    }

    /// <summary>The live view being drawn, for tests.</summary>
    internal NthUiView? View => view;

    /// <summary>The text of the view the row has drawn so far, for tests.</summary>
    internal string ShownText => shownText;

    private void Build(TimelineItem.Content content)
    {
        if (content is not TimelineItem.Content.Tool { Value: { } call } || call.Payload?.LiveView is not { } live)
        {
            Clear();
            return;
        }
        var finished = call.ToolCallStatus != ToolCall.Status.Running;
        if (live.Superseded)
        {
            Clear();
            Child = Note("arrow.down", (live.Title is { Length: > 0 } title ? title : "A live answer") + " was replaced by a newer version below.");
            return;
        }
        if (live.Problem is not null || (call.ToolCallStatus is ToolCall.Status.Failed or ToolCall.Status.Declined && live.Text.Length == 0))
        {
            Clear();
            Child = Note("exclamationmark.triangle", "This live answer could not be shown." + (live.Problem is { Length: > 0 } why ? " " + why : ""), warning: true);
            return;
        }
        if (live.IsHtml) ShowHtml(live);
        else ShowUi(live, finished);
    }

    private void Clear()
    {
        FlushState();
        view = null;
        htmlHost = null;
        shownText = "";
        shownVersion = 0;
        completed = false;
        Child = null;
    }

    // ---- an nth-ui view ----

    private void ShowUi(ToolLiveView live, bool finished)
    {
        var profile = live.Lite ? NthUiProfile.Lite : NthUiProfile.Full;
        if (view is null || shownLite != live.Lite)
        {
            var streaming = !live.Complete && !finished;
            Attach(streaming ? new NthUiView(profile) : Show(live, profile), swap: false);
            shownLite = live.Lite;
            shownText = streaming ? "" : live.Text;
            shownVersion = live.Version;
            completed = !streaming;
        }
        if (live.Version != shownVersion)
        {
            shownVersion = live.Version;
            if (!Same(live.Text, shownText) && !(Extends(live.Text, shownText) && !completed))
            {
                // The model showed the view again or changed it: the new view replaces this one with a crossfade, and the reader's choices come along.
                Attach(Show(live, profile, live.State ?? view!.SaveState()), swap: true);
                shownText = live.Text;
                completed = true;
                return;
            }
            if (live.State is { } states && completed) Load(states);
        }
        if (!Same(live.Text, shownText))
        {
            if (Extends(live.Text, shownText))
            {
                view!.Append(Added(live.Text, shownText));
                shownText = live.Text;
            }
            else
            {
                Attach(Show(live, profile, live.State), swap: true);
                shownText = live.Text;
                completed = true;
            }
        }
        if ((live.Complete || finished) && !completed)
        {
            view!.Complete();
            completed = true;
            if (live.State is { } saved) Load(saved);
        }
    }

    // A view's trailing line breaks do not change what is drawn: the text that settles the row may differ from the text streamed by them.
    private static string Trimmed(string text) => text.TrimEnd('\r', '\n');

    private static bool Same(string next, string old) => string.Equals(Trimmed(next), Trimmed(old), StringComparison.Ordinal);

    private static bool Extends(string next, string old) => Trimmed(next).Length > Trimmed(old).Length && Trimmed(next).StartsWith(Trimmed(old), StringComparison.Ordinal);

    private static string Added(string next, string old) => next.StartsWith(old, StringComparison.Ordinal) ? next[old.Length..] : next[Trimmed(old).Length..];

    private static NthUiView Show(ToolLiveView live, NthUiProfile profile, string? state = null) => NthUiView.Show(live.Text, profile, savedState: state ?? live.State);

    private void Load(string state)
    {
        loading = true;
        try { view!.LoadState(state); }
        finally { loading = false; }
    }

    private void Attach(NthUiView next, bool swap)
    {
        if (view is { } old)
        {
            old.EffectRequested -= OnEffect;
            old.StateChanged -= OnStateChanged;
        }
        FlushState();
        next.EffectRequested += OnEffect;
        next.StateChanged += OnStateChanged;
        next.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (Child is not OpacityCrossfade fade) Child = fade = new OpacityCrossfade();
        if (swap && fade.Current is not null) Transaction.WithAnimation(Animation.Snappy(duration: .24), () => fade.Show(next));
        else fade.Show(next);
        view = next;
        htmlHost = null;
    }

    private void OnEffect(object? sender, NthUiEffectEventArgs e)
    {
        var effect = e.Effect;
        switch (effect.Kind)
        {
            case NthUiEffectKind.Open:
                e.Handled = LiveAnswerEffects.OpenLink.Current(effect.Text);
                break;
            case NthUiEffectKind.Send:
                runtime.SendLiveText(effect.Text);
                e.Handled = true;
                break;
            case NthUiEffectKind.Context:
                runtime.AddLiveContext(effect.Text, effect.Json ?? "null");
                e.Handled = true;
                break;
            case NthUiEffectKind.Call:
                // The model makes the call itself, so the reader's normal tool approvals and guards apply (MCP Apps call directly, W19).
                runtime.SendLiveText($"Call the tool {effect.Server}.{effect.Tool}" + (effect.Json is { Length: > 0 } json && json != "{}" ? $" with {json}" : "") + ".");
                e.Handled = true;
                break;
            case NthUiEffectKind.Export:
                _ = Export(effect);
                e.Handled = true;
                break;
        }
    }

    private async Task Export(NthUiEffect effect)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || view is not { } shown) return;
        var csv = string.Equals(effect.Format, "csv", StringComparison.OrdinalIgnoreCase);
        byte[]? png = csv ? null : shown.ExportPng(effect.Text);
        if (!csv && png is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? "Save table" : "Save image",
            SuggestedFileName = csv ? "table.csv" : "view.png",
            DefaultExtension = csv ? "csv" : "png",
            FileTypeChoices = [csv ? new FilePickerFileType("CSV") { Patterns = ["*.csv"] } : new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        var bytes = csv ? new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(effect.Text)).ToArray() : png!;
        await stream.WriteAsync(bytes);
    }

    // ---- the reader's states ----

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (loading || !completed) return;
        saveTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(SaveDelaySeconds) };
        saveTimer.Tick -= OnSaveTick;
        saveTimer.Tick += OnSaveTick;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        saveTimer?.Stop();
        FlushState();
    }

    /// <summary>Saves the reader's states on the row now, if a save is waiting.</summary>
    internal void FlushState()
    {
        if (saveTimer is not { IsEnabled: true } timer || view is not { } shown) return;
        timer.Stop();
        runtime.SetLiveViewState(entry.Id, shown.SaveState());
    }

    // ---- an HTML widget ----

    private void ShowHtml(ToolLiveView live)
    {
        if (htmlHost is not null && live.Version == shownVersion && live.Text == shownText && live.Complete == completed) return;
        view = null;
        shownText = live.Text;
        shownVersion = live.Version;
        completed = live.Complete;
        htmlHost = LiveAnswerHtml.Host.Current(live, runtime) ?? WidgetCard(live);
        Child = htmlHost;
    }

    private Control WidgetCard(ToolLiveView live)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var heading = new TimelineBaselineRow { Spacing = 8 };
        heading.Children.Add(SwiftImage.SystemName("macwindow").Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)).ForegroundStyle(Chrome.SecondaryText));
        heading.Children.Add(TimelineRows.ChatText(live.Title is { Length: > 0 } title ? title : "Interactive widget", weight: SwiftFont.Weight.Medium, singleLine: true));
        column.Children.Add(heading);
        column.Children.Add(TimelineRows.ChatText(live.Complete ? "This widget runs in a sandbox. Open it in Tabula to use it." : "Writing the widget…", SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText));
        if (live.Complete)
        {
            var open = TimelineRows.GlassCommand("Open in Tabula", () => OpenInTabula(live), prominent: false, small: true);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 4, 0, 0);
            column.Children.Add(open);
        }
        var card = TimelineRows.Pill(column, SwiftColor.Quaternary.Opacity(0.32), new Thickness(14));
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        return card;
    }

    /// <summary>Keeps the widget as an HTML artifact of this chat (the same one again, as a new version if it changed) and opens it in Tabula.</summary>
    private void OpenInTabula(ToolLiveView live)
    {
        var host = ArtifactHost.Shared.Current;
        if (host.Store is not { } store) return;
        var thread = runtime.ThreadID;
        var id = ArtifactStore.NormalizeId("widget-" + live.Id) ?? "widget";
        try
        {
            if (store.Find(thread, id) is null)
                store.Create(thread, new ArtifactDraft { Kind = ArtifactKind.Html, Title = live.Title is { Length: > 0 } title ? title : "Widget", Id = id, Content = live.Text }, ArtifactAuthor.You, null);
            else if (store.Read(thread, id).Text != live.Text)
                store.Update(thread, id, new ArtifactRevision { Content = live.Text }, ArtifactAuthor.You, null);
        }
        catch (Exception error) when (error is ArtifactException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        host.RequestOpen(thread, id);
    }

    // ---- notes ----

    private static Control Note(string symbol, string text, bool warning = false)
    {
        var row = new TimelineBaselineRow { Spacing = 8 };
        row.Children.Add(SwiftImage.SystemName(symbol).Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)).ForegroundStyle(warning ? Chrome.Warning : SwiftColor.Secondary));
        var message = TimelineRows.ChatText(text, color: SwiftColor.Secondary);
        row.Children.Add(message);
        var pill = TimelineRows.Pill(row, warning ? Chrome.Warning.Opacity(0.1) : SwiftColor.Clear, warning ? null : default(Thickness));
        pill.HorizontalAlignment = HorizontalAlignment.Stretch;
        return pill;
    }
}
