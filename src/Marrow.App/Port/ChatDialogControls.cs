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
            // `.textFieldStyle(.roundedBorder)`: the system text field's bezel. ENGINEERING DECISION (proposal ED-229 (4)):
            // the Windows 11 text box (corner radius 4, padding 10,5,6,6, the control fill and hairline SystemButtons use,
            // the accent border while it has focus; UNVERIFIED numbers, recalled; not yet a numbered ED); recorded deviation
            // from the macOS bezel.
            var bezel = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 5, 6, 6), BorderThickness = new Thickness(1), Child = content };
            bezel.Background = SystemButtons.StandardFill.Brush(bezel);
            void Stroke() => bezel.BorderBrush = (box.IsKeyboardFocusWithin ? Chrome.Accent : SystemButtons.StandardStroke).Brush(bezel);
            box.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsKeyboardFocusWithinProperty) Stroke(); };
            Stroke();
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
