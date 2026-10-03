using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

/// <summary>OD-0076 (ED-261 T1-T3): the About update views swap their content over 0.18 s on the frame clock; a test that reads the state a swap
/// ends in lets the swap finish first, so the content that left is gone from the tree.</summary>
internal static class AboutSwap
{
    /// <summary>The content on show: a swap puts the new content after the old one that is still leaving.</summary>
    public static T Live<T>(Control root) where T : Visual => root.GetVisualDescendants().OfType<T>().Last();

    public static void Settle(TestRuntime runtime, ManualFrameSource frames)
    {
        runtime.Time.Advance(TimeSpan.FromMilliseconds(300)); frames.Advance(TimeSpan.FromMilliseconds(300)); Dispatcher.UIThread.RunJobs();
    }
}
