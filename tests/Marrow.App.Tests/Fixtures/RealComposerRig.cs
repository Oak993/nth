using Avalonia;
using Avalonia.Controls;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Draws a real composer pill at 472 wide in a 1400 x 900 window, bottom left, and captures the parts the Customization preview restates (CZ-T45).</summary>
internal static class RealComposerRig
{
    internal static Button SendDisc(Control root) => One<Button>(One<SendButton>(root));

    internal static SwiftImage Chevron(Control chip) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(chip).Single(image => image.SymbolName == "chevron.down");

    /// <summary>The distance from the chevron's left edge back to the content before it, in its row.</summary>
    internal static double GapBeforeChevron(SwiftImage chevron)
    {
        var siblings = ((Panel)chevron.Parent!).Children.Where(c => !ReferenceEquals(c, chevron) && c.Bounds.Width > 0 && c.Bounds.Right <= chevron.Bounds.X + .01);
        return chevron.Bounds.X - siblings.Max(c => c.Bounds.Right);
    }

    internal static RealComposerCapture Capture()
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
        var real = Pill(h, h.Runtime, null);
        var window = h.Main.OnMain(() => Show(real, h.App, 472));
        Pump(h, window);
        RealComposerCapture? result = null;
        byte[] rest = [], typing = [], ring = [], chevronEnd = [];
        (int W, int H) ringSize = default, chevronSize = default, discSize = default;
        double gap = 0;
        string words = "";
        Rect discRect = default;
        h.Main.OnMain(() =>
        {
            words = One<ComposerNSTextView>(real).Placeholder;
            var controls = One<ComposerControls>(real);
            var ringRect = Frame(One<ContextMeter>(controls), window);
            ring = PreviewPixels.WholeRegion(window, ringRect);
            ringSize = ((int)Math.Round(ringRect.Width), (int)Math.Round(ringRect.Height));
            var chip = One<ModelEffortSlot>(controls);
            var chevron = Chevron(chip);
            gap = GapBeforeChevron(chevron);
            var chipRect = Frame(chip, window);
            var chevronRect = Frame(chevron, window);
            var end = new Rect(chevronRect.X - 4, chipRect.Y, chipRect.Right - chevronRect.X + 4, chipRect.Height);
            chevronEnd = PreviewPixels.WholeRegion(window, end);
            chevronSize = ((int)Math.Round(end.Width), (int)Math.Round(end.Height));
            discRect = Frame(SendDisc(controls), window);
            discSize = ((int)Math.Round(discRect.Width), (int)Math.Round(discRect.Height));
            rest = PreviewPixels.WholeRegion(window, discRect);
        });
        h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Text = "Add a keyboard shortcut" });
        Pump(h, window);
        h.Main.OnMain(() => typing = PreviewPixels.WholeRegion(window, discRect));
        Close(h, window);
        result = new RealComposerCapture(ring, ringSize, chevronEnd, chevronSize, gap, rest, typing, discSize, words);
        return result;
    }
}
