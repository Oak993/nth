using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/Popovers.swift (B2 1fbf81e), :1-401.
// Records: PR-SIDEBAR-104, PR-SIDEBAR-105, PR-SIDEBAR-106, PR-THEME-041, UI-SIDEBAR-011, UI-SIDEBAR-012, UI-SIDEBAR-013.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; NSPopover is an Avalonia Popup,
// NSSize is Avalonia.Size, NSImage is Avalonia.Media.IImage, AnyView is Control, a styled Text is StyledText
// (ED-092), a @ViewBuilder parameter is a Func<Control>, a `@MainActor () -> Void` is an Action. A parameter without
// a default that follows one with a default moves before it (ED-095). The static class Popovers keeps the Swift
// file's name (R-NAME-4); the port's presenter seam is Marrow.UI.Port.Presentation (ED-095).
//
// ENGINEERING DECISION: Swift's `@Environment(\.dismiss)` (:208, read by PopoverItem at :266, :271) has no port
// counterpart: SwiftUI auto-injects a dismiss action for anything shown through `.popover`/`.sheet`, but this tree
// has no such automatic per-presentation environment value yet. Every popover this file itself presents
// (RowActionsButton :389-400) therefore sets `closePopoverBox` (:179) on its content explicitly, so
// `PopoverItem`'s `closePopoverBox?.close() ?? dismiss()` (:266, :271) always resolves the box and the `dismiss()`
// arm is unreachable from this file; it is kept in the read (a no-op close) only for a future presenter that opens
// a PopoverItem without setting the box.

/// <summary>The file's extensions of library types (R-NAME-4): <c>extension View</c> (:4-13),
/// <c>extension NSPopover</c> (:15-41) and <c>extension EnvironmentValues</c> (:176-187).</summary>
public static class Popovers
{
    /// <summary>:10 <c>.buttonBorderShape(.capsule)</c>, an inherited SwiftUI environment key restated on every
    /// presented tree; no button template in this tree yet reads it, so it is recorded verbatim as an inherited
    /// attached property (R-VIEW-6) for a later unit to consume.</summary>
    public static readonly AttachedProperty<bool> IsCapsuleButtonBorderShapeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsCapsuleButtonBorderShape", typeof(Popovers), inherits: true);

    /// <summary>:11 <c>.tint(ThemeManager.spec.accent)</c> (PR-THEME-041): the raw optional, not <c>Chrome.accent</c>,
    /// so System/Light/Dark themes (whose <see cref="ThemeSpec.Accent"/> is null) tint with nothing, an upstream
    /// inconsistency reproduced as UPSTREAM DEFECT 66.</summary>
    public static readonly AttachedProperty<SwiftColor?> TintProperty =
        AvaloniaProperty.RegisterAttached<Control, SwiftColor?>("Tint", typeof(Popovers), inherits: true);

    public static bool GetIsCapsuleButtonBorderShape(Control control) => control.GetValue(IsCapsuleButtonBorderShapeProperty);

    public static void SetIsCapsuleButtonBorderShape(Control control, bool value) => control.SetValue(IsCapsuleButtonBorderShapeProperty, value);

    public static SwiftColor? GetTint(Control control) => control.GetValue(TintProperty);

    public static void SetTint(Control control, SwiftColor? value) => control.SetValue(TintProperty, value);

    // :8-12 func presentedChrome()
    public static Control PresentedChrome(this Control view)
    {
        view.SetValue(IsCapsuleButtonBorderShapeProperty, true); // :10
        view.SetValue(TintProperty, ThemeManager.Spec.Accent); // :11 UPSTREAM DEFECT 66, reproduced
        return view;
    }

