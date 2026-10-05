// PORT-ONLY: first-release feature unit SK-15 (DL-17, ONDEMAND-PLAN.md 4.3, skills-PLAN.md Q8). "Already available through your providers": read-only, what each provider reports itself (its synced skills, its own, the user's own).
// Nothing here downloads, copies or switches anything. The section folds open when a provider reports something and shut when nothing is reported (OD-0076).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pages;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class ProvidersSection : Decorator
{
    private readonly StackPanel groups = new() { Spacing = 10 };
    private readonly CzFoldRow fold;
    private string key = "";

    public ProvidersSection()
    {
        var heading = LibraryUi.Text(LibraryWords.ProvidersHeading, 13, secondary: true, weight: SwiftFont.Weight.Semibold);
        var note = LibraryUi.Text(LibraryWords.ProvidersNote, 12, secondary: true, wrap: true);
        var stack = new StackPanel { Spacing = 6, Children = { heading, note, LibraryUi.Panel(groups, 12, 0.04, new Thickness(14, 10)) } };
        fold = new CzFoldRow(stack, false);
        Child = fold;
    }

    public bool IsOpen => fold.Open;

    public IReadOnlyList<string> Providers => groups.Children.OfType<StackPanel>().Select(g => ((TextBlock)g.Children[0]).Text ?? "").ToList();

    public IReadOnlyList<string> SkillNames => groups.Children.OfType<StackPanel>().SelectMany(g => g.Children.OfType<Grid>().Select(r => ((TextBlock)r.Children[0]).Text ?? "")).ToList();

    public void Apply(IReadOnlyList<ProviderSkillGroup> reported)
    {
        fold.Open = reported.Count > 0;
        var next = string.Join("|", reported.Select(g => g.Provider + ":" + string.Join(",", g.Skills.Select(s => s.Name))));
        if (next == key || reported.Count == 0) return;
        key = next;
        groups.Children.Clear();
        foreach (var group in reported)
        {
            var list = new StackPanel { Spacing = 2 };
            list.Children.Add(LibraryUi.Text(group.Provider, 12, weight: SwiftFont.Weight.Semibold));
            foreach (var skill in group.Skills)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Height = 22 };
                var name = LibraryUi.Mono(skill.Name, 11.5);
                var description = LibraryUi.Text(skill.Description, 11.5, secondary: true, trim: true);
                name.VerticalAlignment = VerticalAlignment.Center;
                description.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(description, 1);
                row.Children.Add(name);
                row.Children.Add(description);
                list.Children.Add(row);
            }
            groups.Children.Add(list);
        }
    }
}
