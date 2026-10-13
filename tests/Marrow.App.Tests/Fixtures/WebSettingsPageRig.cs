using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// W10 (ED-NEW-web-w10-4): the small things the Visus and Tabula page tests share: a page shown in a window of its own on the manual frame clock, a control
/// found by its accessible name, a switch read and flipped through its automation peer, a segment or a menu item pressed.
/// </summary>
internal static class WebSettingsPageRig
{
    public static void Show(Control page, Action<Avalonia.Controls.Window> body, double width = 600, double height = 3000, double content = 520)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = new ScrollViewer { Content = new Border { Width = content, Child = page } } };
        window.Show();
        Settle(window);
        try { body(window); }
        finally { window.Close(); }
    }

    public static void Settle(Avalonia.Controls.Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    public static IEnumerable<T> AllNamed<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Where(control => AutomationProperties.GetName(control) == name);

    public static IReadOnlyList<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "")];

    public static void Toggle(Control root, string name)
    {
        var host = Named<SettingsSwitch>(root, name);
        var toggle = host.GetVisualDescendants().OfType<SmallSwitch>().Single();
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).Toggle();
    }

    public static bool IsOn(Control root, string name)
    {
        var toggle = Named<SettingsSwitch>(root, name).GetVisualDescendants().OfType<SmallSwitch>().Single();
        return Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).ToggleState == ToggleState.On;
    }

    /// <summary>Presses the <paramref name="index"/>th segment of a segmented picker.</summary>
    public static void Segment(Control picker, int index) =>
        picker.GetVisualDescendants().OfType<Button>().ElementAt(index).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The titles of a segmented picker, in order.</summary>
    public static IReadOnlyList<string> SegmentTitles(Control picker) =>
        [.. picker.GetVisualDescendants().OfType<Button>().Select(button => AutomationProperties.GetName(button) ?? "")];

    /// <summary>Picks an item of a menu button by its id and returns once the selection was raised.</summary>
    public static void Pick(CzMenuButton menu, string id) => menu.Select(id);

    public static void Press(CzCapsuleButton button) => button.Activate();
}
