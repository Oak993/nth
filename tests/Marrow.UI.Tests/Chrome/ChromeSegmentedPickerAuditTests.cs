using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Audit fixes for UI/Chrome/ChromeSegmentedPicker.swift (B3 52bc792): the segment button :55-71, the strip's
// .fixedSize() :40, the symbol's font :56-58, option equality :33. Expected values are read off the Swift, not the C#.
[Collection("Headless")]
public sealed class ChromeSegmentedPickerAuditTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width = 400)
    {
        var window = new Window { Width = width, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static List<Button> Segments(Control picker) =>
        VisualQuery.Descendants<Button>(picker).OrderBy(b => b.TranslatePoint(new Point(0, 0), picker)!.Value.X).ToList();

    private static Button Segment(Control picker, string title) => Assert.Single(Segments(picker), b => Peer(b).GetName() == title);

    private static ChromeSegmentedPicker<int> IntPicker(CellSample<int> cell) =>
        new(new[] { (1, "One"), (2, "Two"), (3, "Three") }, new Binding<int>(() => cell.Value, v => cell.Value = v));

    [Fact]
    [Trait("Req", "PR-THEME-184")] // ChromeSegmentedPicker.swift:55-71: activating a segment through accessibility runs its action, selection = option.value
    public void Segment_automation_invoke_selects_the_option() => headless.OnUiThread(() =>
    {
        using var frameSource = FrameClock.Source.Override(new ManualFrameSource());
        var cell = new CellSample<int>(1);
        var picker = IntPicker(cell);
        var window = Show(picker);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(Peer(Segment(picker, "Two")));
        invoke.Invoke();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, cell.Value);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-THEME-184")] // ChromeSegmentedPicker.swift:40: .fixedSize(): the strip keeps its ideal width under a narrower proposal
    public void Strip_keeps_its_natural_width_under_a_narrower_parent() => headless.OnUiThread(() =>
    {
        var alone = IntPicker(new CellSample<int>(1));
        var aloneWindow = Show(alone);
        var natural = alone.Bounds.Width;
        aloneWindow.Close();
        var picker = IntPicker(new CellSample<int>(1));
        var window = Show(new Grid { Width = 60, HorizontalAlignment = HorizontalAlignment.Left, Children = { picker } });
        Assert.True(natural > 60);
        Assert.Equal(natural, picker.Bounds.Width, 0.5);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });
}
