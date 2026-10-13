using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Port.Web;
using Marrow.Core.Localisation;
using Marrow.Core.NthUi;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

// PORT-ONLY: web surfaces W10, Settings > Tabula (E:/Droppy/work/design/web-surfaces/DESIGN.md 9 "Tabula (artifacts and live answers)": Live answers
// in chat; Visual answers: Fewer / Balanced / More; Inline HTML widgets; MCP Apps; Map street tiles source (none); Artifacts may use your model
// (Ask each artifact / Never); Artifact storage used (per thread, with Clear); 7.2, 7.7; ED-NEW-web-w10-4, -5). No Swift counterpart. A
// self-contained section like the Visus page: the settings and the few things only the app can do (the storage list, its Clear) are parts.

namespace Marrow.App.Views.Settings;

/// <summary>One thread's artifacts as the storage list shows them.</summary>
public sealed record ArtifactStorageRow(Guid Thread, string Title, long Bytes);

/// <summary>What Settings › Tabula does beyond switching settings; the app binds them (<see cref="TabulaSettingsPage.Bound"/>).</summary>
public sealed class TabulaSettingsParts
{
    public required Func<WebSettings> Settings { get; init; }

    /// <summary>The threads that have artifacts and the bytes each takes, read off the UI thread; null: nothing to list.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<ArtifactStorageRow>>>? Storage { get; init; }

    /// <summary>Removes every artifact of a thread; null: not offered here.</summary>
    public Func<Guid, Task>? ClearArtifacts { get; init; }

    /// <summary>Asks before something that cannot be undone: the question, the detail and the verb; true to go on.</summary>
    public Func<string, string, string, Task<bool>> Confirm { get; init; } = static (_, _, _) => Task.FromResult(true);
}

public sealed class TabulaSettingsPage : Decorator
{
    public const bool IsReady = true;

    public const string Title = "Tabula";
    /// <summary>The words Settings search finds the page by: its title and what a reader would type for its rows.</summary>
    public static IReadOnlyList<string> Keywords =>
    [
        UiText.Get("settings.tabula.keyword_tabula"), UiText.Get("settings.tabula.keyword_artifacts"), UiText.Get("settings.tabula.keyword_live_answers"), UiText.Get("settings.tabula.keyword_visual_answers"),
        UiText.Get("settings.tabula.keyword_widgets"), UiText.Get("settings.tabula.keyword_inline_html"), UiText.Get("settings.tabula.keyword_html"), UiText.Get("settings.tabula.keyword_mcp_apps"),
        UiText.Get("settings.tabula.keyword_apps"), UiText.Get("settings.tabula.keyword_maps"), UiText.Get("settings.tabula.keyword_map_tiles"), UiText.Get("settings.tabula.keyword_street_tiles"),
        UiText.Get("settings.tabula.keyword_tiles"), UiText.Get("settings.tabula.keyword_your_model"), UiText.Get("settings.tabula.keyword_artifact_storage"), UiText.Get("settings.tabula.keyword_storage"),
    ];

    public static string LiveTitle => UiText.Get("settings.tabula.live_title");
    public static string LiveRow => UiText.Get("settings.tabula.live_row");
    public static string LiveDetail => UiText.Get("settings.tabula.live_detail");
    public static string VisualRow => UiText.Get("settings.tabula.visual_row");
    public static string VisualDetail => UiText.Get("settings.tabula.visual_detail");
    public static string HtmlRow => UiText.Get("settings.tabula.html_row");
    public static string HtmlDetail => UiText.Get("settings.tabula.html_detail");
    public static string AppsRow => UiText.Get("settings.tabula.apps_row");
    public static string AppsDetail => UiText.Get("settings.tabula.apps_detail");
    public static string MapsTitle => UiText.Get("settings.tabula.maps_title");
    public static string TilesRow => UiText.Get("settings.tabula.tiles_row");
    public static string TilesNone => UiText.Get("settings.tabula.tiles_none");
    public static string TilesDetail => UiText.Get("settings.tabula.tiles_detail");
    public static string TilesSet => UiText.Get("settings.tabula.tiles_set");
    public static string TilesRemove => UiText.Get("settings.tabula.tiles_remove");
    public static string TilesUse => UiText.Get("settings.tabula.tiles_use");
    public static string TilesCancel => UiText.Get("settings.tabula.tiles_cancel");
    public const string TilesPlaceholder = "https://tiles.example/{z}/{x}/{y}.png";
    /// <summary>The {z}, {x} and {y} are the address's own placeholders, handed to the sentence as arguments.</summary>
    public static string TilesInvalid => UiText.Format("settings.tabula.tiles_invalid", "{z}", "{x}", "{y}");
    public static string ArtifactsTitle => UiText.Get("settings.tabula.artifacts_title");
    public static string ModelRow => UiText.Get("settings.tabula.model_row");
    public static string ModelDetail => UiText.Get("settings.tabula.model_detail");
    public static string AskEach => UiText.Get("settings.tabula.ask_each");
    public static string NeverLabel => UiText.Get("settings.tabula.never_label");
    public static string StorageRow => UiText.Get("settings.tabula.storage_row");
    public static string StorageEmpty => UiText.Get("settings.tabula.storage_empty");
    public static string StorageLoading => UiText.Get("settings.tabula.storage_loading");
    public static string ClearLabel => UiText.Get("settings.tabula.clear_label");

