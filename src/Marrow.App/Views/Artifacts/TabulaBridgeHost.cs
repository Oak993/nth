using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Localisation;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, what Tabula draws for the artifacts' bridge (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.5:
// the model's consent card "This artifact wants to use <model>. It cannot see this chat, your files or your other artifacts.
// Each use counts toward your plan." with Allow for this artifact / Not now, the first call per tool, nth's own "Choose a
// file" sheet over the pane, nth.send's draft, nth.theme's glass tokens, and the network card that lists every exact
// domain with its registrable domain in bold; 2.4 "popups are top-level windows"; the approved canvas index.html .consent;
// ED-NEW-web-w14-1). No Swift counterpart. The process's one bridge host finds the pane that shows the asking artifact and
// asks there; a card is a popup (its own window), so it shows above the page, rising 6 DIP and fading in. With no pane
// showing the artifact every request is refused, as the unbound host does.

namespace Marrow.App.Views.Artifacts;

/// <summary>The bridge host Tabula binds into the artifacts runtime.</summary>
public sealed class TabulaBridgeHost : IArtifactBridgeHost
{
    public static TabulaBridgeHost Instance { get; } = new();

    private readonly List<WeakReference<TabulaView>> views = [];
    private readonly object gate = new();
    private Action? themeChanged;
    private bool themeHooked;

    /// <summary>The words of the model card (6.5).</summary>
    public static string ModelTitle(string model) => UiText.Format("tabula.bridge.model_title", model);

    public static string ModelDetail => UiText.Get("tabula.bridge.model_detail");

    public static string ToolTitle(string server, string tool) => UiText.Format("tabula.bridge.tool_title", server, tool);

    public static string ToolDetail => UiText.Get("tabula.bridge.tool_detail");

    public static string Allow => UiText.Get("tabula.bridge.allow");

    public static string NotNow => UiText.Get("tabula.bridge.not_now");

    public static string Declined => UiText.Get("tabula.bridge.declined");

    /// <summary>ED-NEW-secfinal-40: the words of the draft card.</summary>
    public static string DraftTitle => UiText.Get("tabula.bridge.draft_title");

    public static string DraftDetail(int characters) => UiText.FormatWith(CultureInfo.CurrentCulture, "tabula.bridge.draft_detail", characters.ToString("N0", CultureInfo.CurrentCulture));

    public static string DraftAllow => UiText.Get("tabula.bridge.draft_allow");

    internal void Add(TabulaView view)
    {
        lock (gate)
        {
            views.RemoveAll(w => !w.TryGetTarget(out var v) || v == view);
            views.Add(new WeakReference<TabulaView>(view));
        }
    }

    internal void Remove(TabulaView view)
    {
        lock (gate) views.RemoveAll(w => !w.TryGetTarget(out var v) || v == view);
    }

    /// <summary>The pane on screen that shows <paramref name="artifact"/>, or null.</summary>
    internal TabulaView? Find(ArtifactAddress artifact)
    {
        lock (gate)
            foreach (var weak in views)
                if (weak.TryGetTarget(out var view) && view.Shows(artifact)) return view;
        return null;
    }

    public Task<bool> ConfirmModelAsync(ArtifactAddress artifact, string modelName, CancellationToken cancellationToken) =>
        OnUi(async () => Find(artifact) is { } view
            && await view.Cards.AskAsync(new TabulaCardSpec(PaneGlyphKind.Spark, ModelTitle(modelName), ModelDetail, Allow, NotNow), cancellationToken).ConfigureAwait(true)
            || Declined1(artifact));

    public Task<bool> ConfirmToolAsync(ArtifactAddress artifact, string server, string tool, CancellationToken cancellationToken) =>
        OnUi(async () => Find(artifact) is { } view
            && await view.Cards.AskAsync(new TabulaCardSpec(PaneGlyphKind.Shield, ToolTitle(server, tool), ToolDetail, Allow, NotNow), cancellationToken).ConfigureAwait(true)
            || Declined1(artifact));

    public Task<IReadOnlyList<ArtifactPickedFile>> PickFilesAsync(ArtifactAddress artifact, string accept, bool multiple, bool fromDrop, CancellationToken cancellationToken) =>
        OnUi(() => Find(artifact) is { } view ? view.Cards.PickAsync(accept, multiple, fromDrop, cancellationToken) : Task.FromResult<IReadOnlyList<ArtifactPickedFile>>([]));

