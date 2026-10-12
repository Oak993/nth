using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Panes;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A narrow tab strip (web W3c) on a headless window, with the pane kinds it chose.</summary>
public sealed class NarrowTabStripRig : IDisposable
{
    public NarrowTabStripRig(double width, ThemeVariant? theme = null, FlowDirection direction = FlowDirection.LeftToRight)
    {
        Strip = new NarrowTabStrip { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Height = 44 };
        Window = new Window { Width = 1000, Height = 200, Content = Strip, FlowDirection = direction };
        if (theme is not null) Window.RequestedThemeVariant = theme;
        Window.Show();
        Layout();
    }

    public NarrowTabStrip Strip { get; }

    public Window Window { get; }

    public List<Marrow.Core.Models.PaneKind> Chosen { get; } = [];

    public void Choose() => Strip.TabChosen += Chosen.Add;

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public void Key(Key key)
    {
        Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Layout();
    }

    public void Dispose() => Window.Close();
}