    /// <summary>The app binds the parts (the store); null: the page edits the settings alone.</summary>
    public static Func<TabulaSettingsParts?> Bound { get; set; } = static () => null;

    private readonly TabulaSettingsParts parts;
    private readonly WebSettings settings;
    private readonly StackPanel storageRows = new();
    private readonly TextBlock tilesValue = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 };
    private readonly TextBox tilesField = new() { PlaceholderText = TilesPlaceholder };
    private readonly TextBlock tilesProblem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 8), IsVisible = false };
    private readonly StackPanel tilesEditor = new() { IsVisible = false };
    private readonly CzCapsuleButton tilesSet;
    private readonly CzCapsuleButton tilesRemove;
    private CancellationTokenSource? counting;

    public TabulaSettingsPage()
        : this(Bound() ?? new TabulaSettingsParts { Settings = () => WebSettingsHost.Current })
    {
    }

    public TabulaSettingsPage(TabulaSettingsParts parts)
    {
        this.parts = parts;
        settings = parts.Settings();

        var live = WebSettingsRows.Card(
            WebSettingsRows.Toggle(LiveRow, () => settings.LiveAnswers, on => settings.LiveAnswers = on, LiveDetail),
            WebSettingsRows.Segments(VisualRow, [(VisualAnswerLevel.Fewer, UiText.Get("settings.tabula.visual_fewer")), (VisualAnswerLevel.Balanced, UiText.Get("settings.tabula.visual_balanced")), (VisualAnswerLevel.More, UiText.Get("settings.tabula.visual_more"))],
                () => settings.VisualAnswers, value => settings.VisualAnswers = value, VisualDetail),
            WebSettingsRows.Toggle(HtmlRow, () => settings.InlineHtmlWidgets, on => settings.InlineHtmlWidgets = on, HtmlDetail),
            WebSettingsRows.Toggle(AppsRow, () => settings.McpApps, on => settings.McpApps = on, AppsDetail));

        tilesValue.Foreground = ChromeTokens.SecondaryText.Brush(tilesValue);
        SwiftFont.Subheadline.Apply(tilesValue);
        AutomationProperties.SetName(tilesValue, TilesRow);
        tilesValue.Track(() => settings.MapTileSource, _ => ShowTiles());
        tilesSet = WebSettingsRows.Capsule(TilesSet, ToggleTilesEditor, UiText.Get("settings.tabula.tiles_set_name"));
        tilesRemove = WebSettingsRows.Capsule(TilesRemove, RemoveTiles, UiText.Get("settings.tabula.tiles_remove_name"));
        var use = WebSettingsRows.Capsule(TilesUse, UseTiles, UiText.Get("settings.tabula.tiles_use_name"));
        var cancel = WebSettingsRows.Capsule(TilesCancel, CloseTilesEditor, UiText.Get("settings.tabula.tiles_cancel_name"));
        ChatDialogControls.TextEditor(tilesField, style: "RoundedBorder");
        AutomationProperties.SetName(tilesField, UiText.Get("settings.tabula.tiles_address_name"));
        tilesField.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { UseTiles(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseTilesEditor(); e.Handled = true; }
        };
        SwiftFont.Footnote.Apply(tilesProblem);
        tilesProblem.Foreground = ChromeTokens.Danger.Brush(tilesProblem);
        tilesEditor.Children.Add(new Border { Padding = new Thickness(16, 0, 16, 8), Child = tilesField });
        tilesEditor.Children.Add(tilesProblem);
        tilesEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 16, 12), HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, use } });
        var maps = WebSettingsRows.Card(
            new ChromeRow(TilesRow, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tilesValue, tilesSet, tilesRemove } }, TilesDetail),
            tilesEditor);

        var storageCard = new ChromeCard(() => storageRows);
        var artifacts = new StackPanel
        {
            Spacing = ChromeTokens.SectionSpacing,
            Children =
            {
                WebSettingsRows.Section(ArtifactsTitle, WebSettingsRows.Card(
                    WebSettingsRows.Segments(ModelRow, [(ArtifactModelChoice.AskEachArtifact, AskEach), (ArtifactModelChoice.Never, NeverLabel)],
                        () => settings.ArtifactModel, value => settings.ArtifactModel = value, ModelDetail))),
                new ChromeSection(StorageRow, () => storageCard),
            },
        };

        Child = new StackPanel
        {
            Spacing = ChromeTokens.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { WebSettingsRows.Section(LiveTitle, live), WebSettingsRows.Section(MapsTitle, maps), artifacts },
        };
        ShowTiles();
        ShowStorage(null);
        AttachedToVisualTree += (_, _) => _ = Count();
        DetachedFromVisualTree += (_, _) =>
        {
            counting?.Cancel();
            counting = null;
        };
    }

    /// <summary>The tile source as shown ("None" or its site).</summary>
    public string TilesText => tilesValue.Text ?? "";

    /// <summary>Whether the tile address editor is open.</summary>
    public bool IsEditingTiles => tilesEditor.IsVisible;

    /// <summary>The problem under the tile address, or "".</summary>
    public string TilesProblem => tilesProblem.IsVisible ? tilesProblem.Text ?? "" : "";

    /// <summary>The address field of the editor.</summary>
    public TextBox TilesField => tilesField;

    /// <summary>The storage rows as listed (empty until counted).</summary>
    public IReadOnlyList<ArtifactStorageRow> ListedStorage { get; private set; } = [];

    /// <summary>Counts again (after a Clear, or when the page appears); the rows follow when it ends.</summary>
    public async Task Count()
    {
        if (parts.Storage is not { } storage)
        {
            ShowStorage([]);
            return;
        }
        counting?.Cancel();
        var source = counting = new CancellationTokenSource();
        ShowStorage(null);
        try
        {
            var rows = await storage(source.Token);
            if (!source.IsCancellationRequested) ShowStorage(rows);
        }
        catch (OperationCanceledException)
        {
            // the page went away or counted again
        }
    }

    /// <summary>Opens the tile address editor (the "Set" button), or closes it.</summary>
    public void ToggleTilesEditor()
    {
        if (tilesEditor.IsVisible) CloseTilesEditor();
        else OpenTilesEditor();
    }

    /// <summary>Checks the address in the editor and keeps it as the tile source, or says why not.</summary>
    public void UseTiles()
    {
        var text = tilesField.Text ?? "";
        if (!MapTileTemplate.TryParse(text, out var template))
        {
            tilesProblem.Text = TilesInvalid;
            tilesProblem.IsVisible = true;
            return;
        }
        settings.MapTileSource = template.Text;
        CloseTilesEditor();
        tilesSet.Focus();
    }

    /// <summary>The "Remove" button: no tile source.</summary>
    public void RemoveTiles()
    {
        settings.MapTileSource = "";
        CloseTilesEditor();
    }

    private void OpenTilesEditor()
    {
        tilesField.Text = settings.MapTileSource;
        tilesProblem.IsVisible = false;
        tilesEditor.IsVisible = true;
        tilesField.Focus();
    }

    private void CloseTilesEditor()
    {
        tilesEditor.IsVisible = false;
        tilesProblem.IsVisible = false;
    }

    private void ShowTiles()
    {
        var source = settings.MapTileSource;
        tilesValue.Text = source.Length == 0 ? TilesNone : MapTileTemplate.TryParse(source, out var template) ? template.Host : TilesNone;
        tilesRemove.IsVisible = source.Length > 0;
    }

    private void ShowStorage(IReadOnlyList<ArtifactStorageRow>? rows)
    {
        storageRows.Children.Clear();
        ListedStorage = rows ?? [];
        if (rows is null)
        {
            storageRows.Children.Add(WebSettingsRows.Note(StorageLoading));
            return;
        }
        if (rows.Count == 0)
        {
            storageRows.Children.Add(WebSettingsRows.Note(StorageEmpty));
            return;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i > 0) storageRows.Children.Add(new ChromeRowDivider());
            var clear = WebSettingsRows.Capsule(ClearLabel, parts.ClearArtifacts is null ? null : () => _ = Clear(row), UiText.Format("settings.tabula.clear_name", row.Title));
            storageRows.Children.Add(new ChromeRow(row.Title, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Size(row.Bytes), clear } }));
        }
    }

    private static TextBlock Size(long bytes)
    {
        var text = new TextBlock { Text = SizeText(bytes), VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.Subheadline.Apply(text);
        text.Foreground = ChromeTokens.SecondaryText.Brush(text);
        return text;
    }

    /// <summary>"12 KB", "3.4 MB": the storage list's sizes.</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => UiText.FormatWith(CultureInfo.InvariantCulture, "settings.tabula.size_kb", 0),
        < 1024 * 1024 => UiText.FormatWith(CultureInfo.InvariantCulture, "settings.tabula.size_kb", bytes / 1024),
        < 1024L * 1024 * 1024 => UiText.FormatWith(CultureInfo.InvariantCulture, "settings.tabula.size_mb", bytes / (1024.0 * 1024)),
        _ => UiText.FormatWith(CultureInfo.InvariantCulture, "settings.tabula.size_gb", bytes / (1024.0 * 1024 * 1024)),
    };

    private async Task Clear(ArtifactStorageRow row)
    {
        if (parts.ClearArtifacts is not { } clear) return;
        if (!await parts.Confirm(UiText.Format("settings.tabula.clear_confirm_title", row.Title), UiText.Get("settings.tabula.clear_confirm_body"), ClearLabel)) return;
        await clear(row.Thread);
        await Count();
    }
}
