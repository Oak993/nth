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

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestHeaderTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-PROVREG-045")] // App/Views/Settings/ModelsSettingsPage.swift:417-447; SettingsView.swift:647
    public void Header_has_quiet_rectangular_hit_area_accessibility_and_transactional_rotation()
    {
        var expanded = false;
        With(() => new CollapsibleProviderHeader(ProviderKind.Commandcode, new Binding<bool>(() => expanded, v => expanded = v), "provider settings"), (view, _, h, w) =>
        {
            var button = Assert.Single(Nodes<Button>(view)); Assert.Equal(22, button.Height);
            var grid = Assert.IsType<Grid>(button.Content); Assert.Equal(4, grid.ColumnDefinitions.Count); Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[2].Width.GridUnitType);
            var icon = Assert.Single(Nodes<ProviderIcon>(view)); Assert.Equal(16, icon.Size); Assert.Equal(ProviderKind.Commandcode, icon.Provider);
            var name = Assert.Single(Nodes<TextBlock>(view)); Assert.Equal("Command Code", name.Text); Font(name, 13, FontWeight.SemiBold); Ink(name, name.Foreground, SwiftColor.Secondary); Assert.Equal(new Thickness(8, 0, 0, 0), name.Margin);
            Assert.Contains(name, button.GetVisualDescendants()); Assert.True(name.Bounds.Width > 0);
            var arrow = Assert.Single(Nodes<SwiftImage>(view), image => image.SymbolName is not null); Assert.Equal("chevron.right", arrow.SymbolName); Assert.Equal(SwiftFont.System(11, SwiftFont.Weight.Semibold), SwiftImage.GetFont(arrow)); Ink(arrow, arrow.Foreground, Chrome.SecondaryText);
            void State(bool open) { Assert.Equal(open ? "Hide the provider settings" : "Show the provider settings", ToolTip.GetTip(button)); Assert.Equal(open ? "Expanded" : "Collapsed", AutomationProperties.GetItemStatus(button)); Assert.Equal("Command Code", AutomationProperties.GetName(button)); Assert.Equal("Shows or hides the provider settings", AutomationProperties.GetHelpText(button)); Assert.Equal(1, AutomationProperties.GetHeadingLevel(button)); }
            State(false); Assert.Equal(0, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
            Hover(w, button, new Point(button.Bounds.Width / 2, 11)); Assert.Equal(0, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
            // ModelsSettingsPage.swift:432 spacer is inside .contentShape(.rect). This click is in its empty span.
            Click(w, button, new Point(button.Bounds.Width / 2, 11)); Assert.True(expanded); State(true);
            Assert.Equal(0, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
            var animation = Animation.Spring(response: 0.32, dampingFraction: 0.9); // Chrome.swift:100-104
            Tick(h, 80); Assert.Equal(90 * animation.Progress(0.08), Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle, 5);
            Tick(h, 80); Assert.Equal(90 * animation.Progress(0.16), Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle, 5);
            Tick(h, animation.Duration * 1000 - 160); Assert.Equal(90, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
            button.Focus(); w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); w.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Drain(w); Assert.False(expanded); State(false);
            Tick(h, 80); Assert.Equal(90 * (1 - animation.Progress(0.08)), Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle, 5);
            Tick(h, animation.Duration * 1000); Assert.Equal(0, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
        }, height: 180);
    }
}
