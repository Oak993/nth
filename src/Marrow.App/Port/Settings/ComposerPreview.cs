// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3, CZ-4.6, CZ-4.8; OWNER OD-0085, OD-0089): the Customization page's preview composer. A runtime-free composer assembled from the app
// composer's own visual parts, so it never starts a thread, a provider or a microphone and looks as a real ComposerArea of the same width does (CZ-T45):
//   - the glass plate (ComposerSurface, the pill's shape and material) and the working dots (ComposerWorkingDots under the plate's clip);
//   - the placeholder, drawn by the composer's own text view (ComposerNSTextView), read-only and out of the focus chain;
//   - the attachment button at the plate's left and the trailing model chip, context ring and send button, the real controls' sizes and spacing (VT-10, OD-0091);
//   - the composer light (ComposerLightView) behind the plate, given the settings, the phase, the maker's palette, the plate's rectangle and the stage's bottom edge.
// It is driven by a PreviewSession: Rest, Typing (the playground's string typed one character every 0.055 s) and Running (the stop square, the dots, the spinning ring).
// ENGINEERING DECISION: its height is the real composer's (46, frame-measured in PR-COMPOSER-090) and not the playground's mock 52; CZ-4.3 asks for a composer that looks exactly
// like a real one of the same width, and CZ-3.1 gives the port's component the last word on its own metrics.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed partial class ComposerPreview : Grid
{
    /// <summary>CZ-4.3: the app's placeholder for a Claude thread with Hydra on (ComposerView.swift:285), the playground's string.</summary>
    internal const string Placeholder = "Ask Claude for anything; big jobs go out to a team of heads";

    /// <summary>CZ-4.3 (playground logic.js:37): what the Typing state types, one character every <see cref="TypingPace"/> seconds, from empty.</summary>
    internal const string TypedString = "Add a keyboard shortcut for the theme switch";
    internal const double TypingPace = 0.055;

    /// <summary>CZ-4.3: the frame, the attachment inside the composer (CZ-7b adds the other place): 472 wide, 12 from the stage's left and bottom.</summary>
    internal const double ComposerWidth = 472, ComposerLeft = 12, ComposerBottom = 12;

    /// <summary>The plate's height once laid out: the real composer's 46 (ED-335 item 2, CZ-T45). The page shows the Auto values of a composer of this size (CZ-6) before the preview has been measured.</summary>
    internal const double PillHeight = 46;

    private const double CaretPeriod = 1.06; // playground .caret: blink 1.06s ease-in-out infinite

    private readonly PreviewSession session;
    private readonly CzNumber hintOpacity, typedOpacity;
    private readonly PreviewWorkingDots dots = new();
    private readonly PreviewSendButton send = new();
    private readonly Action<TimeSpan> typingFrame;
    private ComposerLightSettings settings = ComposerLightSettings.Default;
    private WorkingDotsSettings workingDots = WorkingDotsSettings.Default;
    private bool typing;
    private int typedCount;
    private ContinuousInstant typingStart;
    private IFrameSource? frames;
    private bool waiting;
    private readonly Grid textCell, layers;
    private readonly Border text, padded;

    internal ComposerLightView Light { get; } = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly ShaderLightGate gate;
    private int keystrokes;
    internal Border Pill { get; }
    internal ComposerNSTextView Hint { get; }
    internal TextBlock Typed { get; }
    internal Border Caret { get; }
    internal StackPanel Controls { get; }
    internal Button Attach { get; }
    internal PreviewComposerChip Chip { get; }
    internal PreviewRing Ring { get; } = new();

    /// <summary>The send button's disc (28 across).</summary>
    internal Button Send => send.Disc;
    internal string SendSymbol => send.Symbol;
    internal bool DotsShown => dots.Shown;
    internal double DotsOpacity => dots.Opacity01;
    internal double RingAngle => Ring.Angle;
    internal double HintOpacity => Hint.Opacity;
    internal string TypedText => Typed.Text ?? "";

    /// <summary>The settings the light wears: the app's (CZ-13.1), handed in by the page.</summary>
    internal ComposerLightSettings Settings
    {
        get => settings;
        set { settings = value; gate.Apply(value); } // OD-0139: a shader light shows once warm (ED-NEW-lightv2-3)
    }

    /// <summary>CZ-5 (CZ-10.4): the working dots' setting, the app's, handed in by the page; the dots wear its colour for the preview maker.</summary>
    internal WorkingDotsSettings WorkingDots
    {
        get => workingDots;
        set { workingDots = value; UpdateDots(); }
    }

    private void UpdateDots() => dots.Tint = WorkingDotsColor.Of(workingDots, session.Maker);

    internal ComposerPreview(PreviewSession session)
    {
        this.session = session;
        typingFrame = TypingFrame;
        gate = new ShaderLightGate(Light);
        var motion = new CzMotion(this);

        // the text: the composer's own text view draws the placeholder; the typed string is a layer over it that the placeholder crosses with
        Hint = new ComposerNSTextView { Placeholder = Placeholder, IsReadOnly = true, Focusable = false, IsHitTestVisible = false };
        Avalonia.Input.DragDrop.SetAllowDrop(Hint, false);
        Hint.Height = LineHeight(Hint);
        Typed = new TextBlock { Padding = new Thickness(0, 2, 0, 2), Opacity = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Text = "" };
        SwiftFont.SystemFont(14).Apply(Typed);
        Typed.Foreground = SwiftColor.LabelColor.Brush(Typed);
        Caret = new Border { Width = 1.5, Height = 17, IsVisible = false, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Caret.Background = SwiftColor.LabelColor.Brush(Caret);
        // OD-0132 / ED-NEW-T46-centre: the real column (ComposerTextView.PlaceInk) draws the line from its glyph outlines' centre so the ink sits on the composer's
        // vertical centre; the preview's placeholder, the typed words over it and the caret take the same shift, so they cross on one line and match the real one.
        var inkPlace = new TranslateTransform();
        Hint.RenderTransform = Typed.RenderTransform = Caret.RenderTransform = inkPlace;
        void PlaceInk()
        {
            var inkBounds = Hint.Bounds.Height > 0 && Hint.Placeholder.Length > 0 ? Hint.CreatePlaceholderText().BuildGeometry(new Point())?.Bounds : null;
            inkPlace.Y = inkBounds is { Height: > 0 } ink ? Hint.Bounds.Height / 2 - ink.Center.Y - Hint.Padding.Top : 0;
        }
        Hint.SizeChanged += (_, _) => PlaceInk();
        Hint.InkMetricsChanged += PlaceInk;
        textCell = new Grid { Children = { Hint, Typed, Caret } };
        text = new Border { Child = textCell, ClipToBounds = true, MinHeight = 20, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center }; // OD-0132: a one-line composer's text sits on its vertical centre, as the real column's does

        // VT-10, voice-SPEC V-14.1 and CZ-4.3: the inert attachment is a left overlay, as in ComposerArea.MakeAttachButtons.
        var paperclip = SwiftImage.SystemName("paperclip").Font(Chrome.IconFont);
        paperclip.HorizontalAlignment = HorizontalAlignment.Center;
        paperclip.VerticalAlignment = VerticalAlignment.Center;
        paperclip.UseLayoutRounding = false; // ComposerAttachButton.cs:57: the symbol's fractional natural size stays centred in the button
        Attach = PlainControls.Button(paperclip, () => { }); // inert: the preview attaches nothing
        Attach.Chip(false);
        Attach.Width = 34;
        Attach.Height = 28;
        Attach.HorizontalAlignment = HorizontalAlignment.Left;
        Attach.VerticalAlignment = VerticalAlignment.Center; // OD-0132: on the composer's vertical centre (the real inside button: ComposerView.ArrangeOverride, single line)
        Attach.Margin = new Thickness(8, 0, 0, 0);
        Attach.Focusable = false;
        Attach.IsHitTestVisible = false;
        ToolTip.SetTip(Attach, "Attach files");
        AutomationProperties.SetName(Attach, "Attach files");
        Chip = new PreviewComposerChip(session.Maker, () => session.Step(1));
        // ComposerControls with voice off: model chip, ring, send, two apart.
        Controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        Controls.Children.Add(Chip);
        Controls.Children.Add(Ring);
        Controls.Children.Add(send);
        // OD-0132 (ComposerControls ctor, ComposerView.cs): every control centres on the row, so the ring is its natural 25 high
        foreach (var child in Controls.Children) child.VerticalAlignment = VerticalAlignment.Center;
        // the real row's own (ComposerView.ShowControls): a half-pixel centre must not round before its glyphs rasterise, and a fixed fractional width sits on the trailing edge, not centred in a rounded Auto column
        Controls.UseLayoutRounding = false;
        var controlsHost = new TrailingHost { Child = Controls, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, UseLayoutRounding = false };

        // one pill: the text on the left, the controls tucked into its trailing end (ComposerView.swift:143-145, :176-186)
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(text);
        Grid.SetColumn(controlsHost, 1);
        row.Children.Add(controlsHost);
        // ComposerArea.ApplyAttachPlace: the inside button adds 34 to the original 14 px text inset.
        padded = new Border { Child = row, Padding = new Thickness(48, 8, 8, 8) };
        layers = new Grid { Children = { dots, padded, Attach } };
        Pill = (Border)new ComposerSurface().Body(layers);
        Pill.Width = ComposerWidth;
        Pill.HorizontalAlignment = HorizontalAlignment.Left;
        Pill.VerticalAlignment = VerticalAlignment.Bottom;
        Pill.Margin = new Thickness(ComposerLeft, 0, 0, ComposerBottom);
        AutomationProperties.SetName(Pill, "Composer preview");

        Children.Add(Light);
        Children.Add(gate.Host);
        Children.Add(Pill);

        hintOpacity = new CzNumber(motion, 1, v => Hint.Opacity = v);
        typedOpacity = new CzNumber(motion, 0, v => { Typed.Opacity = v; Caret.IsVisible = v > 0.001; });
        Pill.LayoutUpdated += (_, _) => Place();
        LayoutUpdated += (_, _) => Place();
        session.Changed += Apply;
        DetachedFromVisualTree += (_, _) => { session.Changed -= Apply; Stop(); LeaveVoice(); };
        AttachedToVisualTree += (_, _) => { session.Changed -= Apply; session.Changed += Apply; UpdatePalette(); Apply(); };
        ActualThemeVariantChanged += (_, _) => UpdatePalette();
        UpdatePalette();
        Apply(first: true);
    }

    /// <summary>
    /// The real row's column (ComposerView.ShowControls: right-aligned, unrounded) in the preview: the controls keep their fractional width, so the chip's text decides where the chip starts,
    /// while the column is whole pixels and the ring and send sit on the trailing edge to the pixel, as the real ones do. (The Grid rounds a fractional Auto column to the nearest pixel, which
    /// can be narrower than the row, and the overflow would shift the ring and send by that fraction.)
    /// </summary>
    private sealed class TrailingHost : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            return new Size(Math.Ceiling(size.Width), size.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child is { } child)
            {
                var width = Math.Min(child.DesiredSize.Width, finalSize.Width);
                child.Arrange(new Rect(finalSize.Width - width, 0, width, finalSize.Height));
            }
            return finalSize;
        }
    }

    // ComposerTextView.UpdateHeight (:374): the text view is as tall as its one line, at least 18, with the container inset above and below
    private static double LineHeight(ComposerNSTextView view)
    {
        using var layout = new TextLayout("", new Typeface(view.FontFamily, view.FontStyle, view.FontWeight), view.FontSize, null, textWrapping: TextWrapping.Wrap, maxWidth: 1000);
        return Math.Ceiling(Math.Max(18, layout.Height) + view.Padding.Top + view.Padding.Bottom);
    }

    // the plate's rectangle in the light's coordinates, and the stage's bottom edge (the playground's 13 px fade; ComposerLightSlot does the same for a real composer)
    private void Place()
    {
        if (Pill.TranslatePoint(default, Light) is not { } origin) return;
        var rect = new Rect(origin, Pill.Bounds.Size);
        if (Light.ComposerRect != rect) Light.ComposerRect = rect;
        if (Light.FadeBottom != Light.Bounds.Height) Light.FadeBottom = Light.Bounds.Height;
    }

    // CZ-4.5.5: the maker's palette by the composer slot's rule, and the accent per appearance
    private void UpdatePalette()
    {
        string Hex(SwiftColor c, Avalonia.Styling.ThemeVariant theme) { var x = c.Resolve(theme); return $"#{x.R:X2}{x.G:X2}{x.B:X2}"; }
        Light.Palette = PreviewMakers.Palette(session.Maker, Hex(Chrome.Accent, Avalonia.Styling.ThemeVariant.Dark), Hex(Chrome.Accent, Avalonia.Styling.ThemeVariant.Light));
    }

    private string shownMaker = "";
    private PreviewState shownState = PreviewState.Rest;

    private void Apply() => Apply(first: false);

    private void Apply(bool first)
    {
        if (session.Maker != shownMaker)
        {
            if (!first) Chip.Show(session.Maker);
            shownMaker = session.Maker;
            UpdatePalette();
            UpdateDots();
        }
        var state = session.State;
        if (Voice is not null) { ShowVoicePhase(); return; }
        if (first || state != shownState)
        {
            shownState = state;
            Light.Phase = session.Phase;
            var running = state == PreviewState.Running;
            send.Show(running, enabled: state != PreviewState.Rest, animated: !first);
            dots.Show(running);
            Ring.Run(running);
            SetTyping(state == PreviewState.Typing, first);
        }
    }

    // CZ-4.3: Typing swaps the placeholder for the typed string (the placeholder out in 0.15 s, the text in in 0.12 s); leaving crosses them back over 0.25 s
    private void SetTyping(bool on, bool first)
    {
        if (on == typing) return;
        typing = on;
        if (on)
        {
            typedCount = 0;
            Typed.Text = "";
            typingStart = SwiftClock.Now;
            hintOpacity.To(0, CzMotion.Ease(.15));
            typedOpacity.To(1, CzMotion.Ease(.12));
            Wake();
        }
        else
        {
            hintOpacity.To(1, CzMotion.Ease(.25));
            typedOpacity.To(0, CzMotion.Ease(.25));
            Stop();
        }
    }

    private void Wake()
    {
        if (waiting || !typing || TopLevel.GetTopLevel(this) is null) return;
        frames = FrameClock.Source.Current;
        waiting = true;
        frames.RequestFrame(typingFrame);
    }

    private void Stop()
    {
        if (waiting) frames?.CancelFrame(typingFrame);
        waiting = false;
    }

    private void TypingFrame(TimeSpan _)
    {
        waiting = false;
        if (!typing) return;
        var reduce = EnvironmentValues.GetAccessibilityReduceMotion(this);
        var elapsed = (SwiftClock.Now - typingStart).TotalSeconds;
        var count = reduce ? TypedString.Length : Math.Min(TypedString.Length, (int)Math.Floor(elapsed / TypingPace));
        if (count != typedCount)
        {
            typedCount = count;
            Typed.Text = TypedString[..count];
            Typed.Measure(Size.Infinity);
            Caret.Margin = new Thickness(Typed.DesiredSize.Width + 1, 0, 0, 0);
            // OD-0139: each typed character lifts a shader light at the caret, as the page's study does
            if (!reduce && Typed.TranslatePoint(new Point(Typed.DesiredSize.Width + 1, 0), Pill) is { } caret) { Light.CaretX = caret.X; Light.Keystrokes = ++keystrokes; }
        }
        if (!reduce) Caret.Opacity = Blink(elapsed % CaretPeriod / CaretPeriod) * typedOpacity.Value;
        else Caret.Opacity = typedOpacity.Value;
        Wake();
    }

    // @keyframes blink { 0%,45% {1} 55%,95% {0} 100% {1} } with ease-in-out between the keyframes
    private static double Blink(double u)
    {
        static double Smooth(double x) => x * x * (3 - 2 * x);
        if (u < .45) return 1;
        if (u < .55) return 1 - Smooth((u - .45) / .1);
        if (u < .95) return 0;
        return Smooth((u - .95) / .05);
    }
}
