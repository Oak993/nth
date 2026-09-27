using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

public sealed class ForEachRow(string id, List<string> disposed) : Border, IDisposable
{
    public string Id { get; } = id;
    public void Dispose() => disposed.Add(Id);
}

public sealed class ForEachHost : IDisposable
{
    public ForEachHost(Func<ForEachRow, Control>? decorate = null)
    {
        Rows = new ForEach<string, string>(Panel, id => id, id =>
        {
            var row = new ForEachRow(id, Disposed) { Width = 100, Height = 40 };
            Made.Add(row);
            return decorate?.Invoke(row) ?? row;
        }, (_, _) => { });
        Window = new Window { Width = 300, Height = 250, Content = Panel };
        Window.Show();
        Layout();
    }
    public StackPanel Panel { get; } = new() { Width = 100, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    public Window Window { get; }
    public ForEach<string, string> Rows { get; }
    public List<ForEachRow> Made { get; } = [];
    public List<string> Disposed { get; } = [];
    public void Layout() { Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    public void Dispose() => Window.Close();
    public Point Origin(Control row) => row.TranslatePoint(default, Panel)!.Value;
    public double Opacity(Control row)
    {
        var opacity = row.Opacity;
        foreach (var ancestor in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(row))
        {
            if (ReferenceEquals(ancestor, Panel)) break;
            opacity *= ancestor.Opacity;
        }
        return opacity;
    }
}
