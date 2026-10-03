using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Chat;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Every row a mounted timeline has laid out, with the controls in each that would be asked to leave the tree (the row, and the first text inside it), so a test can
/// say what a pass of the timeline's body kept: the same view, in the same stack, in the same order, never taken out of the tree and put back.
/// </summary>
internal sealed class TimelineRowWatch
{
    internal readonly List<string> Detached = [];
    internal readonly Dictionary<string, Control> Rows = [];
    internal StackPanel Stack = null!;

    internal static TimelineRowWatch Of(TimelineMount mount)
    {
        var watch = new TimelineRowWatch();
        foreach (var (id, _, row) in mount.Blocks())
        {
            watch.Rows[id] = row;
            row.DetachedFromVisualTree += (_, _) => watch.Detached.Add(id);
            if (row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault() is { } text) text.DetachedFromVisualTree += (_, _) => watch.Detached.Add(id + " (text)");
        }
        watch.Stack = Assert.IsType<StackPanel>(watch.Rows.Values.First().Parent);
        return watch;
    }

    /// <summary>Every row that was there before (but those named in <paramref name="gone"/>) is the same view in the same stack, in the same order, and none of them left the tree.</summary>
    internal void AssertKept(TimelineMount mount, string what, params string[] gone)
    {
        Assert.Empty(Detached);
        var now = mount.Blocks().ToDictionary(block => block.Id, block => block.Row);
        foreach (var (id, row) in Rows.Where(pair => !gone.Contains(pair.Key)))
        {
            Assert.True(now.TryGetValue(id, out var current), $"{what}: the row {id} is gone");
            Assert.Same(row, current); Assert.Same(Stack, current.Parent);
        }
        var order = mount.Blocks().Select(block => block.Id).Where(id => Rows.ContainsKey(id) && !gone.Contains(id)).ToList();
        Assert.Equal(Rows.Keys.Where(id => !gone.Contains(id)), order);
        AssertInOrder(mount);
    }

    /// <summary>The stack holds the rows in the order of the blocks.</summary>
    internal void AssertInOrder(TimelineMount mount) => Assert.Equal(mount.Blocks().Select(block => block.Row), Stack.Children.OfType<DisplayBlockView>());
}
