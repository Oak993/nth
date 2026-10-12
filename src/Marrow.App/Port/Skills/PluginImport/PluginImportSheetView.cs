// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2). The "Import plugins" sheet, in the language of "Import skills" (ImportSheetView, SK-18): a glass surface 440 wide, a header, the plugins
// found grouped by app with the port's tick boxes, how they come in (nth keeps its own copy, as "+ Add" does), what to do with a plugin already in nth (Skip or Replace; Skip first), then the buttons
// of the phase with "Choose a folder…" on the left. Everything that comes and goes folds or crossfades and the ticks ease (OD-0076; Reduce Motion lands at once).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Skills.Import;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.PluginImport;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.PluginImport;

internal sealed class PluginImportSheetView : Decorator
{
    internal const double Width440 = 440;

    private readonly PluginImportModel model;
    private readonly Action dismiss;
    private readonly CzCrossfadeText status = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText problems = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText duplicateNote = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly StackPanel groups = new() { Spacing = 10 };
    private readonly Dictionary<string, ImportTick> ticks = [];
    private readonly Dictionary<ForeignPluginApp, ImportTick> groupTicks = [];
    private readonly CrossfadeSlot skipSlot = new(), replaceSlot = new();
    private readonly CrossfadeSlot buttons = new();
    private readonly CzFoldRow listFold, duplicateFold, problemsFold;
    private Button? importSelected, importAll, folder;
    private string structure = "";
    private DuplicateChoice? shownChoice;
    private string? buttonsKey;

    public PluginImportSheetView(PluginImportModel model, Action dismiss)
    {
        this.model = model;
        this.dismiss = dismiss;

        var icon = LibraryUi.Tile("square.and.arrow.down", 36, 10);
        var title = LibraryUi.Text(PluginImportWords.Title, 15, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(PluginImportWords.Sub, 12, secondary: true, wrap: true);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), ColumnSpacing = 12 };
        Grid.SetColumn(words, 1);
        header.Children.Add(icon);
        header.Children.Add(words);

        var list = new StackPanel
        {
            Spacing = 6,
            Children = { LibraryUi.Panel(LibraryUi.Scroller(groups, 230), 10, 0.06, new Thickness(10, 6)), LibraryUi.Text(PluginImportWords.CopyNote, 11.5, secondary: true, wrap: true) },
        };
        listFold = new CzFoldRow(list, false);

