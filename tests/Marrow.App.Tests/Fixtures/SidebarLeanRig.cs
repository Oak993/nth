using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Helper for Views/Sidebar/SidebarLeanBudgetTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
// ED-NEW-lean-15: a shown sidebar of many threads, ticked and typed into on the main loop.
internal sealed class SidebarLeanRig(AppModelHarness harness, ManualFrameSource frames, AvaloniaWindow window, SidebarView view, AppModel model)
{
    public AppModelHarness Harness => harness;
    public AvaloniaWindow Window => window;
    public SidebarView View => view;
    public AppModel Model => model;

    public void Tick(int milliseconds)
    {
        harness.Main.AdvanceMilliseconds(milliseconds);
        harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
    }

    private TextBox? box; // found once: a search of the whole tree per keystroke would be the test's own cost, not the list's

    public void Type(string text, int then = 16)
    {
        harness.Main.OnMain(() => (box ??= SearchBox(view)).Text = text);
        Tick(then);
    }

    public void OnMain(Action body) => harness.Main.OnMain(body);
}
