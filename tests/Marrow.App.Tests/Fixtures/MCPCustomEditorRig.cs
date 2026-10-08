using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Fixtures;

// The custom MCP server editor shown through the real popover presenter in a window overlay (MCPCustomEditorRealPresenterTests).
internal sealed class MCPCustomEditorRig : IDisposable
{
    public TestRuntime Runtime = new(); public AppModelHarness Harness = new(withRuntime: false); public ManualFrameSource Frames = new();
    public IDisposable FrameScope, Seam; public ReviewOverlayPresenter Presenter; public AppModel Model = null!; public Avalonia.Controls.Window Window = null!; public MCPCustomSection Section = null!;
    public MCPCustomEditorRig()
    {
        FrameScope = FrameClock.Source.Override(Frames); Presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); Seam = Presentation.Presenter.Override(Presenter);
        Model = Runtime.OnMain(() => new AppModel());
        Runtime.OnMain(() =>
        {
            Section = new MCPCustomSection(""); AppEnvironment.SetAppModel(Section, Model);
            var host = new Border { Child = Section, Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) };
            Window = new Avalonia.Controls.Window { Width = 800, Height = 800, Content = new Border { Background = Brushes.Transparent, Child = host } }; SettingsProviderPopupLayers.Install(Window); Window.Show();
        });
        Settle();
    }
    // Everything the page does on the main actor runs as a main-loop step, and its follow-ups (observation callbacks, the popover's frames) are pumped by Settle.
    public void Do(Action action) { Runtime.OnMain(action); Settle(); }
    public void Settle(int milliseconds = 600) { for (var i = 0; i < milliseconds / 20; i++) { Runtime.Advance(TimeSpan.FromMilliseconds(20)); Frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs(); } Runtime.RunUntilIdle(); Window.UpdateLayout(); }
    public MCPCustomEditor? Editor => Window.GetVisualDescendants().OfType<MCPCustomEditor>().FirstOrDefault();
    public void Press(Point at) => Do(() => { Window.MouseMove(at, RawInputModifiers.None); Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); });
    public void Click(Control target, Point? local = null) => Press(target.TranslatePoint(local ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value);
    public void Key(Key key) => Do(() => { Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.Escape, null); Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.Escape, null); });
    public void OpenAdd() => Click(Window.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Add server") && b.Bounds.Width > 100));
    public void Dispose() { try { Runtime.OnMain(() => Window.Close()); Dispatcher.UIThread.RunJobs(); } finally { Seam.Dispose(); FrameScope.Dispose(); Harness.Dispose(); Runtime.Dispose(); } }
}
