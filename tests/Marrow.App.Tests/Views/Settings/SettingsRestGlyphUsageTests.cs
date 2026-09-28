using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestGlyphUsageTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    // STOP opencode-SettingsView-rest #6: UI-THEME-010's "hover shows the tooltip \"Clear\"" clause
    // (ui_contract.json UI-THEME-010.interactions) cannot be proven with a real pointer. Its glyph, SystemName
    // "circle.dotted", is an unfilled ring: ED-182 (Q4) draws only its stroke, so no pixel is painted at the icon's
    // own centre (or anywhere but the thin ring). Avalonia 12.1.2's compositor hit-tests only recorded paint
    // (Avalonia.Rendering.Composition.CompositingRenderer.HitTest walks the composition draw list built from
    // SwiftImage.Render), unlike SwiftUI, which hit-tests a plain Image's whole layout frame regardless of alpha.
    // Widening it needs a shared change to the Port foundation control (src/Marrow.UI/Port/SwiftImage.cs,
    // ED-179/ED-182), which around 200 other call sites across units not yet merged or reviewed also depend on for
    // intentional click-through (their own explicit IsHitTestVisible = false relies on nothing being painted where a
    // sibling underneath should receive the pointer instead); that file and those call sites are not a
    // SettingsView-rest record and are outside this unit's scope. Every other clause of UI-THEME-010 (font, colour,
    // order, geometry, the tooltip text itself as a static property) is proved below alongside UI-THEME-011, which
    // is fully proved: its glyph, "circle.fill", paints its whole disc and is genuinely hit-testable at its centre.
    [Fact]
    [Trait("Req", "UI-THEME-011")] // SettingsView.swift:528-539; ED-059; ED-062.
    public void Transparency_glyphs_are_static_11_point_secondary_with_8_point_order_and_the_solid_glyph_has_a_real_hover_tooltip()
    {
        With(() => new BackdropOpacitySlider(Binding<double>.Constant(0.5)), (view, _, _, w) =>
        {
            var images = Nodes<SwiftImage>(view).Where(i => i.SymbolName != "arrow.counterclockwise").ToArray(); Assert.Equal(new[] { "circle.dotted", "circle.fill" }, images.Select(i => i.SymbolName));
            var row = Assert.IsType<StackPanel>(images[0].Parent); Assert.Equal(8, row.Spacing); Assert.Same(images[0], row.Children[0]); Assert.IsType<Slider>(row.Children[1]); Assert.Same(images[1], row.Children[2]);
            for (var i = 0; i < 2; i++) { Assert.Equal(SwiftFont.System(11), SwiftImage.GetFont(images[i])); Ink(images[i], images[i].Foreground, Chrome.SecondaryText); Assert.Equal(i == 0 ? "Clear" : "Solid", ToolTip.GetTip(images[i])); Assert.Empty(Nodes<Button>(images[i])); }
            Hover(w, images[1]); Ink(images[1], images[1].Foreground, Chrome.SecondaryText); // real pointer hover on the fillable "Solid" glyph (UI-THEME-011).
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-042")] // SettingsView.swift:374-379; Chrome.swift:1055-1087.
    [Trait("Req", "UI-USAGE-043")]
    public void Usage_row_is_last_after_continue_divider_and_has_literal_wrapped_secondary_detail()
    {
        With(() => new GeneralSettingsPage(), (view, model, _, w) =>
        {
            const string detail = "The plan's limits and credits in a floating panel beside every chat, for the model in use or a pair's two; the usage popover's pop-out button switches it on as well, and it keeps the corner it was last dragged to";
            var row = Row(view, "Show a usage panel"); CheckRow(row, "Show a usage panel", detail);
            var rows = Assert.IsType<StackPanel>(row.Parent); Assert.Same(row, rows.Children[^1]); Assert.IsType<ChromeRowDivider>(rows.Children[^2]); Assert.Equal("Continue after a usage limit", Text(rows.Children[^3]).First());
            Assert.Single(Nodes<SettingsSwitch>(row)); Assert.False(model.Settings.ShowsUsagePanel); model.Settings.ShowsUsagePanel = true; Drain(w); CheckRow(row, "Show a usage panel", detail); Assert.True(model.Settings.ShowsUsagePanel);
        });
    }
}
