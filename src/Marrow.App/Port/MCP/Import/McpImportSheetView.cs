// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). The "Import servers" sheet, in the language of "Import skills" (ImportSheetView, SK-18) and so of Download all (SK-15): a glass surface
// 440 wide, a header, the servers found grouped by app with the port's tick boxes (a file that could not be read sits in its app's group, marked so), what to do with a server already in nth (Skip,
// Replace, Keep both; Skip first), then the buttons of the phase. Everything that comes and goes folds or crossfades and the ticks ease (OD-0076; Reduce Motion lands at once).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Skills.Import;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MCP.Import;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MCP.Import;

internal sealed class McpImportSheetView : Decorator
{
    internal const double Width440 = 440;

    private readonly McpImportModel model;
    private readonly Action dismiss;
    private readonly CzCrossfadeText status = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText failures = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText duplicateNote = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly StackPanel groups = new() { Spacing = 10 };
    private readonly Dictionary<string, ImportTick> ticks = [];
    private readonly Dictionary<ForeignMcpApp, ImportTick> groupTicks = [];
    private readonly CrossfadeSlot skipSlot = new(), replaceSlot = new(), bothSlot = new();
    private readonly CrossfadeSlot buttons = new();
    private readonly CzFoldRow listFold, duplicateFold, failuresFold;
    private Button? importSelected, importAll;
    private string structure = "";
    private DuplicateChoice? shownChoice;
    private string? buttonsKey;

    public McpImportSheetView(McpImportModel model, Action dismiss)
    {
        this.model = model;
        this.dismiss = dismiss;

        var icon = LibraryUi.Tile("square.and.arrow.down", 36, 10);
        var title = LibraryUi.Text(McpImportWords.Title, 15, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(McpImportWords.Sub, 12, secondary: true, wrap: true);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), ColumnSpacing = 12 };
        Grid.SetColumn(words, 1);
        header.Children.Add(icon);
        header.Children.Add(words);

        var list = new StackPanel
        {
            Spacing = 6,
            Children = { LibraryUi.Panel(LibraryUi.Scroller(groups, 230), 10, 0.06, new Thickness(10, 6)), LibraryUi.Text(McpImportWords.SecretsNote, 11.5, secondary: true, wrap: true) },
        };
        listFold = new CzFoldRow(list, false);