    // :28-40 @MainActor func setFixedContent(_:size:) on NSPopover
    public static void SetFixedContent(this Popup popover, Control content, Size size)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch; // :33 .frame(width:height:alignment: .top)
        content.VerticalAlignment = VerticalAlignment.Top;
        var frame = new Border
        {
            Width = size.Width,
            Height = size.Height,
            ClipToBounds = true, // :34 .clipped()
            Child = content,
        };
        popover.Child = frame.PresentedChrome(); // :35
    }

    // :179 @Entry var closePopoverBox: PopoverCloseBox?, an inherited environment value (R-VIEW-6, ED-095).
    public static readonly AttachedProperty<PopoverCloseBox?> ClosePopoverBoxProperty =
        AvaloniaProperty.RegisterAttached<Control, PopoverCloseBox?>("ClosePopoverBox", typeof(Popovers), null, inherits: true);

    public static PopoverCloseBox? GetClosePopoverBox(Control control) => control.GetValue(ClosePopoverBoxProperty);

    public static void SetClosePopoverBox(Control control, PopoverCloseBox? value) => control.SetValue(ClosePopoverBoxProperty, value);

    // :183-184 var closePopover: (@MainActor () -> Void)? { get }
    public static Action? GetClosePopover(Control control) => GetClosePopoverBox(control)?.Close;

    // :185 var closePopover { set }
    public static void SetClosePopover(Control control, Action? value) =>
        SetClosePopoverBox(control, value is null ? null : new PopoverCloseBox(value));
}

// :43-92 struct PopoverScroll<Content: View>: View
public sealed class PopoverScroll : Decorator
{
    private readonly bool isPremeasured; // :64
    private double contentHeight; // :59 @State private var contentHeight: CGFloat
    private readonly ScrollViewer scroll;

    /// <summary>:66-73 <c>@MainActor init(maxHeight:minHeight:width:content:)</c>; content has no default, so it
    /// moves first (ED-095) and callers name the labels.</summary>
    // :82-91 body
    public PopoverScroll(Func<Control> content, double maxHeight = 460, double minHeight = 80, double? width = null)
    {
        isPremeasured = width is not null; // :71
        var body = content();
        contentHeight = width is { } known ? MeasuredHeight(body, known) : 0; // :72
        scroll = new ScrollViewer
        {
            Content = body,
            Height = Math.Min(Math.Max(contentHeight, minHeight), maxHeight), // :90
        };
        scroll.Fades(); // PORT-ONLY (OD-0151): rows dissolve at the edge they scroll past (ScrollEdgeFade)
        if (!isPremeasured)
        {
            // The content keeps its own height (a ScrollView offers it unbounded height): stretched to the viewport it
            // would report the viewport back and the scroll could grow but never shrink.
            body.VerticalAlignment = VerticalAlignment.Top;
            body.LayoutUpdated += (_, _) => // :85-87 onGeometryChange(for: height)
            {
                var height = body.Bounds.Height;
                if (height == contentHeight) return;
                contentHeight = height;
                scroll.Height = Math.Min(Math.Max(contentHeight, minHeight), maxHeight); // :90
            };
        }
        Child = scroll;
    }

    /// <summary>:77-80 <c>@MainActor static func measuredHeight(of:width:)</c>: the height <paramref name="of"/>
    /// takes at <paramref name="width"/>, one unconstrained layout off screen.</summary>
    public static double MeasuredHeight(Control of, double width)
    {
        // :79 NSHostingView(rootView: of.frame(width:)).fittingSize: a hosted view, so measured in a window, where its
        // styles and templates are in effect (detached, a code block's copy button and a table's scroll viewer measured short).
        return Math.Ceiling(StyledMeasure.Of(of, new Size(width, double.PositiveInfinity)).Height); // :79 .rounded(.up)
    }
}

