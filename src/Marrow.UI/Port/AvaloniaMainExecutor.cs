using Avalonia.Threading;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY. Binds Core's MainActor to Avalonia's UI thread (ED-065). Marrow.App calls
/// <c>MainActor.Bind(AvaloniaMainExecutor.Instance)</c> once at start-up, before any window exists; a headless UI
/// test session binds it inside its dispatcher. Posts run FIFO at one priority, never inline (proved on Avalonia
/// 12.1.2 headless by draft B's probe and by this prototype's headless tests).
/// </summary>
public sealed class AvaloniaMainExecutor : IMainExecutor
{
    public static AvaloniaMainExecutor Instance { get; } = new();

    private AvaloniaMainExecutor() { }

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Enqueue(Action work) => Dispatcher.UIThread.Post(work, DispatcherPriority.Default);
}