        var choiceButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { skipSlot, replaceSlot, bothSlot } };
        var duplicateStack = new StackPanel
        {
            Spacing = 6,
            Children = { LibraryUi.Text(McpImportWords.DuplicateHeading, 12, secondary: true, weight: SwiftFont.Weight.Semibold), choiceButtons, duplicateNote },
        };
        duplicateFold = new CzFoldRow(duplicateStack, false);

        failuresFold = new CzFoldRow(failures, false);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        buttons.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(buttons);

        var stack = new StackPanel { Spacing = 12, Children = { header, status, listFold, duplicateFold, failuresFold, footer } };
        var surface = new Border { Child = stack, Padding = new Thickness(18), Width = Width440 }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        this.Named(McpImportWords.Title, "mcp-import-sheet");

        this.Track(() => model.Revision, _ => Apply());
        Apply();
    }

    internal McpImportController Controller => model.Controller;

    private void Finish()
    {
        Controller.Close();
        dismiss();
    }

    public void Apply()
    {
        var c = Controller;
        var phase = c.Phase;
        if (phase == McpImportPhase.Closed) return;
        var found = c.Groups;
        var servers = found.Sum(g => g.Rows.Count(r => !r.Unreadable));
        var apps = found.Count(g => g.Rows.Any(r => !r.Unreadable));
        var anyDuplicate = found.SelectMany(g => g.Rows).Any(r => r.Duplicate);

        listFold.Open = phase == McpImportPhase.Choosing && found.Count > 0;
        duplicateFold.Open = phase == McpImportPhase.Choosing && anyDuplicate;

        status.SetText(phase switch
        {
            McpImportPhase.Scanning => McpImportWords.Scanning,
            McpImportPhase.Choosing => servers == 0 ? McpImportWords.NothingFound : McpImportWords.FoundCount(servers, apps) + " " + McpImportWords.Nothing,
            McpImportPhase.Done => c.Summary,
            _ => "",
        });
        IReadOnlyList<McpImportFailure> failed = phase == McpImportPhase.Done && c.Outcome is { } outcome ? outcome.Failed : [];
        failuresFold.Open = failed.Count > 0;
        if (failed.Count > 0) failures.SetText(string.Join("\n", failed.Select(f => f.Name + ": " + f.Reason)));

        Rebuild(found);
        foreach (var group in found)
        {
            if (groupTicks.TryGetValue(group.App, out var all))
            {
                all.SetChecked(group.AllTicked);
                all.Enabled = phase == McpImportPhase.Choosing;
            }
            foreach (var row in group.Rows)
                if (ticks.TryGetValue(row.Key, out var tick))
                {
                    tick.SetChecked(row.Ticked);
                    tick.Enabled = phase == McpImportPhase.Choosing;
                }
        }

        var choice = c.Choice;
        if (shownChoice != choice)
        {
            var first = shownChoice is null;
            shownChoice = choice;
            var curve = first ? null : LibraryUi.Ease(this, LibraryUi.Fade);
            skipSlot.Show(LibraryButtons.Make(McpImportWords.Skip, () => Controller.SetChoice(DuplicateChoice.Skip), choice == DuplicateChoice.Skip ? ButtonLook.Primary : ButtonLook.Plain, "mcp-import-skip"), curve);
            replaceSlot.Show(LibraryButtons.Make(McpImportWords.Replace, () => Controller.SetChoice(DuplicateChoice.Replace), choice == DuplicateChoice.Replace ? ButtonLook.Primary : ButtonLook.Plain, "mcp-import-replace"), curve);
            bothSlot.Show(LibraryButtons.Make(McpImportWords.KeepBoth, () => Controller.SetChoice(DuplicateChoice.KeepBoth), choice == DuplicateChoice.KeepBoth ? ButtonLook.Primary : ButtonLook.Plain, "mcp-import-keep-both"), curve);
            duplicateNote.SetText(McpImportWords.DuplicateNote(choice switch { DuplicateChoice.Replace => McpImportWords.Replace, DuplicateChoice.KeepBoth => McpImportWords.KeepBoth, _ => McpImportWords.Skip }));
        }

        Buttons(phase, c.TickedCount, servers > 0);
    }

    // The list is made again only when what was found changes; ticking only moves the ticks.
    private void Rebuild(IReadOnlyList<McpImportGroup> found)
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
            var name = LibraryUi.Text(group.Title, 12, weight: SwiftFont.Weight.Semibold);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            if (group.Rows.Any(r => !r.Unreadable))
            {
                var all = new ImportTick(group.Title, "mcp-import-tick-group-" + app, () => Controller.ToggleGroup(app));
                groupTicks[app] = all;
                head.Children.Add(all);
            }
            else head.Children.Add(new Border { Width = 15 });
            head.Children.Add(name);
            var column = new StackPanel { Spacing = 0, Children = { head } };
            foreach (var row in group.Rows)
            {
                var key = row.Key;
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, MinHeight = 26 };
                Control lead;
                if (row.Unreadable) lead = new Border { Width = 15 };
                else
                {
                    var tick = new ImportTick(row.Name, "mcp-import-tick-" + app + "-" + row.Name, () => Controller.Toggle(key));
                    ticks[key] = tick;
                    lead = tick;
                }
                var server = LibraryUi.Mono(row.Name, 11.5);
                server.VerticalAlignment = VerticalAlignment.Center;
                if (row.Unreadable) server.Opacity = 0.6;
                var detail = LibraryUi.Text(row.Detail, 11.5, secondary: true, trim: true);
                detail.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(detail, row.Detail);
                var source = LibraryUi.Text(row.Duplicate ? McpImportWords.AlreadyIn : row.Source, 11, secondary: true);
                source.VerticalAlignment = VerticalAlignment.Center;
                source.Opacity = 0.8;
                Grid.SetColumn(server, 1);
                Grid.SetColumn(detail, 2);
                Grid.SetColumn(source, 3);
                line.Children.Add(lead);
                line.Children.Add(server);
                line.Children.Add(detail);
                line.Children.Add(source);
                column.Children.Add(line);
            }
            groups.Children.Add(column);
        }
    }

    private void Buttons(McpImportPhase phase, int ticked, bool anyServer)
    {
        var key = phase switch
        {
            McpImportPhase.Scanning or McpImportPhase.Choosing => "choose",
            McpImportPhase.Done => "done",
            _ => "busy",
        };
        if (key != buttonsKey)
        {
            var first = buttonsKey is null;
            buttonsKey = key;
            importSelected = importAll = null;
            Control next;
            if (key == "choose")
            {
                importSelected = LibraryButtons.Make(McpImportWords.ImportSelected(ticked), () => Controller.ImportSelected(), ButtonLook.Primary, "mcp-import-selected");
                importAll = LibraryButtons.Make(McpImportWords.ImportAll, () => Controller.ImportAll(), ButtonLook.Accent, "mcp-import-all");
                next = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { LibraryButtons.Make(McpImportWords.Cancel, Finish, ButtonLook.Plain, "mcp-import-cancel"), importAll, importSelected },
                };
            }
            else if (key == "done")
                next = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { LibraryButtons.Make(McpImportWords.Close, Finish, ButtonLook.Primary, "mcp-import-close") } };
            else next = new Border { Height = 28 };
            buttons.Show(next, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (importSelected is not null)
        {
            LibraryButtons.SetWords(importSelected, McpImportWords.ImportSelected(ticked));
            importSelected.IsEnabled = phase == McpImportPhase.Choosing && ticked > 0;
        }
        if (importAll is not null) importAll.IsEnabled = phase == McpImportPhase.Choosing && anyServer;
    }
}
