// PORT-ONLY, owner-ruled addition (OD-0097 D, OD-0095; first-release voice typing, VT-12, voice-SPEC V-12.11): the language question.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// "Which language will you speak?", asked on the first click of the orb when the language default is D (V-7.14): the port's <see cref="PopoverCard"/> (OD-0095: the tip is part of the card's one outline,
/// fill and shadow; its edge 13 pt from its anchor, the point 1 pt short of it) above the orb's slot with the tip down on the orb's centre, below it with the tip up when the window has no room above.
/// 268 wide (the card with its 1 pt edge), the content padded 12 / 12 / 10, corner 14; the material is the pill's (V-12.4): the shared <see cref="GlassMaterial"/> under the card (a rounded rectangle, backdrop
/// blur, no tint of its own), the card's fill the <c>pgBg</c> tint over it, the card's 1 pt edge <c>pgEdge</c> and a 0.5 px <c>pgRim</c> ring inside it. ENGINEERING DECISION: the tip is filled by the card's own
/// tint with no blur behind it (the shared material draws a rounded rectangle, not the card's outline with its tip).
/// Content (SOURCE voice-options.html <c>.vask</c>): the question at 13 semibold in <c>pgLabHi</c>, 9 above the answers <c>English</c>, <c>Hindi</c>, <c>Urdu</c>, <c>Auto-detect</c> (capsules, wrapping, 6 apart), the
/// footer <c>Kept in Settings &gt; Voice typing</c> at 11 in <c>pgLab</c>, 8 below them. A dialog named <c>Voice language</c>.
/// Motion (OD-0076): it opens with its opacity over 0.18 s ease and from a rise of 6 px and a scale of 0.96 over 0.24 s ez, its origin at the tip; an answer lights first and the card closes 0.22 s later
/// (opacity 0.18 s, back to 6 px and 0.96 over 0.24 s); Reduce Motion opens and closes at once. Not designed on the page (ED): Esc, a click outside the card or on the orb again close it with nothing started
/// and nothing stored; on open the focus moves to <c>English</c>, Left/Right and Tab/Shift+Tab move among the four answers (wrapping), Enter/Space pick, and on close the focus goes back to the orb.
/// </summary>
internal sealed class VoiceLanguageQuestion : Panel
{
    internal const string Question = "Which language will you speak?";
    internal const string Footer = "Kept in Settings > Voice typing";
    internal const string DialogName = "Voice language";
    internal const double CardWidth = 268;
    internal const double CardRadius = 14;
    private const double Rise = 6;
    private const double Shrink = 0.96;
    private static readonly string[] AnswerNames = ["English", "Hindi", "Urdu", "Auto-detect"];
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(0.22);

    private readonly LookMotion motion;
    private readonly LookNumber opacityValue;
    private readonly LookNumber progress;
    private readonly PopoverCard card = new() { BodyCornerRadius = CardRadius };
    private readonly Border glass = new();
    private readonly Border rim = new() { IsHitTestVisible = false };
    private readonly Grid shell = new();
    private readonly TextBlock question = new() { Text = Question };
    private readonly TextBlock footer = new() { Text = Footer };
    private readonly WrapPanel answers = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly TranslateTransform rise = new(0, Rise);
    private readonly ScaleTransform scale = new(Shrink, Shrink);
    private readonly List<VoiceAnswerButton> buttons = [];
    private Control? slot;
    private Control? composer;
    private TopLevel? watched;
    private bool open;
    private bool flipped;
    private WorkItem? closing;
    private int serial;
    private Popup? popupHost;

    /// <summary>CZ-7b: the stage's chrome clearance when deciding whether its question fits above.</summary>
    internal double TopClearance { get; set; }

