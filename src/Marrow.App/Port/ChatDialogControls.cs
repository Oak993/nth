// PORT-ONLY: theme-less Avalonia templates for ChatView's system-styled dialog controls.
// ChatView.swift:1558-1600,1672-1717; R-VIEW-1. Glass follows FollowUpEditor/WelcomeView's shared recipe;
// the native text-field/checkbox bezel and caret appearance remain UNVERIFIED under OD-0001.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port;

internal static class ChatDialogControls
{
    // Layer style Content Area / Input Fields / 01 - Idle (macOS 27 UI Kit): fill Light #FFFFFF / Dark #1E1E1E, border
    // Light #000000@0.05 / Dark #FFFFFF@0.04.
    internal static readonly SwiftColor FieldFill = SwiftColor.Dynamic(SwiftColor.Rgb(0xFFFFFF), SwiftColor.Rgb(0x1E1E1E));
    internal static readonly SwiftColor FieldStroke = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.05), new SwiftColor(white: 1, opacity: 0.04));

    public static void Plain(Button button) => button.Template = new FuncControlTemplate<Button>((owner, scope) =>
        new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
    // Native style kinds are kept without guessing the platform's implicit metric values (ChatView STOP #5).
    public static readonly AttachedProperty<string?> InputStyleProperty = AvaloniaProperty.RegisterAttached<Control, string?>("ChatInputStyle", typeof(ChatDialogControls));
    public static readonly AttachedProperty<string?> SymbolProperty = AvaloniaProperty.RegisterAttached<Control, string?>("ChatLabelSymbol", typeof(ChatDialogControls));
    public static string? GetSymbol(Control control) => control.GetValue(SymbolProperty);
    public static void SetSymbol(Control control, string symbol) => control.SetValue(SymbolProperty, symbol);
    public static void Glass(Button button, bool prominent = false)
    {
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive();
        if (prominent) effect = effect.Tint(Chrome.Accent, 0.3); // shared WelcomeView glassProminent recipe.
        SwiftGlass.SetEffect(button, effect); // retained even before the template has been applied, ED-180.
        button.Template = new FuncControlTemplate<Button>((owner, scope) =>
        {
            var label = new ContentPresenter { Name = "PART_ContentPresenter", VerticalAlignment = VerticalAlignment.Center,
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope);
            Control content = label;
            if (GetSymbol(owner) is { } symbol) content = new StackPanel { Orientation = Orientation.Horizontal, Children = { SwiftImage.SystemName(symbol), label } };
            var layer = new Border { Child = content, Height = Chrome.CapsuleContentHeight, Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0) };
            layer.Capsule(RoundedCornerStyle.Continuous);
            SwiftGlass.SetEffect(layer, effect);
            if (prominent) layer.Background = Chrome.Accent.Opacity(0.3).Brush(layer);
            return layer;
        });
    }

    public static void TextEditor(TextBox field, bool showsPrompt = true, string style = "TextEditor")
    {
        field.SetValue(InputStyleProperty, style);
        field.Template = new FuncControlTemplate<TextBox>((box, scope) =>
        {
            var text = new TextPresenter { Name = "PART_TextPresenter", TextWrapping = box.TextWrapping,
                [!TextPresenter.TextProperty] = box[!TextBox.TextProperty],
                [!TextPresenter.PasswordCharProperty] = box[!TextBox.PasswordCharProperty], // a secure field stays masked
                [!TextPresenter.RevealPasswordProperty] = box[!TextBox.RevealPasswordProperty],
                [!TextPresenter.SelectionStartProperty] = box[!TextBox.SelectionStartProperty], // the selection is drawn
                [!TextPresenter.SelectionEndProperty] = box[!TextBox.SelectionEndProperty],
                [!TextPresenter.CaretIndexProperty] = box[!TextBox.CaretIndexProperty] }.RegisterInNameScope(scope);
            var prompt = new TextBlock { IsHitTestVisible = false, Foreground = Chrome.SecondaryText.Brush(box),
                [!TextBlock.TextProperty] = box[!TextBox.PlaceholderTextProperty] };
            PlainControls.FieldBrushes(box, text);
            void Update() => prompt.IsVisible = showsPrompt && string.IsNullOrEmpty(box.Text);
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Update(); };
            Update();
            var content = new Panel { Children = { prompt, text } };
            if (style != "RoundedBorder") return content;
            // `.textFieldStyle(.roundedBorder)`: the system text field's bezel at the regular size every site uses
            // (ChatView.swift:1673,1682, QuestionTab.swift:237-238, SidebarView.swift:1007). OD-0042 / OD-0045: SOURCE FACT
            // (Apple design resource), "Apple macOS 27 UI Kit", Text Fields / <Dark|Light> / 3 Rg and layer style Content
            // Area / Input Fields (read 2026-10-02; E:\Droppy\work\apple-kit\macos27-uikit-controls.md): 24 high, radius 6,
            // fill #1E1E1E / #FFFFFF, a 1 px border #FFFFFF@0.04 / #000000@0.05, the text 6 in from the left and 6 from
            // the right while typing, centred vertically. The kit draws the border outside the 24-point frame; Avalonia
            // draws it inside, so the padding gives back the point (ENGINEERING DECISION). Keyboard focus adds the
            // focus ring, a 3.5 px accent@0.25 band outside the bezel (the accent stands for System Blue). The site's
            // font is kept (ChatView sets callout monospaced on its command field).
            // A single line longer than the field scrolls inside the bezel to keep the caret in view (the TextBox brings
            // its caret into view through the scroll view it finds as PART_ScrollViewer) and never paints past it.
            var line = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Content = content,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            }.RegisterInNameScope(scope);
            var bezel = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 0, 5, 0), BorderThickness = new Thickness(1), MinHeight = 24, ClipToBounds = true, Child = line };
            bezel.Background = FieldFill.Brush(bezel);
            bezel.BorderBrush = FieldStroke.Brush(bezel);
            var ring = Chrome.Accent.Opacity(0.25).Brush(bezel);
            void Focus() => bezel.BoxShadow = box.IsKeyboardFocusWithin && ring is Avalonia.Media.ISolidColorBrush halo
                ? new Avalonia.Media.BoxShadows(new Avalonia.Media.BoxShadow { Spread = 3.5, Color = halo.Color })
                : default;
            box.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsKeyboardFocusWithinProperty) Focus(); };
            bezel.ActualThemeVariantChanged += (_, _) => Focus();
            Focus();
            return bezel;
        });
    }

    public static void Checkbox(CheckBox box)
    {
        box.Template = new FuncControlTemplate<CheckBox>((owner, scope) =>
        {
            var marker = new Decorator();
            void Update() => marker.Child = SwiftImage.SystemName(owner.IsChecked == true ? "checkmark.square.fill" : "square");
            owner.IsCheckedChanged += (_, _) => Update();
            Update();
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { marker,
                new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope) } };
        });
    }

    public static Grid Footer(Control leading, Control cancel, Control save, double? spacing = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        if (spacing is { } gap) grid.ColumnSpacing = gap;
        grid.Children.Add(leading);
        Grid.SetColumn(cancel, 2); grid.Children.Add(cancel);
        Grid.SetColumn(save, 3); grid.Children.Add(save);
        return grid;
    }
}
