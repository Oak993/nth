using System;
using System.Linq;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests;

// REVIEW (adversarial review of unit appcommands, 2026-10-04). Each test kills a mutant that the unit's own tests let live.
[Collection("Headless")]
public sealed class AppCommandsReviewTests(HeadlessSession headless)
{
    // ED-270 / PR-WIN-222, PR-WIN-224, PR-WIN-225, UI-WIN-074: Ctrl+Shift+= reaches Zoom In "+" on a real US machine, and
    // Ctrl+Shift with the French "0" key reaches Actual Size, only because the production reader
    // (Port/KeyboardLayoutText.cs, WindowsKeyboardLayoutText) applies Shift. Every zoom test reads a table layout
    // (Fixtures/KeyboardLayouts.cs), so a reader that drops Shift passes them all. On every layout with letter case the
    // K key types a different character with Shift than without.
    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial: the production reader's Shift clause only
    public void REVIEW_the_Windows_layout_reader_applies_Shift() => headless.OnUiThread(() =>
    {
        var windows = (IKeyboardLayoutText)Activator.CreateInstance(
            typeof(MenuKeyEquivalents).Assembly.GetType("Marrow.App.Port.WindowsKeyboardLayoutText")!, nonPublic: true)!;
        var plain = windows.Typed(PhysicalKey.K, false);
        var shifted = windows.Typed(PhysicalKey.K, true);
        Assert.NotNull(plain);
        Assert.NotNull(shifted);
        Assert.NotEqual(plain, shifted);
    });

    // OD-0053 (answering PR-WIN-115's open question on the row's place, symbol and id): the Welcome tour row's id is
    // "welcome-tour". No test reads it; a palette with any other id passes every test.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // partial for PR-WIN-115: the row's id (OD-0053)
    public void REVIEW_the_welcome_tour_row_has_the_id_welcome_tour() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var (_, window, palette) = CommandPaletteRig.Build(model);
            try
            {
                var titles = CommandPaletteRig.Titles(palette);
                var row = palette.RowsPanel.Children[Array.IndexOf(titles, "Welcome tour")];
                var item = row.GetType().GetField("item", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(row)!;
                Assert.Equal("welcome-tour", (string)item.GetType().GetProperty("Id")!.GetValue(item)!);
            }
            finally { window.Close(); }
        });
    });
}
