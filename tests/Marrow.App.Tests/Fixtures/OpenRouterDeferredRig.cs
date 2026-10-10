using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-stutterfix-2: an OpenRouter picker of 486 models over six makers in a shown window, its frames driven by hand.
internal sealed class OpenRouterDeferredRig : IDisposable
{
    public const int Count = 486;
    private static readonly string[] Makers = ["Acme", "Bolt", "Cirrus", "Delta", "Echo", "Fable"];
    private readonly TestRuntime runtime = new();
    private readonly ManualFrameSource frames = new();
    private readonly IDisposable source;
    public readonly OpenRouterPicker Picker;
    public readonly Avalonia.Controls.Window Window;
    public string? Picked;
    public OpenRouterDeferredRig()
    {
        source = FrameClock.Source.Override(frames);
        Picker = new OpenRouterPicker(Enumerable.Range(0, Count).Select(i => new PickerModel($"{Makers[i * 6 / Count].ToLowerInvariant()}/m{i}", "Model " + i, Makers[i * 6 / Count], "#10AA60")).ToArray(),
            "acme/m0", id => Picked = id, () => { });
        Window = new Avalonia.Controls.Window { Content = Picker, Height = 800 };
        Window.Show(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
    }
    public void Step(int ms) { runtime.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout(); }
    public ScrollViewer Scroll => Assert.Single(Picker.GetVisualDescendants().OfType<ScrollViewer>());
    public PickerRow[] Shown => Picker.GetVisualDescendants().OfType<PickerRow>().ToArray();
    public void Key(Key key) => Picker.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    public void Dispose() { Window.Close(); source.Dispose(); runtime.Dispose(); }
}
