using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Fixtures;

// Extracted from EffortSliderPanelTests: a test file holds one test type and nothing else (R-TEST-3).
public sealed class EffortPanelEnv : IDisposable
{
    private readonly List<IDisposable> owned = [];
    public required AppModel Model;
    public required ChatThread Thread;
    public ManualTimeProvider Time = new();
    public ManualFrameSource Frames = new();
    public Avalonia.Controls.Window? Window;
    public void Own(IDisposable value) => owned.Add(value);
    public void Dispose() { Window?.Close(); for (var i = owned.Count - 1; i >= 0; i--) owned[i].Dispose(); }

    /// <summary>A real window of <paramref name="width"/> x 700 over the content, with the AppModel in its environment.</summary>
    public Avalonia.Controls.Window Show(Control content, double width = 330)
    {
        var host = new Border { Child = content, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        AppEnvironment.SetAppModel(host, Model);
        Window = new Avalonia.Controls.Window { Content = host, Width = width, Height = 700 };
        Window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return Window;
    }

    /// <summary>Time and frames move together, as a display's frames do.</summary>
    public void Step(int milliseconds)
    {
        Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }
}
