using System.Reflection;
using Avalonia;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Chrome;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

public sealed class FloatingSidebarPanelTests
{
    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:17-20
    public void The_panel_declares_the_source_default_size_minimum_margin_and_strip_height()
    {
        Assert.Equal(new Size(300, 560), FloatingSidebarPanel.DefaultSize);
        Assert.Equal(new Size(240, 320), FloatingSidebarPanel.MinSize);
        Assert.Equal(12, FloatingSidebarPanel.FloatingSidebarPanelMargin);
        Assert.Equal(44, FloatingSidebarPanel.StripHeight);
        Assert.Equal(72, Chrome.ContentTopInset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:121-125
    public void The_origin_is_clamped_to_the_area_with_the_margin()
    {
        var clamp = typeof(FloatingSidebarPanel).GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = Assert.IsType<Point>(clamp.Invoke(null, [new Point(-20, 900), new Size(240, 320), new Size(500, 600)]));

        Assert.Equal(new Point(12, 268), result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:121-125
    public void The_lower_margin_wins_when_the_area_is_too_small()
    {
        var clamp = typeof(FloatingSidebarPanel).GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = Assert.IsType<Point>(clamp.Invoke(null, [new Point(900, -20), new Size(240, 320), new Size(100, 100)]));

        Assert.Equal(new Point(12, 12), result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:28-32
    public void The_stored_size_is_floored_at_the_minimum_and_capped_by_the_area_less_the_margins()
    {
        var readSize = typeof(FloatingSidebarPanel).GetMethod("ReadSize", BindingFlags.NonPublic | BindingFlags.Static)!;

        var belowMinimum = Assert.IsType<Size>(readSize.Invoke(null, [new Size(100, 200), new Size(500, 600)]));
        Assert.Equal(new Size(240, 320), belowMinimum);

        var aboveAreaCap = Assert.IsType<Size>(readSize.Invoke(null, [new Size(800, 900), new Size(500, 600)]));
        Assert.Equal(new Size(476, 576), aboveAreaCap);

        var areaSmallerThanMinimum = Assert.IsType<Size>(readSize.Invoke(null, [new Size(300, 560), new Size(100, 100)]));
        Assert.Equal(new Size(240, 320), areaSmallerThanMinimum);
    }
}

// STOP App.Views.Sidebar.FloatingSidebarPanel #2 (helper Claude, 2026-09-25, updates STOP #1 below)
// Kind: missing dependency, confirmed
// AppSettings.SidebarPanelSize/SidebarPanelOrigin (Point?/Size?) now exist (AppSettings.cs:1021-1038), so that part of
// STOP #1 is resolved. The real blocker: FloatingSidebarPanel's constructor reads WindowManager.Shared.Model
// (FloatingSidebarPanel.cs:41), and WindowManager.Shared itself is still MARROW_STUB
// (App/Window/WindowManager.cs:29-30, throws NotImplementedException), so the panel cannot be constructed at all
// until App.Window.WindowManager is ported. PR-SIDEBAR-144 through 148 and 158 (drag, resize, persistence, close,
// rendering) need a constructible panel and stay untested until then; ReadSize and Clamp (PR-SIDEBAR-143), the only
// members with no WindowManager dependency, are now both covered.
//
// STOP App.Views.Sidebar.FloatingSidebarPanel #1
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift:28-34 (B2 1fbf81e)
// Requirements: PR-SIDEBAR-143, PR-SIDEBAR-144, PR-SIDEBAR-145, PR-SIDEBAR-146, PR-SIDEBAR-147, PR-SIDEBAR-148, PR-SIDEBAR-158, UI-SIDEBAR-045, UI-SIDEBAR-046, UI-SIDEBAR-047, UI-SIDEBAR-048, UI-SIDEBAR-049
// What is unclear or missing: The supplied repository context does not show the C# types of AppSettings.SidebarPanelSize and AppSettings.SidebarPanelOrigin, or the Avalonia environment API that supplies AppModel to this view. The panel's constructor and body depend on those APIs to read, write, and render settings; drag, resize, and visual tests likewise need a constructible panel.
// Readings or options: (a) Guess the settings value types and environment access -> an invented, potentially uncompilable API; (b) leave the panel behavior unwritten until those declarations are supplied.
// Needed to proceed: the declarations and types for AppSettings.SidebarPanelSize and AppSettings.SidebarPanelOrigin, and the repository's AppModel environment access pattern.
// Done so far: retained the source-constant assertions and added tests for the independent Swift clamp behavior; the remaining requirements cannot be implemented or tested without the missing declarations.
