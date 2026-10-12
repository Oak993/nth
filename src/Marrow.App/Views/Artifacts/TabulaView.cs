using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, the Tabula pane's body (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1, 6.3, 6.4, 6.5, 3.2
// header overflow, 2.4 airspace, 10 accessibility; the approved canvas index.html section 4: the header .ah with the title's
// menu, the kind and the cost meter, Ask to change, Copy, Save to project and Open in Visus; the version strip .vs; the view
// bar .vbar with Preview / Code / Diff, "Sandboxed · no files · no network" and "Comparing v3 → v4"; the dark well .aview;
// the skeleton .skel; the composer .ask; the status .toast; ED-NEW-web-w14-1). No Swift counterpart.
//
// Rows, top to bottom: the version strip, the view bar, a calm notice (an error, a link, a missing library, a read-only
// copy), the well (preview, code, diff or skeleton), and the Ask to change composer. Nothing of nth's is drawn over the
// well: a page is its own window. Menus and cards are popups (their own windows). The gallery replaces all five rows.

namespace Marrow.App.Views.Artifacts;

public sealed class TabulaView : Grid
{
    /// <summary>The stream key prefix of a change asked from the pane (it ends with the turn, or when the version lands).</summary>
    public const string AskStreamPrefix = "ask:";

    /// <summary>The cost meter stays this long after a call ends (canvas: setTimeout 1600).</summary>
    public static readonly TimeSpan MeterLinger = TimeSpan.FromSeconds(1.6);

