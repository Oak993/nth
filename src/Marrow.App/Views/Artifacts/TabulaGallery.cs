using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, Tabula's gallery (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1 "its last row 'All
// artifacts' opens a gallery page (a Tabula pane view, not a window) with every artifact in the project, or in all threads
// (a segmented filter: This project / All threads), searchable by title and kind, newest first, with the thread name on each
// card. Opening one from another thread opens it read-only with 'Copy into this thread'"; ED-NEW-web-w14-1). No Swift
// counterpart. A page inside the pane: Back, the search field, the filter, and one card per artifact.

namespace Marrow.App.Views.Artifacts;

public sealed class TabulaGallery : Grid
{
    private readonly TabulaContext context;
    private readonly TextBox search = new()
    {
        PlaceholderText = "Search artifacts",
        FontSize = 12.5,
        BorderThickness = default,
        Background = Brushes.Transparent,
        VerticalAlignment = VerticalAlignment.Center,
        MinHeight = 0,
        Padding = new Thickness(0, 4),
    };
    private readonly TabulaSegmented scope = new("Show artifacts of");
    private readonly StackPanel cards = new() { Spacing = 6, Margin = new Thickness(12, 4, 12, 14) };
    private readonly TextBlock empty = new() { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 40, 16, 0), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button back;
    private bool updating;

    public TabulaGallery(TabulaContext context)
    {
        this.context = context;
        RowDefinitions = new RowDefinitions("Auto,Auto,*");
        back = TabulaButtons.Text("Back", () => context.State.OpenGallery(false), PaneGlyphKind.Back, "Back to the artifact");
        scope.Add("This project", TabulaGalleryScope.ThisProject);
        scope.Add("All threads", TabulaGalleryScope.AllThreads);
        scope.Picked += value => context.State.SetGalleryScope((TabulaGalleryScope)value);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8, 8, 12, 4), ColumnSpacing = 8 };
        top.Children.Add(back);
        Grid.SetColumn(scope, 2);
        scope.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(scope);
        var glass = new PaneGlyph(PaneGlyphKind.Search, 13) { VerticalAlignment = VerticalAlignment.Center };
        var field = new Border { Height = 32, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0), Margin = new Thickness(12, 2, 12, 6), BorderThickness = new Thickness(1) };
        var fieldRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { glass } };
        Grid.SetColumn(search, 1);
        fieldRow.Children.Add(search);
        field.Child = fieldRow;
        Grid.SetRow(field, 1);
        var scroll = new ScrollViewer { Content = new StackPanel { Children = { cards, empty } }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        Children.Add(top);
        Children.Add(field);
        Children.Add(scroll);
        void Paint()
        {
            glass.Foreground = PaneTokens.Text3.Brush(glass);
            field.Background = TabulaTokens.InfoFill.Brush(field);
            field.BorderBrush = PaneTokens.Line.Brush(field);
            search.Foreground = PaneTokens.Text1.Brush(search);
            empty.Foreground = PaneTokens.Text2.Brush(empty);
        }
        ActualThemeVariantChanged += (_, _) => { Paint(); Refresh(); };
        Paint();
        search.TextChanged += (_, _) =>
        {
            if (!updating) context.State.SetGallerySearch(search.Text ?? "");
        };
        AutomationProperties.SetName(search, "Search artifacts by title or kind");
        AutomationProperties.SetName(cards, "Artifacts");
        cards.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.List);
    }

    /// <summary>The artifacts listed now, in order.</summary>
    public IReadOnlyList<Artifact> Shown { get; private set; } = [];

    internal IReadOnlyList<Button> Cards => cards.Children.OfType<Button>().ToList();

    internal TextBox Search => search;

    internal TabulaSegmented Scope => scope;

    internal string? EmptyText => empty.IsVisible ? empty.Text : null;

    /// <summary>Lists the artifacts for the state's filter and search, newest first.</summary>
    public void Refresh()
    {
        var state = context.State;
        updating = true;
        if ((search.Text ?? "") != state.GallerySearch) search.Text = state.GallerySearch;
        updating = false;
        scope.Choose(state.GalleryScope);
        back.IsVisible = state.Selected is not null;
        var threads = state.GalleryScope == TabulaGalleryScope.ThisProject ? context.ProjectThreads() : (context.AllThreads ?? context.ProjectThreads)();
        Shown = context.Store.Gallery(threads, state.GallerySearch);
        cards.Children.Clear();
        var now = context.Time.GetUtcNow();
        foreach (var artifact in Shown) cards.Children.Add(Card(artifact, now));
        empty.IsVisible = Shown.Count == 0;
        empty.Text = state.GallerySearch.Trim().Length > 0
            ? "No artifact matches “" + state.GallerySearch.Trim() + "”."
            : state.GalleryScope == TabulaGalleryScope.ThisProject ? "No artifacts in this project yet." : "No artifacts yet.";
    }

    private Button Card(Artifact artifact, DateTimeOffset now)
    {
        var mine = artifact.Thread == context.Thread;
        var where = mine ? "This thread" : context.ThreadName(artifact.Thread) ?? "Another thread";
        var title = new TextBlock { Text = artifact.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var kind = new Border { Height = 20, Padding = new Thickness(7, 0), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = TabulaTokens.KindName(artifact), FontFamily = TabulaTokens.Mono, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center } };
        var detail = new TextBlock
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"v{artifact.Current} · {where} · {TabulaTokens.Ago(artifact.Updated, now)}"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { title } };
        Grid.SetColumn(kind, 1);
        head.Children.Add(kind);
        var face = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0.5),
            Child = new StackPanel { Spacing = 3, Children = { head, detail } },
        };
        var button = PlainControls.Button(face, () => context.State.Select(new TabulaRef(artifact.Thread, artifact.Id))).KeyboardFocus(12);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        void Paint(bool hover)
        {
            face.Background = (hover ? PaneTokens.HoverFill : TabulaTokens.InfoFill).Brush(face);
            face.BorderBrush = PaneTokens.Line.Brush(face);
            title.Foreground = PaneTokens.Text1.Brush(title);
            detail.Foreground = PaneTokens.Text2.Brush(detail);
            kind.Background = PaneTokens.ChipFill.Brush(kind);
            if (kind.Child is TextBlock words) words.Foreground = PaneTokens.Text2.Brush(words);
        }
        button.OnHover(Paint);
        Paint(false);
        AutomationProperties.SetName(button, artifact.Title + ", " + TabulaTokens.KindName(artifact) + ", " + detail.Text);
        button.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.ListItem);
        return button;
    }
}
