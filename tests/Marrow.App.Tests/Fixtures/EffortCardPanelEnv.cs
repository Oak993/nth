using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Extracted from EffortSliderCardTests: a test file holds one test type and nothing else (R-TEST-3).
public sealed class EffortCardPanelEnv : IDisposable
{
    private readonly List<IDisposable> owned = [];
    public required AppModel Model;
    public required ChatThread Thread;
    public required ManualTimeProvider Time;
    public required ManualFrameSource Frames;
    public void Own(IDisposable value) => owned.Add(value);
    public void Dispose() { for (var i = owned.Count - 1; i >= 0; i--) owned[i].Dispose(); }
    public void Step(int ms) { Time.Advance(TimeSpan.FromMilliseconds(ms)); Frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
}