// :94-115 struct PopoverMenu<Content: View>: View
public sealed class PopoverMenu : Decorator
{
    /// <param name="content">:100 <c>@ViewBuilder var content: Content</c>.</param>
    /// <param name="maxHeight">:96 <c>var maxHeight: CGFloat = 460</c>.</param>
    /// <param name="idealHeight">:99 <c>var idealHeight: CGFloat?</c>.</param>
    // :102-114 body
    public PopoverMenu(Func<Control> content, double maxHeight = 460, double? idealHeight = null)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left }; // :104
        stack.Children.Add(content());
        var padded = new Border
        {
            Padding = new Thickness(6), // :107
            Child = stack,
            MinWidth = 210, // :108
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var scroll = new ScrollViewer
        {
            Content = padded,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, // :111 .scrollIndicators(.automatic)
            MaxHeight = maxHeight, // :112
        };
        if (idealHeight is { } ideal) scroll.Height = ideal; // :112 idealHeight
        scroll.Fades(); // PORT-ONLY (OD-0151): rows dissolve at the edge they scroll past (ScrollEdgeFade)
        Child = scroll.PresentedChrome(); // :113
    }
}

// :117-133 struct PopoverSectionHeader: View
public sealed class PopoverSectionHeader : Decorator
{
    // :120-122 init(_ title: String); :124-132 body
    public PopoverSectionHeader(string title)
    {
        var text = new TextBlock { Text = title }; // :125 Text(verbatim: title)
        SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold).Apply(text); // :126
        text.Foreground = Chrome.SecondaryText.Brush(text); // :127
        text.Margin = new Thickness(8, 6, 8, 3); // :128-130
        text.HorizontalAlignment = HorizontalAlignment.Left; // :131 frame(maxWidth: .infinity, alignment: .leading)
        Child = text;
    }
}

// :135-150 struct PopoverNote: View
public sealed class PopoverNote : Decorator
{
    // :138-140 init(_ text: String); :142-149 body
    public PopoverNote(string text)
    {
        // :143 Text(verbatim: text) has no line limit, so a long note wraps inside the menu's width (UI-COMPOSER-021's load error;
        // 2-chat-use.mov 152.6 s).
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.Callout.Apply(block); // :144
        block.Foreground = Chrome.SecondaryText.Brush(block); // :145
        block.Margin = new Thickness(8, 5); // :146-147
        block.HorizontalAlignment = HorizontalAlignment.Left; // :148
        Child = block;
    }
}

// :152-158 struct PopoverDivider: View
public sealed class PopoverDivider : Decorator
{
    // :153-157 body. ENGINEERING DECISION: plain SwiftUI `Divider()` draws the system separator colour, which has no
    // port counterpart; reused here is the same hairline colour ChromeDivider (Chrome.swift:324-331) already
    // establishes for this tree, Chrome.primaryText at 0.22.
    public PopoverDivider()
    {
        var bar = new Border { Height = 1, Margin = new Thickness(8, 5) }; // :155-156
        bar.Background = Chrome.PrimaryText.Opacity(0.22).Brush(bar);
        Child = bar;
    }
}

/// <summary>:160-174 <c>final class PopoverCloseBox: Equatable</c>. Swift's <c>==</c> is identity (<c>===</c>,
/// :171-173), which is a C# class's default equality.</summary>
public sealed class PopoverCloseBox
{
    // :167-169 init(_ close:)
    public PopoverCloseBox(Action close)
    {
        Close = close;
    }

    // :165 let close: @MainActor () -> Void
    public Action Close { get; }
}

/// <summary>
/// :189-337 <c>struct PopoverItem: View</c>. One row. Pass <paramref name="isChecked"/> (true or false) for choice
/// lists so every row keeps the checkmark column.
/// </summary>
public sealed class PopoverItem : Decorator
{
    // :214 private static let confirmationHold: Duration = .milliseconds(650)
    private static TimeSpan ConfirmationHold => TimeSpan.FromMilliseconds(650);

    // :210-211 @State private var isHovering = false, isConfirmed = false
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;
        private bool isConfirmed;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

