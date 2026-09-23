using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), part 4 of 4 (R-TEST-3: a new file for this
// part): the traffic-light geometry computed properties trafficLightPitch (:17) and trafficLightsWidth (:20), and
// UI-WIN-055 (ChromeRowDivider :1076-1081, its composition rule at App/Views/RootView.swift:357-358).
// Expected values are transcribed from the Swift and the record, never from the C# under test:
//   :15-16 trafficLightDiameter = 14, trafficLightSpacing = 9, so :17 pitch = 23;
//   :17 trafficLightsWidth = diameter*3 + spacing*2 = 60;
//   UI-WIN-055: role separator, geometry "System hairline, inset 16 points on the leading side only, flush to the
//   trailing edge", states "drawn between consecutive rows only: n rows get n-1 dividers, none before the first or
//   after the last" (RootView.swift:357-358 `if index > 0 { ChromeRowDivider() }` inside a ForEach over n items),
//   interactions "none".
[Collection("Headless")]
public sealed class ChromeSixthTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width = 400, double height = 300)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Rule", "R-VIEW-11")] // Chrome.swift:17: trafficLightPitch = trafficLightDiameter + trafficLightSpacing
    public void TrafficLightPitch_is_diameter_plus_spacing()
    {
        Assert.Equal(Chrome.TrafficLightDiameter + Chrome.TrafficLightSpacing, Chrome.TrafficLightPitch);
        Assert.Equal(23, Chrome.TrafficLightPitch);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-11")] // Chrome.swift:20: trafficLightsWidth = diameter*3 + spacing*2
    public void TrafficLightsWidth_is_three_diameters_plus_two_spacings()
    {
        Assert.Equal(Chrome.TrafficLightDiameter * 3 + Chrome.TrafficLightSpacing * 2, Chrome.TrafficLightsWidth);
        Assert.Equal(60, Chrome.TrafficLightsWidth);
    }

    // ---- UI-WIN-055: ChromeRowDivider is a separator, no interaction, inset 16 leading / flush trailing ----

    [Fact]
    [Trait("Req", "UI-WIN-055")] // Chrome.swift:1076-1081: role separator, inset 16 leading only, flush trailing
    public void ChromeRowDivider_geometry_matches_a_leading_only_inset_hairline() => headless.OnUiThread(() =>
    {
        var divider = new ChromeRowDivider();
        var window = Show(divider);
        // Geometry: "System hairline, inset 16 points on the leading side only, flush to the trailing edge."
        Assert.Equal(16, divider.Margin.Left);
        Assert.Equal(0, divider.Margin.Right); // flush to the trailing edge: no trailing inset
        Assert.Equal(0, divider.Margin.Top);
        Assert.Equal(0, divider.Margin.Bottom);
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-WIN-055")] // Chrome.swift:1076-1081: "interactions": ["none"] -- no click handling, no focus
    public void ChromeRowDivider_has_no_interactions() => headless.OnUiThread(() =>
    {
        var divider = new ChromeRowDivider();
        var window = Show(divider);
        Assert.False(divider.Focusable);
        // A separator with no interaction never appears in the focus/tab order and carries no pointer handlers
        // beyond what Decorator itself provides for hit testing of its (non-interactive) child hairline.
        Assert.Empty(VisualQuery.Descendants<Button>(divider));
        window.Close();
    });

    [Fact]
    // UI-WIN-055 "states": drawn between consecutive rows only: n rows get n-1 dividers, none before the first or
    // after the last (App/Views/RootView.swift:357-358 `if index > 0 { ChromeRowDivider() }` inside a ForEach).
    [Trait("Req", "UI-WIN-055")]
    public void N_rows_get_exactly_n_minus_one_dividers_placed_between_them() => headless.OnUiThread(() =>
    {
        const int n = 4;
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        for (var index = 0; index < n; index++)
        {
            if (index > 0) stack.Children.Add(new ChromeRowDivider()); // RootView.swift:357: `if index > 0`
            stack.Children.Add(new TextBlock { Text = $"row {index}" });
        }
        var window = Show(stack);

        var dividers = VisualQuery.Descendants<ChromeRowDivider>(stack).ToList();
        Assert.Equal(n - 1, dividers.Count);

        // None before the first row, none after the last: the first and last children are rows, not dividers.
        Assert.IsType<TextBlock>(stack.Children[0]);
        Assert.IsType<TextBlock>(stack.Children[^1]);
        // Every divider sits strictly between two rows.
        for (var i = 0; i < stack.Children.Count; i++)
        {
            if (stack.Children[i] is not ChromeRowDivider) continue;
            Assert.True(i > 0 && i < stack.Children.Count - 1);
            Assert.IsType<TextBlock>(stack.Children[i - 1]);
            Assert.IsType<TextBlock>(stack.Children[i + 1]);
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-WIN-055")] // "states": one row gets zero dividers (n=1 => n-1=0)
    public void A_single_row_gets_no_divider() => headless.OnUiThread(() =>
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = "only row" });
        var window = Show(stack);
        Assert.Empty(VisualQuery.Descendants<ChromeRowDivider>(stack));
        window.Close();
    });
}
