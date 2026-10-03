using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0088 (ED-253, round 4): the transients open over a window (WindowTransients), which the Settings window's Escape closes
// newest first. A popover opened from inside another popover (the MCP editor's Kind picker, MCPSettingsPage.cs:642) hangs from
// a control in the outer popover's own top level (a PopupRoot on Windows), not from the window: it is still over the window,
// and newest. Avalonia 12.1.2 headless cannot open a native popup (CreatePopup returns null), so the chain is built as
// Popup.Open builds it (HeadlessPopupHost.Open: a PopupRoot over its parent top level, its logical parent the popup).
[Collection("Headless")]
public sealed class WindowTransientsTests(HeadlessSession headless)
{
    // A popover's own top level, shown (its content attached to it) as Popup.Open shows it.
    private static PopupRoot Open(Popup popup, TopLevel parent, Control content)
    {
        var root = HeadlessPopupHost.Open(popup, parent, content);
        root.Show();
        Dispatcher.UIThread.RunJobs();
        return root;
    }

    private static Window Shown(Control content)
    {
        var window = new Window { Width = 300, Height = 200, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_popover_opened_inside_another_popover_is_the_newest_over_the_window_and_closes_first() => headless.OnUiThread(() =>
    {
        var anchor = new Button();
        var window = Shown(anchor);
        var kindButton = new Button();
        var editor = new Popup { PlacementTarget = anchor };
        var kind = new Popup { PlacementTarget = kindButton };
        using var scene = new TransientScene();
        try
        {
            var editorRoot = Open(editor, window, new StackPanel { Children = { kindButton } });
            var kindRoot = Open(kind, editorRoot, new Border());
            Assert.Same(editorRoot, TopLevel.GetTopLevel(kindButton)); // the inner anchor is in the editor's own top level
            scene.Show(editor, anchor, "editor");
            scene.Show(kind, kindButton, "kind");
            Assert.True(WindowTransients.DismissNewest(window));
            Assert.Equal(["kind"], scene.Dismissed); // the Kind picker closes; the editor (and its draft) stays
            Assert.True(WindowTransients.IsShown(editor));
            Assert.True(WindowTransients.DismissNewest(window));
            Assert.Equal(["kind", "editor"], scene.Dismissed);
            Assert.False(WindowTransients.DismissNewest(window)); // nothing left: the window's own Escape closes it
            kindRoot.Dispose();
            editorRoot.Dispose();
        }
        finally { window.Close(); }
    });

    // The popup itself placed in the tree of the outer popover's content (a PlainControls.Popover is a child of its row): the
    // same walk, through the popup the inner root hangs from.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_popover_whose_popup_sits_in_another_popovers_content_is_over_the_same_window() => headless.OnUiThread(() =>
    {
        var anchor = new Button();
        var window = Shown(anchor);
        var kindButton = new Button();
        var kind = new Popup { PlacementTarget = kindButton };
        var editor = new Popup { PlacementTarget = anchor };
        using var scene = new TransientScene();
        try
        {
            var editorRoot = Open(editor, window, new StackPanel { Children = { kindButton, kind } });
            var inner = new Border();
            var kindRoot = Open(kind, editorRoot, new StackPanel { Children = { inner } });
            scene.Show(editor, anchor, "editor");
            scene.Show(kind, inner, "kind"); // registered by a control inside the inner popover itself
            Assert.True(WindowTransients.DismissNewest(window));
            Assert.Equal(["kind"], scene.Dismissed);
            kindRoot.Dispose();
            editorRoot.Dispose();
        }
        finally { window.Close(); }
    });

    // A popup with no placement target opens over the top level of its nearest logical ancestor (Popup.Open): the same walk.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_popover_without_a_placement_target_is_over_the_window_of_the_control_that_holds_it() => headless.OnUiThread(() =>
    {
        var anchor = new Button();
        var window = Shown(anchor);
        var bare = new Popup();
        var editor = new Popup { PlacementTarget = anchor };
        using var scene = new TransientScene();
        try
        {
            var editorRoot = Open(editor, window, new StackPanel { Children = { new Button(), bare } });
            var inner = new Border();
            var bareRoot = Open(bare, editorRoot, new StackPanel { Children = { inner } });
            scene.Show(editor, anchor, "editor");
            scene.Show(bare, inner, "bare");
            Assert.Same(window, WindowTransients.WindowOf(inner));
            Assert.True(WindowTransients.DismissNewest(window));
            Assert.Equal(["bare"], scene.Dismissed);
            bareRoot.Dispose();
            editorRoot.Dispose();
        }
        finally { window.Close(); }
    });

    // Another window's popovers (the main window's, or a popover of a popover there) are not over this window: its Escape
    // leaves them open and closes the window.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_window_escape_never_closes_the_popovers_of_another_window() => headless.OnUiThread(() =>
    {
        var settingsAnchor = new Button();
        var mainAnchor = new Button();
        var settings = Shown(settingsAnchor);
        var main = Shown(mainAnchor);
        var mainInner = new Button();
        var mainPopover = new Popup { PlacementTarget = mainAnchor };
        var mainNested = new Popup { PlacementTarget = mainInner };
        var settingsPopover = new Popup { PlacementTarget = settingsAnchor };
        using var scene = new TransientScene();
        try
        {
            var root = Open(mainPopover, main, new StackPanel { Children = { mainInner } });
            scene.Show(settingsPopover, settingsAnchor, "settings");
            scene.Show(mainPopover, mainAnchor, "main");
            scene.Show(mainNested, mainInner, "main nested");
            Assert.True(WindowTransients.DismissNewest(settings));
            Assert.Equal(["settings"], scene.Dismissed); // newest over Settings, past the main window's newer two
            Assert.False(WindowTransients.DismissNewest(settings));
            Assert.Equal(["settings"], scene.Dismissed);
            Assert.True(WindowTransients.IsShown(mainPopover));
            Assert.True(WindowTransients.IsShown(mainNested));
            Assert.True(WindowTransients.DismissNewest(main));
            Assert.Equal(["settings", "main nested"], scene.Dismissed);
            root.Dispose();
        }
        finally
        {
            settings.Close();
            main.Close();
        }
    });
}
