// PORT-ONLY: the SwiftUI view forms the chrome controls are built from, on an Avalonia with no theme package (none is referenced, so every control the port shows carries its own template): a plain-style Button and TextField, `.background {}`, `.onHover`, `.popover(isPresented:arrowEdge:)`, `.fixedSize()`, a font on any view, and a colour that animates under the transaction (ED-071, R-VIEW-1, R-VIEW-4, R-VIEW-9).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. SwiftUI view forms with no Avalonia counterpart of the same shape.</summary>
public static class PlainControls
{
    // A native Popup needs a non-null child before IsOpen is set, but an outer popover whose body contains another
    // popover must not mount that nested popup before the outer body has opened (MCPSettingsPage.swift:632-638).
    private sealed class DeferredPopoverContent : Border
    {
        public DeferredPopoverContent() { Width = 1; Height = 1; Background = Brushes.Transparent; }
    }

    // ENGINEERING DECISION: `.buttonStyle(.plain)` draws the label and nothing else, so the template is the content
    // alone; the Avalonia Button keeps Button semantics (click on release, keyboard activation, the Invoke pattern).
    private static readonly FuncControlTemplate<Button> PlainButtonTemplate = new((button, scope) =>
        new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
        }.RegisterInNameScope(scope));

    // ENGINEERING DECISION: `.textFieldStyle(.plain)` is the text and its prompt with no bezel: a TextPresenter over the
    // prompt, which shows only while the field is empty. Rendering of the caret and selection is UNVERIFIED (OD-0001).
    private static readonly FuncControlTemplate<TextBox> PlainTextFieldTemplate = new((box, scope) =>
    {
        var prompt = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = box[!TextBox.PlaceholderTextProperty],
            [!TextBlock.ForegroundProperty] = box[!TextBox.PlaceholderForegroundProperty],
        };
        // A text input's prompt keeps the face's own line box under any .font() above the field (ED-290 (1)).
        prompt.SetValue(SwiftFont.LineBoxProperty, double.NaN);
        var presenter = new TextPresenter
        {
            Name = "PART_TextPresenter",
            VerticalAlignment = VerticalAlignment.Center,
            [!TextPresenter.TextProperty] = box[!TextBox.TextProperty],
            // A SecureField's masking is drawn by the presenter: without these the typed secret showed in the clear.
            [!TextPresenter.PasswordCharProperty] = box[!TextBox.PasswordCharProperty],
            [!TextPresenter.RevealPasswordProperty] = box[!TextBox.RevealPasswordProperty],
            // The presenter draws the selection and the caret from its own values: without these a selection was never
            // highlighted (the TextBox's changed, the presenter's stayed empty).
            [!TextPresenter.SelectionStartProperty] = box[!TextBox.SelectionStartProperty],
            [!TextPresenter.SelectionEndProperty] = box[!TextBox.SelectionEndProperty],
            [!TextPresenter.CaretIndexProperty] = box[!TextBox.CaretIndexProperty],
        }.RegisterInNameScope(scope);
        FieldBrushes(box, presenter);
        void ShowPrompt() => prompt.IsVisible = string.IsNullOrEmpty(box.Text);
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowPrompt();
        };
        ShowPrompt();
        return new Panel { Background = Brushes.Transparent, Children = { prompt, presenter } }; // A native plain TextField accepts input across its frame, including while empty (Chrome.swift:789-792).
    });

    /// <summary>
    /// <c>.textFieldStyle(.plain)</c> on a field built elsewhere (a SecureField's masking, a custom binding): the same
    /// template as <see cref="TextField"/>. The app carries no theme package, so a TextBox without it draws nothing.
    /// </summary>
    public static T PlainTextField<T>(T box) where T : TextBox
    {
        box.Template = PlainTextFieldTemplate;
        box.PlaceholderForeground = SwiftColor.PlaceholderTextColor.Brush(box);
        return box;
    }

    /// <summary>
    /// The caret and selection colours a port-templated field draws with: the field's own when it sets them, otherwise
    /// the system selection colour (NSColor.selectedTextBackgroundColor) and an accent caret (UNVERIFIED for plain
    /// fields: the composer's caret is the accent by its source, ComposerTextView :248; without a theme package a
    /// TextBox has no brushes at all, so neither was drawn).
    /// </summary>
    public static void FieldBrushes(TextBox box, TextPresenter presenter)
    {
        presenter.SelectionBrush = box.SelectionBrush ?? SwiftColor.SelectedTextBackgroundColor.Brush(presenter);
        presenter.CaretBrush = box.CaretBrush ?? Marrow.UI.Chrome.Chrome.AccentNSColor.Brush(presenter);
    }

    /// <summary>
    /// <c>Divider()</c> in a vertical stack: SwiftUI's horizontal hairline, one point high and full width, in the 0.22
    /// primary-text wash the port already uses for it (ShortcutsSettingsPage.Divider, ChromeDivider, Chrome.swift:327).
    /// Not Avalonia's Separator: the app carries no theme package, so a Separator has no template and draws nothing.
    /// </summary>
    public static Control Divider()
    {
        var bar = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        bar.Background = Marrow.UI.Chrome.Chrome.PrimaryText.Opacity(0.22).Brush(bar);
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(bar, Avalonia.Automation.AccessibilityView.Raw);
        return bar;
    }

    /// <summary><c>Button(action:) { label }.buttonStyle(.plain)</c>.</summary>
    public static Button Button(Control label, Action action)
    {
        var button = new Button { Template = PlainButtonTemplate, Content = label, Padding = default };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Restores the app's existing SystemButtons focus band on a custom plain button, without changing its resting content or layout.</summary>
    public static Button KeyboardFocus(this Button button, double? cornerRadius = null)
    {
        button.Template = new FuncControlTemplate<Button>((owner, scope) =>
        {
            var content = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope);
            var ring = new Border
            {
                Margin = new Thickness(-SystemButtons.FocusBand),
                BorderThickness = new Thickness(SystemButtons.FocusBand),
                IsVisible = false,
                IsHitTestVisible = false,
            };
            if (cornerRadius is { } radius) ring.RoundedRectangle(radius + SystemButtons.FocusBand, RoundedCornerStyle.Continuous);
            else ring.Capsule(RoundedCornerStyle.Circular);
            ring.BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(ring);
            Avalonia.Automation.AutomationProperties.SetAccessibilityView(ring, Avalonia.Automation.AccessibilityView.Raw);
            void Focus() => ring.IsVisible = owner.IsEffectivelyEnabled && owner.Classes.Contains(":focus-visible");
            owner.Classes.CollectionChanged += (_, _) => Focus();
            owner.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty) Focus(); };
            Focus();
            return new Panel { Children = { content, ring } };
        });
        return button;
    }

    /// <summary>
    /// <c>TextField(prompt, text: $text).textFieldStyle(.plain)</c>: the field reads the binding whenever what it read
    /// changes and writes it on every edit (R-VIEW-7). The prompt is drawn in the placeholder text colour.
    /// </summary>
    public static TextBox TextField(string prompt, Binding<string> text)
    {
        var box = new TextBox
        {
            Template = PlainTextFieldTemplate,
            PlaceholderText = prompt,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            Padding = default,
            BorderThickness = default,
        };
        box.PlaceholderForeground = SwiftColor.PlaceholderTextColor.Brush(box);
        box.Text = text.Value;
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var typed = box.Text ?? "";
            if (!string.Equals(text.Value, typed, StringComparison.Ordinal)) text.Value = typed;
        };
        box.Track(() => text.Value, value =>
        {
            if (!string.Equals(box.Text, value, StringComparison.Ordinal)) box.Text = value;
        });
        return box;
    }

    /// <summary><c>content.background { behind }</c>: <paramref name="behind"/> takes the content's size, under it.</summary>
    public static Panel Background(Control content, Control behind) => new() { Children = { behind, content } };

    /// <summary><c>.onHover { hovering in ... }</c>: called with true as the pointer enters and false as it leaves.</summary>
    public static T OnHover<T>(this T control, Action<bool> action) where T : Control
    {
        control.PointerEntered += (_, _) => action(true);
        control.PointerExited += (_, _) => action(false);
        return control;
    }

    /// <summary>
    /// <c>.popover(isPresented:arrowEdge:) { content }</c> anchored on <paramref name="anchor"/>: a light-dismiss
    /// popup by it (<paramref name="placement"/> default Bottom, for arrowEdge .bottom; Right for arrowEdge .trailing,
    /// App.Views.Sidebar.SidebarView.cs), presented through <see cref="Presentation.Presenter"/> whenever the binding
    /// changes. The content is built each time the popover shows and dropped when it closes, as SwiftUI builds a
    /// popover's content only while it is presented. A dismissal from outside writes false back to the binding.
    /// When <paramref name="buildBeforeOpen"/> is false, a one-pixel transparent child reserves the native popup
    /// slot until <c>Opened</c>; this is for a presented body that owns a nested popup of its own.
    /// </summary>
    public static Popup Popover(Control anchor, Binding<bool> isPresented, Func<Control> content, PlacementMode placement = PlacementMode.Bottom, bool buildBeforeOpen = true)
    {
        var popup = new Popup
        {
            Placement = placement,
            PlacementTarget = anchor,
            IsLightDismissEnabled = true,
        };
        popup.Opened += (_, _) =>
        {
            if (PopoverHost.Content(popup) is DeferredPopoverContent) popup.Child = content(); // OD-0095: the presenter may have hung it on its card
            else popup.Child ??= content();
        };
        popup.Closed += (_, _) =>
        {
            popup.Child = null;
            if (isPresented.Value) isPresented.Value = false;
        };
        var shown = false;
        popup.Track(() => isPresented.Value, presented =>
        {
            if (presented == shown) return;
            shown = presented;
            if (presented)
            {
                if (buildBeforeOpen) popup.Child ??= content(); // Build before native Popup.Open captures its child; lazy per presentation.
                else if (popup.Child is null) popup.Child = new DeferredPopoverContent();
            }
            Presentation.SetPresented(popup, presented, anchor, () => isPresented.Value = false);
        });
        return popup;
    }

    /// <summary>
    /// <c>.fixedSize(horizontal:vertical:)</c> for a view whose other modifiers do not change its size: measures the
    /// child unconstrained on each fixed axis and holds <paramref name="owner"/> to exactly that size there, so a
    /// parent can neither compress nor stretch it. Call from the owner's MeasureOverride.
    /// </summary>
    public static Size MeasureFixed(Layoutable owner, Control? child, Size available, bool horizontal = true, bool vertical = true)
    {
        if (child is null) return default;
        child.Measure(new Size(
            horizontal ? double.PositiveInfinity : available.Width,
            vertical ? double.PositiveInfinity : available.Height));
        var size = child.DesiredSize;
        if (horizontal)
        {
            owner.MinWidth = size.Width;
            owner.MaxWidth = size.Width;
        }
        if (vertical)
        {
            owner.MinHeight = size.Height;
            owner.MaxHeight = size.Height;
        }
        return size;
    }

    /// <summary><c>.font(font)</c> on any view: the inherited text properties its text and symbols draw with.</summary>
    public static T Font<T>(this T control, SwiftFont font) where T : Control
    {
        var resolved = font.Resolve();
        TextElement.SetFontFamily(control, resolved.FontFamily);
        TextElement.SetFontSize(control, resolved.FontSize);
        TextElement.SetFontWeight(control, resolved.FontWeight);
        TextElement.SetFontStyle(control, resolved.FontStyle);
        if (resolved.FontFeatures is { } features) TextElement.SetFontFeatures(control, features);
        // ED-290 (1): the text style's line box travels with the font to every TextBlock below (SwiftFont.LineBoxProperty).
        control.SetValue(SwiftFont.LineBoxProperty, control is TextBox ? double.NaN : font.LineBoxValue);
        return control;
    }

    /// <summary><c>colorScheme == .dark</c> for a resolved theme variant (a custom variant counts through the one it inherits).</summary>
    public static bool IsDark(ThemeVariant? theme)
    {
        for (var variant = theme; variant is not null; variant = variant.InheritVariant)
        {
            if (variant == ThemeVariant.Dark) return true;
        }
        return false;
    }
}

