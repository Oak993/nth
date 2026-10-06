// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The "Import skills" sheet, in the Download all dialog's style (SK-15): a glass surface 440 wide, a header, the places found grouped by app with the port's tick boxes, the
// way to bring them in (Copy or Link), then the buttons of the phase. Everything that comes and goes folds or crossfades and the ticks ease (OD-0076; Reduce Motion lands at once). It draws what the ImportController holds and
// sends it the user's presses.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportSheetView : Decorator
{
    internal const double Width440 = 440;

    private readonly ImportModel model;
    private readonly Action dismiss;
    private readonly CzCrossfadeText status = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText problems = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText modeNote = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText duplicate = new("") { FontSize = 12.5, Wrap = true, FontWeight = FontWeight.Medium };
    private readonly StackPanel groups = new() { Spacing = 10 };
    private readonly Dictionary<string, ImportTick> ticks = [];
    private readonly Dictionary<ForeignApp, ImportTick> groupTicks = [];
    private readonly CrossfadeSlot copySlot = new();
    private readonly CrossfadeSlot linkSlot = new();
    private readonly CrossfadeSlot buttons = new();
    private readonly CzFoldRow listFold, modeFold, duplicateFold, problemsFold;
    private readonly ImportTick rest;
    private Button? importSelected, importAll, folder;
    private string structure = "";
    private ImportMode? shownMode;
    private string? buttonsKey;

    public ImportSheetView(ImportModel model, Action dismiss)
    {
        this.model = model;
        this.dismiss = dismiss;

        var icon = LibraryUi.Tile("square.and.arrow.down", 36, 10);
        var title = LibraryUi.Text(ImportWords.Title, 15, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(ImportWords.Sub, 12, secondary: true);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), ColumnSpacing = 12 };
        Grid.SetColumn(words, 1);
        header.Children.Add(icon);
        header.Children.Add(words);

        var list = new StackPanel { Spacing = 6, Children = { LibraryUi.Panel(LibraryUi.Scroller(groups, 230), 10, 0.06, new Thickness(10, 6)) } };
        listFold = new CzFoldRow(list, false);

        var modeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copySlot, linkSlot } };
        var modeStack = new StackPanel
        {
            Spacing = 6,
            Children = { LibraryUi.Text(ImportWords.ModeHeading, 12, secondary: true, weight: SwiftFont.Weight.Semibold), modeButtons, modeNote },
        };
        modeFold = new CzFoldRow(modeStack, false);

        rest = new ImportTick(ImportWords.ApplyToRest, "import-rest", () => rest!.SetChecked(!rest.Checked));
        var restRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { rest, LibraryUi.Text(ImportWords.ApplyToRest, 12, secondary: true) },
        };
        var choices = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                LibraryButtons.Make(ImportWords.KeepBoth, () => Answer(DuplicateChoice.KeepBoth), ButtonLook.Plain, "import-keep-both"),
                LibraryButtons.Make(ImportWords.Replace, () => Answer(DuplicateChoice.Replace), ButtonLook.Danger, "import-replace"),
                LibraryButtons.Make(ImportWords.Skip, () => Answer(DuplicateChoice.Skip), ButtonLook.Plain, "import-skip"),
            },
        };
        duplicateFold = new CzFoldRow(LibraryUi.Panel(new StackPanel { Spacing = 10, Children = { duplicate, choices, restRow } }, 10, 0.06, new Thickness(12, 10)), false);

        problemsFold = new CzFoldRow(problems, false);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        buttons.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(buttons);

        var stack = new StackPanel { Spacing = 12, Children = { header, status, listFold, problemsFold, modeFold, duplicateFold, footer } };
        var surface = new Border { Child = stack, Padding = new Thickness(18), Width = Width440 }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        this.Named(ImportWords.Title, "import-sheet");

        this.Track(() => model.Revision, _ => Apply());
        Apply();
    }

    internal ImportController Controller => model.Controller;

    private void Answer(DuplicateChoice choice) => Controller.AnswerDuplicate(choice, rest.Checked);

    private void Cancel()
    {
        Controller.Close();
        dismiss();
    }

    private void CloseDone()
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
        var skills = found.Sum(g => g.Rows.Count);

        listFold.Open = phase is ImportPhase.Choosing && found.Count > 0;
        modeFold.Open = phase is ImportPhase.Choosing && found.Count > 0;
        duplicateFold.Open = phase == ImportPhase.AskingDuplicate;
        if (phase != ImportPhase.AskingDuplicate && rest.Checked) rest.SetChecked(false);
        if (c.Question is { } question) duplicate.SetText(ImportWords.Duplicate(question.Incoming.Name));

        status.SetText(phase switch
        {
            ImportPhase.Scanning => ImportWords.Scanning,
            ImportPhase.Choosing => found.Count == 0 ? ImportWords.NothingFound : ImportWords.FoundCount(skills, found.Count) + " " + ImportWords.Nothing,
            ImportPhase.Importing => ImportWords.Importing,
            ImportPhase.Done => c.Summary,
            _ => "",
        });
        var unread = phase == ImportPhase.Choosing ? c.ProblemCount : 0;
        problemsFold.Open = unread > 0;
        if (unread > 0) problems.SetText(ImportWords.NotReadCount(unread));

        Rebuild(found);
        foreach (var group in found)
        {
            if (groupTicks.TryGetValue(group.App, out var all))
            {
                all.SetChecked(group.AllTicked);
                all.Enabled = phase == ImportPhase.Choosing && group.Rows.Any(r => !r.AlreadyImported);
            }
            foreach (var row in group.Rows)
                if (ticks.TryGetValue(row.Key, out var tick))
                {
                    tick.SetChecked(row.Ticked);
                    tick.Enabled = phase == ImportPhase.Choosing && !row.AlreadyImported;
                }
        }

        var mode = c.Mode;
        if (shownMode != mode)
        {
            var first = shownMode is null;
            shownMode = mode;
            var curve = first ? null : LibraryUi.Ease(this, LibraryUi.Fade);
            copySlot.Show(LibraryButtons.Make(ImportWords.Copy, () => Controller.SetMode(ImportMode.Copy), mode == ImportMode.Copy ? ButtonLook.Primary : ButtonLook.Plain, "import-copy"), curve);
            linkSlot.Show(LibraryButtons.Make(ImportWords.Link, () => Controller.SetMode(ImportMode.Link), mode == ImportMode.Link ? ButtonLook.Primary : ButtonLook.Plain, "import-link"), curve);
            modeNote.SetText(mode == ImportMode.Copy ? ImportWords.CopyNote : ImportWords.LinkNote);
        }

        Buttons(phase, c.TickedCount, found.SelectMany(g => g.Rows).Any(r => !r.AlreadyImported));
    }

    // The list is made again only when the places found change; ticking only moves the ticks.
    private void Rebuild(IReadOnlyList<ImportGroup> found)
    {
        var next = string.Join("\n", found.Select(g => g.App + ":" + string.Join(",", g.Rows.Select(r => r.Key + (r.AlreadyImported ? "!" : "")))));
        if (next == structure) return;
        structure = next;
        groups.Children.Clear();
        ticks.Clear();
        groupTicks.Clear();
        foreach (var group in found)
        {
            var app = group.App;
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 24 };
            var all = new ImportTick(group.Title, "import-tick-group-" + app, () => Controller.ToggleGroup(app));
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
                var tick = new ImportTick(row.Name, "import-tick-" + app + "-" + row.Name, () => Controller.Toggle(key));
                ticks[key] = tick;
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, MinHeight = 26 };
                var skill = LibraryUi.Mono(row.Name, 11.5);
                skill.VerticalAlignment = VerticalAlignment.Center;
                var description = LibraryUi.Text(row.Description, 11.5, secondary: true, trim: true);
                description.VerticalAlignment = VerticalAlignment.Center;
                var source = LibraryUi.Text(row.AlreadyImported ? ImportWords.AlreadyImported : row.Source, 11, secondary: true);
                source.VerticalAlignment = VerticalAlignment.Center;
                source.Opacity = 0.8;
                Grid.SetColumn(skill, 1);
                Grid.SetColumn(description, 2);
                Grid.SetColumn(source, 3);
                line.Children.Add(tick);
                line.Children.Add(skill);
                line.Children.Add(description);
                line.Children.Add(source);
                column.Children.Add(line);
            }
            groups.Children.Add(column);
        }
    }

    private void Buttons(ImportPhase phase, int ticked, bool anyOpen)
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
                folder = LibraryButtons.Make(ImportWords.ChooseFolder, () => _ = Controller.AddFolderAsync(), ButtonLook.Plain, "import-folder");
                importSelected = LibraryButtons.Make(ImportWords.ImportSelected(ticked), () => _ = Controller.ImportSelectedAsync(), ButtonLook.Primary, "import-selected");
                importAll = LibraryButtons.Make(ImportWords.ImportAll, () => _ = Controller.ImportAllAsync(), ButtonLook.Accent, "import-all");
                var right = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { LibraryButtons.Make(ImportWords.Cancel, Cancel, ButtonLook.Plain, "import-cancel"), importAll, importSelected },
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
                Grid.SetColumn(right, 1);
                row.Children.Add(folder);
                row.Children.Add(right);
                next = row;
            }
            else if (key == "done")
                next = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { LibraryButtons.Make(ImportWords.Close, CloseDone, ButtonLook.Primary, "import-close") } };
            else next = new Border { Height = 28 };
            buttons.Show(next, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (importSelected is not null)
        {
            LibraryButtons.SetWords(importSelected, ImportWords.ImportSelected(ticked));
            importSelected.IsEnabled = phase == ImportPhase.Choosing && ticked > 0;
        }
        if (importAll is not null) importAll.IsEnabled = phase == ImportPhase.Choosing && anyOpen;
        if (folder is not null) folder.IsEnabled = phase == ImportPhase.Choosing;
    }
}