        public bool IsConfirmed { get => Read(isConfirmed); set => Set(ref isConfirmed, value); }
    }

    private readonly ViewState state = new();

    /// <summary>The confirmation acknowledgement the row is currently showing, for tests: null until the row is
    /// clicked with a <c>confirmation</c>.</summary>
    public bool IsConfirmed => state.IsConfirmed;

    /// <summary>:216-240 <c>init(_:detail:symbol:image:asset:leading:isChecked:isEnabled:isDestructive:confirmation:action:)</c>;
    /// action has no default, so it moves after the title (ED-095) and callers name the labels.</summary>
    // :257-336 body
    public PopoverItem(
        string title,
        Action action,
        string? detail = null,
        string? symbol = null,
        IImage? image = null,
        string? asset = null,
        Control? leading = null,
        bool? isChecked = null,
        bool isEnabled = true,
        bool isDestructive = false,
        string? confirmation = null)
        : this(title, null, action, detail, symbol, image, asset, leading, isChecked, isEnabled, isDestructive, confirmation)
    {
    }

    /// <summary>:242-255 <c>init(_:text:symbol:image:action:)</c>, a row whose title is styled text (<c>titleText</c>,
    /// :193); action has no default, so it moves before the defaulted ones (ED-095).</summary>
    public PopoverItem(string title, StyledText text, Action action, string? symbol = null, IImage? image = null)
        : this(title, text, action, null, symbol, image, null, null, null, true, false, null)
    {
    }

    private PopoverItem(
        string title,
        StyledText? titleText,
        Action action,
        string? detail,
        string? symbol,
        IImage? image,
        string? asset,
        Control? leading,
        bool? isChecked,
        bool isEnabled,
        bool isDestructive,
        string? confirmation)
    {
        var row = new PopoverRowPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :275 HStack(spacing: 8)

        if (isChecked is { } isCheckedValue) // :276-281
        {
            var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold));
            check.Opacity = isCheckedValue ? 1 : 0; // :279
            check.Width = 14; // :280
            row.Children.Add(check);
        }

        var leadingSlot = new Panel { Width = 16, Height = 16 }; // :284, :289, :294, :299, :304 the leading 16pt column
        Control? shown = null; // the view the slot currently holds
        var shownTransitional = false; // whether it leaves under its .transition (:300, :305)
        bool? builtConfirmed = null;
        void UpdateLeading()
        {
            if (builtConfirmed == state.IsConfirmed) return;
            builtConfirmed = state.IsConfirmed;
            Control? next = null;
            var transitional = false;
            if (leading is not null) // :282-284
            {
                leading.Width = 16;
                leading.Height = 16;
                next = leading;
            }
            else if (asset is not null) // :285-289
            {
                var assetImage = SwiftImage.Named(asset).Resizable().AspectRatio(ContentMode.Fit);
                assetImage.Width = 16;
                assetImage.Height = 16;
                next = assetImage;
            }
            else if (image is not null) // :290-294
            {
                var imageControl = new Image { Source = image, Width = 16, Height = 16, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapInterpolationMode(imageControl, BitmapInterpolationMode.HighQuality);
                next = imageControl;
            }
            else if (state.IsConfirmed) // :295-300
            {
                var confirmed = SwiftImage.SystemName("checkmark").Font(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold))
                    .ForegroundStyle(Chrome.Success);
                confirmed.Width = 16;
                next = confirmed;
                transitional = true;
            }
            else if (symbol is not null) // :301-305
            {
                var glyph = SwiftImage.SystemName(symbol).Font(SwiftFont.Callout);
                glyph.Width = 16;
                next = glyph;
                transitional = true;
            }

            // :300, :305 .transition(.scale(scale: 0.6).combined(with: .opacity)) on the checkmark and the symbol; the
            // swap runs under the snappy transaction of :263.
            if (shown is not null)
            {
                if (shownTransitional) SwiftTransition.Remove(leadingSlot, shown);
                else leadingSlot.Children.Remove(shown);
            }
            if (next is not null)
            {
                if (transitional) SwiftTransition.Insert(leadingSlot, leadingSlot.Children.Count, next.Transition(SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity)));
                else leadingSlot.Children.Add(next);
            }
            shown = next;
            shownTransitional = transitional;

            // :282-305 the leading slot exists only when one of the branches produced a view; a plain row has no
            // slot, so its title follows the row padding (and the checkmark column, if any) directly.
            var present = row.Children.Contains(leadingSlot);
            if (next is not null && !present) row.Children.Insert(isChecked is null ? 0 : 1, leadingSlot);
            else if (next is null && present) row.Children.Remove(leadingSlot);
        }
        UpdateLeading();
        row.Track(() => state.IsConfirmed, _ => UpdateLeading()); // on the row: the slot itself is detached while empty

        var titleBlock = new MiddleTruncatingText(); // :307-311 .lineLimit(1).truncationMode(.middle)
        SwiftFont.Body.Apply(titleBlock); // :308
        void UpdateTitle()
        {
            if (state.IsConfirmed || titleText is null)
            {
                titleBlock.FullText = state.IsConfirmed ? confirmation ?? title : title; // :307
            }
            else
            {
                titleBlock.StyledFullText = titleText; // :307 the styled Text, middle-truncated like a plain one
            }
        }
        UpdateTitle();
        titleBlock.Track(() => state.IsConfirmed, _ => UpdateTitle());
        row.Children.Add(titleBlock);

        row.Spacer = new Control { MinWidth = 16, HorizontalAlignment = HorizontalAlignment.Stretch }; // :312 Spacer(minLength: 16), takes the row's spare width (PopoverRowPanel)
        row.Children.Add(row.Spacer);

        if (detail is not null) // :313-318
        {
            var detailBlock = new TextBlock { Text = detail, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
            SwiftFont.Callout.Apply(detailBlock); // :315
            detailBlock.Foreground = Chrome.SecondaryText.Brush(detailBlock); // :316
            row.Children.Add(detailBlock);
        }

        row.HorizontalAlignment = HorizontalAlignment.Stretch;
        var foreground = new AnimatedColor(row);
        row.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, foreground.Brush); // :320
        foreground.Set(isDestructive ? Chrome.Danger : Chrome.PrimaryText, null);
        row.Margin = new Thickness(8, 0); // :321 .padding(.horizontal, 8)
        row.Height = 26; // :322 .frame(height: 26)

        var fill = new AnimatedColor(row);
        var background = new Border
        {
            Child = row,
            Background = fill.Brush, // :323-326
        }.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        fill.Set(SwiftColor.Clear, null);
        background.Background = fill.Brush;

        var button = PlainControls.Button(background, () => Activate(action, confirmation));
        button.IsEnabled = isEnabled; // :330 .disabled(!isEnabled)
        button.Opacity = isEnabled ? 1 : 0.4; // :331
        AutomationProperties.SetName(button, detail is not null ? $"{title}, {detail}" : title); // :332
        button.PointerEntered += (_, _) => SetHovering(true, isEnabled, fill);
        button.PointerExited += (_, _) => SetHovering(false, isEnabled, fill);

        Child = button;
    }

    private void SetHovering(bool hovering, bool isEnabled, AnimatedColor fill)
    {
        Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering); // :334-335
        fill.Set(hovering && isEnabled ? Chrome.Overlay(0.1) : SwiftColor.Clear, Chrome.Hover); // :325
    }

    // :258-273 Button { ... }
    private void Activate(Action action, string? confirmation)
    {
        if (confirmation is not null && !state.IsConfirmed) // :259
        {
            action(); // :262
            Transaction.WithAnimation(Animation.Snappy(duration: 0.22), () => state.IsConfirmed = true); // :263
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(ConfirmationHold); // :265
                var close = Popovers.GetClosePopover(this);
                close?.Invoke(); // :266
            });
            return; // :268
        }
        if (state.IsConfirmed) return; // :270
        var closeNow = Popovers.GetClosePopover(this);
        closeNow?.Invoke(); // :271
        MainActor.Spawn(() => // :273 Task { @MainActor in action() }
        {
            action();
            return Task.CompletedTask;
        });
    }
}

