// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 clarification "Sections C"). The Plugins page and the Skills page: two pages of their own, one body. Layout D (the library list with the detail pane), the
// Download all button at the list's foot with its confirmation, the per-skill switches, "Already available through your providers" (Skills only), Reset and the not-affiliated footer. The page keeps no state of its own: it draws
// what the page's LibraryController holds (kept for the life of the app, so a run and its dialog survive the sidebar going elsewhere and back) and sends it the user's presses.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Import;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Terms;
using Marrow.Services.Port.Skills.Pages;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal abstract class LibraryPage : Decorator, ISettingsPageChrome
{
    internal const double PaneHeight = 340;
    internal const double ListWidth = 188;

    private readonly LibraryModel? model;
    private readonly LibrarySection section;
    private readonly CustomAddKind ownKind;
    private readonly CustomOwnModel? own;
    private readonly Dictionary<string, CustomOwnRow> ownRows = [];
    private readonly Dictionary<string, CustomOwnDetail> ownDetails = [];
    private readonly Control ownHeading = LibraryUi.Text("Your own", 11, secondary: true);
    private readonly CzCrossfadeText listing = new("") { FontSize = 12, Secondary = true };
    private readonly StackPanel rows = new() { Spacing = 2 };
    private readonly Dictionary<string, LibraryRowView> rowViews = [];
    private readonly Dictionary<string, DetailView> details = [];
    private readonly CrossfadeSlot detailSlot = new();
    private readonly CzCrossfadeText downloadAllWords = new("") { FontSize = 12, FontWeight = FontWeight.Medium };
    private readonly Button downloadAll;
    private readonly ProvidersSection providers = new();
    private readonly ResetArea? reset;
    private string? shownDetail;
    private DialogSheet? sheet;
    private Action? closeSheet;
    private bool closingFromHere;

    protected LibraryPage(LibrarySection section, bool tools = false)
    {
        this.section = section;
        ownKind = tools ? CustomAddKind.Tools : section == LibrarySection.Plugins ? CustomAddKind.Plugins : CustomAddKind.Skills;
        own = CustomAddSeams.Backends.Current.Own;
        model = tools ? SkillsPageSeams.Controllers.Current.Tools() : SkillsPageSeams.Controllers.Current.For(section);
        ChromeAccessories = model is null ? [] : HeaderActions(tools);
        downloadAll = PlainControls.Button(downloadAllWords, OpenDownloadAll);
        SwiftGlass.StyleButton(downloadAll);
        downloadAll.HorizontalAlignment = HorizontalAlignment.Stretch;
        downloadAll.HorizontalContentAlignment = HorizontalAlignment.Center;
        downloadAll.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        downloadAll.Named("Download all " + (tools ? "tools" : section.Noun()), "download-all");
        if (model is null)
        {
            var note = LibraryUi.Text(LibraryWords.Unavailable, 12.5, secondary: true, wrap: true);
            Child = new StackPanel { Spacing = 20, Children = { note, Footer() } };
            return;
        }
        reset = new ResetArea(model);

        var list = new Border { Width = ListWidth, Height = PaneHeight, Padding = new Thickness(6) }.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        list.Background = ChromeTokens.Overlay(0.04).Brush(list);
        var column = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 6 };
        var scroll = LibraryUi.Scroller(rows);
        Grid.SetRow(downloadAll, 1);
        column.Children.Add(scroll);
        column.Children.Add(downloadAll);
        list.Child = column;

        detailSlot.Width = double.NaN;
        var detail = new Border { Height = PaneHeight, Child = LibraryUi.Scroller(detailSlot), ClipToBounds = true };
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions(ListWidth + ",*"), ColumnSpacing = 16 };
        Grid.SetColumn(detail, 1);
        panes.Children.Add(list);
        panes.Children.Add(detail);

        var page = new StackPanel { Spacing = 20 };
        if (!tools) page.Children.Add(listing);
        page.Children.Add(panes);
        if (!tools && section == LibrarySection.Skills) page.Children.Add(providers);
        if (!tools && section == LibrarySection.Skills && SkillsPageSeams.Imports.Current.Model is { } imports) page.Children.Add(importArea = new ImportArea(imports)); // SK-18
        page.Children.Add(Footer(reset));
        Child = page;

        this.Track(() => model.Revision, _ => Refresh());
        this.Track(() => model.DialogHidden, _ => SyncDialog(model.Dialog));
        if (own is not null) this.Track(() => own.Revision, _ => Refresh());
        AttachedToVisualTree += (_, _) => { if (own is not null) _ = own.Perform(own.Refresh); };
        AttachedToVisualTree += (_, _) => SyncDialog(model.Dialog); // a page built again finds the run it left and shows its dialog, unless the user hid it
        DetachedFromVisualTree += (_, _) => CloseSheet();
        Refresh();
    }

    // PORT-ONLY ED-NEW-mcpimport-2 (owner 2026-10-11: "Skills, Plugins and MCP need to have the add and import options"): "+ Add" and "Import" side by side in the header on all three pages.
    // Skills' Import opens SK-18's sheet (the in-page action stays); Plugins' opens "Import plugins"; the optional tools page keeps "+ Add" alone.
    private ImportArea? importArea;

    private IReadOnlyList<Control> HeaderActions(bool tools)
    {
        var add = new CustomAddAction(tools ? CustomAddKind.Tools : section == LibrarySection.Plugins ? CustomAddKind.Plugins : CustomAddKind.Skills);
        if (tools) return [add];
        Button? import = null;
        if (section == LibrarySection.Skills && SkillsPageSeams.Imports.Current.Model is not null) import = PageHeaderButton.Import(() => importArea?.Open(ImportSheetAnchor.For(import!)));
        else if (section == LibrarySection.Plugins && PluginImport.PluginImportLauncher.Model() is not null) import = PageHeaderButton.Import(() => PluginImport.PluginImportLauncher.Open(import!));
        return import is null ? [add] : [add, import];
    }

    /// <inheritdoc />
    public bool HasChromeControls => model is not null;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; }

    internal LibraryModel? Model => model;

    internal IReadOnlyDictionary<string, LibraryRowView> RowViews => rowViews;

    internal IReadOnlyDictionary<string, DetailView> Details => details;

    internal CrossfadeSlot DetailSlot => detailSlot;

    internal string? ShownDetail => shownDetail;

    internal Button DownloadAllButton => downloadAll;

    internal string DownloadAllWords => downloadAllWords.Text;

    internal string ListingShown => listing.Text;

    internal ProvidersSection Providers => providers;

    internal ResetArea? Reset => reset;

    internal DialogSheet? Sheet => sheet;

    internal StackPanel RowStack => rows;
    internal IReadOnlyDictionary<string, CustomOwnRow> OwnRows => ownRows;
    internal IReadOnlyDictionary<string, CustomOwnDetail> OwnDetails => ownDetails;

    private static Control Footer(ResetArea? reset = null)
    {
        var notice = LibraryUi.Text(SkillsNotices.Footer, 11, secondary: true, wrap: true);
        notice.Opacity = 0.85;
        var stack = new StackPanel { Spacing = 10 };
        if (reset is not null) stack.Children.Add(reset);
        stack.Children.Add(notice);
        return stack;
    }

    private void OpenDownloadAll()
    {
        if (model is null) return;
        model.DialogHidden = false;
        model.Controller.OpenDownloadAll();
    }

    private void Refresh()
    {
        if (model is null) return;
        var controller = model.Controller;
        var all = controller.Rows;
        var selectedId = controller.SelectedId;
        var ownSelected = own?.Selected(ownKind);

        listing.SetText(controller.Listing);

        var wanted = new List<Control>();
        foreach (var row in all)
        {
            if (!rowViews.TryGetValue(row.Id, out var view)) rowViews[row.Id] = view = new LibraryRowView(model, row, () => own?.Select(ownKind, null));
            view.Apply(row, ownSelected is null && row.Id == selectedId);
            wanted.Add(view);
        }
        if (own is not null)
        {
            var owned = own.Items(ownKind);
            foreach (var stale in ownRows.Keys.Where(id => !owned.Any(i => i.Id == id)).ToArray())
            { ownRows.Remove(stale); ownDetails.Remove(stale); }
            if (owned.Count > 0) wanted.Add(ownHeading);
            foreach (var item in owned)
            {
                if (!ownRows.TryGetValue(item.Id, out var view)) ownRows[item.Id] = view = new CustomOwnRow(own, ownKind, item);
                view.Apply(item, ownSelected == item.Id);
                wanted.Add(view);
            }
        }
        for (var i = rows.Children.Count - 1; i >= 0; i--)
            if (!wanted.Contains(rows.Children[i])) rows.Children.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
            if (i >= rows.Children.Count || !ReferenceEquals(rows.Children[i], wanted[i])) rows.Children.Insert(i, wanted[i]);

        if (own is not null && own.Items(ownKind).FirstOrDefault(i => i.Id == ownSelected) is { } ownItem)
        {
            if (!ownDetails.TryGetValue(ownItem.Id, out var ownDetail)) ownDetails[ownItem.Id] = ownDetail = new CustomOwnDetail(own, ownKind, ownItem);
            ownDetail.Apply(ownItem);
            if (shownDetail != ownItem.Id)
            {
                var first = shownDetail is null;
                shownDetail = ownItem.Id;
                detailSlot.Show(ownDetail, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
            }
        }
        else if (selectedId is not null && all.FirstOrDefault(r => r.Id == selectedId) is { } selected)
        {
            if (!details.TryGetValue(selectedId, out var detail)) details[selectedId] = detail = new DetailView(model, selected);
            detail.Apply(selected, controller.RemoveAsked(selectedId));
            if (shownDetail != selectedId)
            {
                var first = shownDetail is null;
                shownDetail = selectedId;
                detailSlot.Show(detail, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
            }
        }

        downloadAllWords.SetText(model.DownloadAllLabel);
        downloadAll.IsEnabled = controller.CanDownloadAll;
        providers.Apply(controller.Providers);
        reset!.Apply(controller.ResetAsked);
        SyncDialog(model.Dialog);
    }

    // The dialog is a sheet in the window over everything (the design's dimmed window). It opens when the controller has a dialog open, closes when it has none, and stays hidden while a run goes on that the user hid.
    private void SyncDialog(RunView dialog)
    {
        if (model is null) return;
        if (!dialog.IsOpen)
        {
            CloseSheet();
            if (model.DialogHidden) model.DialogHidden = false;
            return;
        }
        if (model.DialogHidden) { CloseSheet(); return; }
        if (sheet is not null) { sheet.Dialog.Apply(dialog); return; }
        if (TopLevel.GetTopLevel(this) is null) return;
        ChatSheetPresentation.Show(this, dismiss =>
        {
            closeSheet = dismiss;
            var content = new DownloadAllDialog(model, dismiss, section == LibrarySection.Plugins);
            sheet = new DialogSheet(content);
            return sheet;
        }, OnSheetClosed, inWindow: true, still: LibraryUi.Still(this));
        sheet?.Dialog.Apply(dialog);
    }

    private void CloseSheet()
    {
        if (sheet is null) return;
        closingFromHere = true;
        try { closeSheet?.Invoke(); }
        finally { closingFromHere = false; sheet = null; closeSheet = null; }
    }

    // Escape (or a click on Hide) closed the sheet: in the confirmation that is Cancel; while a run goes on it only hides the dialog; in the end phases it is Close.
    private void OnSheetClosed()
    {
        sheet = null;
        closeSheet = null;
        if (closingFromHere || model is null) return;
        var controller = model.Controller;
        switch (controller.Dialog.Phase)
        {
            case RunPhase.Confirm: controller.CancelConfirmation(); break;
            case RunPhase.Downloading or RunPhase.Paused: model.DialogHidden = true; break;
            case RunPhase.Done or RunPhase.Partial: controller.CloseRun(); break;
        }
    }
}
