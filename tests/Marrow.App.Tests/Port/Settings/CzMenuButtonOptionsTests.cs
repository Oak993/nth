using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Settings;

// VT-13 (voice-SPEC V-16.3.1; ED-312 CZ-12.12): a menu button whose options and shown value follow the data without a cut: the Language row with no item checked
// (language default D) and the Microphone row whose list follows the devices. The shared control gains SetOptions; its constructor and Select are unchanged.
[Collection("Headless")]
public sealed class CzMenuButtonOptionsTests(HeadlessSession headless)
{
    private static void With(Control control, Action<HostWindow> body)
    {
        using var runtime = new TestRuntime();
        using var presenter = Presentation.Presenter.Override(new ReviewOverlayPresenter(Presentation.Presenter.Current));
        var window = new HostWindow { Width = 700, Height = 500, Content = control };
        SettingsProviderPopupLayers.Install(window);
        window.Show(); window.UpdateLayout();
        try { body(window); } finally { window.Close(); }
    }

    [Fact]
    [Trait("Decision", "ED-312")] // V-16.3.1 D: no item is checked and the button shows its own text
    public void No_item_can_be_checked_and_the_button_shows_its_own_text() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Language", [("auto", "Auto-detect"), ("en", "English")], "auto");
        menu.SetOptions([("auto", "Auto-detect"), ("en", "English")], null, "Asked on first use");
        Assert.Null(menu.SelectedId);
        Assert.All(menu.Items, item => Assert.False(item.Checked));
        Assert.Equal("Asked on first use", menu.ValueLabel.Text);
        var peer = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(menu.Items[0]));
        Assert.False(peer.IsSelected);
    });

    [Fact]
    [Trait("Decision", "ED-312")] // opening a menu with nothing checked must not throw and focuses the first item
    public void A_menu_with_no_checked_item_opens_on_its_first_item() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Language", [("auto", "Auto-detect"), ("en", "English")], "auto");
        menu.SetOptions([("auto", "Auto-detect"), ("en", "English")], null, "Asked on first use");
        With(menu, _ =>
        {
            menu.OpenMenu();
            Assert.True(menu.IsOpen);
            Assert.True(menu.Items[0].IsFocused);
            menu.CloseMenu();
        });
    });

    [Fact]
    [Trait("Decision", "ED-312")] // the options are replaced, the check follows the id, and no selection event is raised for a data change
    public void New_options_replace_the_items_check_the_selected_id_and_raise_no_selection_event() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Microphone", [("", "Windows default")], "");
        var raised = new List<string>();
        menu.SelectionChanged += raised.Add;
        menu.SetOptions([("", "Windows default"), ("a", "Headset"), ("b", "Array")], "b");
        Assert.Equal(["", "a", "b"], menu.Items.Select(i => i.Id));
        Assert.Equal("b", menu.SelectedId);
        Assert.Equal([false, false, true], menu.Items.Select(i => i.Checked));
        Assert.Equal("Array", menu.ValueLabel.Text);
        Assert.Empty(raised);
        menu.Select("a");
        Assert.Equal(["a"], raised);
    });

    [Fact]
    [Trait("Decision", "ED-312")] // OD-0076: a changed value crossfades and its width eases; nothing cuts
    public void A_changed_value_crossfades_and_the_width_eases_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var bound = FrameClock.Source.Override(frames);
        var menu = new CzMenuButton("Microphone", [("", "Windows default"), ("a", "A much longer microphone name")], "");
        var width = menu.ValueWidth;
        menu.SetOptions([("", "Windows default"), ("a", "A much longer microphone name")], "a");
        Assert.Equal(0, menu.ValueLabel.IncomingOpacity);
        Assert.Equal(1, menu.ValueLabel.OutgoingOpacity);
        Assert.Equal(width, menu.ValueWidth);
        runtime.AdvanceMilliseconds(120); frames.Advance(TimeSpan.FromMilliseconds(120));
        Assert.InRange(menu.ValueLabel.IncomingOpacity, .001, .999);
        Assert.InRange(menu.ValueWidth, width + .01, 1000);
        runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, menu.ValueLabel.IncomingOpacity);
        Assert.Equal(0, menu.ValueLabel.OutgoingOpacity);
    });

    [Fact]
    [Trait("Decision", "ED-312")] // the same text again is no change at all: no crossfade starts
    public void The_same_value_starts_no_crossfade() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var bound = FrameClock.Source.Override(frames);
        var menu = new CzMenuButton("Microphone", [("", "Windows default"), ("a", "Headset")], "");
        menu.SetOptions([("", "Windows default"), ("a", "Headset"), ("b", "Array")], "");
        Assert.Equal(1, menu.ValueLabel.IncomingOpacity);
        Assert.Equal(0, menu.ValueLabel.OutgoingOpacity);
        Assert.Equal(3, menu.Items.Count);
    });

    [Fact]
    [Trait("Decision", "ED-312")] // an open menu (or one still fading out) keeps its items; the new options arrive once it is gone
    public void A_change_while_the_menu_is_open_waits_until_it_has_faded_out() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Microphone", [("", "Windows default"), ("a", "Headset")], "");
        With(menu, _ =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var bound = FrameClock.Source.Override(frames);
            menu.OpenMenu();
            runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
            menu.SetOptions([("", "Windows default"), ("a", "Headset"), ("b", "Array")], "b");
            Assert.Equal(2, menu.Items.Count);
            Assert.Equal("", menu.SelectedId);
            menu.CloseMenu();
            Assert.Equal(2, menu.Items.Count);
            runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Equal(2, menu.Items.Count);
            runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(["", "a", "b"], menu.Items.Select(i => i.Id));
            Assert.Equal("b", menu.SelectedId);
            Assert.Equal("Array", menu.ValueLabel.Text);
        });
    });

    [Fact]
    [Trait("Decision", "ED-312")] // the constructor's programming-error rules hold for new options too
    public void Empty_duplicate_or_unknown_options_throw() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Language", [("a", "A"), ("b", "B")], "a");
        Assert.Throws<ArgumentException>(() => menu.SetOptions([], "a"));
        Assert.Throws<ArgumentException>(() => menu.SetOptions([("a", "A"), ("a", "B")], "a"));
        Assert.Throws<ArgumentException>(() => menu.SetOptions([("a", "A")], "zzz"));
        Assert.Throws<ArgumentException>(() => menu.SetOptions([("a", "A")], null));
        Assert.Equal(["a", "b"], menu.Items.Select(i => i.Id));
        Assert.Equal("a", menu.SelectedId);
    });
}
