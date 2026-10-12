using System.Globalization;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Panes;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.8 and index.html .drw/.log/.net/.drwstrip; ED-NEW-web-w9-3. Virtualized rows, bounded service rings, one chrome update per 33ms.
public sealed class PreviewConsoleDrawer : Border, IDisposable
{
    private readonly ProjectPreviewBody owner;
    private readonly ListBox rows = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly ObservableCollection<object> displayed = [];
    private readonly TextBox search = new() { PlaceholderText = "Search", MinWidth = 90, Height = 24, FontSize = 11 };
    private readonly ComboBox levels = new() { ItemsSource = new[] { "All levels", "error", "warn", "info", "log", "debug" }, SelectedIndex = 0, Height = 24, FontSize = 11, MinWidth = 82 };
    private readonly Button strip;
    private readonly Button consoleTab;
    private readonly Button networkTab;
    private readonly Grid expanded = new() { RowDefinitions = new RowDefinitions("32,*") };
    private readonly DispatcherTimer timer;
    private bool network;
    private bool dirty;
    public PreviewConsoleDrawer(ProjectPreviewBody owner)
    {
        this.owner = owner; State.Load(owner.Tab.PreviewState);
        Margin = new Thickness(6); CornerRadius = new CornerRadius(10); ClipToBounds = true;
        BorderThickness = new Thickness(.5); BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); Background = new SolidColorBrush(Color.FromArgb(71, 0, 0, 0));
        strip = DevicePresetBar.Action("Console", () => { State.Restore(); Refresh(); }); strip.Height = 24;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(8, 4) };
        consoleTab = DevicePresetBar.Action("Console", () => { network = false; RenderRows(); });
        networkTab = DevicePresetBar.Action("Network", () => { network = true; RenderRows(); });
        foreach (var tab in new[] { consoleTab, networkTab })
        {
            tab.Height = 24; tab.Padding = new Thickness(10, 0); tab.CornerRadius = new CornerRadius(7);
            tab.ActualThemeVariantChanged += (_, _) => RefreshTabs(); tools.Children.Add(tab);
        }
        tools.Children.Add(levels); tools.Children.Add(search);
        tools.Children.Add(DevicePresetBar.Action("Clear", () => owner.Runtime.Diagnostics?.Clear()));
        var preserve = new CheckBox { Content = "Preserve log", FontSize = 11 };
        preserve.IsCheckedChanged += (_, _) => { if (owner.Runtime.Diagnostics is { } d) d.PreserveLog = preserve.IsChecked == true; }; tools.Children.Add(preserve);
        expanded.Children.Add(new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(rows, 1); expanded.Children.Add(rows);
        rows.ItemTemplate = new FuncDataTemplate<object>((item, _) => Row(item));
        rows.ItemsSource = displayed;
        search.TextChanged += (_, _) => RenderRows(); levels.SelectionChanged += (_, _) => RenderRows();
        owner.Runtime.Changed += Queue;
        owner.Runtime.DiagnosticsChanged += Queue;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => { if (dirty) { dirty = false; if (!IsStrip) RenderRows(); owner.Bar.Refresh(); strip.Content = "Console · " + (owner.Runtime.Diagnostics?.Errors ?? 0).ToString(CultureInfo.InvariantCulture) + " errors"; } });
        AttachedToVisualTree += (_, _) => timer.Start(); DetachedFromVisualTree += (_, _) => timer.Stop();
        Refresh();
    }
    public PreviewDrawerState State { get; } = new();
    public bool IsStrip => State.Open && State.Collapsed;
    public double ShownHeight => State.ShownHeight;
    public void Refresh()
    {
        Child = IsStrip ? strip : expanded;
        strip.Content = "Console · " + (owner.Runtime.Diagnostics?.Errors ?? 0).ToString(CultureInfo.InvariantCulture) + " errors";
        owner.Tab.SetPreviewState(owner.Tab.PreviewState with { ConsoleOpen = State.Open, ConsoleCollapsed = State.Collapsed, ConsoleHeight = State.Height });
        if (!owner.DrawerHeightChanged()) { Height = ShownHeight; IsVisible = State.Open; Margin = State.Open ? new Thickness(6) : new Thickness(0); }
        RenderRows();
    }
    private void Queue() => dirty = true;
    private void RenderRows()
    {
        RefreshTabs();
        var data = (network ? owner.Runtime.Diagnostics?.FindNetwork(search.Text ?? "").Cast<object>().ToArray()
            : owner.Runtime.Diagnostics?.FindConsole(search.Text ?? "", levels.SelectedIndex > 0 ? levels.SelectedItem as string : null).Cast<object>().ToArray()) ?? [];
        if (data.Length == 0) { displayed.Clear(); return; }
        var first = displayed.IndexOf(data[0]);
        if (first > 0) for (var i = 0; i < first; i++) displayed.RemoveAt(0);
        for (var i = 0; i < data.Length; i++)
            if (i >= displayed.Count) displayed.Add(data[i]); else if (!Equals(displayed[i], data[i])) displayed[i] = data[i];
        while (displayed.Count > data.Length) displayed.RemoveAt(displayed.Count - 1);
    }
    private void RefreshTabs()
    {
        consoleTab.Background = network ? Brushes.Transparent : PaneTokens.HoverFill.Brush(this);
        networkTab.Background = network ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        consoleTab.Foreground = network ? PaneTokens.Text2.Brush(this) : PaneTokens.Text1.Brush(this);
        networkTab.Foreground = network ? PaneTokens.Text1.Brush(this) : PaneTokens.Text2.Brush(this);
    }
    private Control Row(object? item)
    {
        var isNetwork = item is PreviewNetworkEntry;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions(isNetwork ? "44,*,44,70,60,60,Auto" : "42,*,Auto"), Margin = new Thickness(10, 5), ColumnSpacing = 8 };
        string text; string draft; bool failed;
        if (item is PreviewConsoleEntry log) { text = log.Text; draft = owner.Runtime.Diagnostics!.ToChat(log); failed = log.Level == "error"; }
        else if (item is PreviewNetworkEntry request) { text = request.Path; draft = owner.Runtime.Diagnostics!.ToChat(request); failed = request.Failed; }
        else return new Border();
        var label = new TextBlock { Text = text, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono"), TextWrapping = TextWrapping.Wrap, Foreground = failed ? new SolidColorBrush(Color.Parse(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "#ff8a8a" : "#7d0014")) : PaneTokens.Text1.Brush(this) };
        Grid.SetColumn(label, 1); line.Children.Add(label);
        var send = DevicePresetBar.Action("Send to chat", () => owner.Runtime.AddDraft(draft)); send.Height = 22; send.FontSize = 11; send.CornerRadius = new CornerRadius(11); send.BorderThickness = new Thickness(1); send.BorderBrush = PaneTokens.Line.Brush(this); Grid.SetColumn(send, isNetwork ? 6 : 2); line.Children.Add(send);
        ((StackPanel)send.Content!).Children.OfType<TextBlock>().Single().FontSize = 11;
        void Column(string value, int column)
        {
            var cell = new TextBlock { Text = value, FontSize = 12, FontFamily = label.FontFamily, Foreground = label.Foreground, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cell, column); line.Children.Add(cell);
        }
        if (item is PreviewConsoleEntry entry) Column(entry.Level, 0);
        if (item is PreviewNetworkEntry row)
        {
            Column(row.Method, 0); Column(row.Status.ToString(CultureInfo.InvariantCulture), 2); Column(row.Type, 3); Column(row.Size.ToString("0", CultureInfo.InvariantCulture) + " B", 4); Column(row.Milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms", 5);
            var details = new VisusPopover(label, () => Details(row)); line.Children.Add(details.Popup);
            line.DetachedFromVisualTree += (_, _) => details.SetOpen(false);
            label.PointerReleased += (_, e) => { details.SetOpen(true); e.Handled = true; };
        }
        return new Border { BorderThickness = new Thickness(0, 0, 0, .5), BorderBrush = PaneTokens.Line.Brush(this), Child = line };
    }
    private Control Details(PreviewNetworkEntry row)
    {
        var result = new TextBox { Text = row.Detail, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 320, FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12 };
        var stack = new StackPanel { Width = 420, Spacing = 8, Margin = new Thickness(12), Children = { result } };
        stack.Children.Add(DevicePresetBar.Action("Response preview", () => _ = Response()));
        stack.Children.Add(DevicePresetBar.Action("Send to chat", () => owner.Runtime.AddDraft(owner.Runtime.Diagnostics!.ToChat(row) + "\n" + result.Text)));
        async Task Response()
        {
            try { if (owner.Runtime.Diagnostics is { } d) result.Text = row.Detail + "\n" + await d.ResponsePreviewAsync(row); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException) { result.Text = row.Detail + "\nResponse unavailable"; }
        }
        return stack;
    }
    public void Dispose() { timer.Stop(); owner.Runtime.Changed -= Queue; owner.Runtime.DiagnosticsChanged -= Queue; }
}
