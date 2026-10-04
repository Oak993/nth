using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Extracted from EffortSliderChipRecordsTests: a test file holds one test type and nothing else (R-TEST-3).
public sealed class EffortChipRig : IDisposable
{
    private readonly List<IDisposable> owned = [];
    public required AppModel Model;
    public required ChatThread Thread;
    public required ModelEffortButton Chip;
    public required Avalonia.Controls.Window Window;
    public required RecordingPresenter Presenter;
    public Button Button => Chip.GetVisualDescendants().OfType<Button>().Single();
    public Grid Frame => Assert.IsType<Grid>(Assert.IsType<Border>(Button.Content).Child);
    public void Own(IDisposable value) => owned.Add(value);
    public void Dispose() { Window.Close(); for (var i = owned.Count - 1; i >= 0; i--) owned[i].Dispose(); }
    public void Click() { Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
    public static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); } // a layout pass over what the jobs changed
    public IEnumerable<TextBlock> Texts => Chip.GetVisualDescendants().OfType<TextBlock>();
    public TextBlock Text(string text) => Texts.Single(t => t.Text == text);
    public SwiftImage? Symbol(string name) => Chip.GetVisualDescendants().OfType<SwiftImage>().SingleOrDefault(i => i.SymbolName == name);
    public double Left(Control control) => control.TranslatePoint(default, Frame)!.Value.X;
    public double Right(Control control) => Left(control) + control.Bounds.Width;
}
