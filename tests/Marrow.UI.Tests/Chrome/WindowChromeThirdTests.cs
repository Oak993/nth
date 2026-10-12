using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/WindowChrome.swift (B3 52bc792). Part 2 of 2 (this file): the rest of
// SidebarLayout (PR-WIN-046 through PR-WIN-048, unchanged at B3). The resize strip's tests (PR-WIN-049 through
// PR-WIN-057) moved to SidebarSplitterTests when the strip became the shared Splitter (web W1, ED-NEW-web-w1-3). See
// WindowChromeTests (window/titlebar/buttons, part 1) and WindowChromeSecondTests (SidebarLayout up to PR-WIN-045).
[Collection("Headless")]
public sealed class WindowChromeThirdTests
{
    // ---- PR-WIN-046, PR-WIN-047, PR-WIN-048: the rest of SidebarLayout ----

    [Trait("Req", "PR-WIN-046")] // WindowChrome.swift:358-364
    [Fact]
    public void Toggling_open_sets_the_width_to_resting_and_slides_by_minus_that_width()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout(); // closed, restingWidth 300
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsVisible);
        Assert.Equal(300, layout.Width); // :360 width = restingWidth before the slide starts
        Assert.True(layout.IsSliding);
        Assert.Equal(-300, layout.SlideDelta); // :361 opening ? -restingWidth : width
    }

    [Trait("Req", "PR-WIN-046")] // WindowChrome.swift:358-364, closing direction
    [Fact]
    public void Toggling_closed_slides_by_the_current_width_and_flips_visibility()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.False(layout.IsVisible); // :362 isVisible.toggle()
        Assert.True(layout.IsSliding);
        Assert.Equal(300, layout.SlideDelta); // :361 opening ? ... : width
    }

    [Trait("Req", "PR-WIN-047")] // WindowChrome.swift:366-370, upstream defect 63
    [Fact]
    public void A_toggle_rewrites_both_keys_even_though_startsOpen_did_not_change()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.Defaults.Writes.Clear();
        runtime.OnMain(() => layout.Toggle());
        Assert.Contains("sidebarWidth", runtime.Defaults.Writes);
        Assert.Contains("sidebarStartsOpen", runtime.Defaults.Writes); // rewritten though StartsOpen is untouched
        Assert.Equal(false, runtime.Defaults.Object("sidebarStartsOpen")); // the default materialised on disk
    }

    [Trait("Req", "PR-WIN-047")] // WindowChrome.swift:368, persist always writes restingWidth, never the live width
    [Fact]
    public void Persist_writes_the_resting_width_not_the_live_width()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout(); // restingWidth 268 default
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(150)); // live width 150, above the collapse threshold, below the minimum
        runtime.OnMain(() => layout.EndDrag()); // settles below 210 minimum -> 210, becomes new restingWidth
        Assert.Equal(210.0, runtime.Defaults.Object("sidebarWidth"));
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:279-280, :310-320
    [Fact]
    public void A_slide_settles_after_420_milliseconds_by_default()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(419);
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(1);
        Assert.False(layout.IsSliding);
        Assert.Equal(0, layout.SlideDelta);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:280, reduce motion halves the run to 220
    [Fact]
    public void A_slide_settles_after_220_milliseconds_with_reduce_motion()
    {
        using var runtime = new TestRuntime();
        using var _ = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        runtime.AdvanceMilliseconds(219);
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(1);
        Assert.False(layout.IsSliding);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:313, a later beginSlide cancels the pending clear
    [Fact]
    public void A_new_slide_cancels_the_previous_pending_clear()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle()); // opens, slide #1 armed for 420ms
        runtime.AdvanceMilliseconds(300);
        runtime.OnMain(() => layout.Toggle()); // closes, slide #2 restarts the 420ms window
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(300); // slide #1's original deadline (600ms total) has passed
        Assert.True(layout.IsSliding); // still sliding: #2 owns the clock now
        runtime.AdvanceMilliseconds(120);
        Assert.False(layout.IsSliding);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:322-325, beginDrag cancels and clears immediately
    [Fact]
    public void BeginDrag_cancels_a_pending_slide_and_clears_it_immediately()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsSliding);
        runtime.OnMain(() => layout.BeginDrag());
        Assert.False(layout.IsSliding); // cleared immediately, not after the run
        Assert.Equal(0, layout.SlideDelta);
        runtime.AdvanceMilliseconds(500); // the cancelled task's own clear must never re-fire
        Assert.False(layout.IsSliding);
    }
}