    public VoiceLanguageQuestion()
    {
        motion = new LookMotion(this);
        opacityValue = new LookNumber(motion, 0);
        progress = new LookNumber(motion, 0);
        IsHitTestVisible = false;
        Opacity = 0;
        ClipToBounds = false;
        card.Width = CardWidth;
        card.TipEdge = PopoverTipEdge.Bottom;
        glass.CornerRadius = new CornerRadius(CardRadius);
        glass.IsHitTestVisible = false;
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(CardRadius, RoundedCornerStyle.Continuous)));
        // ED-NEW-voicefix-ui-8: the card's glass is its own piece; inside the composer's glass container it melted into the composer pill below it and flared its top edge
        SwiftGlass.SetStandsAlone(glass, true);
        // ED-NEW-glassmerge-1 (OD-0147): the PopoverCard casts the card's one shadow (OD-0095), so the glass under it draws none of the canvas's past its edge: one shadow, not two,
        // and the composer's outline beside the card stays as it was
        SwiftGlass.SetCastsOwnShadow(glass, true);
        rim.CornerRadius = new CornerRadius(CardRadius);
        SwiftFont.System(13, SwiftFont.Weight.Semibold).Apply(question);
        SwiftFont.System(11).Apply(footer);
        question.Margin = new Thickness(0, 0, 0, 9);
        footer.Margin = new Thickness(0, 8, 0, 0);
        for (var i = 0; i < AnswerNames.Length; i++)
        {
            var button = new VoiceAnswerButton(VoiceLanguage.QuestionAnswers[i], AnswerNames[i]);
            button.Click += (_, _) => Pick(button);
            buttons.Add(button);
            answers.Children.Add(button);
        }
        var content = new StackPanel { Orientation = Orientation.Vertical, Children = { question, answers, footer } };
        card.Child = new Border { Child = content, Padding = new Thickness(12, 12, 12, 10) };
        shell.Children.Add(glass);
        shell.Children.Add(card);
        shell.Children.Add(rim);
        shell.RenderTransform = new TransformGroup { Children = { scale, rise } };
        Children.Add(shell);
        AutomationProperties.SetName(this, DialogName);
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Window);
        card.PropertyChanged += (_, e) =>
        {
            if (e.Property == PopoverCard.TipEdgeProperty) FitMaterial();
        };
        motion.Frame += Apply;
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => Tint();
        DetachedFromVisualTree += (_, _) => Unwatch();
        Tint();
        FitMaterial();
        Apply();
    }

    /// <summary>The answers, in the question's order (English, Hindi, Urdu, Auto-detect).</summary>
    internal IReadOnlyList<VoiceAnswerButton> Answers => buttons;

    internal PopoverCard Card => card;

    internal Border Glass => glass;

    internal Grid Shell => shell;

    internal TextBlock QuestionText => question;

    internal TextBlock FooterText => footer;

    /// <summary>A capsule was picked: the code to store and listen with (en, hi, ur, auto).</summary>
    public event Action<string>? Answered;

    /// <summary>Esc, a click outside the card or on the orb: closed with nothing stored.</summary>
    public event Action? Dismissed;

    /// <summary>Gives the keyboard back (to the orb) when the card closes.</summary>
    public Action? ReturnFocus { get; set; }

    /// <summary>The card is open: on screen and taking the pointer (it closes 0.22 s after an answer).</summary>
    public bool IsOpen => open;

    /// <summary>Whether the card hangs below the orb, its tip up (no room above in the window).</summary>
    public bool IsBelow => flipped;

    internal double ShownOpacity => opacityValue.Value;

    /// <summary>How far the open motion has come: 0 at rest (6 px low, 0.96), 1 open.</summary>
    internal double Progress => progress.Value;

    /// <summary>The orb's slot the card hangs from, and the composer whose edges hold it.</summary>
    public void Attach(Control orbSlot, Control composerView)
    {
        if (slot is not null) slot.LayoutUpdated -= PlaceOnLayout;
        if (composer is not null) composer.LayoutUpdated -= PlaceOnLayout;
        slot = orbSlot;
        composer = composerView;
        composerView.LayoutUpdated += PlaceOnLayout;
        orbSlot.LayoutUpdated += PlaceOnLayout;
    }

    /// <summary>CZ-7b: put this same question in the popover presenter, beyond the stage's rounded clip.</summary>
    internal void AttachPopup(Control orbSlot, Control composerView, Popup popup)
    {
        Attach(orbSlot, composerView);
        popupHost = popup;
    }

    private void PlaceOnLayout(object? sender, EventArgs e) => Place();

    /// <summary>Opens the card. The focus goes to English.</summary>
    public void Open(bool takeFocus = true)
    {
        closing?.Cancel();
        closing = null;
        serial++;
        foreach (var button in buttons) button.Reset();
        if (open) return;
        open = true;
        IsHitTestVisible = true;
        Watch();
        Place();
        opacityValue.To(1, LookCurve.Ease(0.18));
        progress.To(1, LookCurve.Ez(0.24));
        InvalidateMeasure();
        Apply(); // visible now, so the keyboard can land on English
        if (takeFocus && !buttons[0].Focus()) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (open && !buttons.Any(b => b.IsFocused)) buttons[0].Focus(); });
    }

    /// <summary>CZ-4.7.8: a pick-started demonstration answers through the same button path and close animation.</summary>
    internal void Answer(string code)
    {
        var button = buttons.Find(b => b.Code == code);
        if (button is not null) Pick(button);
    }

    /// <summary>CZ-7b: a replaced or cancelled demonstration must not leave a delayed close behind.</summary>
    internal void DismissImmediately()
    {
        closing?.Cancel();
        closing = null;
        serial++;
        if (open) BeginClose();
    }

    /// <summary>Closes the card now (Esc, a click outside, the orb again, the session left Asking): the close motion, nothing stored.</summary>
    public void Close()
    {
        if (closing is not null) return; // an answer was picked: the card closes 0.22 s after it, whatever the session does meanwhile
        serial++;
        if (!open) return;
        BeginClose();
    }

    private void BeginClose()
    {
        open = false;
        IsHitTestVisible = false;
        Unwatch();
        opacityValue.To(0, LookCurve.Ease(0.18));
        progress.To(0, LookCurve.Ez(0.24));
        ReturnFocus?.Invoke();
    }

    // an answer lights first and the card closes 0.22 s later
    private void Pick(VoiceAnswerButton button)
    {
        if (!open || closing is not null) return;
        button.Pick();
        Answered?.Invoke(button.Code);
        var mine = ++serial;
        if (motion.Still)
        {
            BeginClose();
            return;
        }
        closing = MainActor.AsyncAfter(CloseDelay, () =>
        {
            if (mine != serial) return;
            closing = null;
            if (open) BeginClose();
        });
    }

    // ---- placement: above the orb's slot (below when the window has no room above), the right edge at the orb's centre + 34, kept inside the composer -----------

    private void Place()
    {
        if (popupHost is not null) { PlacePopup(); return; }
        if (slot is null || composer is null || Parent is not Visual host || !open && opacityValue.Value <= 0) return;
        if (slot.TranslatePoint(default, host) is not { } origin || TopLevel.GetTopLevel(slot) is not { } top) return;
        var slotRect = new Rect(origin, slot.Bounds.Size);
        var height = card.DesiredSize.Height > 0 ? card.DesiredSize.Height : (card.Bounds.Height > 0 ? card.Bounds.Height : 110);
        var room = slot.TranslatePoint(default, top)?.Y ?? double.PositiveInfinity;
        var below = room < height;
        var composerWidth = composer.Bounds.Width;
        var centre = slotRect.Center.X;
        var left = centre + 34 - CardWidth;
        left = composerWidth <= CardWidth ? 0 : Math.Clamp(left, 0, composerWidth - CardWidth);
        var topEdge = below ? slotRect.Bottom : slotRect.Top - height;
        if (flipped != below)
        {
            flipped = below;
            card.TipEdge = below ? PopoverTipEdge.Top : PopoverTipEdge.Bottom;
        }
        card.TipOffset = centre - left;
        Canvas.SetLeft(this, left);
        Canvas.SetTop(this, topEdge);
        shell.RenderTransformOrigin = card.TipOrigin;
    }

    private void PlacePopup()
    {
        if (slot is null || composer is null || !open && opacityValue.Value <= 0 || slot.TranslatePoint(default, composer) is not { } origin) return;
        shell.Measure(Size.Infinity);
        var height = shell.DesiredSize.Height;
        var centre = origin.X + slot.Bounds.Width / 2;
        var left = Math.Clamp(centre + 34 - CardWidth, 0, Math.Max(0, composer.Bounds.Width - CardWidth));
        var room = TopLevel.GetTopLevel(slot) is { } top ? slot.TranslatePoint(default, top)?.Y ?? double.PositiveInfinity : double.PositiveInfinity;
        flipped = room - height < TopClearance;
        card.TipEdge = flipped ? PopoverTipEdge.Top : PopoverTipEdge.Bottom;
        card.TipOffset = centre - left;
        popupHost!.HorizontalOffset = left;
        popupHost.VerticalOffset = flipped ? origin.Y + slot.Bounds.Height : origin.Y - height;
        shell.RenderTransformOrigin = card.TipOrigin;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        shell.Measure(Size.Infinity);
        return shell.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        shell.Arrange(new Rect(finalSize));
        FitMaterial();
        return finalSize;
    }

    // the glass and the rim sit on the card's body: the card less the tip's room (13) on the tip's side
    private void FitMaterial()
    {
        var below = card.TipEdge == PopoverTipEdge.Top;
        var margin = below ? new Thickness(0, 13, 0, 0) : new Thickness(0, 0, 0, 13);
        glass.Margin = margin;
        rim.Margin = margin;
    }

    private void Tint()
    {
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        card.Background = new SolidColorBrush(tokens.PgBg);
        card.BorderBrush = new SolidColorBrush(tokens.PgEdge);
        rim.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, Spread = 0.5, Color = tokens.PgRim });
        var colour = tokens.PgLabHi;
        question.Foreground = new SolidColorBrush(colour);
        footer.Foreground = new SolidColorBrush(tokens.PgLab);
    }

    private void Apply()
    {
        var t = progress.Value;
        Opacity = opacityValue.Value;
        // The stage's locked .vask[data-below] grows upward from its top tip.
        rise.Y = (popupHost is not null && flipped ? -Rise : Rise) * (1 - t);
        scale.ScaleX = scale.ScaleY = Shrink + (1 - Shrink) * t;
        shell.RenderTransformOrigin = card.TipOrigin;
        IsVisible = open || opacityValue.Value > 0 || progress.Value > 0;
    }

    // ---- keys and pointer -------------------------------------------------------------------------------------------------------------

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (!open) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Dismissed?.Invoke();
                break;
            case Key.Left or Key.Right or Key.Tab:
                var backwards = e.Key == Key.Left || e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                var at = buttons.FindIndex(button => button.IsFocused);
                var next = at < 0 ? 0 : (at + (backwards ? buttons.Count - 1 : 1)) % buttons.Count;
                buttons[next].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }

    private void Watch()
    {
        if (watched is not null || TopLevel.GetTopLevel(this) is not { } top) return;
        watched = top;
        top.AddHandler(InputElement.PointerPressedEvent, OnPress, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Unwatch()
    {
        watched?.RemoveHandler(InputElement.PointerPressedEvent, OnPress);
        watched = null;
    }

    // a press outside the card closes it with nothing started; a press on the orb is the session's own (the orb clicked again closes the question)
    private void OnPress(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!open || e.Source is not Visual source) return;
        if (this.IsVisualAncestorOf(source) || ReferenceEquals(source, this)) return;
        if (slot is not null && slot.IsVisualAncestorOf(source)) return;
        Dismissed?.Invoke();
    }
}
