using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, Tabula's "Ask to change" (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.3 "an inline composer
// at the bottom of the pane ('Describe the change…'). Sending posts a turn in the main thread ... Selecting text in Markdown
// or code first adds 'Change this part' with the selection"; 2.4 airspace; the approved canvas index.html .ask: a 46 DIP glass
// pill, the input and a round send button, rising into place; ED-NEW-web-w14-1). No Swift counterpart. The composer has its
// own row under the preview rather than floating over it: a page is its own window and would cover it. It rises 12 DIP and
// fades in on a spring (Reduce Motion: the fade only); Return sends, Escape closes, Shift+Return is a new line.

namespace Marrow.App.Views.Artifacts;

public sealed class TabulaAsk : Border
{
    public const string Placeholder = "Describe the change…";

    private const double Rise = 12;

    private readonly TextBox input = new()
    {
        PlaceholderText = Placeholder,
        BorderThickness = default,
        Background = Brushes.Transparent,
        FontSize = 13,
        AcceptsReturn = false,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 120,
        Padding = new Thickness(0, 6),
        VerticalAlignment = VerticalAlignment.Center,
        MinHeight = 0,
    };
    private readonly Button send;
    private readonly Border part;
    private readonly TextBlock partText = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabulaMotion motion;
    private bool open;

    public TabulaAsk()
    {
        Margin = new Thickness(12, 0, 12, 12);
        IsVisible = false;
        send = TabulaButtons.Glyph(PaneGlyphKind.Send, "Send", Submit, 34);
        if (send.Content is Border sendFace)
        {
            sendFace.CornerRadius = new CornerRadius(17);
            sendFace.Background = TabulaTokens.PillFill.Brush(sendFace);
            send.OnHover(on => sendFace.Background = (on ? PaneTokens.HoverFill : TabulaTokens.PillFill).Brush(sendFace));
        }
        var clear = TabulaButtons.Glyph(PaneGlyphKind.Close, "Remove the chosen part", () => PartCleared?.Invoke(), 20);
        var partRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var partLabel = new TextBlock { Text = "Change this part", FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        partRow.Children.Add(partLabel);
        partRow.Children.Add(partText);
        partRow.Children.Add(clear);
        part = new Border { Padding = new Thickness(10, 3, 3, 3), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Child = partRow, IsVisible = false };
        partText.MaxWidth = 260;

        var pill = new Border { MinHeight = 46, CornerRadius = new CornerRadius(23), Padding = new Thickness(16, 6, 6, 6), BorderThickness = new Thickness(0.5) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(input);
        Grid.SetColumn(send, 1);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(send);
        pill.Child = grid;
        Child = new StackPanel { Children = { part, pill } };
        void Paint()
        {
            pill.Background = PaneTokens.CardFill(ActualThemeVariant, expanded: true);
            pill.BorderBrush = PaneTokens.Line.Brush(pill);
            pill.BoxShadow = PaneTokens.Shadow(ActualThemeVariant, lifted: true);
            part.Background = TabulaTokens.PillFill.Brush(part);
            partLabel.Foreground = PaneTokens.Text1.Brush(partLabel);
            partText.Foreground = PaneTokens.Text2.Brush(partText);
            input.Foreground = PaneTokens.Text1.Brush(input);
        }
        ActualThemeVariantChanged += (_, _) => Paint();
        Paint();
        motion = new TabulaMotion(this, opacity: 0, rise: Rise);
        input.KeyDown += OnKey;
        input.TextChanged += (_, _) => TextChanged?.Invoke(input.Text ?? "");
        AutomationProperties.SetName(input, "Describe the change");
        AutomationProperties.SetName(this, "Ask to change");
    }

    /// <summary>The user sent the change (its words).</summary>
    public event Action<string>? Sent;

    /// <summary>Escape: the composer closes.</summary>
    public event Action? Dismissed;

    public event Action? PartCleared;

    public event Action<string>? TextChanged;

    public bool IsOpen => open;

    public string Text
    {
        get => input.Text ?? "";
        set => input.Text = value;
    }

    public string? Part => part.IsVisible ? partText.Text : null;

    internal TextBox Field => input;

    internal Button SendButton => send;

    internal TabulaMotion Motion => motion;

    /// <summary>Opens or closes the composer; open moves the focus into it.</summary>
    public void SetOpen(bool value, string? selection)
    {
        part.IsVisible = value && !string.IsNullOrWhiteSpace(selection);
        partText.Text = selection is null ? null : "“" + Shorten(selection) + "”";
        ToolTip.SetTip(part, selection);
        if (open == value) return;
        open = value;
        if (value)
        {
            IsVisible = true;
            motion.To(1, 0);
            input.Focus();
            input.CaretIndex = input.Text?.Length ?? 0;
        }
        else
        {
            motion.To(0, Rise, () =>
            {
                if (!open) IsVisible = false;
            });
        }
    }

    private static string Shorten(string text)
    {
        var line = string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length > 80 ? line[..80] + "…" : line;
    }

    private void Submit()
    {
        var text = (input.Text ?? "").Trim();
        if (text.Length == 0) return;
        Sent?.Invoke(text);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismissed?.Invoke();
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var at = input.CaretIndex;
            input.Text = (input.Text ?? "").Insert(at, "\n");
            input.CaretIndex = at + 1;
            return;
        }
        Submit();
    }
}