    // ED-NEW-secfinal-40: each nth.send meets this card; only its button adds the text to the draft.
    public Task<bool> ConfirmDraftAsync(ArtifactAddress artifact, string text, CancellationToken cancellationToken) =>
        OnUi(async () => Find(artifact) is { } view
            && await view.Cards.AskAsync(new TabulaCardSpec(PaneGlyphKind.Edit, DraftTitle, DraftDetail(text.Length), DraftAllow, NotNow)
            {
                Lines = [(ArtifactDraftText.Preview(text), null)],
            }, cancellationToken).ConfigureAwait(true)
            || Declined1(artifact));

    public void Draft(ArtifactAddress artifact, string text) => Dispatcher.UIThread.Post(() => Find(artifact)?.DraftFromArtifact(text));

    public ArtifactTheme Theme => ThemeFor(Application.Current?.ActualThemeVariant ?? ThemeVariant.Light);

    public event Action? ThemeChanged
    {
        add
        {
            lock (gate)
            {
                themeChanged += value;
                if (themeHooked || Application.Current is not { } app) return;
                themeHooked = true;
                app.ActualThemeVariantChanged += (_, _) => themeChanged?.Invoke();
            }
        }
        remove
        {
            lock (gate) themeChanged -= value;
        }
    }

    // A "no" is said once, in the pane's status line (canvas: 'Not allowed. The artifact got a polite "no".').
    private bool Declined1(ArtifactAddress artifact)
    {
        Find(artifact)?.ShowStatus(Declined);
        return false;
    }

    /// <summary>nth's glass tokens as the page's CSS variables (nth.theme), for one appearance.</summary>
    public static ArtifactTheme ThemeFor(ThemeVariant theme)
    {
        var dark = theme == ThemeVariant.Dark;
        string Css(SwiftColor color)
        {
            var c = color.Resolve(theme);
            return string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{Math.Round(c.A / 255.0, 3)})");
        }
        var variables = new Dictionary<string, string>
        {
            ["--nth-bg"] = Css(TabulaTokens.Well),
            ["--nth-text"] = Css(PaneTokens.Text1),
            ["--nth-text-2"] = Css(PaneTokens.Text2),
            ["--nth-text-3"] = Css(PaneTokens.Text3),
            ["--nth-line"] = Css(PaneTokens.Line),
            ["--nth-fill"] = Css(TabulaTokens.InfoFill),
            ["--nth-fill-2"] = Css(PaneTokens.HoverFill),
            ["--nth-accent"] = Css(Marrow.UI.Chrome.Chrome.Accent),
            ["--nth-primary"] = Css(TabulaTokens.PrimaryFill),
            ["--nth-primary-text"] = Css(TabulaTokens.PrimaryText),
            ["--nth-error"] = Css(TabulaTokens.ErrorText),
            ["--nth-font"] = "\"Segoe UI Variable Text\", \"Segoe UI\", system-ui, sans-serif",
            ["--nth-mono"] = "\"Cascadia Mono\", Consolas, ui-monospace, monospace",
            ["--nth-radius"] = "14px",
            ["--nth-radius-small"] = "9px",
        };
        return new ArtifactTheme(dark, variables);
    }

    private static Task<T> OnUi<T>(Func<Task<T>> work)
    {
        if (Dispatcher.UIThread.CheckAccess()) return work();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { done.TrySetResult(await work()); }
            catch (OperationCanceledException) { done.TrySetCanceled(); }
            catch (Exception error) { done.TrySetException(error); }
        });
        return done.Task;
    }
}

/// <summary>A consent card's words: the glyph, the title, the detail, and the two buttons.</summary>
public sealed record TabulaCardSpec(PaneGlyphKind Glyph, string Title, string Detail, string Primary, string Secondary)
{
    /// <summary>Lines under the detail (the network card's domains, each with its registrable domain in bold).</summary>
    public IReadOnlyList<(string Text, string? Bold)> Lines { get; init; } = [];

