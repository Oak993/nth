using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Shared rig of the Settings finish tests: the real settings window (SettingsRestFixture) with real pointer and keyboard
// input, plus the checks several records share. Every expected value passed in is transcribed from the pinned Swift by the
// test that calls it.
public abstract class SettingsFinishFixture(HeadlessSession headless) : Marrow.App.Tests.Views.Settings.SettingsRestFixture(headless)
{
    protected static string[] Titles(Control view) => Nodes<SidebarRow>(view).Select(r => Text(r).Single()).ToArray();
    protected static Button RowButton(Control view, string title) => Assert.Single(Nodes<Button>(Nodes<SidebarRow>(view).Single(r => Text(r).Single() == title)));
    protected static Point TopLeft(Control c, Visual to) => c.TranslatePoint(new Point(0, 0), to)!.Value;
    protected static void Type(Avalonia.Controls.Window w, TextBox field, string text) { Click(w, field); w.KeyTextInput(text); Drain(w); }
    protected static void Press(Avalonia.Controls.Window w, Key key) { w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Drain(w); }
    protected static void None<T>(Control root, string where) where T : class => Assert.True(Nodes<T>(root).Length == 0, "unexpected " + typeof(T).Name + " " + where);
    protected static GlassEffect Glass(Control root) => SwiftGlass.GetEffect(Assert.Single(Nodes<Control>(root), c => SwiftGlass.GetEffect(c) is not null))!;

    // Chrome.swift:827-868 ChromeSearchField as placed by SettingsView.swift:245-253 in the pane's chrome row.
    protected void CheckPaneSearch(Control view, Avalonia.Controls.Window w, AppModelHarness h, string prompt)
    {
        var search = Assert.Single(Nodes<ChromeSearchField>(view)); var detail = Field<Control>(view, "detail"); var field = Assert.Single(Nodes<TextBox>(search));
        // :246-264 in the chrome row beside the compact title (never in the scrolling page), the row 32pt tall at minimum.
        None<ChromeSearchField>(Assert.Single(Nodes<ScrollViewer>(detail)), "inside the scrolling page"); var row = Nodes<Grid>(detail).Single(g => g.MinHeight == 32 && Nodes<ChromeCompactTitle>(g).Length == 1);
        Assert.Equal(row.Bounds.Width, TopLeft(search, row).X + 200, 3); Assert.Equal(0, TopLeft(search, row).Y, 3);
        // :861-863 frame(width: 200, height: 28) after 10pt of horizontal padding, then 2pt above and below: a fixed 200 x 32.
        Assert.Equal(200, search.Bounds.Width, 3); Assert.Equal(32, search.Bounds.Height, 3); Assert.Equal(200, ChromeSearchField.Width); Assert.Single(Nodes<Border>(search), b => b.Padding == new Thickness(0, 2));
        var framed = Assert.Single(Nodes<Border>(search), b => b.Width == 200); Assert.Equal(28, framed.Bounds.Height, 3); Assert.Equal(new Thickness(10, 0), framed.Padding);
        // :834-837, :844-851 the magnifier .callout semibold in secondary, the prompt, 12.5pt primary text, the clear glyph .callout at 0.8 of secondary; 6pt between the three.
        var glass = Assert.Single(Nodes<SwiftImage>(search), i => i.SymbolName == "magnifyingglass"); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(glass)); Ink(glass, glass.Foreground, Chrome.SecondaryText);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(glass)); // :838
        Assert.Equal(prompt, field.PlaceholderText); Assert.Equal(12.5, field.FontSize); Ink(field, field.Foreground, Chrome.PrimaryText);
        var clear = Assert.Single(Nodes<Button>(search)); var x = Assert.Single(Nodes<SwiftImage>(clear)); Assert.Equal("xmark.circle.fill", x.SymbolName); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout), SwiftImage.GetFont(x)); Ink(x, x.Foreground, Chrome.SecondaryText.Opacity(0.8));
        Assert.Equal(10, TopLeft(glass, search).X, 3); Assert.Equal(6, TopLeft(field, search).X - (TopLeft(glass, search).X + glass.Bounds.Width), 3); Assert.Equal(6, TopLeft(clear, search).X - (TopLeft(field, search).X + field.Bounds.Width), 3); Assert.Equal(200 - 10, TopLeft(clear, search).X + clear.Bounds.Width, 3);
        // :841 the focus ring is switched off (unlike the sidebar field); :867 presses on it never drag the window; :864 a continuous, non-interactive glass capsule washed 0.3 with the theme.
        Assert.True(field.IsSet(Control.FocusAdornerProperty)); Assert.Null(field.FocusAdorner); Assert.Single(Nodes<NoWindowDragArea>(search));
        var effect = Glass(search); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.Equal(Chrome.GlassTint, effect.TintColor); Assert.Equal(0.3, effect.TintOpacity); Assert.False(effect.IsInteractive);
        // :854-858 empty: still laid out, drawn at opacity 0, disabled and out of the accessibility tree; the field starts at the same place once text arrives (the text never shifts).
        Assert.Equal("Clear search", AutomationProperties.GetName(clear)); Assert.Equal(0, clear.Opacity); Assert.False(clear.IsEnabled); Assert.True(clear.IsVisible); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear)); var fieldAt = TopLeft(field, search); var clearAt = TopLeft(clear, search);
        Type(w, field, "ab"); Assert.Equal("ab", field.Text); Assert.Equal(1, clear.Opacity); Assert.True(clear.IsEnabled); Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear));
        Assert.Equal(fieldAt, TopLeft(field, search)); Assert.Equal(clearAt, TopLeft(clear, search)); Assert.Equal(200, search.Bounds.Width, 3);
        // :866 Escape empties it; the clear button (a point on the drawn ring, the cross is knocked out of the glyph) empties it too.
        Press(w, Key.Escape); Assert.Equal("", field.Text ?? ""); Assert.Equal(0, clear.Opacity);
        Type(w, field, "ab"); Click(w, clear, new Point(4, 8)); Assert.Equal("", field.Text ?? ""); Assert.Equal(0, clear.Opacity);
    }
}
