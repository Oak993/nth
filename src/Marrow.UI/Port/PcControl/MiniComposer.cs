// PORT-ONLY: Tactus pill v2, the card's reply field (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Mini composer: one line that
// grows to 4, placeholder "Answer, or say what to do instead", and a Send button. Enter sends. Shift+Enter is a new
// line", "While an IME composition is open, Enter commits the composition and does not send", "It never steals focus";
// canvas `.mc`, `.mct`, `.mcs`). The field is the one place on the pill and card whose click activates the overlay
// (OverlayActivation.Activates, ED-NEW-pcctl-20 item 8). Enter and Esc are the card's (CardShield.Key): the field only
// reports them, with whether an IME composition is open (the presenter's preedit text).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.PcControl;

public sealed class MiniComposer : Border
{
    /// <summary>One line, and the most it grows to (four lines of 20 DIP and its 5 DIP padding).</summary>
    public const double LineHeight = 20;
    public const double MinFieldHeight = 30;
    public const double MaxFieldHeight = 90;

    /// <summary>The Send button's control id (a free control: no shield, no rest).</summary>
    public const string SendId = "send";

    private TextPresenter? presenter;

    public MiniComposer(string placeholder)
    {
        Placeholder = placeholder;
        Margin = new Thickness(0, 12, 0, 0);
        Padding = new Thickness(14, 5, 5, 5);
        CornerRadius = new CornerRadius(20);
        Background = CardLook.Field;
        BoxShadow = CardLook.FieldRing;
        Field = new TextBox
        {
            Name = "composer",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = CardLook.Text,
            FontSize = 14,
            Foreground = CardLook.Text1,
            PlaceholderText = placeholder,
            PlaceholderForeground = CardLook.Text3,
            MinHeight = MinFieldHeight,
            MaxHeight = MaxFieldHeight,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Bottom,
            Template = FieldTemplate,
        };
        AutomationProperties.SetName(Field, placeholder);
        OverlayActivation.SetActivates(Field, true);
        Send = new CardButton(CardButtonLook.Send, CardLook.Icon(CardLook.Send, 14, CardLook.Text1)) { IsEnabled = false, VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetName(Send, "Send");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(Field, 0);
        Grid.SetColumn(Send, 1);
        Send.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(Field);
        row.Children.Add(Send);
        Child = row;
        Field.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Send.IsEnabled = HasText;
            else if (e.Property == IsKeyboardFocusWithinProperty) BoxShadow = Field.IsKeyboardFocusWithin ? CardLook.FieldRingOn : CardLook.FieldRing;
        };
        Field.AddHandler(KeyDownEvent, OnFieldKey, RoutingStrategies.Tunnel);
        Field.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5) Grew?.Invoke();
        };
    }

    public string Placeholder { get; }

    public TextBox Field { get; }

    public CardButton Send { get; }

    public string Text
    {
        get => Field.Text ?? "";
        set => Field.Text = value;
    }

    public bool HasText => !string.IsNullOrWhiteSpace(Field.Text);

    /// <summary>An IME composition is open (the presenter shows preedit text).</summary>
    public bool Composing => presenter?.PreeditText is { Length: > 0 };

    /// <summary>The field has the keyboard.</summary>
    public bool Focused => Field.IsKeyboardFocusWithin;

    /// <summary>The field's height changed (a line came or went), so the card's height may change.</summary>
    public event Action? Grew;

    /// <summary>Enter, Shift+Enter or Esc in the field: the card decides (CardShield.Key) and marks it handled.</summary>
    public event Action<KeyEventArgs>? KeyPressed;

    /// <summary>The template's presenter (tests set its preedit text).</summary>
    internal TextPresenter? Presenter => presenter;

    private void OnFieldKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape) KeyPressed?.Invoke(e);
    }

    /// <summary>Inserts a new line at the caret (Shift+Enter).</summary>
    public void NewLine()
    {
        var text = Field.Text ?? "";
        var at = Math.Clamp(Field.CaretIndex, 0, text.Length);
        var start = Math.Min(Field.SelectionStart, Field.SelectionEnd);
        var end = Math.Max(Field.SelectionStart, Field.SelectionEnd);
        if (end > start && end <= text.Length)
        {
            text = text.Remove(start, end - start);
            at = start;
        }
        Field.Text = text.Insert(at, "\n");
        Field.CaretIndex = at + 1;
        Field.SelectionStart = Field.SelectionEnd = at + 1;
    }

    // The port carries no theme package (PlainControls.PlainTextField): the text, its prompt while empty, and a scroller
    // that keeps the caret in view once the field reaches its four lines.
    private FuncControlTemplate<TextBox> FieldTemplate => new((box, scope) =>
    {
        var prompt = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 0, 5),
            LineHeight = LineHeight,
            [!TextBlock.TextProperty] = box[!TextBox.PlaceholderTextProperty],
            [!TextBlock.ForegroundProperty] = box[!TextBox.PlaceholderForegroundProperty],
            [!TextBlock.FontSizeProperty] = box[!TextBox.FontSizeProperty],
            [!TextBlock.FontFamilyProperty] = box[!TextBox.FontFamilyProperty],
        };
        var text = new TextPresenter
        {
            Name = "PART_TextPresenter",
            Margin = new Thickness(0, 5, 0, 5),
            LineHeight = LineHeight,
            TextWrapping = TextWrapping.Wrap,
            CaretBrush = CardLook.Text1,
            SelectionBrush = CardLook.White(0.3),
            [!TextPresenter.TextProperty] = box[!TextBox.TextProperty],
            [!TextPresenter.SelectionStartProperty] = box[!TextBox.SelectionStartProperty],
            [!TextPresenter.SelectionEndProperty] = box[!TextBox.SelectionEndProperty],
            [!TextPresenter.CaretIndexProperty] = box[!TextBox.CaretIndexProperty],
            [!Avalonia.Controls.Documents.TextElement.ForegroundProperty] = box[!TextBox.ForegroundProperty],
        }.RegisterInNameScope(scope);
        presenter = text;
        void ShowPrompt() => prompt.IsVisible = string.IsNullOrEmpty(box.Text) && string.IsNullOrEmpty(text.PreeditText);
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowPrompt();
        };
        text.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextPresenter.PreeditTextProperty) ShowPrompt();
        };
        ShowPrompt();
        var scroller = new ScrollViewer
        {
            Name = "PART_ScrollViewer",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = new Panel { Background = Brushes.Transparent, Children = { prompt, text } },
        }.RegisterInNameScope(scope);
        return scroller;
    });
}
