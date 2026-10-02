using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// ED-236: the fake DWM cloak BootStageWindowTests records (moved out of the test class: one class per file).
internal sealed class CloakLog(AvaloniaWindow main, BootStageWindow[] boot)
{
    public List<(string Event, bool Visible, bool ShowActivated, double Elapsed, double ContentOpacity)> Events { get; } = [];
    public bool Accepts { get; init; } = true;

    public bool Cloak(AvaloniaWindow window, bool cloaked)
    {
        Assert.Same(main, window);
        Events.Add((cloaked ? "cloak" : "uncloak", main.IsVisible, main.ShowActivated, boot[0].Stage.Elapsed, ((Control)main.Content!).Opacity));
        return Accepts;
    }

    public void Shown() => Events.Add(("show", main.IsVisible, main.ShowActivated, boot[0].Stage.Elapsed, ((Control)main.Content!).Opacity));
}
