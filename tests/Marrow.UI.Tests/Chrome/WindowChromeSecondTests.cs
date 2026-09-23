using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/WindowChrome.swift (B2 1fbf81e). Part 2 of 2: SidebarLayout
// (PR-WIN-038 through PR-WIN-045). See WindowChromeTests for part 1 (window configuration, titlebar and
// traffic-light placement), which needs the headless UI thread this file's TestRuntime cannot share.
// Constants transcribed from WindowChrome.swift:240-243, 274-277:
//   minimumWidth 210, maximumWidth 440, collapseThreshold 110, defaultWidth 268
//   trafficLightsFitWidth = trafficLightLeading (18) + trafficLightsWidth (60) + trafficLightClearance (10) = 88
[Collection("Headless")]
public sealed class WindowChromeSecondTests
{
    [Trait("Req", "PR-WIN-038")] // WindowChrome.swift:287-291
    [Fact]
    public void A_missing_stored_width_defaults_to_268()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        Assert.Equal(268, layout.Width);
    }

    [Trait("Req", "PR-WIN-038")] // WindowChrome.swift:289, a non-Double stored value reads as absent
    [Fact]
    public void A_stored_width_of_the_wrong_type_reads_as_absent()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarWidth", "not a double");
        var layout = new SidebarLayout();
        Assert.Equal(268, layout.Width);
    }

    [Trait("Req", "PR-WIN-038")] // WindowChrome.swift:290, clamp to 210...440
    [Theory]
    [InlineData(100.0, 210.0)]
    [InlineData(300.0, 300.0)]
    [InlineData(900.0, 440.0)]
    public void The_stored_width_is_clamped_to_210_440_on_read(double stored, double expected)
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarWidth", stored);
        var layout = new SidebarLayout();
        Assert.Equal(expected, layout.Width);
    }

    [Trait("Req", "PR-WIN-039")] // WindowChrome.swift:292-295
    [Fact]
    public void A_missing_starts_open_value_seeds_all_three_flags_closed()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        Assert.False(layout.StartsOpen);
        Assert.False(layout.IsVisible);
        Assert.False(layout.HoldsTrafficLights);
    }

    [Trait("Req", "PR-WIN-039")] // WindowChrome.swift:292-295, the one stored Bool seeds all three
    [Fact]
    public void A_stored_starts_open_true_seeds_all_three_flags_open()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        var layout = new SidebarLayout();
        Assert.True(layout.StartsOpen);
        Assert.True(layout.IsVisible);
        Assert.True(layout.HoldsTrafficLights);
    }

    [Trait("Req", "PR-WIN-040")] // WindowChrome.swift:298
    [Fact]
    public void Rendered_width_is_the_width_when_visible_and_zero_when_hidden()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        Assert.Equal(300, layout.RenderedWidth);
        runtime.OnMain(() => layout.Toggle());
        Assert.Equal(0, layout.RenderedWidth);
    }

    [Trait("Req", "PR-WIN-041")] // WindowChrome.swift:274-277, 300-308
    [Fact]
    public void The_threshold_is_exactly_88_and_the_flag_follows_the_last_laid_out_width()
    {
        Assert.Equal(88, SidebarLayout.TrafficLightsFitWidth);
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        Assert.False(layout.HoldsTrafficLights);
        runtime.OnMain(() => layout.NoteLaidOutWidth(87));
        Assert.False(layout.HoldsTrafficLights);
        runtime.OnMain(() => layout.NoteLaidOutWidth(88));
        Assert.True(layout.HoldsTrafficLights);
    }

    [Trait("Req", "PR-WIN-041")] // WindowChrome.swift:304, an unchanged report writes nothing
    [Fact]
    public void An_unchanged_laid_out_width_report_is_a_no_op()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        var notifications = 0;
        layout.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SidebarLayout.HoldsTrafficLights)) notifications++; };
        runtime.OnMain(() => layout.NoteLaidOutWidth(88));
        Assert.Equal(1, notifications);
        runtime.OnMain(() => layout.NoteLaidOutWidth(88)); // same value: lastLaidOutWidth guard trips
        Assert.Equal(1, notifications);
    }

    [Trait("Req", "PR-WIN-041")] // WindowChrome.swift:285, the -1 sentinel: the first report always lands
    [Fact]
    public void The_very_first_laid_out_width_report_always_lands_even_at_zero()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        var notifications = 0;
        layout.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SidebarLayout.HoldsTrafficLights)) notifications++; };
        runtime.OnMain(() => layout.NoteLaidOutWidth(0)); // still below 88, but the first report ever
        Assert.Equal(0, notifications); // false -> false is not a change, but the sentinel guard must not suppress it
        runtime.OnMain(() => layout.NoteLaidOutWidth(0)); // now genuinely unchanged
        Assert.Equal(0, notifications);
    }

    [Trait("Req", "PR-WIN-042")] // WindowChrome.swift:322-338
    [Fact]
    public void Dragging_out_from_hidden_starts_the_width_at_zero()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        Assert.False(layout.IsVisible);
        runtime.OnMain(() => layout.BeginDrag());
        Assert.True(layout.IsVisible);
        Assert.Equal(0, layout.Width);
        Assert.True(layout.IsDragging);
    }

    [Trait("Req", "PR-WIN-042")] // WindowChrome.swift:327-329, already visible: anchors at the current width
    [Fact]
    public void Dragging_from_visible_anchors_at_the_current_width()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag());
        Assert.Equal(300, layout.Width); // unchanged: anchorWidth = width, no jump to zero
        runtime.OnMain(() => layout.Drag(0));
        Assert.Equal(300, layout.Width);
    }

    [Trait("Req", "PR-WIN-043")] // WindowChrome.swift:340-342, upstream defect 64
    [Fact]
    public void While_dragging_the_width_follows_the_pointer_below_the_210_minimum()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag()); // anchorWidth = 0 (hidden)
        runtime.OnMain(() => layout.Drag(50));
        Assert.Equal(50, layout.Width); // NOT floored at 210
    }

    [Trait("Req", "PR-WIN-043")] // WindowChrome.swift:341, clamped to 0...440
    [Theory]
    [InlineData(-40.0, 0.0)]
    [InlineData(500.0, 440.0)]
    public void The_dragged_width_is_clamped_to_0_440(double translation, double expected)
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(translation));
        Assert.Equal(expected, layout.Width);
    }

    [Trait("Req", "PR-WIN-044")] // WindowChrome.swift:344-356, :242
    [Fact]
    public void Releasing_below_110_collapses_and_restores_the_resting_width()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(-250)); // 300 - 250 = 50, below collapseThreshold 110
        runtime.OnMain(() => layout.EndDrag());
        Assert.False(layout.IsVisible);
        Assert.Equal(300, layout.Width); // snapped back to the resting width, not left at 50
        Assert.False(layout.IsDragging);
        Assert.Contains("sidebarWidth", runtime.Defaults.Writes);
        Assert.Contains("sidebarStartsOpen", runtime.Defaults.Writes);
    }

    [Trait("Req", "PR-WIN-045")] // WindowChrome.swift:349-353
    [Fact]
    public void Releasing_at_110_or_above_settles_at_no_less_than_210_and_becomes_the_new_resting_width()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(-150)); // 300 - 150 = 150, in the 110...210 band
        runtime.OnMain(() => layout.EndDrag());
        Assert.True(layout.IsVisible);
        Assert.Equal(210, layout.Width); // springs OUT to 210, not left at 150

        // The 210 floor becomes the new resting width: collapsing and reopening returns to 210, not 300.
        runtime.OnMain(() => layout.Toggle());
        Assert.False(layout.IsVisible);
        runtime.OnMain(() => layout.Toggle());
        Assert.Equal(210, layout.Width);
    }

    [Trait("Req", "PR-WIN-045")] // WindowChrome.swift:350, a release above 210 keeps the dragged width
    [Fact]
    public void Releasing_above_210_settles_at_the_dragged_width()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(350));
        runtime.OnMain(() => layout.EndDrag());
        Assert.Equal(350, layout.Width);
    }
}
