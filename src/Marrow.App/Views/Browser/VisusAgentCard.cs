using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.NthUi;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W11, the cards the web tools ask with (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10: the per-site
// card "Let Claude Sonnet use example.com in Visus?" with Allow once / Always allow / Don't allow, never Always on a shared
// tab; the cross-origin card with the full address, its query spelled out and highlighted, "This address carries text from the
// page you were on", and Allow once / Always for this site pair / Don't allow; the guarded-action card with the element's own
// words, the content rows, the chat and the model, Confirm / Cancel and "Enter never confirms"; the web_eval card; "the Tactus
// sheet rules: no default focus, Enter does nothing, 400 ms shield, never under the pointer, the keyboard path of 10.1"; the
// canvas's .pop .pt2 .kv .who .row .pbtn .warn .foot; ED-NEW-web-w11-1). No Swift counterpart. The card is a top-level popup
// over the toolbar or the page (never drawn by the page) with nth's own icon strip, so a page cannot fake it.

namespace Marrow.App.Views.Browser;

/// <summary>One agent card: the ask's words and its buttons.</summary>
public sealed class VisusAgentCard : Border
{
    /// <summary>The guarded-action card's width (the canvas's .guard); the others are a prompt's.</summary>
    public const double GuardWidth = 300;

    /// <summary>How far the card keeps from the pointer when it would open under it (DIPs).</summary>
    public const int PointerMargin = 16;

    private readonly long shownAt;
    private readonly List<Button> buttons = [];
    private bool closed;

    public VisusAgentCard(VisusAgentAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        Ask = ask;
        shownAt = VisusHost.Time.GetTimestamp();
        Padding = new Thickness(14);
        var body = new StackPanel { Orientation = Orientation.Vertical };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { Icon(), Text("nth · Visus", 11.5, PaneTokens.Text3) },
        });
        string detail;
        switch (ask.Request)
        {
            case WebSiteAsk site:
                Width = 310;
                Title = site.Title;
                detail = site.Shared ? "Your tab, shared with the model in this chat." : "A tab the model opened in this chat.";
                body.Children.Add(Heading(PaneGlyphKind.Shield, Title, detail));
                body.Children.Add(Rows([new("Site", site.Origin)]));
                Row(body, ("Allow once", VisusAgentChoice.Once, true), site.AllowAlways ? ("Always allow", VisusAgentChoice.Always, false) : ((string, VisusAgentChoice, bool)?)null, ("Don't allow", VisusAgentChoice.Deny, false));
                body.Children.Add(Foot(VisusPrompt.Foot));
                break;
            case WebNavigateAsk navigate:
                Width = 310;
                Title = navigate.Title;
                detail = navigate.From.Count > 0 ? "The model read " + string.Join(", ", navigate.From.Select(WebPolicy.SourceName)) + " before this." : "";
                body.Children.Add(Heading(PaneGlyphKind.Shield, Title, detail));
                body.Children.Add(Address(navigate.Url));
                if (navigate.Query.Count > 0) body.Children.Add(Rows(navigate.Query.Select(q => new KeyValuePair<string, string>(q.Name, q.Value)).ToList(), navigate.Query.Select(q => q.FromPage).ToList()));
                if (navigate.CarriesPageText) body.Children.Add(Warning(WebNavigateAsk.CarriesNote));
                Row(body, ("Allow once", VisusAgentChoice.Once, true), ("Always for this site pair", VisusAgentChoice.Always, false), ("Don't allow", VisusAgentChoice.Deny, false));
                body.Children.Add(Foot(VisusPrompt.Foot));
                break;
            case WebGuardAsk guard:
                Width = GuardWidth;
                Title = guard.Title;
                body.Children.Add(Heading(PaneGlyphKind.Shield, Title, guard.Detail));
                if (guard.Rows.Count > 0) body.Children.Add(Rows(guard.Rows));
                body.Children.Add(Who(guard.Chat, guard.Model));
                Row(body, (guard.ConfirmLabel, VisusAgentChoice.Once, true), ("Cancel", VisusAgentChoice.Deny, false));
                body.Children.Add(Foot(guard.Foot));
                break;
            case WebEvalAsk eval:
                Width = 310;
                Title = eval.Title;
                body.Children.Add(Heading(PaneGlyphKind.Shield, Title, "The model's scripts run in this preview's page, for this project only."));
                body.Children.Add(Rows([new("Site", eval.Origin)]));
                Row(body, ("Allow", VisusAgentChoice.Once, true), ("Don't allow", VisusAgentChoice.Deny, false));
                body.Children.Add(Foot(VisusPrompt.Foot));
                break;
            default:
                throw new ArgumentException("Not a web ask.", nameof(ask));
        }
        Child = body;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(this, Title);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Window);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointer, RoutingStrategies.Tunnel);
    }

    public VisusAgentAsk Ask { get; }

    /// <summary>The card's first line ("Send invite?", "Let Claude Sonnet use example.com in Visus?").</summary>
    public string Title { get; }

    /// <summary>The card's buttons in order (the first is the strong one).</summary>
    public IReadOnlyList<Button> Buttons => buttons;

    /// <summary>The button for <paramref name="choice"/>, if the card has it.</summary>
    public Button? ButtonFor(VisusAgentChoice choice) => buttons.FirstOrDefault(b => b.Tag is VisusAgentChoice c && c == choice);

    /// <summary>Within the 400 ms shield of appearing: the buttons ignore clicks and keys.</summary>
    public bool IsShielded => VisusHost.Time.GetElapsedTime(shownAt) < VisusPrompt.Shield;

    /// <summary>The card was answered (or went away) and should close.</summary>
    public event Action<VisusAgentCard>? Done;

    /// <summary>F6 into the card (10.1): its first button takes the keyboard. It never does so by itself.</summary>
    public bool FocusFirst() => !closed && buttons.Count > 0 && buttons[0].Focus(NavigationMethod.Tab);

    /// <summary>The ask went away without an answer here (withdrawn, answered elsewhere): the card closes.</summary>
    public void Close()
    {
        if (closed) return;
        closed = true;
        Done?.Invoke(this);
    }

    /// <summary>Esc, the window's Esc: Don't allow (the shield does not hold a refusal back).</summary>
    public void Refuse()
    {
        if (closed) return;
        closed = true;
        Ask.Answer(VisusAgentChoice.Deny);
        Done?.Invoke(this);
    }

    internal void Choose(VisusAgentChoice choice)
    {
        if (closed || IsShielded) return; // a click or key that landed as the card appeared does nothing
        closed = true;
        Ask.Answer(choice);
        Done?.Invoke(this);
    }

    /// <summary>
    /// "Never under the pointer": how far (screen pixels, along x) to move a card at <paramref name="card"/> so that it keeps
    /// <paramref name="margin"/> from <paramref name="pointer"/>; 0 when the pointer is already clear of it. It moves the shorter way.
    /// </summary>
    public static int PointerShift(PixelRect card, PixelPoint? pointer, int margin)
    {
        if (pointer is not { } p) return 0;
        var near = new PixelRect(card.X - margin, card.Y - margin, card.Width + 2 * margin, card.Height + 2 * margin);
        if (!near.Contains(p)) return 0;
        var right = p.X + margin - card.X; // the card's left edge moves past the pointer
        var left = p.X - margin - card.Right; // its right edge moves before it (negative)
        return Math.Abs(left) <= right ? left : right;
    }

    private void Row(StackPanel body, params (string Label, VisusAgentChoice Choice, bool Strong)?[] items)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var item in items)
        {
            if (item is not { } button) continue;
            var choice = button.Choice;
            var made = NthUiPress.Button(button.Label, button.Strong ? NthUiFaceStyle.Primary : NthUiFaceStyle.Plain, null, () => Choose(choice), 30);
            made.Tag = choice;
            made.IsDefault = false; // Enter never answers a card
            made.IsCancel = false; // Esc is handled here
            made.Margin = new Thickness(0, 0, 6, 6);
            AutomationProperties.SetName(made, button.Label);
            buttons.Add(made);
            row.Children.Add(made);
        }
        body.Children.Add(row);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true; // Enter does nothing here, and never confirms
                return;
            case Key.Escape:
                e.Handled = true;
                Refuse();
                return;
            case Key.Space when IsShielded:
                e.Handled = true;
                return;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && IsShielded) e.Handled = true; // a Button clicks on Space's release
    }

    private void OnPointer(object? sender, PointerEventArgs e)
    {
        if (IsShielded) e.Handled = true;
    }

    private static Control Heading(PaneGlyphKind kind, string title, string detail)
    {
        var glyph = new PaneGlyph(kind, 20) { Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        glyph.Foreground = PaneTokens.Text1.Brush(glyph);
        glyph.ActualThemeVariantChanged += (_, _) => glyph.Foreground = PaneTokens.Text1.Brush(glyph);
        var heading = Text(title, 13.5, PaneTokens.Text1, wrap: true);
        heading.FontWeight = FontWeight.SemiBold;
        var words = new StackPanel { Orientation = Orientation.Vertical, Children = { heading } };
        if (detail.Length > 0) words.Children.Add(Text(detail, 12.5, PaneTokens.Text2, wrap: true, margin: new Thickness(0, 2, 0, 0)));
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(glyph, Dock.Left);
        top.Children.Add(glyph);
        top.Children.Add(words);
        return top;
    }

    // The full address, its query values highlighted (DESIGN 5.10 "the full URL with the query string expanded and highlighted").
    private static Control Address(string url)
    {
        var block = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace") };
        void Paint()
        {
            block.Inlines ??= new InlineCollection();
            block.Inlines.Clear();
            foreach (var part in WebAgentUrls.Parts(url))
            {
                var run = new Run(part.Text) { Foreground = (part.Highlight ? PaneTokens.Text1 : PaneTokens.Text2).Brush(block) };
                if (part.Highlight)
                {
                    run.FontWeight = FontWeight.SemiBold;
                    run.Background = VisusTokens.ChipFill.Brush(block);
                }
                block.Inlines.Add(run);
            }
        }
        Paint();
        block.ActualThemeVariantChanged += (_, _) => Paint();
        AutomationProperties.SetName(block, url);
        return block;
    }

    // The canvas's .kv: one row per field, the name in the narrow column; a value from the page read is highlighted.
    private static Control Rows(IReadOnlyList<KeyValuePair<string, string>> rows, IReadOnlyList<bool>? highlighted = null)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        for (var i = 0; i < rows.Count; i++)
        {
            var name = Text(rows[i].Key, 12, PaneTokens.Text3, margin: new Thickness(0, 0, 10, 0));
            name.Width = 54;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Top;
            var value = Text(rows[i].Value, 12, PaneTokens.Text1, wrap: true);
            if (highlighted is { } marks && i < marks.Count && marks[i]) value.FontWeight = FontWeight.SemiBold;
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 6) };
            DockPanel.SetDock(name, Dock.Left);
            line.Children.Add(name);
            line.Children.Add(value);
            stack.Children.Add(line);
        }
        var box = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 10, 0, 0), Child = stack };
        void Look() => box.Background = VisusTokens.AddressFill.Brush(box);
        Look();
        box.ActualThemeVariantChanged += (_, _) => Look();
        return box;
    }

    // The canvas's .who: the chat and the model as chips.
    private static Control Who(string? chat, string model)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var word in new[] { chat, model }.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            var chip = new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Child = Text(word!, 11, PaneTokens.Text2),
            };
            void Look() => chip.Background = VisusTokens.ChipFill.Brush(chip);
            Look();
            chip.ActualThemeVariantChanged += (_, _) => Look();
            row.Children.Add(chip);
        }
        return row;
    }

    private static Control Warning(string text)
    {
        var glyph = new PaneGlyph(PaneGlyphKind.Warning, 13) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
        var words = Text(text, 12, VisusTokens.NotSecureText, wrap: true);
        glyph.Foreground = VisusTokens.NotSecureText.Brush(glyph);
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        row.Children.Add(words);
        AutomationProperties.SetName(row, text);
        return row;
    }

    private static TextBlock Foot(string text) => Text(text, 11.5, PaneTokens.Text3, wrap: true, margin: new Thickness(0, 10, 0, 0));

    private static Control Icon()
    {
        var icon = AppIconImage.Make(14);
        icon.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    private static TextBlock Text(string text, double size, SwiftColor color, bool wrap = false, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = margin ?? default,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.Foreground = color.Brush(block);
        block.ActualThemeVariantChanged += (_, _) => block.Foreground = color.Brush(block);
        return block;
    }
}