    private readonly TabulaContext context;
    private readonly TabulaState state;
    private readonly TabulaVersionStrip strip = new();
    private readonly TabulaSegmented modes = new("View");
    private readonly TabulaSegmented layouts = new("Diff layout");
    private readonly Button sandbox;
    private readonly TextBlock sandboxText = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabulaStatus status = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340 };
    private readonly Button compareLabel;
    private readonly TextBlock compareText = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button edit;
    private readonly Button save;
    private readonly Button cancel;
    private readonly Border viewBar;
    private readonly TabulaNotice notice = new();
    private readonly Border well;
    private readonly TabulaPreview preview;
    private readonly TabulaCodeView code = new() { IsVisible = false };
    private readonly TabulaDiffView diff = new() { IsVisible = false };
    private readonly TabulaSkeleton skeleton = new() { IsVisible = false };
    private readonly TabulaMotion skeletonFade;
    private readonly TabulaAsk ask = new();
    private readonly TabulaGallery gallery;
    private readonly StackPanel empty;
    private readonly Button emptyGallery;
    private readonly Panel menus = new() { IsHitTestVisible = false, Width = 0, Height = 0 };
    private Popup? menu;
    private TabulaMenuState? menuState;
    private Artifact? artifact;
    private ArtifactSnapshot? snapshot;
    private int shownVersion;
    private ArtifactStream? creating;
    private ArtifactStream? updating;
    private string? headerKey;
    private ArtifactRuntimeError? error;
    private string? link;
    private string? missing;
    private ArtifactModelStatus? model;
    private long tokens;
    private DateTimeOffset? meterUntil;
    private (Guid Thread, string Id)? noticeFor;
    private bool editing;
    private bool renderQueued;
    private bool attached;

    public TabulaView(TabulaContext context)
    {
        this.context = context;
        state = context.State;
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto");
        preview = new TabulaPreview(context);
        gallery = new TabulaGallery(context) { IsVisible = false };
        Content = new PaneContent(this, "Tabula");
        Content.SetTitleMenu(OpenTitleMenu);

        // ---- the view bar ----
        modes.Add("Preview", TabulaViewMode.Preview);
        modes.Add("Code", TabulaViewMode.Code);
        modes.Add("Diff", TabulaViewMode.Diff);
        modes.Picked += value => SetView((TabulaViewMode)value);
        layouts.Add("Inline", TabulaDiffLayout.Inline);
        layouts.Add("Side by side", TabulaDiffLayout.SideBySide);
        layouts.Picked += value => state.SetDiffLayout((TabulaDiffLayout)value);
        var shield = new PaneGlyph(PaneGlyphKind.Shield, 12) { VerticalAlignment = VerticalAlignment.Center };
        var sandboxRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { shield, sandboxText } };
        var sandboxFace = new Border { Height = 26, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = sandboxRow };
        sandbox = PlainControls.Button(sandboxFace, OpenArtifactMenu).KeyboardFocus(8);
        sandbox.OnHover(on => sandboxFace.Background = on ? PaneTokens.HoverFill.Brush(sandboxFace) : Brushes.Transparent);
        AutomationProperties.SetName(sandbox, "Artifact permissions");
        compareLabel = PlainControls.Button(new Border { Height = 26, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(8), Child = compareText, Background = Brushes.Transparent }, OpenCompareMenu).KeyboardFocus(8);
        edit = TabulaButtons.Text("Edit", BeginEdit, PaneGlyphKind.Edit);
        save = TabulaButtons.Pill("Save", SaveEdit, primary: true, name: "Save the edit");
        cancel = TabulaButtons.Text("Cancel", CancelEdit);
        var left = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { modes, sandbox } };
        sandbox.Margin = new Thickness(6, 0, 0, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Children = { status, compareLabel, layouts, edit, cancel, save } };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { left } };
        Grid.SetColumn(right, 1);
        bar.Children.Add(right);
        viewBar = new Border { Padding = new Thickness(12, 8), Child = bar };
        Grid.SetRow(viewBar, 1);
        Grid.SetRow(notice, 2);
        notice.Dismissed += DismissNotice;

        // ---- the well ----
        var area = new Panel { Children = { preview, code, diff, skeleton } };
        well = new Border { Margin = new Thickness(TabulaTokens.Bezel, 0, TabulaTokens.Bezel, TabulaTokens.Bezel), ClipToBounds = true, Child = area };
        Grid.SetRow(well, 3);
        skeletonFade = new TabulaMotion(skeleton, opacity: 0);
        Grid.SetRow(ask, 4);
        ask.Sent += SendAsk;
        ask.Dismissed += () => state.OpenAsk(false);
        ask.PartCleared += state.ClearAskSelection;
        ask.TextChanged += text => state.AskText = text;

        // ---- the empty pane ----
        var emptyTitle = new TextBlock { Text = "No artifacts yet", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var emptyHint = new TextBlock
        {
            Text = "Ask any model to build an app, a diagram or a document. It opens here, with every version kept.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        emptyGallery = TabulaButtons.Text("All artifacts", () => state.OpenGallery(true), PaneGlyphKind.Artifacts);
        emptyGallery.HorizontalAlignment = HorizontalAlignment.Center;
        empty = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 0), Children = { emptyTitle, emptyHint, emptyGallery }, IsVisible = false };
        Grid.SetRowSpan(empty, 5);
        Grid.SetRowSpan(gallery, 5);

        strip.VersionPicked += version => state.ShowVersion(artifact is { } a && version == a.Current ? null : version);
        strip.ComparePicked += version => state.SetCompare(version);
        code.FileChosen += path => state.SetCodeFile(path);

        Children.Add(strip);
        Children.Add(viewBar);
        Children.Add(notice);
        Children.Add(well);
        Children.Add(ask);
        Children.Add(empty);
        Children.Add(gallery);
        Children.Add(menus);
        Cards = new TabulaCards(well, menus, context.Files) { Status = ShowStatus };

        preview.ErrorReported += e => { error = e; noticeFor = Selected(); Render(); };
        preview.LinkRequested += url => { link = url; noticeFor = Selected(); Render(); };
        preview.LibraryMissing += url => { missing ??= url; noticeFor = Selected(); Render(); };
        preview.ModelStatusChanged += OnModelStatus;
        preview.ModelUsed += result => { tokens += result.TotalTokens; Render(); };
        preview.Ready += () =>
        {
            if (error is null) return;
            error = null;
            Render();
        };

        void Paint()
        {
            well.Background = TabulaTokens.Well.Brush(well);
            shield.Foreground = PaneTokens.Text3.Brush(shield);
            sandboxText.Foreground = PaneTokens.Text3.Brush(sandboxText);
            compareText.Foreground = PaneTokens.Text2.Brush(compareText);
            emptyTitle.Foreground = PaneTokens.Text2.Brush(emptyTitle);
            emptyHint.Foreground = PaneTokens.Text3.Brush(emptyHint);
        }
        ActualThemeVariantChanged += (_, _) => Paint();
        Paint();
        AutomationProperties.SetName(this, "Tabula");
        Render();
    }

    /// <summary>The pane's content: this body, its title (with the artifacts menu), subtitle and header actions.</summary>
    public PaneContent Content { get; }

    /// <summary>The consent cards and file sheet over the preview.</summary>
    public TabulaCards Cards { get; }

    public TabulaState State => state;

    public Artifact? Artifact => artifact;

    /// <summary>The version on screen.</summary>
    public int ShownVersion => shownVersion;

    public ArtifactSnapshot? Snapshot => snapshot;

    /// <summary>A create stream drawn as its skeleton (or its text arriving).</summary>
    public ArtifactStream? Creating => creating;

    public bool IsSkeletonShown => skeleton.IsVisible;

    public bool IsEmpty => empty.IsVisible;

    public string? StatusText => status.Shown;

    internal TabulaVersionStrip Strip => strip;

    internal TabulaSegmented Modes => modes;

    internal TabulaSegmented Layouts => layouts;

    internal TabulaPreview Preview => preview;

    internal TabulaCodeView Code => code;

    internal TabulaDiffView Diff => diff;

    internal TabulaSkeleton Skeleton => skeleton;

    internal TabulaAsk Ask => ask;

    internal TabulaGallery Gallery => gallery;

    internal TabulaNotice Notice => notice;

    internal TextBlock SandboxText => sandboxText;

    internal string? CompareText => compareLabel.IsVisible ? compareText.Text : null;

    internal Button EditButton => edit;

    internal Button SaveButton => save;

    internal Popup? Menu => menu;

    /// <summary>Whether the open menu is presented (false once it was dismissed).</summary>
    internal bool IsMenuOpen => menuState?.IsOpen == true;

    /// <summary>The rows of the open menu (its items' titles), for tests and the screen reader.</summary>
    internal IReadOnlyList<PopoverItem> MenuItems => menu?.Child is Control root ? root.GetLogicalDescendants().OfType<PopoverItem>().ToList() : [];

    /// <summary>The pane shows this artifact's page (the bridge asks here).</summary>
    internal bool Shows(ArtifactAddress address) => attached && preview.Mount?.Artifact == address;

    // ---- lifetime ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (attached) return;
        attached = true;
        state.Changed += QueueRender;
        context.Store.Changed += OnStoreChanged;
        if (context.Streams is { } streams)
        {
            streams.Changed += OnStream;
            streams.Ended += OnStreamEnded;
        }
        if (context.Runtime is { } runtime) runtime.Data.GrantsChanged += OnGrants;
        TabulaBridgeHost.Instance.Add(this);
        Render();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (!attached) return;
        attached = false;
        state.Changed -= QueueRender;
        context.Store.Changed -= OnStoreChanged;
        if (context.Streams is { } streams)
        {
            streams.Changed -= OnStream;
            streams.Ended -= OnStreamEnded;
        }
        if (context.Runtime is { } runtime) runtime.Data.GrantsChanged -= OnGrants;
        TabulaBridgeHost.Instance.Remove(this);
        CloseMenu();
    }

    private void QueueRender()
    {
        if (renderQueued) return;
        renderQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            renderQueued = false;
            Render();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void OnStoreChanged(ArtifactChange change) => context.Post(() =>
    {
        if (change.Thread != context.Thread && state.Selected?.Thread != change.Thread && !state.IsGalleryOpen) return;
        if (state.Selected is { } selected && selected.Thread == change.Thread && selected.Id == change.Id && change.Change == ArtifactChangeKind.Updated)
            Landed(change);
        Render();
    });

    // A new version of the shown artifact: the page reloads when it follows the latest, and the change asked here is done.
    private void Landed(ArtifactChange change)
    {
        if (state.PendingAsk is { } pending && pending.Id == change.Id && change.Version > pending.FromVersion)
        {
            state.SetPendingAsk(null);
            context.Streams?.End(change.Thread, AskStreamPrefix + change.Id);
            if (context.Store.Find(change.Thread, change.Id)?.Version(change.Version) is { } version)
                ShowStatus(string.Create(CultureInfo.InvariantCulture, $"v{change.Version} written{By(version.Author)} · “{pending.Prompt}”"));
        }
        if (state.Version is null && preview.Mode == TabulaPreviewMode.Web) preview.Reload();
    }

    private static string By(ArtifactAuthor author) => author.IsYou ? " by you" : author.Model is { Length: > 0 } name ? " by " + name : "";

    private void OnStream(ArtifactStream stream)
    {
        if (stream.Thread != context.Thread) return;
        context.Post(QueueRender);
    }

    private void OnStreamEnded(ArtifactStream stream)
    {
        if (stream.Thread != context.Thread) return;
        context.Post(() =>
        {
            if (stream.Key.StartsWith(AskStreamPrefix, StringComparison.Ordinal) && state.PendingAsk?.Id == stream.Id) state.SetPendingAsk(null);
            QueueRender();
        });
    }

    private void OnGrants(ArtifactAddress address) => context.Post(() =>
    {
        if (preview.Mount?.Artifact == address || (artifact is { } a && a.Thread == address.Thread && a.Id == address.Id)) Render();
    });

    private void OnModelStatus(ArtifactModelStatus value)
    {
        var wasRunning = model?.Running == true;
        model = value;
        if (wasRunning && !value.Running)
        {
            meterUntil = context.Time.GetUtcNow() + MeterLinger;
            _ = Linger();
        }
        Render();

        async Task Linger()
        {
            if (!await FrameClock.TrySleep(MeterLinger)) return;
            Render();
        }
    }

    // ---- what is shown ----

    private (Guid Thread, string Id)? Selected() => state.Selected is { } s ? (s.Thread, s.Id) : null;

    /// <summary>Draws the state: the gallery, a skeleton, the empty pane, or an artifact at a version in a view.</summary>
    public void Render()
    {
        CloseMenuIfStale();
        if (state.IsGalleryOpen)
        {
            ShowGallery();
            return;
        }
        gallery.IsVisible = false;
        var streams = context.Streams?.Of(context.Thread) ?? [];
        creating = streams.LastOrDefault(s => !s.IsUpdate && !s.Key.StartsWith(AskStreamPrefix, StringComparison.Ordinal)
            && (s.Id is null || !context.Store.Exists(context.Thread, s.Id)));
        if (creating is { } stream)
        {
            ShowCreating(stream);
            return;
        }
        artifact = state.Selected is { } selected ? context.Store.Find(selected.Thread, selected.Id) : null;
        if (artifact is not { } shown)
        {
            ShowEmpty();
            return;
        }
        updating = streams.LastOrDefault(s => s.IsUpdate && s.Id == shown.Id && s.Thread == shown.Thread && !s.Key.StartsWith(AskStreamPrefix, StringComparison.Ordinal));
        ShowArtifact(shown);
    }

    private void Rows(bool artifactRows)
    {
        strip.IsVisible = artifactRows;
        viewBar.IsVisible = artifactRows || creating is not null;
        well.IsVisible = artifactRows || creating is not null;
        empty.IsVisible = !artifactRows && creating is null;
    }

    private void ShowGallery()
    {
        strip.IsVisible = false;
        viewBar.IsVisible = false;
        notice.Hide();
        well.IsVisible = false;
        empty.IsVisible = false;
        ask.SetOpen(false, null);
        preview.SetActive(false);
        gallery.IsVisible = true;
        gallery.Refresh();
        Header("All artifacts", null, [], "gallery");
    }

    private void ShowEmpty()
    {
        snapshot = null;
        preview.Clear();
        notice.Hide();
        HideSkeleton();
        code.IsVisible = false;
        diff.IsVisible = false;
        ask.SetOpen(false, null);
        Rows(artifactRows: false);
        emptyGallery.IsVisible = context.Store.Gallery(context.ProjectThreads()).Count > 0;
        Header("Tabula", null, [], "empty");
    }

    private void ShowCreating(ArtifactStream stream)
    {
        artifact = null;
        snapshot = null;
        notice.Hide();
        ask.SetOpen(false, null);
        Rows(artifactRows: false);
        strip.IsVisible = false;
        sandboxText.Text = TabulaTokens.SandboxText;
        modes.SetEnabled(TabulaViewMode.Diff, false);
        var view = state.View == TabulaViewMode.Diff ? TabulaViewMode.Preview : state.View;
        modes.Choose(view);
        compareLabel.IsVisible = false;
        layouts.IsVisible = false;
        EditButtons(false);
        var kind = stream.Kind;
        var kindName = kind is { } k ? ArtifactKinds.DisplayName(k) : null;
        Header(stream.Title ?? "New artifact", kindName is null ? "writing" : kindName + " · writing", [], "creating:" + stream.Key);
        diff.IsVisible = false;
        if (view == TabulaViewMode.Code)
        {
            preview.SetActive(false);
            preview.IsVisible = false;
            HideSkeleton();
            code.IsVisible = true;
            code.Show(stream.Content, kind is { } streamKind ? TabulaPreview.LanguageOf(streamKind, null) : null);
            return;
        }
        code.IsVisible = false;
        preview.IsVisible = true;
        // Preview shows a skeleton until the text can be drawn: Markdown, nth-ui and code as they arrive; a page once it exists.
        if (kind is { } drawn && stream.Content.Length > 0 && drawn is ArtifactKind.Markdown or ArtifactKind.NthUi or ArtifactKind.Code && preview.ShowStream(drawn, stream.Content, null))
        {
            preview.SetActive(true);
            HideSkeleton();
            return;
        }
        preview.Clear();
        preview.SetActive(false);
        ShowSkeleton();
    }

    private void ShowArtifact(Artifact shown)
    {
        var version = state.Version is { } wanted && shown.Version(wanted) is not null ? wanted : shown.Current;
        if (noticeFor != (shown.Thread, shown.Id))
        {
            error = null;
            link = null;
            missing = null;
            noticeFor = (shown.Thread, shown.Id);
        }
        if (shownVersion != version || snapshot is null || snapshot.Artifact.Id != shown.Id || snapshot.Artifact.Thread != shown.Thread
            || snapshot.Artifact.Current != shown.Current || snapshot.Version.Number != version)
        {
            snapshot = Read(shown.Thread, shown.Id, version);
            shownVersion = version;
        }
        if (snapshot is not { } current)
        {
            ShowEmpty();
            return;
        }
        Rows(artifactRows: true);
        var readOnly = state.IsReadOnly;
        var pending = state.PendingAsk is { } ask1 && ask1.Id == shown.Id && !readOnly ? ask1 : null;
        var view = state.View == TabulaViewMode.Diff && shown.Versions.Count < 2 ? TabulaViewMode.Preview : state.View;
        var compare = CompareVersion(shown, version);
        strip.Show(shown, version, compare, view == TabulaViewMode.Diff, pending);
        modes.SetEnabled(TabulaViewMode.Diff, shown.Versions.Count > 1);
        modes.Choose(view);
        sandboxText.Text = SandboxWords(current);
        compareLabel.IsVisible = view == TabulaViewMode.Diff;
        layouts.IsVisible = view == TabulaViewMode.Diff;
        layouts.Choose(state.DiffLayout);
        compareText.Text = string.Create(CultureInfo.InvariantCulture, $"Comparing v{compare} → v{version}");
        AutomationProperties.SetName(compareLabel, compareText.Text + ". Choose the version to compare with");
        HideSkeleton();

        switch (view)
        {
            case TabulaViewMode.Preview:
                if (editing) EndEdit();
                code.IsVisible = false;
                diff.IsVisible = false;
                preview.IsVisible = true;
                preview.Show(current, state.Version);
                // A version being written of what is shown, while the latest is followed, draws as it arrives (6.4).
                if (updating is { Content.Length: > 0 } writing && state.Version is null) preview.ShowStream(shown.Kind, writing.Content, shown.Language);
                preview.SetActive(true);
                EditButtons(false);
                break;
            case TabulaViewMode.Code:
                preview.SetActive(false);
                preview.IsVisible = false;
                diff.IsVisible = false;
                code.IsVisible = true;
                ShowCode(current, shown);
                EditButtons(!readOnly && updating is null);
                break;
            default:
                if (editing) EndEdit();
                preview.SetActive(false);
                preview.IsVisible = false;
                code.IsVisible = false;
                diff.IsVisible = true;
                if (Read(shown.Thread, shown.Id, compare) is { } before) diff.Show(before, current, state.DiffLayout);
                EditButtons(false);
                break;
        }

        ShowNotice(shown, version, readOnly);
        ask.SetOpen(state.IsAskOpen && !readOnly, state.AskSelection);
        if (ask.Text != state.AskText) ask.Text = state.AskText;
        Header(shown.Title, Subtitle(shown, readOnly), Actions(shown, version, readOnly),
            string.Create(CultureInfo.InvariantCulture, $"{shown.Thread}/{shown.Id}/{version}/{readOnly}"));
    }

    private ArtifactSnapshot? Read(Guid thread, string id, int version)
    {
        try
        {
            return context.Store.Read(thread, id, version);
        }
        catch (ArtifactException)
        {
            return null;
        }
    }

    /// <summary>The version Diff compares with: the chosen one, else the one before the shown one (the one after for v1).</summary>
    private int CompareVersion(Artifact shown, int version)
    {
        if (state.Compare is { } chosen && chosen != version && shown.Version(chosen) is not null) return chosen;
        var numbers = shown.Versions.Select(v => v.Number).ToList();
        var at = numbers.IndexOf(version);
        return at > 0 ? numbers[at - 1] : numbers.Count > 1 ? numbers[1] : version;
    }

    private void ShowCode(ArtifactSnapshot current, Artifact shown)
    {
        if (editing) return;
        var following = state.Version is null;
        if (updating is { Content.Length: > 0 } writing && following && current.Files is null)
        {
            code.Show(writing.Content, TabulaPreview.LanguageOf(shown.Kind, shown.Language));
            return;
        }
        if (current.Files is { } files)
        {
            var entry = current.Version.Entry ?? ArtifactStore.DefaultEntry;
            var paths = files.Keys.OrderBy(p => string.Equals(p, entry, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var file = state.CodeFile is { } chosen && files.ContainsKey(chosen) ? chosen : paths.FirstOrDefault() ?? entry;
            code.Show(files.TryGetValue(file, out var text) ? text : "", CodeHighlight.LanguageOfPath(file), paths, file);
            return;
        }
        code.Show(current.Text ?? "", TabulaPreview.LanguageOf(shown.Kind, shown.Language));
    }

    private string SandboxWords(ArtifactSnapshot current)
    {
        if (context.Runtime is { } runtime && runtime.AllowedNetwork(current) is { Count: > 0 } sites)
            return "Sandboxed · no files · " + (sites.Count == 1 ? "network: " + sites[0] : string.Create(CultureInfo.InvariantCulture, $"network: {sites.Count} sites"));
        return TabulaTokens.SandboxText;
    }

    private string Subtitle(Artifact shown, bool readOnly)
    {
        var parts = new List<string> { TabulaTokens.KindName(shown) };
        var now = context.Time.GetUtcNow();
        if (model is { IsPaused: true }) parts.Add(ArtifactModelMeter.PausedMessage);
        else if (model is { Running: true } || meterUntil is { } until && now < until)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"model · {tokens:N0} tokens"));
        if (readOnly) parts.Add("read-only");
        return string.Join(" · ", parts);
    }

    private IReadOnlyList<PaneHeaderAction> Actions(Artifact shown, int version, bool readOnly)
    {
        var chip = string.Create(CultureInfo.InvariantCulture, $"v{version}");
        return
        [
            new PaneHeaderAction("Version " + chip.TrimStart('v') + ", choose a version", () => OpenVersionMenu(0), Text: chip, Menu: true),
            readOnly
                ? new PaneHeaderAction("Copy into this thread", CopyIntoThread, Glyph: PaneGlyphKind.Plus, Text: "Copy into this thread")
                : new PaneHeaderAction("Ask to change", ToggleAsk, Glyph: PaneGlyphKind.Spark, Text: "Ask to change"),
            new PaneHeaderAction("Copy", () => _ = Copy(), Glyph: PaneGlyphKind.Copy),
            new PaneHeaderAction("Save to project", () => _ = SaveToProject(), Glyph: PaneGlyphKind.Save),
            new PaneHeaderAction("Open in Visus", OpenInVisus, Glyph: PaneGlyphKind.OpenExternal),
            new PaneHeaderAction("Export", () => OpenExportMenu(5), Glyph: PaneGlyphKind.Export, Menu: true),
        ];
    }

    private void Header(string title, string? subtitle, IReadOnlyList<PaneHeaderAction> actions, string key)
    {
        var full = key + "|" + title + "|" + subtitle;
        if (full == headerKey) return;
        // Only the words changed (the meter): the actions stay, so the row does not rebuild.
        var sameActions = headerKey is { } old && old.Split('|')[0] == key;
        headerKey = full;
        Content.Update(title, subtitle, sameActions ? null : actions);
    }

    private void ShowNotice(Artifact shown, int version, bool readOnly)
    {
        if (error is { } e)
        {
            var buttons = readOnly ? [] : new (string, Action)[] { ("Ask to fix", () => AskToFix(e)) };
            notice.Show("This artifact hit an error", e.Message, error: true, buttons);
        }
        else if (link is { } url)
            notice.Show(ArtifactMount.OpenLinkTitle, Redact(url), error: false, ("Open", () => { link = null; context.OpenLink(url); Render(); }));
        else if (missing is { } library)
            notice.Show("This artifact uses a library nth has no offline copy of", Redact(library), error: false);
        else if (readOnly)
            notice.Show(string.Create(CultureInfo.InvariantCulture, $"From “{context.ThreadName(shown.Thread) ?? "another thread"}”"),
                "Read-only here. Copy it into this thread to change it.", error: false, ("Copy into this thread", CopyIntoThread));
        else notice.Hide();
    }

    /// <summary>An address as the notice shows it: scheme, host and path, never its query (it may carry the page's data).</summary>
    internal static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Path) : url.Split('?', '#')[0];

    private void DismissNotice()
    {
        if (error is not null) error = null;
        else if (link is not null) link = null;
        else if (missing is not null) missing = "";
        notice.Hide();
        if (missing == "") missing = null;
        Render();
    }

    private void EditButtons(bool canEdit)
    {
        edit.IsVisible = canEdit && !editing;
        save.IsVisible = editing;
        cancel.IsVisible = editing;
    }

    private void ShowSkeleton()
    {
        if (skeleton.IsVisible && skeletonFade.Opacity > 0.99) return;
        skeleton.IsVisible = true;
        skeletonFade.To(1, 0);
    }

    private void HideSkeleton()
    {
        if (!skeleton.IsVisible) return;
        skeletonFade.To(0, 0, () =>
        {
            if (skeletonFade.Opacity <= 0.001) skeleton.IsVisible = false;
        });
    }

    // ---- the view bar ----

    private void SetView(TabulaViewMode view)
    {
        if (view == TabulaViewMode.Diff && artifact is { Versions.Count: < 2 }) return;
        state.SetView(view);
    }

    private void BeginEdit()
    {
        if (snapshot is null || state.IsReadOnly) return;
        editing = true;
        code.BeginEdit();
        EditButtons(true);
    }

    private void CancelEdit() => EndEdit();

    private void EndEdit()
    {
        editing = false;
        code.EndEdit();
        EditButtons(state.View == TabulaViewMode.Code && !state.IsReadOnly);
    }

    private void SaveEdit()
    {
        if (artifact is not { } shown || snapshot is not { } current) return;
        var text = code.EditedText;
        try
        {
            var revision = current.Files is not null && code.File is { } file
                ? new ArtifactRevision { Files = new Dictionary<string, string?> { [file] = text } }
                : new ArtifactRevision { Content = text };
            if (current.Version.Number != shown.Current && current.Files is null) revision = new ArtifactRevision { Content = text };
            var saved = context.Store.Update(shown.Thread, shown.Id, revision, ArtifactAuthor.You, null);
            EndEdit();
            state.ShowVersion(null);
            ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Saved as v{saved.Version.Number}"));
        }
        catch (ArtifactException failure)
        {
            ShowStatus(failure.Message);
        }
    }

    // ---- header actions ----

    private void ToggleAsk()
    {
        if (state.IsAskOpen)
        {
            state.OpenAsk(false);
            return;
        }
        var selection = state.View == TabulaViewMode.Code ? code.SelectedText : preview.SelectedText();
        state.OpenAsk(true, selection);
    }

    private void SendAsk(string text)
    {
        if (artifact is not { } shown || state.IsReadOnly) return;
        context.SendTurn(TabulaPane.AskMessage(shown, shownVersion, text, state.AskSelection));
        state.SetPendingAsk(new TabulaPendingAsk(shown.Id, shown.Current, text, context.Time.GetUtcNow()));
        // The change ends with its turn (the fenced filter ends a chat's streams then) or when its version lands.
        context.Streams?.Begin(shown.Thread, AskStreamPrefix + shown.Id, shown.Id, shown.Title, shown.Kind, isUpdate: true);
        ask.Text = "";
        state.AskText = "";
        state.OpenAsk(false);
        if (state.View != TabulaViewMode.Preview) state.SetView(TabulaViewMode.Preview);
        if (state.Version is not null) state.ShowVersion(null);
    }

    private void AskToFix(ArtifactRuntimeError e)
    {
        if (artifact is not { } shown) return;
        context.SendTurn(TabulaPane.FixMessage(shown, shownVersion, e));
        error = null;
        ShowStatus("Asked the chat to fix it");
        Render();
    }

    /// <summary>The words a copied kind is called in the status ("Copied v4 · HTML · 6.2 KB").</summary>
    public static string FormatWord(Artifact artifact, string? file = null) => artifact.Kind switch
    {
        ArtifactKind.Html => "HTML",
        ArtifactKind.React => artifact.Language is "tsx" ? "TSX" : "JSX",
        ArtifactKind.Svg => "SVG",
        ArtifactKind.Mermaid => "Mermaid",
        ArtifactKind.Markdown => "Markdown",
        ArtifactKind.NthUi => "nth-ui",
        ArtifactKind.Project => file is null ? "project" : Path.GetFileName(file),
        _ => artifact.Language is { Length: > 0 } language ? language : "text",
    };

    /// <summary>Copy (6.3): the content; a project copies the file the Code view shows, else its entry.</summary>
    private async Task Copy()
    {
        if (artifact is not { } shown || snapshot is not { } current) return;
        string text;
        string? file = null;
        if (current.Files is { } files)
        {
            file = state.View == TabulaViewMode.Code && code.File is { } chosen ? chosen : current.Version.Entry ?? ArtifactStore.DefaultEntry;
            text = files.TryGetValue(file, out var found) ? found : "";
        }
        else text = current.Text ?? "";
        if (!await context.Clipboard.SetTextAsync(this, text)) return;
        ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Copied v{shownVersion} · {FormatWord(shown, file)} · {TabulaTokens.Size(Encoding.UTF8.GetByteCount(text))}"));
    }

    /// <summary>Copy as image (6.3: svg, mermaid and rendered views).</summary>
    private async Task CopyImage()
    {
        if (state.View != TabulaViewMode.Preview || !preview.IsActive)
        {
            state.SetView(TabulaViewMode.Preview);
            Render();
        }
        var png = await preview.CaptureAsync(CancellationToken.None);
        if (png is null || !await context.Clipboard.SetImageAsync(this, png))
        {
            ShowStatus("The preview could not be copied as an image");
            return;
        }
        ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Copied v{shownVersion} as an image"));
    }

    private static bool CopiesAsImage(Artifact shown) => shown.Kind is ArtifactKind.Svg or ArtifactKind.Mermaid or ArtifactKind.Html or ArtifactKind.React
        or ArtifactKind.Project or ArtifactKind.Markdown or ArtifactKind.NthUi;

    /// <summary>Save to project (6.3): a save dialog in the project, "artifacts/&lt;title&gt;.&lt;ext&gt;" by default; a project saves as its folder.</summary>
    private async Task SaveToProject()
    {
        if (artifact is not { } shown || snapshot is not { } current) return;
        var folder = context.ProjectFolder();
        var relative = ArtifactExport.SaveToProjectPath(shown);
        var start = folder is null ? null : Directory.Exists(Path.Combine(folder, "artifacts")) ? Path.Combine(folder, "artifacts") : folder;
        var isProject = current.Files is not null;
        var extension = isProject ? "" : ArtifactKinds.Extension(shown.Kind, shown.Language);
        var path = await context.Files.SaveAsync(this, new TabulaSaveRequest("Save to project", start, Path.GetFileName(relative), FormatWord(shown) + " file", extension, isProject));
        if (path is null) return;
        try
        {
            if (current.Files is { } files)
            {
                Directory.CreateDirectory(path);
                var root = Path.GetFullPath(path);
                foreach (var (name, text) in files)
                {
                    var target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    FileOps.WriteAtomic(target, Encoding.UTF8.GetBytes(text));
                }
            }
            else
            {
                if (Path.GetDirectoryName(path) is { } parent) Directory.CreateDirectory(parent);
                FileOps.WriteAtomic(path, Encoding.UTF8.GetBytes(current.Text ?? ""));
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Could not save: " + failure.Message);
            return;
        }
        ShowStatus(SavedWords(folder, path));
    }

    /// <summary>"Saved to my-app/artifacts/pricing-calculator.html · it shows in Changes" (canvas), or the full path outside the project.</summary>
    public static string SavedWords(string? projectFolder, string path)
    {
        if (projectFolder is { } project)
        {
            var root = Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Saved to " + Path.GetFileName(root) + "/" + full[(root.Length + 1)..].Replace('\\', '/') + " · it shows in Changes";
        }
        return "Saved to " + path;
    }

    private void OpenInVisus()
    {
        if (snapshot is not { } current) return;
        if (context.OpenInVisus is { } open)
        {
            open(current);
            ShowStatus("Opened in a Visus tab at " + SandboxSites.ArtifactHost(current.Artifact.Thread, current.Artifact.Id) + ", its own sandboxed site");
            return;
        }
        context.Expand();
        ShowStatus("Opened full size");
    }

    private void CopyIntoThread()
    {
        if (artifact is not { } shown || !state.IsReadOnly) return;
        try
        {
            var copy = context.Store.Fork(shown.Thread, shown.Id, shownVersion, context.Thread, ArtifactAuthor.You);
            state.Select(new TabulaRef(context.Thread, copy.Artifact.Id));
            ShowStatus("Copied into this thread as “" + copy.Artifact.Title + "”");
        }
        catch (ArtifactException failure)
        {
            ShowStatus(failure.Message);
        }
    }

    private void Restore(int version)
    {
        if (artifact is not { } shown || state.IsReadOnly) return;
        try
        {
            var restored = context.Store.Restore(shown.Thread, shown.Id, version, ArtifactAuthor.You);
            state.ShowVersion(null);
            ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Restored v{version} as v{restored.Version.Number}"));
        }
        catch (ArtifactException failure)
        {
            ShowStatus(failure.Message);
        }
    }

    private void Remix(int version)
    {
        if (artifact is not { } shown) return;
        try
        {
            var remix = context.Store.Fork(shown.Thread, shown.Id, version, context.Thread, ArtifactAuthor.You, shown.Title + " remix");
            state.Select(new TabulaRef(context.Thread, remix.Artifact.Id));
            ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Remixed v{version} into “{remix.Artifact.Title}”"));
        }
        catch (ArtifactException failure)
        {
            ShowStatus(failure.Message);
        }
    }

    /// <summary>Export (6.3) in one format: a save dialog, then the exporter; a format that needs the page uses the shown one or a hidden one.</summary>
    private async Task Export(ArtifactExportFormat format)
    {
        if (artifact is not { } shown || snapshot is not { } current) return;
        if (context.Runtime is not { } runtime)
        {
            ShowStatus("Export needs the artifacts runtime");
            return;
        }
        var name = ArtifactExport.FileName(shown, format, shownVersion);
        var extension = ArtifactExport.Extension(shown, format);
        var path = await context.Files.SaveAsync(this, new TabulaSaveRequest("Export " + shown.Title, null, name, FormatName(format), extension));
        if (path is null) return;
        bool done;
        try
        {
            done = await ExportTo(runtime, current, format, path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or ArtifactException)
        {
            ShowStatus("Could not export: " + failure.Message);
            return;
        }
        ShowStatus(done ? "Exported " + Path.GetFileName(path) : "The export did not finish");
    }

    private async Task<bool> ExportTo(ArtifactRuntime runtime, ArtifactSnapshot current, ArtifactExportFormat format, string path)
    {
        var kind = current.Artifact.Kind;
        if (!ArtifactExporter.NeedsView(kind, format))
        {
            FileOps.WriteAtomic(path, runtime.Exporter.Export(current, format));
            return true;
        }
        // A native view draws its own picture.
        if (format == ArtifactExportFormat.Png && kind is ArtifactKind.NthUi or ArtifactKind.Markdown or ArtifactKind.Code)
        {
            if (state.View != TabulaViewMode.Preview) { state.SetView(TabulaViewMode.Preview); Render(); }
            if (await preview.CaptureAsync(CancellationToken.None) is not { } png) return false;
            FileOps.WriteAtomic(path, png);
            return true;
        }
        // The page on screen when it shows this version (a capture needs it visible; printing does not).
        if (preview.Mode == TabulaPreviewMode.Web && preview.Mount?.Version == state.Version && preview.View?.Engine is { } engine
            && (format != ArtifactExportFormat.Png || preview.IsActive))
            return await runtime.Exporter.ExportAsync(current, format, path, engine, CancellationToken.None);
        using var hidden = await TabulaOffscreen.OpenAsync(context, current, CancellationToken.None);
        return hidden?.Engine is { } offscreen && await runtime.Exporter.ExportAsync(current, format, path, offscreen, CancellationToken.None);
    }

    /// <summary>A format's name in its menu row and dialog.</summary>
    public static string FormatName(ArtifactExportFormat format) => format switch
    {
        ArtifactExportFormat.Html => "Standalone HTML",
        ArtifactExportFormat.Markdown => "Markdown",
        ArtifactExportFormat.Pdf => "PDF",
        ArtifactExportFormat.Docx => "Word document",
        ArtifactExportFormat.Png => "PNG image",
        ArtifactExportFormat.Svg => "SVG image",
        ArtifactExportFormat.Zip => "Folder as .zip",
        _ => "Source file",
    };

    // ---- menus (popups: their own windows, above the page) ----

    private Control AnchorFor(int action)
    {
        if (this.FindAncestorOfType<PaneCard>()?.Header is { } header)
        {
            if (action >= 0 && action < header.ActionViews.Count && header.ActionViews[action].IsVisible) return header.ActionViews[action];
            if (action >= 0 && header.MoreButton.IsVisible) return header.MoreButton;
            if (action == -1 && header.TitleButton is { } title) return title;
        }
        return action == -2 ? sandbox : action == -3 ? compareLabel : viewBar;
    }

    private void OpenMenu(Control anchor, Func<Control> build)
    {
        CloseMenu();
        // The menu is presented the app's way (its glass card, its motion, Escape and light dismiss): the popover follows
        // an observable flag, and the popup leaves the panel once it has closed.
        var presented = new TabulaMenuState();
        Popup? shown = null;
        shown = PlainControls.Popover(anchor, new Binding<bool>(() => presented.IsOpen, value => presented.IsOpen = value), () => new PopoverMenu(build));
        shown.Closed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (presented.IsOpen || shown is null) return;
            menus.Children.Remove(shown);
            if (menu == shown)
            {
                menu = null;
                menuState = null;
            }
        });
        menus.Children.Add(shown);
        menu = shown;
        menuState = presented;
        presented.IsOpen = true;
    }

    internal void CloseMenu()
    {
        if (menu is not { } shown) return;
        var presented = menuState;
        menu = null;
        menuState = null;
        if (presented is { IsOpen: true } && shown.IsOpen) presented.IsOpen = false; // closes with its motion; Closed removes it
        else
        {
            if (presented is not null) presented.IsOpen = false;
            menus.Children.Remove(shown);
        }
    }

    private void CloseMenuIfStale()
    {
        if (menuState is { IsOpen: false }) CloseMenu();
    }

    private Action Then(Action action) => () =>
    {
        CloseMenu();
        action();
    };

    /// <summary>The title's menu (6.1): this thread's artifacts, newest first, then "All artifacts".</summary>
    private void OpenTitleMenu() => OpenMenu(AnchorFor(-1), () =>
    {
        var stack = new StackPanel();
        var mine = context.Store.List(context.Thread).OrderByDescending(a => a.Updated).ToList();
        if (mine.Count > 0) stack.Children.Add(new PopoverSectionHeader("In this thread"));
        foreach (var item in mine)
        {
            var target = new TabulaRef(item.Thread, item.Id);
            stack.Children.Add(new PopoverItem(item.Title, Then(() => state.Select(target)),
                detail: TabulaTokens.KindName(item) + " · v" + item.Current.ToString(CultureInfo.InvariantCulture), isChecked: state.Selected == target));
        }
        if (mine.Count > 0) stack.Children.Add(new PopoverDivider());
        stack.Children.Add(new PopoverItem("All artifacts", Then(() => state.OpenGallery(true))));
        return stack;
    });

    /// <summary>The version chip's menu (6.3): every version with its prompt, then Compare, Restore and Remix.</summary>
    private void OpenVersionMenu(int action)
    {
        if (artifact is not { } shown) return;
        OpenMenu(AnchorFor(action), () =>
        {
            var stack = new StackPanel();
            foreach (var version in shown.Versions.Reverse())
            {
                var number = version.Number;
                var label = number == shown.Current ? string.Create(CultureInfo.InvariantCulture, $"v{number} · latest") : string.Create(CultureInfo.InvariantCulture, $"v{number}");
                stack.Children.Add(new PopoverItem(label, Then(() => state.ShowVersion(number == shown.Current ? null : number)),
                    detail: TabulaVersionStrip.Caption(version), isChecked: number == shownVersion));
            }
            stack.Children.Add(new PopoverDivider());
            if (shown.Versions.Count > 1)
                stack.Children.Add(new PopoverItem("Compare with another version", Then(() => state.SetView(TabulaViewMode.Diff))));
            if (shownVersion != shown.Current && !state.IsReadOnly)
                stack.Children.Add(new PopoverItem(string.Create(CultureInfo.InvariantCulture, $"Restore v{shownVersion} as the latest"), Then(() => Restore(shownVersion))));
            stack.Children.Add(new PopoverItem(string.Create(CultureInfo.InvariantCulture, $"Remix v{shownVersion}"), Then(() => Remix(shownVersion)),
                detail: "A new artifact from this version"));
            return stack;
        });
    }

    /// <summary>The compare label's menu: the version to compare the shown one with.</summary>
    private void OpenCompareMenu()
    {
        if (artifact is not { } shown) return;
        OpenMenu(AnchorFor(-3), () =>
        {
            var stack = new StackPanel();
            stack.Children.Add(new PopoverSectionHeader(string.Create(CultureInfo.InvariantCulture, $"Compare v{shownVersion} with")));
            var compare = CompareVersion(shown, shownVersion);
            foreach (var version in shown.Versions.Reverse().Where(v => v.Number != shownVersion))
            {
                var number = version.Number;
                stack.Children.Add(new PopoverItem(string.Create(CultureInfo.InvariantCulture, $"v{number}"), Then(() => state.SetCompare(number)),
                    detail: TabulaVersionStrip.Caption(version), isChecked: number == compare));
            }
            return stack;
        });
    }

    /// <summary>Export's menu (6.3): Copy as image where it applies, then each format of the kind.</summary>
    private void OpenExportMenu(int action)
    {
        if (artifact is not { } shown) return;
        OpenMenu(AnchorFor(action), () =>
        {
            var stack = new StackPanel();
            if (CopiesAsImage(shown)) stack.Children.Add(new PopoverItem("Copy as image", Then(() => _ = CopyImage())));
            if (stack.Children.Count > 0) stack.Children.Add(new PopoverDivider());
            stack.Children.Add(new PopoverSectionHeader("Export"));
            foreach (var format in ArtifactExport.Formats(shown.Kind))
            {
                var chosen = format;
                stack.Children.Add(new PopoverItem(FormatName(format), Then(() => _ = Export(chosen)), detail: "." + ArtifactExport.Extension(shown, format)));
            }
            return stack;
        });
    }

    /// <summary>The artifact's menu on the sandbox line (6.5): network, and the grants with Remove.</summary>
    private void OpenArtifactMenu()
    {
        if (artifact is not { } shown || snapshot is not { } current || context.Runtime is not { } runtime) return;
        var address = new ArtifactAddress(shown.Thread, shown.Id);
        OpenMenu(AnchorFor(-2), () =>
        {
            var stack = new StackPanel();
            stack.Children.Add(new PopoverSectionHeader("This artifact"));
            var allowed = runtime.AllowedNetwork(current);
            if (allowed.Count > 0)
                stack.Children.Add(new PopoverItem("Turn off network", Then(() => runtime.Data.AllowNetwork(address, null)), detail: string.Join(", ", allowed)));
            else
                stack.Children.Add(new PopoverItem("Allow network…", Then(() => _ = AllowNetwork(runtime, current, address)), detail: "Off", isEnabled: !state.IsReadOnly));
            var grants = runtime.Data.Grants(address);
            if (grants.Model || grants.Tools.Count > 0)
            {
                stack.Children.Add(new PopoverDivider());
                stack.Children.Add(new PopoverSectionHeader("Allowed"));
                if (grants.Model) stack.Children.Add(new PopoverItem("Your model", Then(() => runtime.Data.AllowModel(address, false)), detail: "Remove"));
                foreach (var tool in grants.Tools)
                {
                    var granted = tool;
                    stack.Children.Add(new PopoverItem(granted.Server + " · " + granted.Tool, Then(() => runtime.Data.RevokeTool(address, granted.Server, granted.Tool)), detail: "Remove"));
                }
            }
            return stack;
        });
    }

    private async Task AllowNetwork(ArtifactRuntime runtime, ArtifactSnapshot current, ArtifactAddress address)
    {
        var request = runtime.NetworkRequest(current);
        if (request.Hosts.Count == 0)
        {
            ShowStatus(request.Dropped.Count > 0 ? "None of the sites this version names can be allowed" : "This version names no sites to reach");
            return;
        }
        if (await Cards.AskNetworkAsync(request, CancellationToken.None)) runtime.Data.AllowNetwork(address, request.HostNames);
        else ShowStatus("Network stays off");
    }

    // ---- the bridge ----

    internal void DraftFromArtifact(string text)
    {
        context.Draft(text);
        ShowStatus("Added to your message. Send it when you are ready.");
    }

    internal void ShowStatus(string text) => status.Show(text);
}

/// <summary>Whether a Tabula menu is presented: observable, so the popover presents and dismisses it as the app's others.</summary>
internal sealed class TabulaMenuState : ObservableModel
{
    private bool isOpen;

    public bool IsOpen
    {
        get => Read(isOpen);
        set => Set(ref isOpen, value);
    }
}
