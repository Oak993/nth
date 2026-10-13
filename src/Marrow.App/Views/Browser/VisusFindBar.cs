using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Localisation;
using Marrow.Core.Port;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.App.Views.Artifacts;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: approved index.html .fbar (330, radius14, 13pt query, 11.5pt mono count), DESIGN 5.3; ED-NEW-web-w8c-1.
public sealed class VisusFindBar : Border
{
    private readonly VisusFindController find;
    private readonly Action close;
    private readonly TextBlock count = new() { FontSize = 11.5, FontWeight = FontWeight.Medium, FontFamily = TabulaTokens.Mono, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox matchCase = new() { Content = "Aa", FontSize = 11.5, Padding = default, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock matchCaseText = new() { Text = "Aa", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly AnimatedColor caseFill;
    private readonly PaneGlyph search = new(PaneGlyphKind.Search, 16) { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button previous;
    private readonly Button next;
    private string query = "";
    public TextBox Box { get; }
    public VisusFindBar(VisusFindController find, Action close)
    {
        this.find = find; this.close = close;
        Width = 328; Padding = new Thickness(11, 5, 5, 5);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,6,*,6,Auto,6,22,6,26,6,26,6,26"), MinHeight = 26 };
        Box = PlainControls.TextField(UiText.Get("visus.find.find_in_page"), new Binding<string>(() => query, value => query = value));
        Box.FontSize = 13; Box.MinWidth = 36; Box.VerticalContentAlignment = VerticalAlignment.Center;
        Box.TextChanged += (_, _) => Search();
        Box.KeyDown += OnKey;
        previous = Glyph(PaneGlyphKind.Chevron, UiText.Get("visus.find.previous"), () => find.Move(true), 180);
        next = Glyph(PaneGlyphKind.Chevron, UiText.Get("visus.find.next"), () => find.Move(false));
        var caseFace = new Border { Width = 22, Height = 24, CornerRadius = new CornerRadius(6), Child = matchCaseText };
        caseFill = new AnimatedColor(caseFace); caseFace.Background = caseFill.Brush;
        matchCase.Content = caseFace; matchCase.Width = 22; matchCase.Height = 24; matchCase.KeyboardFocus(6);
        var controls = new Control[] { search, Box, count, matchCase, previous, next, Glyph(PaneGlyphKind.Close, UiText.Get("visus.find.close"), close) };
        for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i * 2); row.Children.Add(controls[i]); }
        Child = row;
        AutomationProperties.SetName(Box, UiText.Get("visus.find.find_in_page"));
        AutomationProperties.SetName(matchCase, UiText.Get("visus.find.match_case"));
        ToolTip.SetTip(matchCase, UiText.Get("visus.find.match_case"));
        matchCase.IsCheckedChanged += (_, _) => { RefreshCase(true); Search(); };
        ActualThemeVariantChanged += (_, _) => Refresh();
        find.Changed += Refresh;
        RefreshCase(false); Refresh();
    }
    private Button Glyph(PaneGlyphKind glyph, string name, Action action, double rotation = 0)
    {
        var icon = new PaneGlyph(glyph, 14);
        if (rotation != 0) icon.RenderTransform = new RotateTransform(rotation);
        var face = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Child = icon };
        var fill = new AnimatedColor(face); face.Background = fill.Brush;
        var ink = new AnimatedColor(icon); icon.Foreground = ink.Brush;
        var button = PlainControls.Button(face, action).KeyboardFocus(8);
        button.Padding = default; button.Height = 26; button.Width = 26;
        var hovering = false;
        void Look(bool animate)
        {
            var on = hovering && button.IsEffectivelyEnabled;
            var reduced = EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
            var curve = animate && !reduced ? Animation.TimingCurve(.25, .1, .25, 1, .15) : null;
            // Keep the same RGB at alpha0: CSS interpolates this tint without a grey fringe.
            fill.Set(on ? PaneTokens.HoverFill : PaneTokens.HoverFill.Opacity(0), curve);
            ink.Set(on ? PaneTokens.Text1 : PaneTokens.Text2, curve);
        }
        button.OnHover(value => { hovering = value; Look(true); });
        button.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) Look(true); };
        Look(false);
        AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
        return button;
    }
    internal void Search() => _ = find.SearchAsync(Box.Text ?? "", matchCase.IsChecked == true);
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { close(); e.Handled = true; }
        else if (e.Key == Key.Enter && (e.KeyModifiers & ~KeyModifiers.Shift) == 0) { find.Move(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
    }
    private void Refresh()
    {
        Box.Foreground = PaneTokens.Text1.Brush(this); Box.PlaceholderForeground = PaneTokens.Text3.Brush(this);
        matchCaseText.Foreground = PaneTokens.Text1.Brush(this);
        count.Text = find.CountText; count.Foreground = PaneTokens.Text2.Brush(this);
        search.Foreground = PaneTokens.Text2.Brush(this);
        previous.IsEnabled = next.IsEnabled = find.Result.MatchCount > 0;
        previous.Opacity = next.Opacity = find.Result.MatchCount > 0 ? 1 : .35;
        AutomationProperties.SetName(count, find.CountText);
    }
    private void RefreshCase(bool animate)
    {
        var reduced = EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
        caseFill.Set(matchCase.IsChecked == true ? PaneTokens.HoverFill : SwiftColor.Clear,
            animate && !reduced ? Marrow.UI.Chrome.Chrome.Hover : null);
    }
    public void FocusQuery() { Box.Focus(); Box.SelectAll(); }
    internal void Dispose() => find.Changed -= Refresh;
}
