using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ModelsSettingsSectionRig;

namespace Marrow.App.Tests.Views.Settings;

// The chosen-model rows of DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926), :30-65 (the plain stack),
// :160-296 (PinnedModelRow, RowControl): their stack, the lifted row, and the row's geometry, typography and colours.
[Collection("Headless")]
public sealed class ModelsSettingsPagePinnedRowTests(HeadlessSession headless)
{
    private static ModelPin Pin(string id, ProviderKind provider = ProviderKind.Claude) => new(provider, id);

    private static Action<AppModel> Pins(params string[] ids) => model =>
    {
        foreach (var id in ids) model.Settings.AddToModelList(Pin(id));
    };

    private static PinnedModelRow[] Rows(ModelsSettingsSectionRig rig) => Of<PinnedModelRow>(rig.Page).ToArray();

    private static StackPanel SlotOf(PinnedModelRow row) => Assert.IsType<StackPanel>(row.Parent);

    private static double DividerOpacity(PinnedModelRow row) => Assert.IsType<ChromeRowDivider>(SlotOf(row).Children[0]).Opacity;

    private static Border Grip(PinnedModelRow row) => Of<Border>(row).First(border => AutomationProperties.GetName(border) == "Drag to reorder");

    private static Point Centre(Control control, Visual to) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), to)!.Value;

    /// <summary>The Border that holds the row's 10/9/10/9 padding: the surface the lift is drawn on.</summary>
    private static Border Surface(PinnedModelRow row) => Of<Border>(row).First(border => border.Padding.Top == 9);

    private static double OffsetY(PinnedModelRow row) => (SlotOf(row).RenderTransform as TranslateTransform)?.Y ?? 0;

    /// <summary>Presses the grip of the n-th row and moves down by the given points: the page now holds a drag.</summary>
    private static Point Drag(ModelsSettingsSectionRig rig, int index, double down)
    {
        var start = Centre(Grip(Rows(rig)[index]), rig.Window);
        rig.Window.MouseMove(start);
        rig.Window.MouseDown(start, MouseButton.Left);
        rig.Sync();
        rig.Window.MouseMove(new Point(start.X, start.Y + down));
        rig.Sync();
        return new Point(start.X, start.Y + down);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-184")] // ModelsSettingsPage.swift:42-62 a plain VStack(spacing: 0) of every chosen row, each VStack(spacing: 0) { divider; row }; :51 the first row's divider is 0
    public void Every_chosen_model_is_built_in_a_plain_zero_spacing_stack_with_a_divider_above_each_row() => headless.OnUiThread(() =>
    {
        var ids = Enumerable.Range(0, 15).Select(i => "model" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var rig = new ModelsSettingsSectionRig([], Pins(ids), height: 2600);
        var rows = Rows(rig);
        Assert.Equal(15, rows.Length); // eager: all of them are built, none virtualised away
        var stack = Assert.IsType<StackPanel>(SlotOf(rows[0]).Parent);
        Assert.Equal(0, stack.Spacing);
        Assert.Equal(15, stack.Children.Count);
        foreach (var row in rows)
        {
            var slot = SlotOf(row);
            Assert.Same(stack, slot.Parent);
            Assert.Equal(0, slot.Spacing);
            Assert.Equal(2, slot.Children.Count);
            Assert.IsType<ChromeRowDivider>(slot.Children[0]); // the divider is above its row
        }
        Assert.Equal(0, DividerOpacity(rows[0])); // none above the first row
        Assert.All(rows.Skip(1), row => Assert.Equal(1, DividerOpacity(row)));
        Assert.All(rows, row => Assert.True(SlotOf(row).Children[0].IsVisible)); // hidden by opacity, never removed
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-184")] // ModelsSettingsPage.swift:51 `pin == visiblePins.first`: the first VISIBLE row, while a search filters the list
    public void The_divider_is_hidden_on_the_first_visible_row_while_a_search_filters_the_list() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo", "charlie"));
        Assert.Equal([0d, 1d, 1d], Rows(rig).Select(DividerOpacity).ToArray());

        rig.Page.SetQuery("bravo");
        rig.Sync();
        var only = Assert.Single(Rows(rig));
        Assert.Equal(0, DividerOpacity(only)); // bravo is first among the visible rows

        rig.Page.SetQuery("");
        rig.Sync();
        Assert.Equal([0d, 1d, 1d], Rows(rig).Select(DividerOpacity).ToArray());
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-184")] // ModelsSettingsPage.swift:51,60-61 the dragged row: divider 0, offset by the drag, zIndex 1; the others keep divider 1, offset 0, zIndex 0; no reflow
    public void The_dragged_row_hides_its_divider_follows_the_drag_and_draws_above_its_neighbours_without_a_reflow() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo", "charlie"));
        var before = Rows(rig).Select(row => SlotOf(row).Bounds).ToArray();

        Drag(rig, 1, 5);

        var rows = Rows(rig);
        var slots = rows.Select(SlotOf).ToArray();
        Assert.Equal([0, 1, 0], slots.Select(slot => slot.ZIndex).ToArray());
        Assert.Equal([0d, 0d, 1d], rows.Select(DividerOpacity).ToArray()); // first row and the dragged row (second): 0
        Assert.Equal(5, OffsetY(rows[1]), 1);
        Assert.Equal(0, OffsetY(rows[0]));
        Assert.Equal(0, OffsetY(rows[2]));
        Assert.Equal(before, slots.Select(slot => slot.Bounds).ToArray()); // the divider vanished by opacity: nothing moved or resized

        rig.Window.MouseUp(new Point(0, 0), MouseButton.Left);
        rig.Sync();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-187")] // ModelsSettingsPage.swift:253-262 RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Chrome.overlay(0.12)).shadow(color: .black.opacity(0.28), radius: 10, y: 4); .scaleEffect(1.02)
    public void The_lifted_row_has_a_radius_10_fill_of_12_percent_a_black_shadow_and_a_scale_of_1_02() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo", "charlie"));
        Drag(rig, 0, 5);
        rig.Advance(2); // the lift has settled

        var lifted = Rows(rig)[0];
        var layer = Of<Border>(lifted).Single(border => border.BoxShadow.Count == 1);
        Assert.Equal(new CornerRadius(10), layer.CornerRadius);
        Assert.Equal(Chrome.Overlay(0.12).Resolve(layer.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(layer.Background).Color);
        var shadow = layer.BoxShadow[0];
        Assert.Equal(10, shadow.Blur);
        Assert.Equal(4, shadow.OffsetY);
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(0, shadow.Spread);
        Assert.False(shadow.IsInset);
        Assert.Equal(0, shadow.Color.R);
        Assert.Equal(0, shadow.Color.G);
        Assert.Equal(0, shadow.Color.B);
        Assert.InRange(shadow.Color.A, 70, 72); // 28% of 255
        Assert.Equal(1, layer.Opacity, 0.001);

        var scale = Of<Control>(lifted).Concat([lifted]).Select(control => control.RenderTransform).OfType<ScaleTransform>().First();
        Assert.Equal(1.02, scale.ScaleX, 0.001);
        Assert.Equal(1.02, scale.ScaleY, 0.001);

        rig.Window.MouseUp(new Point(0, 0), MouseButton.Left);
        rig.Sync();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-187")] // ModelsSettingsPage.swift:255-262 the background exists only `if isDragged`; :262 scaleEffect(isDragged ? 1.02 : 1): no other row has either
    public void No_other_row_has_a_background_a_shadow_or_a_scale() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo", "charlie"));
        void NothingLifted(IEnumerable<PinnedModelRow> rows)
        {
            foreach (var row in rows)
            {
                Assert.All(Of<Border>(row), border => Assert.Equal(0, border.BoxShadow.Count));
                Assert.All(Of<Border>(row).Where(border => border.CornerRadius.TopLeft == 10), border => Assert.Null(border.Background));
                Assert.DoesNotContain(Of<Control>(row).Concat([row]), control => control.RenderTransform is ScaleTransform { ScaleX: > 1.0001 });
            }
        }
        NothingLifted(Rows(rig)); // at rest

        Drag(rig, 1, 5);
        rig.Advance(2);
        NothingLifted(Rows(rig).Where((_, index) => index != 1)); // while the second is held

        rig.Window.MouseUp(new Point(0, 0), MouseButton.Left);
        rig.Advance(2);
        NothingLifted(Rows(rig)); // dropped
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-187")] // ModelsSettingsPage.swift:263 .animation(.spring(response: 0.25, dampingFraction: 0.8), value: isDragged): both the lift and the drop animate
    public void The_lift_and_the_drop_animate_with_a_spring_of_response_0_25_and_damping_0_8() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo", "charlie"));
        var spring = Animation.Spring(response: 0.25, dampingFraction: 0.8);
        double Scale() => Of<Control>(Rows(rig)[0]).Concat([Rows(rig)[0]]).Select(control => control.RenderTransform).OfType<ScaleTransform>().Select(transform => transform.ScaleX).DefaultIfEmpty(1).Max();
        double Fill() => Of<Border>(Rows(rig)[0]).Where(border => border.BoxShadow.Count == 1).Select(border => border.Opacity).DefaultIfEmpty(0).Max();

        var at = Drag(rig, 0, 5);
        Assert.Equal(1, Scale(), 0.002); // the first frame of the lift has not moved the row yet
        Assert.Equal(0, Fill(), 0.01);

        rig.Advance(0.1);
        var progress = spring.Progress(0.1);
        Assert.InRange(progress, 0.05, 0.95); // moving, not at either end
        Assert.Equal(1 + 0.02 * progress, Scale(), 0.002); // mid-lift, on the spring
        Assert.Equal(Math.Clamp(progress, 0, 1), Fill(), 0.02);

        rig.Window.MouseMove(new Point(at.X, at.Y + 3)); // the page rebuilds its rows as the pointer moves: the lift carries on from where it was
        rig.Sync();
        Assert.Equal(1 + 0.02 * progress, Scale(), 0.002);
        Assert.Equal(Math.Clamp(progress, 0, 1), Fill(), 0.02);

        rig.Advance(2);
        Assert.Equal(1.02, Scale(), 0.001);
        Assert.Equal(1, Fill(), 0.001);

        rig.Window.MouseUp(new Point(0, 0), MouseButton.Left);
        rig.Sync();
        Assert.Equal(1.02, Scale(), 0.002); // dropped: the drop has not moved the row yet either
        rig.Advance(0.1);
        Assert.Equal(1 + 0.02 * (1 - spring.Progress(0.1)), Scale(), 0.002);
        rig.Advance(2);
        Assert.Equal(1, Scale(), 0.001);
        Assert.Equal(0, Fill(), 0.001);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-187")] // ModelsSettingsPage.swift:164,255-262 a row built held is lifted at once; a row built at rest has no background, shadow or scale
    public void A_row_built_held_is_lifted_and_a_row_built_at_rest_is_not() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha"));
        var rest = new PinnedModelRow(rig.Model, Pin("alpha"), isDragged: false, _ => { }, () => { });
        var held = new PinnedModelRow(rig.Model, Pin("alpha"), isDragged: true, _ => { }, () => { });
        Assert.Equal(0, Of<Border>(rest).Count(border => border.BoxShadow.Count > 0));
        Assert.DoesNotContain(Of<Control>(rest).Concat([rest]), control => control.RenderTransform is ScaleTransform);
        Assert.Equal(1, Of<Border>(held).Count(border => border.BoxShadow.Count > 0));
        Assert.Contains(Of<Control>(held).Concat([held]), control => control.RenderTransform is ScaleTransform { ScaleX: > 1.019 and < 1.021 });
    });

    private static ModelOption Known(string id, string name, string[] efforts, string? defaultEffort, string? fastTier = null) =>
        new(id, name, efforts: new ValueList<string>(efforts), defaultEffort: defaultEffort, fastTier: fastTier);

    private static Action<AppModel> KnownCodexModels(params ModelPin[] pins) => model =>
    {
        model.Providers.UpdateCatalog(new ValueList<ModelOption>(
        [
            Known("m1", "Alpha-One", ["low", "medium", "high"], "medium", fastTier: "flex"),
            Known("m2", "Bravo-Two", [], null),
            Known("m3", "Charlie-Three", ["low", "xhigh"], null),
        ]), ProviderKind.Codex);
        foreach (var pin in pins) model.Settings.AddToModelList(pin);
    };

    private static string[] TextsOf(PinnedModelRow row) => Of<TextBlock>(row).Select(text => text.Text ?? "").ToArray();

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:200-206,266-273 title: shortName; summary: provider · effort title + " effort" · Fast
    public void A_chosen_row_is_titled_by_the_short_name_and_summarises_provider_effort_and_fast_mode() => headless.OnUiThread(() =>
    {
        var m1 = Pin("m1", ProviderKind.Codex);
        var m2 = Pin("m2", ProviderKind.Codex);
        var m3 = Pin("m3", ProviderKind.Codex);
        using var rig = new ModelsSettingsSectionRig([], KnownCodexModels(m1, m2, m3));
        Assert.Equal(["Alpha One", "Codex · Medium effort"], TextsOf(Rows(rig)[0])); // no choice yet: the model's own default effort
        Assert.Equal(["Bravo Two", "Codex"], TextsOf(Rows(rig)[1])); // no efforts, no effort part
        Assert.Equal(["Charlie Three", "Codex · Default effort"], TextsOf(Rows(rig)[2])); // efforts, but neither a choice nor a default: "" titled "Default"

        rig.Model.Settings.SetPreference(new ModelPreference { Effort = "xhigh", FastMode = true }, m1.Provider, m1.ModelID);
        rig.Model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = true }, m3.Provider, m3.ModelID);
        rig.Model.Settings.SetPreference(new ModelPreference { FastMode = true }, m2.Provider, m2.ModelID);
        rig.Sync();
        Assert.Equal(["Alpha One", "Codex · Extra High effort · Fast"], TextsOf(Rows(rig)[0])); // "xhigh" reads Extra High; the model has a fast tier
        Assert.Equal(["Bravo Two", "Codex"], TextsOf(Rows(rig)[1])); // fast mode on a model without a fast tier adds nothing
        Assert.Equal(["Charlie Three", "Codex · Low effort"], TextsOf(Rows(rig)[2])); // no fast tier: no "Fast"
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:200,266-273 a model the catalogue does not know still appears, titled by its raw id, summarised by its provider alone
    public void A_model_the_catalogue_does_not_know_is_titled_by_its_raw_id() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], model => model.Settings.AddToModelList(Pin("some/raw-id", ProviderKind.Grok)));
        Assert.Equal(["some/raw-id", "Grok"], TextsOf(Assert.Single(Rows(rig))));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:177-181,246,250-252 HStack spacing 10, grip 24 x 24, minus 24 x 24, padding leading 10, trailing 10, vertical 9
    public void A_chosen_row_has_a_24_point_grip_and_minus_and_the_source_padding() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha"));
        var row = Assert.Single(Rows(rig));
        Assert.Equal(new Thickness(10, 9, Chrome.RowControlTrailingPadding, 9), Surface(row).Padding);
        Assert.Equal(10, Chrome.RowControlTrailingPadding);

        var grip = Grip(row);
        Assert.Equal((24d, 24d), (grip.Width, grip.Height));
        var minus = Of<Button>(row).Single(button => AutomationProperties.GetName(button) == "Remove from the picker");
        Assert.Equal((24d, 24d), (minus.Width, minus.Height));
        Assert.Equal("Remove from the picker", ToolTip.GetTip(minus));

        double Left(Control control) => control.TranslatePoint(default, rig.Window)!.Value.X;
        var surface = Surface(row);
        Assert.Equal(Left(surface) + 10, Left(grip), 0.5); // leading padding 10: the grip sits against it
        Assert.Equal(Left(surface) + surface.Bounds.Width - 10, Left(minus) + minus.Bounds.Width, 0.5); // trailing 10
        var block = Of<Button>(row).First(button => button != minus);
        Assert.Equal(10, Left(block) - (Left(grip) + grip.Bounds.Width), 0.5); // spacing 10 after the grip
        Assert.Equal(10, Left(minus) - (Left(block) + block.Bounds.Width), 0.5); // and before the minus
        var rowHeight = Math.Max(24, Of<TextBlock>(row).Sum(text => text.Bounds.Height) + 2);
        Assert.Equal(rowHeight + 18, surface.Bounds.Height, 0.5); // vertical padding 9 above and below
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:195-206 provider icon 16 in a 22-wide frame; text block spacing 2; name 13, summary 11
    public void A_chosen_row_sets_the_icon_in_a_22_wide_frame_and_stacks_name_and_summary_2_points_apart() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], KnownCodexModels(Pin("m1", ProviderKind.Codex)));
        var row = Assert.Single(Rows(rig));
        var icon = Of<ProviderIcon>(row).Single();
        Assert.Equal(16, icon.Size);
        var frame = Assert.IsType<Border>(icon.Parent);
        Assert.Equal(22, frame.Width);

        var title = Of<TextBlock>(row).Single(text => text.Text == "Alpha One");
        var summary = Of<TextBlock>(row).Single(text => text.Text == "Codex · Medium effort");
        Assert.Equal(2, summary.Bounds.Y - title.Bounds.Bottom, 0.5);
        Assert.Equal(13, title.FontSize);
        Assert.Equal(FontWeight.Normal, title.FontWeight);
        Assert.Equal(11, summary.FontSize);
        Assert.Equal(FontWeight.Normal, summary.FontWeight);
        Assert.Equal(Chrome.PrimaryText.Resolve(title.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
        Assert.Equal(Chrome.SecondaryText.Resolve(summary.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(summary.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:178-183,208-216 grip 11 medium Chrome.secondaryText at 0.7 until hovered; hint 12 Chrome.secondaryText, shown only while the block is hovered
    public void The_grip_brightens_on_hover_and_the_slider_hint_shows_only_while_the_block_is_hovered() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha"));
        var row = Assert.Single(Rows(rig));
        var glyph = Of<SwiftImage>(row).Single(image => image.SymbolName == "line.3.horizontal");
        var gripFont = SwiftImage.GetFont(glyph)!.Resolve();
        Assert.Equal(11, gripFont.FontSize);
        Assert.Equal(FontWeight.Medium, gripFont.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
        var hint = Of<SwiftImage>(row).Single(image => image.SymbolName == "slider.horizontal.3");
        var hintFont = SwiftImage.GetFont(hint)!.Resolve();
        Assert.Equal(12, hintFont.FontSize);
        Assert.Equal(FontWeight.Normal, hintFont.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(hint.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(hint.Foreground).Color);

        Assert.Equal(0.7, glyph.Opacity, 0.001);
        Assert.Equal(0, hint.Opacity);

        rig.Window.MouseMove(Centre(Grip(row), rig.Window));
        rig.Sync();
        Assert.Equal(1, glyph.Opacity, 0.001); // the pointer is on the grip: full opacity
        Assert.Equal(0, hint.Opacity); // and not on the block

        var block = Of<Button>(row).First(button => AutomationProperties.GetName(button) != "Remove from the picker");
        rig.Window.MouseMove(Centre(block, rig.Window));
        rig.Sync();
        Assert.Equal(0.7, glyph.Opacity, 0.001);
        Assert.Equal(1, hint.Opacity); // the pointer is on the block: the slider hint shows

        rig.Window.MouseMove(new Point(2, 2));
        rig.Sync();
        Assert.Equal(0, hint.Opacity);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-038")] // ModelsSettingsPage.swift:246-248,284-295 the minus is a 12 medium Chrome.secondaryText "minus.circle" that removes the model inside Chrome.panelSlide
    public void The_minus_removes_the_model_from_the_picker_inside_the_panel_slide() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], Pins("alpha", "bravo"));
        rig.WatchModelList();

        var row = Rows(rig)[0];
        var minus = Of<Button>(row).Single(button => AutomationProperties.GetName(button) == "Remove from the picker");
        var glyph = Of<SwiftImage>(minus).Single();
        Assert.Equal("minus.circle", glyph.SymbolName);
        var font = SwiftImage.GetFont(glyph)!.Resolve();
        Assert.Equal(12, font.FontSize);
        Assert.Equal(FontWeight.Medium, font.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);

        rig.Click(minus);

        Assert.Equal([Pin("bravo")], rig.Model.Settings.ModelList.ToArray());
        Assert.Equal(Chrome.PanelSlide, rig.ModelListTransaction);
    });
}