    /// <summary>A warning under the lines (the network card's dropped entries).</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// The pane's cards, one at a time: each a popup over the preview (its own window, so above the page), centred on it. A
/// second request waits for the first. Escape is the secondary button; a card whose request is cancelled (the page left)
/// closes as "no".
/// + ED-NEW-web-sec-artifacts-3 (security review L1): the artifact chooses when a card appears, so a card follows Visus's
/// prompt rules (8.4, 10.1): it takes no focus when it appears, Enter does nothing in it, F6 moves into it (its words, not a
/// button) and F6 again gives the keyboard back, and for <see cref="VisusPromptCard.Shield"/> after it appears or the
/// keyboard enters it, its buttons ignore clicks and keys, on the Visus clock.
/// </summary>
public sealed class TabulaCards
{
    private readonly Control anchor;
    private readonly Panel host;
    private readonly ITabulaFiles files;
    private readonly SemaphoreSlim one = new(1, 1);
    private Popup? popup;
    private TaskCompletionSource<int>? answer;
    private Control? lead;
    private IInputElement? returnTo;
    private long shieldFrom;

    internal TabulaCards(Control anchor, Panel host, ITabulaFiles files)
    {
        this.anchor = anchor;
        this.host = host;
        this.files = files;
    }

    /// <summary>Says something in the pane's status line (a refused file).</summary>
    public Action<string>? Status { get; set; }

    /// <summary>The card on screen (its title), or null.</summary>
    public string? Showing { get; private set; }

    internal Popup? Popup => popup;

    internal IReadOnlyList<Button> Buttons => popup?.Child is Control root ? root.GetLogicalDescendants().OfType<Button>().ToList() : [];

