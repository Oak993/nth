using Avalonia.Controls;
using Marrow.UI.Port.Nth;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Nth;

// ED-NEW-lean-19 (lean pass L19): 300 rows arriving where a skeleton stood, in a window the size of Settings (780 x 580). The budget
// counts the transitions NthArrive.Rows puts on the rows (deterministic; Debug and Release alike).
[Collection("Headless")]
public sealed class NthArriveBudgetTests(HeadlessSession headless)
{
    // Measured: 600 on the unchanged code (two per row; limit then 900, 1.5x); 50 after L19 (rows 0-24 rise). Limit 1.3x.
    private const int TransitionsLimit = 65;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void Three_hundred_arriving_rows_create_transitions_within_budget() => headless.OnUiThread(() =>
    {
        var stack = new StackPanel();
        var rows = Enumerable.Range(0, 300).Select(_ => (Control)new Border { Height = 34 }).ToList();
        foreach (var row in rows) stack.Children.Add(row);
        var window = new Window { Width = 780, Height = 580, Content = new ScrollViewer { Content = stack } };
        window.Show();
        try
        {
            NthArrive.Rows(rows);
            var transitions = rows.Sum(row => row.Transitions?.Count ?? 0);
            Assert.True(transitions <= TransitionsLimit, $"300 arriving rows create {transitions} transitions (limit {TransitionsLimit})");
        }
        finally { window.Close(); }
    });
}