/// <summary>
/// PORT-ONLY. A colour a view draws with (a foreground style or a shape's fill) that follows its control's appearance
/// and, set inside an animated transaction, moves to the new colour along that animation (R-VIEW-4). Spans are
/// measured on SwiftClock and each step waits for a frame (ED-117).
/// </summary>
public sealed class AnimatedColor
{
    private readonly Control owner;
    private SwiftColor? color;
    private Color target;
    private IFrameSource? source;
    private Action<TimeSpan>? pending;

    public AnimatedColor(Control owner)
    {
        this.owner = owner;
        owner.ActualThemeVariantChanged += (_, _) =>
        {
            if (color is null) return;
            // ED-254 (OD-0076, SPEC 6.5): inside a tree that turns over, the new appearance's colour is eased to.
            if (AppearanceTurn.Eases(owner))
            {
                Set(color, AppearanceTurn.Curve);
                return;
            }
            // A fade still running goes on towards the colour the new appearance resolves, from where it stands (a view put
            // into another parent, a popover's list shown again, is re-evaluated without having stopped its fade).
            target = color.Resolve(owner.ActualThemeVariant);
            if (pending is not null) return;
            Brush.Color = target;
        };
    }

    /// <summary>The brush the view draws with.</summary>
    public SolidColorBrush Brush { get; } = new();

    /// <summary>Sets the colour, animated along <paramref name="animation"/> when there is one.</summary>
    public void Set(SwiftColor value, Animation? animation)
    {
        color = value;
        var to = value.Resolve(owner.ActualThemeVariant);
        target = to;
        Stop();
        if (animation is null || Brush.Color == to)
        {
            Brush.Color = to;
            return;
        }
        var from = Brush.Color;
        var start = SwiftClock.Now;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var complete = FrameClock.IsComplete(start, now, span);
            Brush.Color = complete ? target : Mix(from, target, animation.Progress((now - start).TotalSeconds));
            if (complete) pending = null;
            else frames.RequestFrame(tick!);
        };
        source = frames;
        pending = tick;
        frames.RequestFrame(tick);
    }

    private void Stop()
    {
        if (pending is { } frame) source?.CancelFrame(frame);
        pending = null;
    }

    private static Color Mix(Color from, Color to, double progress)
    {
        static byte Channel(byte a, byte b, double p) => (byte)Math.Clamp(Math.Round(a + (b - a) * p, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(
            Channel(from.A, to.A, progress),
            Channel(from.R, to.R, progress),
            Channel(from.G, to.G, progress),
            Channel(from.B, to.B, progress));
    }
}
