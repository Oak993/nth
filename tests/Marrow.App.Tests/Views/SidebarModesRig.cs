using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// ED-NEW-sidebarmodes-1: SidebarModesTests' launched root view, stepped on the manual frame clock.
internal sealed record SidebarModesRig(LaunchHarness Launch, AppModel Model, RootView Root, AvaloniaWindow Window, ManualFrameSource Frames)
{
    public double ColumnWidth => Launch.Harness.Main.OnMain(() => Root.SidebarColumn.Bounds.Width);
    public double DetailX => Launch.Harness.Main.OnMain(() => Root.DetailClip.TranslatePoint(default, Root)!.Value.X);
    public FloatingSidebarPanel? Panel => Launch.Harness.Main.OnMain(() => Root.FloatingPanel);

    // One frame: the clock and the frame source move on together, then the model's posted changes and the layout run.
    public void Step(int milliseconds)
    {
        Launch.Harness.Main.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Launch.Harness.Main.OnMain(() => Frames.Advance(TimeSpan.FromMilliseconds(milliseconds)));
        Settle();
    }

    public void Settle()
    {
        Launch.Harness.Main.RunUntilIdle();
        Launch.Harness.Main.OnMain(() => { Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        Launch.Harness.Main.RunUntilIdle();
    }

    public void Finish() { for (var i = 0; i < 60; i++) Step(16); }

    public void OnMain(Action action) => Launch.Harness.Main.OnMain(action);
}