        var choiceButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { skipSlot, replaceSlot } };
        duplicateFold = new CzFoldRow(new StackPanel
        {
            Spacing = 6,
            Children = { LibraryUi.Text(PluginImportWords.DuplicateHeading, 12, secondary: true, weight: SwiftFont.Weight.Semibold), choiceButtons, duplicateNote },
        }, false);

        problemsFold = new CzFoldRow(problems, false);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        buttons.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(buttons);

        var stack = new StackPanel { Spacing = 12, Children = { header, status, listFold, problemsFold, duplicateFold, footer } };
        var surface = new Border { Child = stack, Padding = new Thickness(18), Width = Width440 }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        this.Named(PluginImportWords.Title, "plugin-import-sheet");

        this.Track(() => model.Revision, _ => Apply());
        Apply();
    }

    internal PluginImportController Controller => model.Controller;

    private void Finish()
    {
        Controller.Close();
        dismiss();
    }

    public void Apply()
    {
        var c = Controller;
        var phase = c.Phase;
        if (phase == ImportPhase.Closed) return;
        var found = c.Groups;
        var plugins = found.Sum(g => g.Rows.Count);
        var anyDuplicate = found.SelectMany(g => g.Rows).Any(r => r.Duplicate);

        listFold.Open = phase == ImportPhase.Choosing && plugins > 0;
        duplicateFold.Open = phase == ImportPhase.Choosing && anyDuplicate;

        status.SetText(phase switch
        {
            ImportPhase.Scanning => ImportWords.Scanning,
            ImportPhase.Choosing => plugins == 0 ? PluginImportWords.NothingFound : PluginImportWords.FoundCount(plugins, found.Count) + " " + ImportWords.Nothing,
            ImportPhase.Importing => ImportWords.Importing,
            ImportPhase.Done => c.Summary,
            _ => "",
        });
        var failed = phase == ImportPhase.Done && c.Outcome is { } outcome ? outcome.Failed : [];
        var unread = phase == ImportPhase.Choosing ? c.ProblemCount : 0;
        problemsFold.Open = failed.Count > 0 || unread > 0;
        if (failed.Count > 0) problems.SetText(string.Join("\n", failed.Select(f => f.Name + ": " + f.Reason)));
        else if (unread > 0) problems.SetText(ImportWords.NotReadCount(unread));

        Rebuild(found);
        foreach (var group in found)
        {
            if (groupTicks.TryGetValue(group.App, out var all))
            {
                all.SetChecked(group.AllTicked);
                all.Enabled = phase == ImportPhase.Choosing;
            }
            foreach (var row in group.Rows)
                if (ticks.TryGetValue(row.Key, out var tick))
                {
                    tick.SetChecked(row.Ticked);
                    tick.Enabled = phase == ImportPhase.Choosing;
                }
        }

        var choice = c.Choice;
        if (shownChoice != choice)
        {
            var first = shownChoice is null;
            shownChoice = choice;
            var curve = first ? null : LibraryUi.Ease(this, LibraryUi.Fade);
            skipSlot.Show(LibraryButtons.Make(ImportWords.Skip, () => Controller.SetChoice(DuplicateChoice.Skip), choice == DuplicateChoice.Skip ? ButtonLook.Primary : ButtonLook.Plain, "plugin-import-skip"), curve);
            replaceSlot.Show(LibraryButtons.Make(ImportWords.Replace, () => Controller.SetChoice(DuplicateChoice.Replace), choice == DuplicateChoice.Replace ? ButtonLook.Primary : ButtonLook.Plain, "plugin-import-replace"), curve);
            duplicateNote.SetText(choice == DuplicateChoice.Replace ? PluginImportWords.ReplaceNote : PluginImportWords.SkipNote);
        }

        Buttons(phase, c.TickedCount, plugins > 0);
    }

    private void Rebuild(IReadOnlyList<PluginImportGroup> found)
    {
        var next = string.Join("\n", found.Select(g => g.App + ":" + string.Join(",", g.Rows.Select(r => r.Key + (r.Duplicate ? "!" : "")))));
        if (next == structure) return;
        structure = next;
        groups.Children.Clear();
        ticks.Clear();
        groupTicks.Clear();
        foreach (var group in found)
        {
            var app = group.App;
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 24 };
            var all = new ImportTick(group.Title, "plugin-import-tick-group-" + app, () => Controller.ToggleGroup(app));
            groupTicks[app] = all;
            var name = LibraryUi.Text(group.Title, 12, weight: SwiftFont.Weight.Semibold);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            head.Children.Add(all);
            head.Children.Add(name);
            var column = new StackPanel { Spacing = 0, Children = { head } };
            foreach (var row in group.Rows)
            {
                var key = row.Key;
                var tick = new ImportTick(row.Name, "plugin-import-tick-" + app + "-" + row.Name, () => Controller.Toggle(key));
                ticks[key] = tick;
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, MinHeight = 26 };
                var plugin = LibraryUi.Mono(row.Name, 11.5);
                plugin.VerticalAlignment = VerticalAlignment.Center;
                var description = LibraryUi.Text(row.Description, 11.5, secondary: true, trim: true);
                description.VerticalAlignment = VerticalAlignment.Center;
                var source = LibraryUi.Text(row.Duplicate ? PluginImportWords.AlreadyAdded : row.Source, 11, secondary: true);
                source.VerticalAlignment = VerticalAlignment.Center;
                source.Opacity = 0.8;
                Grid.SetColumn(plugin, 1);
                Grid.SetColumn(description, 2);
                Grid.SetColumn(source, 3);
                line.Children.Add(tick);
                line.Children.Add(plugin);
                line.Children.Add(description);
                line.Children.Add(source);
                column.Children.Add(line);
            }
            groups.Children.Add(column);
        }
    }

    private void Buttons(ImportPhase phase, int ticked, bool anyPlugin)
    {
        var key = phase switch
        {
            ImportPhase.Scanning or ImportPhase.Choosing => "choose",
            ImportPhase.Done => "done",
            _ => "busy",
        };
        if (key != buttonsKey)
        {
            var first = buttonsKey is null;
            buttonsKey = key;
            importSelected = importAll = folder = null;
            Control next;
            if (key == "choose")
            {
                folder = LibraryButtons.Make(ImportWords.ChooseFolder, () => _ = Controller.AddFolderAsync(), ButtonLook.Plain, "plugin-import-folder");
                importSelected = LibraryButtons.Make(ImportWords.ImportSelected(ticked), () => _ = Controller.ImportSelectedAsync(), ButtonLook.Primary, "plugin-import-selected");
                importAll = LibraryButtons.Make(ImportWords.ImportAll, () => _ = Controller.ImportAllAsync(), ButtonLook.Accent, "plugin-import-all");
                var right = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { LibraryButtons.Make(ImportWords.Cancel, Finish, ButtonLook.Plain, "plugin-import-cancel"), importAll, importSelected },
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
                Grid.SetColumn(right, 1);
                row.Children.Add(folder);
                row.Children.Add(right);
                next = row;
            }
            else if (key == "done")
                next = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { LibraryButtons.Make(ImportWords.Close, Finish, ButtonLook.Primary, "plugin-import-close") } };
            else next = new Border { Height = 28 };
            buttons.Show(next, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (importSelected is not null)
        {
            LibraryButtons.SetWords(importSelected, ImportWords.ImportSelected(ticked));
            importSelected.IsEnabled = phase == ImportPhase.Choosing && ticked > 0;
        }
        if (importAll is not null) importAll.IsEnabled = phase == ImportPhase.Choosing && anyPlugin;
        if (folder is not null) folder.IsEnabled = phase == ImportPhase.Choosing;
    }
}
