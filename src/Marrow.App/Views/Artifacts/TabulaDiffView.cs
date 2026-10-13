using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Diff;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Services.Git;
using Marrow.Services.Web;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W14, Tabula's Diff view (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.3 "a 'Diff' toggle compares
// any two versions (side by side or inline, the same diff renderer as Changes)", 6.1 "Diff compares per file"; the approved
// canvas index.html "Comparing v3 → v4"; ED-NEW-web-w14-1). No Swift counterpart. Inline is the Changes pane's own file
// card (a project's files each in one, collapsible) or its line list for a single file; side by side puts the Changes rows
// in two columns, the old version left, the new one right, row by row.

namespace Marrow.App.Views.Artifacts;

public sealed class TabulaDiffView : Border
{
    private readonly StackPanel column = new() { Spacing = 8, Margin = new Thickness(10, 10, 10, 14) };
    private readonly ScrollViewer scroll;
    private readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DiffFile> files = [];
    private TabulaDiffLayout layout;
    private bool project;
    private string? key;

    public TabulaDiffView()
    {
        scroll = new ScrollViewer
        {
            Content = column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Child = scroll;
        AutomationProperties.SetName(this, UiText.Get("tabula.diff.name"));
    }

    /// <summary>The files that differ (the Changes model).</summary>
    public IReadOnlyList<DiffFile> DiffFiles => files;

    public TabulaDiffLayout Layout => layout;

    /// <summary>The "no changes" words when the two versions are the same, else null.</summary>
    public string? EmptyText { get; private set; }

    /// <summary>Compares <paramref name="before"/> with <paramref name="after"/>; nothing is rebuilt when neither changed.</summary>
    public void Show(ArtifactSnapshot before, ArtifactSnapshot after, TabulaDiffLayout value)
    {
        var next = string.Create(CultureInfo.InvariantCulture, $"{after.Artifact.Thread}/{after.Artifact.Id}/{before.Version.Number}/{after.Version.Number}/{after.Artifact.Current}/{value}");
        if (next == key) return;
        key = next;
        layout = value;
        project = before.IsProject || after.IsProject;
        var name = ArtifactExport.Slug(after.Artifact.Title) + "." + ArtifactKinds.Extension(after.Artifact.Kind, after.Artifact.Language);
        files = ArtifactDiff.Between(before, after, name);
        EmptyText = files.Count == 0
            ? UiText.FormatWith(CultureInfo.InvariantCulture, "tabula.diff.no_changes", before.Version.Number, after.Version.Number)
            : null;
        Build();
    }

    private void Build()
    {
        column.Children.Clear();
        if (EmptyText is { } empty)
        {
            var words = new TextBlock { Text = empty, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            words.Foreground = PaneTokens.Text2.Brush(words);
            column.Children.Add(words);
            return;
        }
        foreach (var file in files)
        {
            if (layout == TabulaDiffLayout.SideBySide) column.Children.Add(SideBySide(file));
            else if (project) column.Children.Add(new DiffFileCard(file, collapsed.Contains(file.Path), () => Toggle(file.Path)));
            else column.Children.Add(new DiffLinesView(file, showsLineNumbers: true));
        }
    }

    private void Toggle(string path)
    {
        if (!collapsed.Remove(path)) collapsed.Add(path);
        Build();
    }

    private Control SideBySide(DiffFile file)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        var rows = ArtifactDiff.SideBySide(file);
        var at = 0;
        if (project)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = file.Path, FontFamily = TabulaTokens.Mono, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 2, 4, 6) };
            name.Foreground = PaneTokens.Text1.Brush(name);
            Grid.SetColumnSpan(name, 3);
            grid.Children.Add(name);
            at++;
        }
        foreach (var row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (row.Header is { } header)
            {
                var text = new TextBlock { Text = header, FontFamily = TabulaTokens.Mono, FontSize = 11, Margin = new Thickness(6, 6, 6, 2) };
                text.Foreground = PaneTokens.Text3.Brush(text);
                Grid.SetRow(text, at);
                Grid.SetColumnSpan(text, 3);
                grid.Children.Add(text);
            }
            else
            {
                Add(grid, row.Left, at, 0);
                Add(grid, row.Right, at, 2);
            }
            at++;
        }
        var rule = new Border { Width = 1 };
        rule.Background = PaneTokens.Line.Brush(rule);
        Grid.SetColumn(rule, 1);
        Grid.SetRowSpan(rule, Math.Max(1, at));
        grid.Children.Add(rule);
        return grid;
    }

    private static void Add(Grid grid, DiffLine? line, int row, int column)
    {
        Control cell = line is null ? new Border() : new DiffLineRow(line, showsLineNumbers: true);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }
}
