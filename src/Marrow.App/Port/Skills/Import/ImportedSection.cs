// PORT-ONLY: first-release feature unit SK-18 (OD-0118). "Imported skills" on the Skills page: one row per skill brought in, with a switch (like the switches of a set), the quiet "from <app>" tag and a two-step Remove (the trash asks,
// Remove runs; the same two steps as Reset). The section folds open when something is imported and shut when nothing is (OD-0076). Nothing here touches the original.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Import;

internal sealed class ImportedSection : Decorator
{
    private readonly ImportModel model;
    private readonly StackPanel list = new() { Spacing = 0 };
    private readonly CzFoldRow fold;
    private readonly Dictionary<string, Row> rows = [];

    public ImportedSection(ImportModel model)
    {
        this.model = model;
        var heading = LibraryUi.Text(ImportWords.SectionHeading, 13, secondary: true, weight: SwiftFont.Weight.Semibold);
        var note = LibraryUi.Text(ImportWords.SectionNote, 12, secondary: true, wrap: true);
        fold = new CzFoldRow(new StackPanel { Spacing = 6, Children = { heading, note, LibraryUi.Panel(list, 12, 0.04, new Thickness(14, 6)) } }, false);
        Child = fold;
        this.Track(() => model.Revision, _ => Apply());
        Apply();
    }

    public bool IsOpen => fold.Open;

    public IReadOnlyCollection<string> Ids => rows.Keys;

    public void Apply()
    {
        var now = model.Controller.Imported;
        fold.Open = now.Count > 0;
        foreach (var gone in rows.Keys.Where(id => now.All(i => i.Id != id)).ToArray())
        {
            list.Children.Remove(rows[gone].View);
            rows.Remove(gone);
        }
        foreach (var skill in now)
        {
            if (!rows.TryGetValue(skill.Id, out var row))
            {
                rows[skill.Id] = row = new Row(model, skill, this);
                list.Children.Add(row.View);
            }
            row.Apply(skill);
        }
    }

    private sealed class Row
    {
        private readonly ImportModel model;
        private readonly string id;
        private readonly CzCrossfadeText description = new("") { FontSize = 11.5, Secondary = true };
        private readonly CzCrossfadeText tag = new("") { FontSize = 11, Secondary = true };
        private readonly CrossfadeSlot action = new();
        private readonly Control owner;
        private bool? asked;

        public Row(ImportModel model, ImportedSkill skill, Control owner)
        {
            this.model = model;
            this.owner = owner;
            id = skill.Id;
            var toggle = new SettingsSwitch(new Binding<bool>(() => { _ = model.Revision; return Current()?.Enabled ?? false; }, on => model.Controller.SetEnabled(id, on)));
            toggle.Named(skill.Name, "imported-switch-" + id);
            var name = LibraryUi.Mono(skill.Name, 11.5);
            name.VerticalAlignment = VerticalAlignment.Center;
            tag.Opacity = 0.85;
            var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { name, description, tag } };
            action.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto"), ColumnSpacing = 8, MinHeight = 44, Margin = new Thickness(0, 3) };
            Grid.SetColumn(words, 1);
            Grid.SetColumn(action, 2);
            grid.Children.Add(toggle);
            grid.Children.Add(words);
            grid.Children.Add(action);
            View = grid;
            Ask(false, first: true);
        }

        public Control View { get; }

        private ImportedSkill? Current() => model.Controller.Imported.FirstOrDefault(i => i.Id == id);

        public void Apply(ImportedSkill skill)
        {
            description.SetText(skill.Description);
            var mode = skill.Mode == ImportMode.Link ? ImportWords.Linked : ImportWords.Copied;
            var from = ImportWords.From(Title(skill.App)) + " · " + mode;
            tag.SetText(skill.OriginalMissing ? from + " · " + ImportWords.OriginalMissing : from);
        }

        private void Ask(bool ask, bool first = false)
        {
            if (asked == ask) return;
            asked = ask;
            Control next;
            if (ask)
            {
                next = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        LibraryUi.Text(ImportWords.RemoveAsk, 12, secondary: true),
                        LibraryButtons.Make(ImportWords.Remove, () => model.Controller.Remove(id), ButtonLook.Danger, "imported-remove-confirm-" + id),
                        LibraryButtons.Glyph("xmark", ImportWords.Cancel, "imported-remove-cancel-" + id, () => Ask(false)),
                    },
                };
            }
            else next = LibraryButtons.Glyph("trash", ImportWords.Remove, "imported-remove-" + id, () => Ask(true));
            action.Show(next, first ? null : LibraryUi.Ease(owner, LibraryUi.Fade));
        }
    }

    internal static string Title(ForeignApp app) => app switch
    {
        ForeignApp.ClaudeCode => ImportWords.ClaudeCode,
        ForeignApp.Codex => ImportWords.Codex,
        ForeignApp.Antigravity => ImportWords.Antigravity,
        ForeignApp.Cursor => ImportWords.Cursor,
        _ => ImportWords.Folders,
    };
}