    /// <summary>Asks; true for the primary button.</summary>
    public async Task<bool> AskAsync(TabulaCardSpec spec, CancellationToken cancellationToken)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            return await Present(spec, null, cancellationToken).ConfigureAwait(true) == 1;
        }
        finally
        {
            one.Release();
        }
    }

    /// <summary>nth's "Choose a file" sheet (6.5): the system picker on its button; the artifact gets names, types and bytes only.</summary>
    public async Task<IReadOnlyList<ArtifactPickedFile>> PickAsync(string accept, bool multiple, bool fromDrop, CancellationToken cancellationToken)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var detail = fromDrop ? UiText.Get("tabula.bridge.pick_detail_dropped") : UiText.Get("tabula.bridge.pick_detail");
            var spec = new TabulaCardSpec(PaneGlyphKind.Save,
                multiple ? UiText.Get("tabula.bridge.pick_title_many") : UiText.Get("tabula.bridge.pick_title_one"), detail,
                multiple ? UiText.Get("tabula.bridge.pick_button_many") : UiText.Get("tabula.bridge.pick_button_one"), UiText.Get("tabula.common.cancel"))
            {
                Warning = string.IsNullOrWhiteSpace(accept) ? null : UiText.Format("tabula.bridge.pick_accepts", accept.Trim()),
            };
            if (await Present(spec, null, cancellationToken).ConfigureAwait(true) != 1) return [];
            var paths = await files.OpenAsync(anchor, multiple ? UiText.Get("tabula.bridge.picker_title_many") : UiText.Get("tabula.bridge.picker_title_one"), multiple).ConfigureAwait(true);
            var picked = new List<ArtifactPickedFile>();
            var refused = new List<string>();
            foreach (var path in paths.Take(multiple ? 32 : 1))
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                // A file over the limit is never read: the pane says so and the artifact gets the rest.
                if (info.Length > ArtifactBridgeLimits.MaxPickedFileBytes)
                {
                    refused.Add(info.Name);
                    continue;
                }
                picked.Add(new ArtifactPickedFile(info.Name, MimeType(info.Extension), await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(true)));
            }
            if (refused.Count > 0)
                Status?.Invoke(refused.Count == 1
                    ? UiText.Format("tabula.bridge.refused_one", refused[0])
                    : UiText.FormatWith(CultureInfo.InvariantCulture, "tabula.bridge.refused_many", refused.Count));
            return picked;
        }
        finally
        {
            one.Release();
        }
    }

    /// <summary>The network card (6.5): the declared domains, the dropped entries, and the warning; true for Allow.</summary>
    public Task<bool> AskNetworkAsync(ArtifactNetworkRequest request, CancellationToken cancellationToken)
    {
        var lines = request.Hosts.Select(h => (h.Host, (string?)h.RegistrableDomain)).ToList();
        var dropped = request.Dropped.Count == 0 ? null
            : UiText.Format("tabula.bridge.network_dropped", string.Join(", ", request.Dropped.Select(d => d.Source + " (" + d.Reason + ")")));
        var spec = new TabulaCardSpec(PaneGlyphKind.Warning, UiText.Get("tabula.bridge.network_title"), ArtifactNetwork.Warning, UiText.Get("tabula.bridge.network_allow"), NotNowText)
        {
            Lines = lines,
            Warning = dropped,
        };
        return AskAsync(spec, cancellationToken);
    }

    private static string NotNowText => TabulaBridgeHost.NotNow;

    /// <summary>Answers the card on screen (tests, and the buttons): 1 primary, 0 secondary.</summary>
    internal void Answer(int choice) => answer?.TrySetResult(choice);

    /// <summary>Within the shield of the card appearing or of the keyboard entering it: its buttons ignore clicks and keys.</summary>
    internal bool IsShielded => VisusHost.Time.GetElapsedTime(shieldFrom) < VisusPromptCard.Shield;

    // A button's answer, unless the shield is up (a click or key that landed as the card appeared does nothing).
    private void Press(int choice)
    {
        if (!IsShielded) Answer(choice);
    }

    // F6 into the card (10.1): its words take the keyboard; where it came from is where F6 again gives it back.
    private bool FocusLead()
    {
        if (lead is not { } words || popup?.Child is not Visual card) return false;
        var from = TopLevel.GetTopLevel(anchor)?.FocusManager?.GetFocusedElement();
        if (!(from is Visual visual && (ReferenceEquals(card, visual) || card.IsVisualAncestorOf(visual)))) returnTo = from;
        return words.Focus(NavigationMethod.Tab);
    }

    private void ReturnFocus()
    {
        var target = returnTo;
        returnTo = null;
        if (target is InputElement { IsEffectivelyVisible: true } element && TopLevel.GetTopLevel(element) is not null && element.Focus(NavigationMethod.Tab)) return;
        anchor.Focus(NavigationMethod.Tab);
    }

    private async Task<int> Present(TabulaCardSpec spec, object? _, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        answer = done;
        returnTo = null;
        var card = Build(spec);
        var motion = new TabulaMotion(card, opacity: 0, rise: 6);
        var shown = new Popup
        {
            Child = card,
            PlacementTarget = anchor,
            Placement = PlacementMode.Center,
            IsLightDismissEnabled = false,
            Topmost = false,
        };
        host.Children.Add(shown);
        popup = shown;
        Showing = spec.Title;
        shown.IsOpen = true;
        shieldFrom = VisusHost.Time.GetTimestamp();
        motion.To(1, 0);
        // F6 reaches the open card (10.1) as it reaches Visus's prompts: the same list, the same workspace hook.
        var window = TopLevel.GetTopLevel(anchor);
        using var prompt = window is null ? null : VisusPrompts.Add(window, FocusLead);
        if (window is not null && anchor.FindAncestorOfType<PaneWorkspace>() is { PromptFirst: null } panes) panes.PromptFirst = () => VisusPrompts.FocusNewest(window);
        using var cancel = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => done.TrySetResult(0)));
        void Detached(object? s, VisualTreeAttachmentEventArgs e) => done.TrySetResult(0);
        anchor.DetachedFromVisualTree += Detached;
        int result;
        try
        {
            result = await done.Task.ConfigureAwait(true);
        }
        finally
        {
            anchor.DetachedFromVisualTree -= Detached;
            var hadKeyboard = card.IsKeyboardFocusWithin;
            answer = null;
            lead = null;
            Showing = null;
            popup = null;
            shown.IsOpen = false;
            host.Children.Remove(shown);
            if (hadKeyboard) ReturnFocus();
            returnTo = null;
        }
        return result;
    }

    private Border Build(TabulaCardSpec spec)
    {
        var glyph = new PaneGlyph(spec.Glyph, 20) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        var title = new TextBlock { Text = spec.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { Text = spec.Detail, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var words = new StackPanel { Spacing = 2, Children = { title, detail } };
        foreach (var (text, bold) in spec.Lines)
        {
            var line = new TextBlock { FontFamily = TabulaTokens.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            var at = bold is null ? -1 : text.LastIndexOf(bold, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && bold is not null)
            {
                line.Inlines = new InlineCollection();
                if (at > 0) line.Inlines.Add(new Run(text[..at]));
                line.Inlines.Add(new Run(text.Substring(at, bold.Length)) { FontWeight = FontWeight.Bold });
                if (at + bold.Length < text.Length) line.Inlines.Add(new Run(text[(at + bold.Length)..]));
            }
            else line.Text = text;
            words.Children.Add(line);
        }
        TextBlock? warning = null;
        if (spec.Warning is { } warn)
        {
            warning = new TextBlock { Text = warn, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            words.Children.Add(warning);
        }
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Children = { glyph } };
        Grid.SetColumn(words, 1);
        head.Children.Add(words);
        // 10.1: the card's words are where F6 lands (a reader hears the question before any button). The ring is the app's
        // focus band; the margin gives back its room so nothing moves when it shows.
        var question = new Border
        {
            Focusable = true,
            Margin = new Thickness(-6),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(9),
            BorderBrush = Brushes.Transparent,
            Child = head,
        };
        question.GotFocus += (_, _) => question.BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(question);
        question.LostFocus += (_, _) => question.BorderBrush = Brushes.Transparent;
        AutomationProperties.SetName(question, VisusPromptCard.Sentence(spec.Title) + " " + spec.Detail);
        lead = question;
        var primary = TabulaButtons.Pill(spec.Primary, () => Press(1), primary: true);
        var secondary = TabulaButtons.Pill(spec.Secondary, () => Press(0));
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Children = { primary, secondary } };
        secondary.Margin = new Thickness(6, 0, 0, 0);
        var card = new Border
        {
            Width = 320,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(0.5),
            Child = new StackPanel { Children = { question, row } },
        };
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        void Paint()
        {
            card.Background = PaneTokens.CardFill(anchor.ActualThemeVariant, expanded: true);
            card.BorderBrush = PaneTokens.Line.Brush(anchor);
            card.BoxShadow = PaneTokens.Shadow(anchor.ActualThemeVariant, lifted: true);
            glyph.Foreground = PaneTokens.Text1.Brush(anchor);
            title.Foreground = PaneTokens.Text1.Brush(anchor);
            detail.Foreground = PaneTokens.Text2.Brush(anchor);
            foreach (var line in words.Children.OfType<TextBlock>().Skip(2)) line.Foreground = PaneTokens.Text1.Brush(anchor);
            if (warning is not null) warning.Foreground = TabulaTokens.ErrorText.Brush(anchor);
        }
        Paint();
        // 8.4, 10.1: Enter does nothing, Esc gives the safe answer at once, F6 gives the keyboard back, and within the shield
        // a Space (its press or its release) or a click on the card does nothing. No control takes the focus by itself.
        card.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    return;
                case Key.Escape:
                    e.Handled = true;
                    Answer(0);
                    return;
                case Key.F6:
                    e.Handled = true;
                    ReturnFocus();
                    return;
                case Key.Space when IsShielded:
                    e.Handled = true;
                    return;
            }
        }, RoutingStrategies.Tunnel);
        card.AddHandler(InputElement.KeyUpEvent, (_, e) => { if (e.Key == Key.Space && IsShielded) e.Handled = true; }, RoutingStrategies.Tunnel);
        card.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (IsShielded) e.Handled = true; }, RoutingStrategies.Tunnel);
        card.AddHandler(InputElement.PointerReleasedEvent, (_, e) => { if (IsShielded) e.Handled = true; }, RoutingStrategies.Tunnel);
        card.PropertyChanged += (_, e) =>
        {
            // 10.1: the shield also runs from the moment the keyboard enters the card.
            if (e.Property == InputElement.IsKeyboardFocusWithinProperty && e.NewValue is true) shieldFrom = VisusHost.Time.GetTimestamp();
        };
        AutomationProperties.SetName(card, VisusPromptCard.Sentence(spec.Title) + " " + VisusPromptCard.F6Hint);
        AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Assertive);
        card.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Pane);
        AutomationProperties.SetHelpText(card, spec.Detail);
        return card;
    }

    /// <summary>The type an artifact is told for a picked file (by its extension; unknown is octet-stream).</summary>
    public static string MimeType(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "bmp" => "image/bmp",
        "pdf" => "application/pdf",
        "txt" or "log" => "text/plain",
        "csv" => "text/csv",
        "tsv" => "text/tab-separated-values",
        "json" => "application/json",
        "md" => "text/markdown",
        "html" or "htm" => "text/html",
        "css" => "text/css",
        "js" or "mjs" => "text/javascript",
        "xml" => "application/xml",
        "zip" => "application/zip",
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "mp4" => "video/mp4",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream",
    };
}

/// <summary>The bridge's file limit as Tabula reads it (ArtifactBridge keeps its own copy internal).</summary>
public static class ArtifactBridgeLimits
{
    /// <summary>DESIGN 6.5 "Files over 20 MB are refused".</summary>
    public const long MaxPickedFileBytes = 20L * 1024 * 1024;
}