/// <summary>
/// PORT-ONLY: the port's stand-in for the row's <c>HStack(spacing: 8)</c> with a <c>Spacer(minLength: 16)</c>
/// (Popovers.swift :275, :312). Children take their natural width; the <see cref="Spacer"/> takes 16 plus whatever
/// width the row is given beyond that, so the detail sits at the trailing edge; when the row is narrower than its
/// content the <see cref="MiddleTruncatingText"/> title is given what is left, as SwiftUI compresses a truncating
/// Text before anything else (:307-311). Still a horizontal StackPanel to every caller; an <see cref="HStack"/>, so each
/// child sits on the row's centre line as :275's default alignment puts it (a stretched title drew at the row's top).
/// </summary>
internal sealed class PopoverRowPanel : HStack
{
    /// <summary>:312 the <c>Spacer(minLength: 16)</c> child.</summary>
    public Control? Spacer { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        var title = visible.OfType<MiddleTruncatingText>().FirstOrDefault();
        double total = 0, height = 0;
        foreach (var child in visible.Where(c => c != title))
        {
            child.Measure(infinite);
            total += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (title is not null)
        {
            var others = total + Spacing * (visible.Count - 1);
            var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - others);
            title.Measure(new Size(room, availableSize.Height));
            total += title.DesiredSize.Width;
            height = Math.Max(height, title.DesiredSize.Height);
        }
        return new Size(total + Spacing * Math.Max(0, visible.Count - 1), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        var spacer = Spacer;
        var natural = visible.Sum(c => c.DesiredSize.Width) + Spacing * Math.Max(0, visible.Count - 1);
        var extra = Math.Max(0, finalSize.Width - natural);
        double x = 0;
        foreach (var child in visible)
        {
            var width = child.DesiredSize.Width + (child == spacer ? extra : 0);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}

/// <summary>
/// PORT-ONLY (R-FILE-2 exception noted at header): the port's stand-in for `.lineLimit(1).truncationMode(.middle)`
/// (PR-SIDEBAR-106, UI-SIDEBAR-013). Avalonia's TextBlock has no middle-truncation mode, so this measures the full
/// text and, when it does not fit, replaces the centre with U+2026 HORIZONTAL ELLIPSIS, widening from an empty
/// centre until the candidate no longer fits. Exact truncation points are UNVERIFIED (OD-0001: no macOS reference
/// to compare against).
/// </summary>
// Popovers.swift:307-311: the title is a Text with .contentTransition(.numericText()); a change made inside an animation
// (the confirmation swap under the snappy transaction of :263) morphs (MorphingTextBlock).
public sealed class MiddleTruncatingText : MorphingTextBlock
{
    private const char Ellipsis = '…'; // U+2026 HORIZONTAL ELLIPSIS
    private string full = "";
    private StyledText? styled;

    public string FullText
    {
        get => full;
        set
        {
            if (Transaction.Current is { } animation && styled is null && value != full && !string.IsNullOrEmpty(Text))
                BeginMorph(animation); // :311 from the title shown now to the one measuring presents next
            full = value;
            styled = null;
            InvalidateMeasure();
        }
    }

    /// <summary>:242-255, :307 a styled title (<c>titleText</c>): it passes through the same middle truncation as a
    /// plain one, the cut taken from the runs' combined text and the ellipsis styled like the run it follows.</summary>
    public StyledText? StyledFullText
    {
        get => styled;
        set
        {
            styled = value;
            InvalidateMeasure();
        }
    }

    private string PlainFull => styled is null ? full : string.Concat(styled.Runs.Select(run => run.Text));

    private void Present(string text, StyledText? runs)
    {
        if (runs is null)
        {
            Inlines?.Clear();
            Text = text;
        }
        else
        {
            Text = null;
            runs.Render(this);
            OnMeasureInvalidated(); // an inline change made while measuring keeps the cached text layout; its width is read straight after
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Present(full, styled);
        if (PlainFull.Length == 0 || double.IsInfinity(availableSize.Width))
            return Glide(MeasureText(availableSize));
        var natural = MeasureText(availableSize);
        if (natural.Width <= availableSize.Width)
            return Glide(natural);
        Truncate(availableSize.Width);
        return Glide(MeasureText(availableSize));
    }

    /// <summary>Leaves the widest candidate that fits (an ellipsis alone if none does) presented.</summary>
    private void Truncate(double maxWidth)
    {
        var text = PlainFull;
        var lo = 0;
        var hi = System.Globalization.StringInfo.ParseCombiningCharacters(text).Length;
        var best = 0;
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            PresentCandidate(text, mid);
            var width = MeasureText(unconstrained).Width;
            if (width <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        PresentCandidate(text, best);
    }

    /// <summary>The first kept/2 characters, U+2026, then the last kept - kept/2 characters of <paramref name="text"/>.</summary>
    public static string Candidate(string text, int kept)
    {
        var (headEnd, tailStart) = Cut(text, kept);
        return text.Substring(0, headEnd) + Ellipsis + text.Substring(tailStart);
    }

    // UTF-16 offsets where the head ends and the tail starts for `kept` kept characters. A Swift Character is an
    // extended grapheme cluster (:307-311 Text truncates by Character), so the cut falls only between clusters.
    private static (int HeadEnd, int TailStart) Cut(string text, int kept)
    {
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var head = kept / 2;
        var tail = kept - head;
        int At(int cluster) => cluster >= starts.Length ? text.Length : starts[cluster];
        return (At(head), At(starts.Length - tail));
    }

    private void PresentCandidate(string text, int kept)
    {
        if (styled is null)
        {
            Present(Candidate(text, kept), null);
            return;
        }
        var (headEnd, tailStart) = Cut(text, kept);
        var runs = new List<StyledText.Run>();
        var offset = 0;
        var ellipsisAdded = false;
        StyledText.Run? style = null;
        foreach (var run in styled.Runs)
        {
            var start = offset;
            var end = offset + run.Text.Length;
            offset = end;
            if (run.Image is not null)
            {
                if (start < headEnd || start >= tailStart)
                {
                    if (start >= tailStart && !ellipsisAdded) { runs.Add(style is null ? EllipsisRun(run) : EllipsisRun(style)); ellipsisAdded = true; }
                    runs.Add(run);
                }
                continue;
            }
            var headPart = Math.Max(0, Math.Min(end, headEnd) - start);
            if (headPart > 0)
            {
                runs.Add(run with { Text = run.Text.Substring(0, headPart) });
                style = run;
            }
            var tailFrom = Math.Max(start, tailStart);
            if (tailFrom < end)
            {
                if (!ellipsisAdded) { runs.Add(EllipsisRun(style ?? run)); ellipsisAdded = true; }
                runs.Add(run with { Text = run.Text.Substring(tailFrom - start) });
            }
        }
        if (!ellipsisAdded) runs.Add(EllipsisRun(style ?? styled.Runs[0]));
        Present("", StyledText.FromRuns(runs));
    }

    private static StyledText.Run EllipsisRun(StyledText.Run like) => like with { Text = Ellipsis.ToString(), Image = null, Link = null };
}

// MARK: - Row actions

/// <summary>:341-352 <c>struct RowAction: Identifiable</c> (R-VAL-1). The memberwise init's labels are the
/// property names.</summary>
public sealed record RowAction
{
    public required string Title { get; init; } // :343
    public string? Symbol { get; init; } // :344
    public bool IsDestructive { get; init; } = false; // :345
    public bool StartsGroup { get; init; } = false; // :346
    public string? Confirms { get; init; } // :348
    public required Action Action { get; init; } // :349

    // :351 var id: String
    public string Id => Title;
}

// :354-363 struct RowActionItems: View
public sealed class RowActionItems : Decorator
{
    /// <param name="actions">:355 <c>let actions: [RowAction]</c>.</param>
    // :357-362 body
    public RowActionItems(IReadOnlyList<RowAction> actions)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in actions)
        {
            if (item.StartsGroup) stack.Children.Add(new PopoverDivider()); // :359
            stack.Children.Add(new PopoverItem(item.Title, item.Action, symbol: item.Symbol, isDestructive: item.IsDestructive, confirmation: item.Confirms)); // :360
        }
        Child = stack;
    }
}

// :365-382 struct RowActionMenuButtons: View
//
// ENGINEERING DECISION: this view is `Menu { RowActionMenuButtons(actions:) }`'s content at every call site outside
// this file, hosted by AppKit as an NSMenu; a Decorator (one Child) cannot hold that native menu itself, so this
// exposes the built MenuItem/Separator controls for a caller to install on an Avalonia ContextMenu.Items (its own
// native menu host), the direct counterpart of an NSMenu.
public sealed class RowActionMenuButtons : Decorator
{
    /// <param name="actions">:366 <c>let actions: [RowAction]</c>.</param>
    /// <summary>The rows this produces, in order, for a caller's <c>ContextMenu.Items</c>.</summary>
    public IReadOnlyList<Control> Items { get; }

    // :368-381 body
    public RowActionMenuButtons(IReadOnlyList<RowAction> actions)
    {
        var items = new List<Control>();
        foreach (var item in actions)
        {
            if (item.StartsGroup) items.Add(new Separator()); // :370 Divider()
            var menuItem = new MenuItem
            {
                Header = item.Symbol is { } symbol
                    ? new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { SwiftImage.SystemName(symbol).Font(Chrome.IconFont), new TextBlock { Text = item.Title } },
                    } // :374-375 Label(item.title, systemImage: symbol)
                    : new TextBlock { Text = item.Title }, // :376-377 Text(item.title)
            };
            if (item.IsDestructive) menuItem.Foreground = Chrome.Danger.Brush(menuItem); // :371 Button(role: .destructive)
            menuItem.Click += (_, _) => item.Action(); // :372 item.action(), synchronous inside the AppKit menu callback
            items.Add(menuItem);
        }
        Items = items;
    }
}

// :384-401 struct RowActionsButton: View
public sealed class RowActionsButton : Decorator
{
    /// <param name="actions">:386 <c>let actions: [RowAction]</c>.</param>
    /// <param name="isPresented">:387 <c>@Binding var isPresented: Bool</c>.</param>
    // :389-400 body
    public RowActionsButton(IReadOnlyList<RowAction> actions, Binding<bool> isPresented)
        : this(() => actions, isPresented)
    {
    }

    /// <summary>The same button for a row that keeps it while its own record changes under it: SwiftUI hands the kept
    /// button the actions its owner's body builds now (:386), so the row passes how to build them and the menu reads
    /// them as it opens. A menu already open keeps the items it opened with.</summary>
    public RowActionsButton(Func<IReadOnlyList<RowAction>> actions, Binding<bool> isPresented)
    {
        var icon = new RowAccessoryIcon("ellipsis"); // :393
        var button = PlainControls.Button(icon, () => isPresented.Value = !isPresented.Value); // :390-392, :395 .buttonStyle(.plain)
        ToolTip.SetTip(button, "More"); // :396 .help("More")
        var popover = PlainControls.Popover(button, isPresented, () => // :397-399, arrowEdge .bottom is Popover's default placement
        {
            var menu = new PopoverMenu(() => new RowActionItems(actions()));
            // See the file header ENGINEERING DECISION: supplies dismiss()'s port counterpart.
            Popovers.SetClosePopover(menu, () => isPresented.Value = false);
            return menu;
        });
        Child = new Panel { Children = { button, popover } };
    }
}
