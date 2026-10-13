using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Views;
using Marrow.Core.Localisation;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.NthUi;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8a and W8b, the prompts a Visus tab asks in (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.3 "Visus
// asks: '<site> wants to open <App name>' with the scheme shown, and Remember for this site only for mailto and tel", 8.4 the
// prompt rules: "Prompts never take focus when they appear. Enter does nothing in them; Esc refuses. F6 moves into the open
// prompt; Tab and Space work inside it. For 400 ms after a prompt appears, clicks and keys on its buttons are ignored", 10.1
// "focus lands on the prompt's text, not on a button ... F6 again returns focus to where it was", "the appearance announcement
// ends with 'Press F6 to answer.' The prompt's buttons are named in full", "a Space within 400 ms of the prompt appearing or
// of focus entering it is ignored", 2.4 prompts are top-level popups (never under the native page), 13.3; the canvas's .pop
// .pt2 .warn .row .pbtn .foot; ED-NEW-web-w8a-2, ED-NEW-web-w8b-4). No Swift counterpart. Every prompt hangs from the trust
// strip with a title strip carrying nth's own icon, so a page cannot draw a look-alike in its own area (the strip sits over
// the toolbar, outside any page).

namespace Marrow.App.Views.Browser;

/// <summary>
/// One Visus prompt card (10.1): its text takes the keyboard on F6, Tab and Shift+Tab move between its controls, Space presses
/// the focused one, Enter does nothing, Esc gives the ask's safe answer, F6 again gives the keyboard back. For 400 ms after it
/// appears, and after the keyboard enters it, its buttons ignore clicks and keys.
/// </summary>
public abstract class VisusPromptCard : Border
{
    /// <summary>How long after it appears (or the keyboard enters it) the card ignores clicks and keys (8.4, 10.1).</summary>
    public static readonly TimeSpan Shield = TimeSpan.FromMilliseconds(400);

    /// <summary>The end of every appearance announcement (10.1; resource key visus.prompt.f6_hint).</summary>
    public static string F6Hint => UiText.Get("visus.prompt.f6_hint");

    /// <summary>The card's width (the canvas's .pop).</summary>
    public const double CardWidth = 310;

    private readonly Border lead;
    private readonly WrapPanel buttons;
    private long shieldFrom;
    private IInputElement? returnTo;
    private bool closed;

    protected VisusPromptCard(VisusAsk ask, string title, string detail, PaneGlyphKind glyph)
    {
        Ask = ask;
        Title = title;
        Detail = detail;
        shieldFrom = VisusHost.Time.GetTimestamp();
        Width = CardWidth;
        Padding = new Thickness(14);
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                Icon(),
                Text("nth · Visus", 11.5, PaneTokens.Text3),
            },
        };
        var heading = Text(title, 13.5, PaneTokens.Text1, wrap: true);
        heading.FontWeight = FontWeight.SemiBold;
        var words = new StackPanel { Orientation = Orientation.Vertical, Children = { heading } };
        if (detail.Length > 0)
        {
            var line = Text(detail, 12.5, PaneTokens.Text2, wrap: true);
            line.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(line);
        }
        var mark = new PaneGlyph(glyph, 20) { Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        mark.Foreground = PaneTokens.Text1.Brush(mark);
        mark.ActualThemeVariantChanged += (_, _) => mark.Foreground = PaneTokens.Text1.Brush(mark);
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(mark, Dock.Left);
        top.Children.Add(mark);
        top.Children.Add(words);
        // 10.1: the prompt's text is where F6 lands (a reader hears the question before any button). The ring is the app's
        // focus band; the margin gives back its room so nothing moves when it shows.
        lead = new Border
        {
            Focusable = true,
            Margin = new Thickness(-6),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(9),
            BorderBrush = Brushes.Transparent,
            Child = top,
        };
        AutomationProperties.SetName(lead, detail.Length > 0 ? Sentence(title) + " " + detail : title);
        lead.GotFocus += (_, _) => lead.BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(lead);
        lead.LostFocus += (_, _) => lead.BorderBrush = Brushes.Transparent;
        buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        Body = new StackPanel { Orientation = Orientation.Vertical, Children = { strip, lead } };
        Child = Body;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(this, Announcement);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Assertive);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Window);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointer, RoutingStrategies.Tunnel);
        PropertyChanged += (_, e) =>
        {
            // 10.1: the shield also runs from the moment the keyboard enters the card.
            if (e.Property == IsKeyboardFocusWithinProperty && e.NewValue is true) shieldFrom = VisusHost.Time.GetTimestamp();
        };
        ask.Answered += OnAnswered;
    }

    /// <summary>The question the card answers.</summary>
    public VisusAsk Ask { get; }

    /// <summary>The card's question ("example.com wants to open Mail (mailto:)").</summary>
    public string Title { get; }

    /// <summary>The line under it (the address, the size and source of a file, the realm).</summary>
    public string Detail { get; }

    /// <summary>What a screen reader hears when the card appears (10.1).</summary>
    public string Announcement => Sentence(Detail.Length > 0 ? Sentence(Title) + " " + Detail : Title) + " " + F6Hint;

    /// <summary>The text as a sentence: a full stop added unless it already ends in one (no "page.." read aloud).</summary>
    internal static string Sentence(string text) => text.Length == 0 || text[^1] is '.' or '!' or '?' or '…' ? text : text + ".";

    /// <summary>The card's text, where F6 puts the keyboard (10.1).</summary>
    public Control Lead => lead;

    /// <summary>The card's buttons, in order.</summary>
    public IReadOnlyList<Button> Buttons => buttons.Children.OfType<Button>().ToList();

    /// <summary>Within <see cref="Shield"/> of appearing or of the keyboard entering it: the buttons ignore clicks and keys.</summary>
    public bool IsShielded => VisusHost.Time.GetElapsedTime(shieldFrom) < Shield;

    /// <summary>The card was answered (or cancelled from outside) and should go.</summary>
    public event Action<VisusPromptCard>? Done;

    /// <summary>F6 gave the keyboard back (10.1); the pane puts it on the page when the element it came from is gone.</summary>
    public event Action<VisusPromptCard>? FocusReturned;

    protected StackPanel Body { get; }

    protected bool IsClosed => closed;

    /// <summary>
    /// F6 into the card (10.1): its text takes the keyboard, and <paramref name="from"/> is where F6 again gives it back. It never
    /// does so by itself.
    /// </summary>
    public bool FocusFirst(IInputElement? from = null)
    {
        if (closed) return false;
        if (from is not null && !(from is Visual visual && (ReferenceEquals(visual, this) || this.IsVisualAncestorOf(visual)))) returnTo = from;
        return lead.Focus(NavigationMethod.Tab);
    }

    /// <summary>Esc, a closed tab, a page that went away: the safe answer (Block, Cancel, Dismiss), whatever the shield says.</summary>
    public void Dismiss() => Ask.AnswerSafe();

    /// <summary>A button of the card: <paramref name="label"/> on its face, <paramref name="name"/> for a screen reader (10.1).</summary>
    protected Button AddButton(string label, string name, bool primary, Action pressed, out TextBlock face)
    {
        var button = NthUiPress.Plain(new Button());
        button.Content = NthUiPress.Face(button, primary ? NthUiFaceStyle.Primary : NthUiFaceStyle.Plain, label, null, 30, out face);
        button.Click += (_, _) => Press(pressed);
        button.IsDefault = false;
        button.IsCancel = false; // Esc is the card's own (the window's Esc must not run first)
        AutomationProperties.SetName(button, name);
        buttons.Children.Add(button);
        return button;
    }

    protected Button AddButton(string label, string name, bool primary, Action pressed) => AddButton(label, name, primary, pressed, out _);

    /// <summary>Adds the button row, then the foot (the canvas's .row and .foot), after whatever the card added to its body.</summary>
    protected void Finish(string foot)
    {
        Body.Children.Add(buttons);
        Body.Children.Add(Text(foot, 11.5, PaneTokens.Text3, wrap: true, margin: new Thickness(0, 10, 0, 0)));
    }

    /// <summary>Runs a button's answer unless the shield is up (a click or key that landed as the card appeared does nothing).</summary>
    protected void Press(Action pressed)
    {
        if (closed || IsShielded) return;
        pressed();
    }

    /// <summary>Enter in one of the card's own text fields (a sign-in, a page's prompt); false leaves Enter doing nothing.</summary>
    protected virtual bool OnFieldEnter(TextBox field) => false;

    private void OnAnswered(VisusAsk ask)
    {
        ask.Answered -= OnAnswered;
        if (closed) return;
        closed = true;
        Done?.Invoke(this);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true; // Enter does nothing on a button (8.4, 10.1)
                if (e.Source is TextBox field && !IsShielded) OnFieldEnter(field);
                return;
            case Key.Escape:
                e.Handled = true;
                Dismiss();
                return;
            case Key.F6:
                e.Handled = true;
                ReturnFocus();
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

    /// <summary>F6 again (10.1): the keyboard goes back where it was.</summary>
    internal void ReturnFocus()
    {
        var target = returnTo;
        returnTo = null;
        if (target is InputElement { IsEffectivelyVisible: true } element && TopLevel.GetTopLevel(element) is not null && element.Focus(NavigationMethod.Tab)) return;
        FocusReturned?.Invoke(this);
    }

    private static Control Icon()
    {
        var icon = AppIconImage.Make(14);
        icon.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    internal static string Shorten(string text, int limit = 120) => text.Length <= limit ? text : text[..(limit - 3)] + "…";

    internal static TextBlock Text(string text, double size, SwiftColor color, bool wrap = false, Thickness? margin = null)
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

/// <summary>DESIGN 8.3, 8.4: "example.com wants to open Mail (mailto:)" with Open and Cancel.</summary>
public sealed class VisusPrompt : VisusPromptCard
{
    /// <summary>The prompt's foot, word for word (the canvas's prompts; resource key visus.prompt.foot).</summary>
    public static string Foot => UiText.Get("visus.prompt.foot");

    public VisusPrompt(VisusExternalLaunch launch, string appName)
        : base(launch, ExternalProtocols.PromptTitle(launch.Source, appName, launch.Scheme), Shorten(launch.Uri), PaneGlyphKind.OpenExternal)
    {
        Launch = launch;
        if (launch.CanRemember)
        {
            Remember = new CheckBox { Content = Text(UiText.Get("visus.prompt.remember"), 12.5, PaneTokens.Text1), Margin = new Thickness(30, 10, 0, 0) };
            ChatDialogControls.Checkbox(Remember);
            AutomationProperties.SetName(Remember, UiText.Format("visus.prompt.remember_name", launch.Source, appName));
            Body.Children.Add(Remember);
        }
        OpenButton = AddButton(UiText.Format("visus.prompt.open_app", appName), UiText.Format("visus.prompt.open_app_name", launch.Scheme, appName), primary: false, () => Launch.Answer(true, Remember?.IsChecked == true));
        CancelButton = AddButton(UiText.Get("visus.card.cancel"), UiText.Format("visus.prompt.cancel_name", appName), primary: false, () => Launch.Answer(false));
        Finish(Foot);
        AutomationProperties.SetHelpText(this, launch.Uri);
    }

    public VisusExternalLaunch Launch { get; }

    public Button OpenButton { get; }

    public Button CancelButton { get; }

    /// <summary>"Remember for this site" (mailto and tel from a browse or preview tab only).</summary>
    public CheckBox? Remember { get; }

    /// <summary>Cancel, as the Cancel button gives it (the shield holds).</summary>
    public void Cancel() => Press(() => Launch.Answer(false));
}

/// <summary>
/// The open Visus prompts of a window, newest last: F6 goes into the newest (PaneWorkspace.PromptFirst, 10.1). App links,
/// permissions, downloads, sign-ins and page dialogs all join the same list (W8b).
/// </summary>
public static class VisusPrompts
{
    private static readonly List<(TopLevel Window, Func<bool> Focus)> Open = [];

    /// <summary>A prompt opened in <paramref name="window"/>; the result removes it.</summary>
    public static IDisposable Add(TopLevel window, Func<bool> focus)
    {
        var entry = (window, focus);
        Open.Add(entry);
        return new Removal(() => Open.Remove(entry));
    }

    /// <summary>Whether a prompt is open in <paramref name="window"/>.</summary>
    public static bool AnyIn(TopLevel? window) => window is not null && Open.Any(entry => ReferenceEquals(entry.Window, window));

    /// <summary>F6: the newest prompt of <paramref name="window"/> takes the keyboard; false when it has none.</summary>
    public static bool FocusNewest(TopLevel? window)
    {
        for (var i = Open.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Open[i].Window, window) && Open[i].Focus()) return true;
        return false;
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        private Action? pending = remove;

        public void Dispose()
        {
            pending?.Invoke();
            pending = null;
        }
    }
}

/// <summary>A quiet line under the address field ("Visus does not run script addresses."), gone after a few seconds.</summary>
public sealed class VisusNotice : Border
{
    /// <summary>How long a notice stays.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3.2);

    private readonly TextBlock text = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private DispatcherTimer? timer;

    public VisusNotice()
    {
        Padding = new Thickness(12, 8);
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new PaneGlyph(PaneGlyphKind.Warning, 13) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) }, text },
        };
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    public string Text => text.Text ?? "";

    /// <summary>The notice should go (its time ran out).</summary>
    public event Action? Expired;

    internal void Show(string message)
    {
        text.Text = message;
        AutomationProperties.SetName(this, message);
        timer?.Stop();
        if (!Dispatcher.UIThread.CheckAccess()) return;
        timer = new DispatcherTimer(Duration, DispatcherPriority.Background, (_, _) =>
        {
            timer?.Stop();
            timer = null;
            Expired?.Invoke();
        });
        timer.Start();
    }

    internal void Stop()
    {
        timer?.Stop();
        timer = null;
    }

    private void ApplyLook()
    {
        text.Foreground = PaneTokens.Text1.Brush(this);
        if (Child is StackPanel { Children: [PaneGlyph glyph, ..] }) glyph.Foreground = PaneTokens.Text2.Brush(this);
    }
}
