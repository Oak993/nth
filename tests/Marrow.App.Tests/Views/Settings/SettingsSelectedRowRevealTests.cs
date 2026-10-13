using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Owner ruling CU-SETTINGS-REVEAL (2026-10-13), ED-NEW-settings-reveal-1.
// Real Settings rows, viewport geometry and input; the existing glide is the independent motion control.
[Collection("Headless")]
public sealed class SettingsSelectedRowRevealTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private static ScrollViewer Sidebar(Control view) => Assert.Single(Nodes<ScrollViewer>(Field<Control>(view, "sidebar")));
    private static SidebarRow Nav(Control view, SettingsPage page) => Nodes<SidebarRow>(Field<Control>(view, "sidebar")).Single(row => Text(row).Contains(page.Title));
    private static void Inside(Control view, SettingsPage page)
    {
        var scroll = Sidebar(view); var row = Nav(view, page);
        var top = row.TranslatePoint(default, scroll)!.Value.Y;
        Assert.InRange(top, -0.001, scroll.Viewport.Height - row.Bounds.Height + 0.001);
        Assert.True(row.IsSelected);
    }
    private static void Settle(AppModelHarness h, Avalonia.Controls.Window window)
    {
        for (var i = 0; i < 90; i++) { Tick(h, 1000 / 60.0); Drain(window); }
    }
    private static void Glide(Control view, SettingsPage page, AppModelHarness h, Avalonia.Controls.Window window)
    {
        var scroll = Sidebar(view); var row = Nav(view, page); var from = scroll.Offset.Y;
        var top = row.TranslatePoint(default, scroll)!.Value.Y;
        var target = Math.Clamp(from + (top < 0 ? top : Math.Max(0, top + row.Bounds.Height - scroll.Viewport.Height)), 0, scroll.Extent.Height - scroll.Viewport.Height);
        Assert.NotEqual(from, target);
        // A separate driver runs the sidebar's existing programmatic glide over the same distance.
        var controlOffset = new Vector(0, from);
        var control = new ScrollDriver(() => controlOffset, value => controlOffset = value,
            () => new Vector(0, scroll.Extent.Height - scroll.Viewport.Height), () => false);
        control.ScrollTo(new Vector(0, target), SoftScroll.KeyAnimation);
        try
        {
            var previous = from; var previousControl = from;
            for (var i = 0; i < 90; i++)
            {
                Tick(h, 1000 / 60.0); Drain(window);
                Assert.InRange(Math.Abs(scroll.Offset.Y - previous), 0, Math.Abs(controlOffset.Y - previousControl) + 0.001);
                if (i == 0) { Assert.NotEqual(from, scroll.Offset.Y); Assert.False(Field<StackPanel>(view, "rows").IsHitTestVisible); }
                previous = scroll.Offset.Y; previousControl = controlOffset.Y;
            }
            Inside(view, page);
            Assert.Equal(target, scroll.Offset.Y, 3);
            Assert.True(Field<StackPanel>(view, "rows").IsHitTestVisible);
        }
        finally { control.Stop(); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_Models_with_the_existing_glide() => Request(SettingsPage.Models);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_Pills_with_the_existing_glide() => Request(SettingsPage.Pills);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_SourceControl_with_the_existing_glide() => Request(SettingsPage.SourceControl);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_Shortcuts_with_the_existing_glide() => Request(SettingsPage.Shortcuts);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_Archive_with_the_existing_glide() => Request(SettingsPage.Archive);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_reveals_About_with_the_existing_glide() => Request(SettingsPage.About);

    private void Request(SettingsPage page)
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = page; Drain(window);
            Assert.Equal(0, Sidebar(view).Offset.Y); // no click-frame jump
            Glide(view, page, h, window);
            var offset = Sidebar(view).Offset;
            SettingsNavigation.Shared.RequestedPage = page; Drain(window); Settle(h, window);
            Assert.Equal(offset, Sidebar(view).Offset); // an already fully visible row stays put
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_request_waiting_before_open_reveals_the_selected_row_after_layout()
    {
        With(() => { SettingsNavigation.Shared.RequestedPage = SettingsPage.About; return new SettingsView(); }, (view, _, h, window) =>
        {
            Assert.Equal(0, Sidebar(view).Offset.Y); Glide(view, SettingsPage.About, h, window);
            Assert.Null(SettingsNavigation.Shared.RequestedPage);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Clearing_search_reveals_the_selected_row_when_the_full_list_returns()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            field.Text = "About"; Drain(window); Settle(h, window); Inside(view, SettingsPage.About);
            field.Text = ""; Drain(window); Glide(view, SettingsPage.About, h, window);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Search_return_reveals_the_first_page_above_the_viewport()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            var scroll = Sidebar(view);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Settle(h, window);
            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            field.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Drain(window);
            Glide(view, SettingsPage.General, h, window);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_visible_row_click_and_visible_external_selection_leave_the_list_in_place()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Glide(view, SettingsPage.About, h, window);
            var scroll = Sidebar(view); var offset = scroll.Offset;
            Click(window, Nav(view, SettingsPage.Shortcuts)); Settle(h, window); Inside(view, SettingsPage.Shortcuts);
            Assert.Equal(offset, scroll.Offset);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Drain(window); Settle(h, window); Inside(view, SettingsPage.Models);
            Assert.Equal(offset, scroll.Offset);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Reduce_motion_reveals_at_once_and_keeps_the_normal_150_ms_freeze()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Inside(view, SettingsPage.About);
            var list = Field<StackPanel>(view, "rows"); Assert.False(list.IsHitTestVisible);
            Tick(h, 149); Drain(window); Assert.False(list.IsHitTestVisible);
            Tick(h, 2); Drain(window); Assert.True(list.IsHitTestVisible);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Reopening_a_retained_view_reveals_its_remembered_page()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Settle(h, window);
            var host = Assert.IsType<Border>(view.Parent); host.Child = null; Drain(window);
            Sidebar(view).Offset = default; host.Child = view; Drain(window);
            Glide(view, SettingsPage.About, h, window);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_partially_clipped_bottom_row_is_revealed_fully()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            var scroll = Sidebar(view); var row = Nav(view, SettingsPage.Models);
            var top = row.TranslatePoint(default, scroll)!.Value.Y;
            scroll.Offset = new Vector(0, top + row.Bounds.Height / 2 - scroll.Viewport.Height); Drain(window); Tick(h, 160);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Drain(window); Glide(view, SettingsPage.Models, h, window);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_partially_clipped_top_row_is_revealed_fully()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            var scroll = Sidebar(view); var row = Nav(view, SettingsPage.Customization);
            var top = row.TranslatePoint(default, scroll)!.Value.Y;
            scroll.Offset = new Vector(0, top + row.Bounds.Height / 2); Drain(window); Tick(h, 160);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Customization; Drain(window); Glide(view, SettingsPage.Customization, h, window);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Reopening_an_attached_hidden_panel_reveals_its_remembered_page()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Settle(h, window);
            var host = Assert.IsType<Border>(view.Parent); host.IsVisible = false; Drain(window);
            Sidebar(view).Offset = default; host.IsVisible = true; Drain(window);
            Glide(view, SettingsPage.About, h, window);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_external_request_excluded_by_search_restores_its_row_and_reveals_it()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            field.Text = "theme"; Drain(window); Settle(h, window);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            Assert.Equal("", field.Text ?? ""); Glide(view, SettingsPage.About, h, window);
            field.Text = "About"; Drain(window); Settle(h, window);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window); Settle(h, window);
            Assert.Equal("About", field.Text); Inside(view, SettingsPage.About);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_new_visible_selection_stops_an_earlier_reveal_without_moving_again()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            Tick(h, 1000 / 60.0); Drain(window); Assert.True(Sidebar(view).Offset.Y > 0);
            var at = Sidebar(view).Offset;
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Appearance; Drain(window); Settle(h, window);
            Inside(view, SettingsPage.Appearance); Assert.Equal(at, Sidebar(view).Offset);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_visible_row_click_before_the_first_reveal_frame_keeps_the_offset_unchanged()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            // First prove the external reveal exists, then request it again from the top for a click before its first frame.
            Glide(view, SettingsPage.About, h, window);
            Sidebar(view).Offset = default; Drain(window); Tick(h, 160);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            Click(window, Nav(view, SettingsPage.Appearance)); Settle(h, window);
            Inside(view, SettingsPage.Appearance); Assert.Equal(0, Sidebar(view).Offset.Y);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_harmless_sidebar_key_keeps_reveal_ownership_for_the_next_selection()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            Assert.Single(Nodes<Button>(Nav(view, SettingsPage.General))).Focus();
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            Tick(h, 1000 / 60.0); Drain(window); Assert.True(Sidebar(view).Offset.Y > 0);
            var at = Sidebar(view).Offset;
            window.KeyPress(Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null); Drain(window);
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Appearance; Drain(window); Settle(h, window);
            Inside(view, SettingsPage.Appearance); Assert.Equal(at, Sidebar(view).Offset);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Resizing_during_a_reveal_keeps_ownership_for_a_new_visible_selection()
    {
        With(() => new SettingsView(), (view, _, h, window) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(window);
            Tick(h, 1000 / 60.0); Drain(window); Assert.True(Sidebar(view).Offset.Y > 0);
            var oldMax = Sidebar(view).Extent.Height - Sidebar(view).Viewport.Height;
            view.Height = 650; Drain(window); Tick(h, 1000 / 60.0); Drain(window);
            Assert.InRange(Sidebar(view).Extent.Height - Sidebar(view).Viewport.Height, 1, oldMax - 1);
            var next = Nav(view, SettingsPage.Appearance); var top = next.TranslatePoint(default, Sidebar(view))!.Value.Y;
            Assert.InRange(top, 0, Sidebar(view).Viewport.Height - next.Bounds.Height);
            var at = Sidebar(view).Offset;
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Appearance; Drain(window); Settle(h, window);
            Inside(view, SettingsPage.Appearance); Assert.Equal(at, Sidebar(view).Offset);
        });
    }
}
