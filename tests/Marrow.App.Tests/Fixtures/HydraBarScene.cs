using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Views.Chat;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helper for Views/Chat/HydraDecorationGuardTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
// ED-NEW-lean-8: the scripted steps a guard run takes on a shown Hydra progress bar.
internal sealed class HydraBarScene(HydraProgressBar bar, Avalonia.Controls.Window window, List<TimelineEntry> entries, DateTimeOffset startedAt, DateTimeOffset? finishedAt)
{
    public void Add(ToolCall.Kind kind, double inSeconds)
    {
        entries.Add(HydraDecorationGuardTests.ToolEntry(kind, SwiftDate.Now.AddSeconds(inSeconds)));
        bar.UpdateEntries([.. entries]);
    }

    public void Hover(int? stave)
    {
        window.MouseMove(stave is { } index ? new Point(index * 6 + 1, HydraDecorationGuardTests.Top + 14) : new Point(299, 1));
        Dispatcher.UIThread.RunJobs();
    }

    public void ReduceMotion(bool on)
    {
        EnvironmentValues.SetAccessibilityReduceMotion(bar, on);
        Dispatcher.UIThread.RunJobs();
    }

    public void Theme(ThemeVariant theme)
    {
        window.RequestedThemeVariant = theme;
        Dispatcher.UIThread.RunJobs();
    }

    public void Status(HydraHeadInfo.Status status)
    {
        bar.UpdateTimes(startedAt, status == HydraHeadInfo.Status.Running ? null : finishedAt, status);
        Dispatcher.UIThread.RunJobs();
    }
}
