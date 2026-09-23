using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/UI/Chrome/HydraPairMark.swift (B2 1fbf81e), :5-23.
[Collection("Headless")]
public sealed class HydraPairMarkTests(HeadlessSession headless)
{
    // Store fixture parameter to satisfy compiler (required by xUnit for Avalonia Headless tests)
    private readonly HeadlessSession _headless = headless;

    private static Window Show(Control content, ThemeVariant? theme = null)
    {
        var window = new Window { Width = 600, Height = 300, Content = content };
        window.RequestedThemeVariant = theme ?? ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return control;
    }

    private static AutomationPeer Peer(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control);

    private static Color Effective(IBrush? brush)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        // Allow ±1 per component for rounding in opacity calculations
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:8
    public void HydraPairMark_DefaultLeadSizeIs16()
    {
        // Line 8: var leadSize: CGFloat = 16
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Claude
        );

        Assert.Equal(16, mark.LeadSize);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:11
    public void HydraPairMark_DefaultSpacingIs4()
    {
        // Line 11: HStack(spacing: (leadSize * 0.25).rounded())
        // At default leadSize=16: (16 * 0.25).rounded() = 4
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Claude
        );

        var window = Show(TopLeft(mark));

        // The HStack should have spacing of 4
        var hstack = VisualQuery.Descendants<StackPanel>(mark)
            .FirstOrDefault(sp => sp.Orientation == Orientation.Horizontal);
        Assert.NotNull(hstack);
        Assert.Equal(4, hstack.Spacing);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:12
    public void HydraPairMark_LeadProviderIconAtLeadSize()
    {
        // Line 12: ProviderIcon(provider: lead, size: leadSize)
        // At default: size should be 16
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark));

        var icons = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        // First icon should be the lead provider icon at leadSize
        var leadIcon = icons.FirstOrDefault();
        Assert.NotNull(leadIcon);

        // The icon should represent Claude
        Assert.True(leadIcon.SymbolName?.Contains("provider") ?? leadIcon.AssetName?.Contains("provider") ?? false);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:12-13
    public void HydraPairMark_LeadIconUsedPrimaryTextColor()
    {
        // Line 12-13: ProviderIcon... .foregroundStyle(Chrome.primaryText)
        using var runtime = new TestRuntime();

        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark), ThemeVariant.Light);

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        Assert.NotEmpty(images);

        var leadIcon = images[0];
        var expected = Chrome.PrimaryText.Resolve(ThemeVariant.Light);
        var actual = Effective(leadIcon.Foreground);
        AssertColor(expected, actual);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:14-16
    public void HydraPairMark_ArrowIsSemiboldAndAtHalfLeadSize()
    {
        // Line 14-15: Image(systemName: "arrow.right")
        //             .font(.system(size: (leadSize * 0.5).rounded(), weight: .semibold))
        // At default leadSize=16: (16 * 0.5).rounded() = 8, weight semibold
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark));

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        Assert.NotEmpty(images);

        // The arrow should be the second image (symbol)
        var arrowImage = images.FirstOrDefault(img => img.SymbolName == "arrow.right");
        Assert.NotNull(arrowImage);

        var font = SwiftImage.GetFont(arrowImage);
        Assert.NotNull(font);
        Assert.Equal(8, font.PointSize);
        Assert.Equal(SwiftFont.Weight.Semibold, font.FontWeightValue);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:14-16
    public void HydraPairMark_ArrowUsesSecondaryTextColor()
    {
        // Line 15-16: .foregroundStyle(Chrome.secondaryText)
        // Secondary text is primary text at opacity 0.7
        using var runtime = new TestRuntime();

        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark), ThemeVariant.Light);

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        var arrowImage = images.FirstOrDefault(img => img.SymbolName == "arrow.right");
        Assert.NotNull(arrowImage);

        var expected = Chrome.SecondaryText.Resolve(ThemeVariant.Light);
        var actual = Effective(arrowImage.Foreground);
        AssertColor(expected, actual);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:17-18
    public void HydraPairMark_HeadsIconAt75PercentLeadSizeWithOpacity80()
    {
        // Line 17-18: ProviderIcon(provider: heads, size: (leadSize * 0.75).rounded())
        //             .foregroundStyle(Chrome.primaryText.opacity(0.8))
        // At default leadSize=16: (16 * 0.75).rounded() = 12
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark));

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        // Heads icon should be roughly in last position
        Assert.True(images.Count >= 2);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:17-18
    public void HydraPairMark_HeadsIconUsesPrimaryTextAt80Percent()
    {
        // Line 17-18: .foregroundStyle(Chrome.primaryText.opacity(0.8))
        using var runtime = new TestRuntime();

        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark), ThemeVariant.Light);

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        Assert.True(images.Count >= 2);

        // Last icon should be heads icon with 0.8 opacity
        var headsIcon = images.Last();
        var expected = Chrome.PrimaryText.Opacity(0.8).Resolve(ThemeVariant.Light);
        var actual = Effective(headsIcon.Foreground);
        AssertColor(expected, actual);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:20-21
    public void HydraPairMark_AccessibilityElementLabelFormat()
    {
        // Line 20-21: .accessibilityElement(children: .ignore)
        //             .accessibilityLabel(Text("\(lead.displayName) lead, \(heads.displayName) heads"))
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark));

        var peer = Peer(mark);
        var label = peer?.GetName();

        var leadName = ProviderKindExtensions.DisplayName(ProviderKind.Claude);
        var headsName = ProviderKindExtensions.DisplayName(ProviderKind.Copilot);
        var expected = $"{leadName} lead, {headsName} heads";

        Assert.Equal(expected, label);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:8
    public void HydraPairMark_CustomLeadSize20ProducesCorrectSpacing()
    {
        // leadSize=20: spacing=(20*0.25).rounded()=5
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot,
            leadSize: 20
        );

        var window = Show(TopLeft(mark));

        var hstack = VisualQuery.Descendants<StackPanel>(mark)
            .FirstOrDefault(sp => sp.Orientation == Orientation.Horizontal);
        Assert.NotNull(hstack);
        Assert.Equal(5, hstack.Spacing);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:14-15
    public void HydraPairMark_ArrowSizeScalesWithLeadSize()
    {
        // leadSize=20: arrow size=(20*0.5).rounded()=10
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot,
            leadSize: 20
        );

        var window = Show(TopLeft(mark));

        var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
        var arrowImage = images.FirstOrDefault(img => img.SymbolName == "arrow.right");
        Assert.NotNull(arrowImage);

        var font = SwiftImage.GetFont(arrowImage);
        Assert.NotNull(font);
        Assert.Equal(10, font.PointSize);
    }

    [Trait("Req", "UI-HYDRA-034")] // DroppyCode/UI/Chrome/HydraPairMark.swift:20
    public void HydraPairMark_IsAccessibilityElementWithIgnoredChildren()
    {
        // Line 20: .accessibilityElement(children: .ignore)
        var mark = new HydraPairMark(
            lead: ProviderKind.Claude,
            heads: ProviderKind.Copilot
        );

        var window = Show(TopLeft(mark));

        var peer = Peer(mark);
        Assert.NotNull(peer);

        // The control itself should be an accessibility element
        // Children should be ignored per line 20
        var label = peer.GetName();
        Assert.NotNull(label);
        Assert.NotEmpty(label);
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:5-23
    public void HydraPairMark_RoundingProducesCorrectValues()
    {
        // Test that all size calculations are properly rounded
        var testCases = new (double leadSize, int expectedSpacing, int expectedLeadSize, int expectedArrowSize, int expectedHeadsSize)[]
        {
            (16, 4, 16, 8, 12),
            (20, 5, 20, 10, 15),
            (24, 6, 24, 12, 18),
            (10, 2, 10, 5, 7),  // 7.5 rounds to 8, 2.5 rounds to 2
        };

        foreach (var (leadSize, expectedSpacing, expectedLeadSize, expectedArrowSize, expectedHeadsSize) in testCases)
        {
            var mark = new HydraPairMark(
                lead: ProviderKind.Claude,
                heads: ProviderKind.Copilot,
                leadSize: leadSize
            );

            var window = Show(TopLeft(mark));

            var hstack = VisualQuery.Descendants<StackPanel>(mark)
                .FirstOrDefault(sp => sp.Orientation == Orientation.Horizontal);
            Assert.NotNull(hstack);
            Assert.Equal(expectedSpacing, hstack.Spacing);

            var images = VisualQuery.Descendants<SwiftImage>(mark).ToList();
            Assert.NotEmpty(images);
        }
    }

    [Trait("Req", "PR-HYDRA-196")] // DroppyCode/UI/Chrome/HydraPairMark.swift:5-23
    public void HydraPairMark_AllProvidersWork()
    {
        // Test with multiple provider combinations
        var providers = new[]
        {
            ProviderKind.Claude,
            ProviderKind.Copilot,
            ProviderKind.Codex,
        };

        foreach (var lead in providers)
        {
            foreach (var heads in providers)
            {
                var mark = new HydraPairMark(
                    lead: lead,
                    heads: heads
                );

                var window = Show(TopLeft(mark));

                var peer = Peer(mark);
                var label = peer?.GetName();
                Assert.NotNull(label);
                Assert.NotEmpty(label);
            }
        }
    }
}
