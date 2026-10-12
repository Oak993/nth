// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 inputs segmented, stepper, slider, toggle, textfield, select, date; 7.5 "simple
// steppers and sliders", accent only for the current value and focus; 10 inputs are real range, toggle and text controls;
// canvas index.html .uic .cl (a 12 px label 5 above its control), .stp (a 32 high capsule, radius 12, minus, the value in
// tabular numerals at least 30 wide, plus), input[type=range] (fills the row, at least 140 wide); ED-NEW-web-w16a-1). No
// Swift counterpart. Every input reads its bound state and writes it through the runtime (which clamps, snaps and checks
// it), so a change recomputes every let and the nodes that read it in the same frame. A stepper and a slider are range
// controls (RangeValue pattern, arrow, page, Home and End keys); a toggle is a switch (Toggle pattern); a select and a date
// open a small menu under them and also step with Up and Down while closed.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Marrow.Core.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>An input's label over its control (canvas .cl).</summary>
public abstract class NthUiInputView : NthUiNodeView
{
    private readonly StackPanel column = new() { Spacing = NthUiTokens.LabelGap };
    private readonly TextBlock? label;

    protected NthUiInputView(NthUiContext context, NthUiNode node, bool labelAbove = true) : base(context, node)
    {
        if (labelAbove && node.Arg("label") is not null)
        {
            label = NthUiText.Block(Runtime.Display(node, "label"), NthUiTokens.Caption, NthUiTokens.Text2);
            column.Children.Add(label);
        }
    }

    public override Control Control => column;

    /// <summary>The words that name the control: its label, else the state it sets.</summary>
    public string Name => Node.Arg("label") is null ? Node.Name ?? "" : Runtime.Display(Node, "label");

    protected NthUiValue Current => Runtime.Get(Node.Name ?? "");

    protected void SetField(Control field)
    {
        column.Children.Add(field);
        if (label is not null) AutomationProperties.SetLabeledBy(field, label);
    }

    /// <summary>Writes <paramref name="value"/> through the runtime and recomputes what reads it.</summary>
    protected void Write(NthUiValue value)
    {
        Context.Changed(Runtime.SetInput(Node, value));
        Sync();
    }

    public override void Refresh()
    {
        if (label is not null) label.Text = Runtime.Display(Node, "label");
        Sync();
    }

    /// <summary>Shows the bound state.</summary>
    protected abstract void Sync();

    protected static NthUiFace Field(TemplatedControl owner) => new(owner, NthUiFaceStyle.Plain, NthUiTokens.ControlRadius)
    {
        Height = NthUiTokens.ControlHeight,
        BorderThickness = new Thickness(1),
    };
}

/// <summary>A range control whose user changes go through <see cref="Requested"/> (the runtime then sets the value).</summary>
public abstract class NthUiRange : RangeBase
{
    private bool syncing;

    /// <summary>The user asked for this value (a key, a press, a drag, assistive technology).</summary>
    public event Action<double>? Requested;

    /// <summary>Shows the value the runtime holds.</summary>
    public void Show(double minimum, double maximum, double value, double step)
    {
        syncing = true;
        try
        {
            Minimum = double.MinValue;
            Maximum = maximum;
            Minimum = minimum;
            Value = value;
            SmallChange = step;
            LargeChange = step * 10;
        }
        finally
        {
            syncing = false;
        }
        InvalidateVisual();
    }

    protected void Request(double value) => Requested?.Invoke(value);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && !syncing) Request(Value);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty) Changed();
    }

    protected virtual void Changed() => InvalidateVisual();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        double? target = e.Key switch
        {
            Key.Up or Key.Right => Value + SmallChange,
            Key.Down or Key.Left => Value - SmallChange,
            Key.PageUp => Value + LargeChange,
            Key.PageDown => Value - LargeChange,
            Key.Home when Minimum > -1e14 => Minimum,
            Key.End when Maximum < 1e14 => Maximum,
            _ => null,
        };
        if (target is not double t) return;
        e.Handled = true;
        Request(t);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NthUiRangePeer(this);
}

/// <summary>The range peer with the control type each range shows (a spinner or a slider).</summary>
public sealed class NthUiRangePeer(NthUiRange owner) : RangeBaseAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() =>
        Owner is NthUiStepper ? AutomationControlType.Spinner : AutomationControlType.Slider;

    protected override string GetClassNameCore() => Owner.GetType().Name;
}

/// <summary>The stepper capsule: minus, the value, plus.</summary>
public sealed class NthUiStepper : NthUiRange
{
    private Button? minus;
    private Button? plus;
    private TextBlock? text;
    private Border? capsule;
    private string unit = "";
    private Func<double, string> format = v => v.ToString(CultureInfo.InvariantCulture);

    public NthUiStepper()
    {
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        Template = new FuncControlTemplate<NthUiStepper>((owner, _) => owner.Build());
        Classes.CollectionChanged += (_, _) => Ring();
    }

    protected override Type StyleKeyOverride => typeof(NthUiStepper);

    /// <summary>The minus and plus buttons' names.</summary>
    public (string Fewer, string More) Names { get; set; } = ("Fewer", "More");

    /// <summary>The value as shown (with its unit).</summary>
    public string Shown => text?.Text ?? "";

    public void Configure(Func<double, string> formatter, string? unitText)
    {
        format = formatter;
        unit = unitText ?? "";
        Changed();
    }

    private Control Build()
    {
        minus = Step("minus", Names.Fewer, -1);
        plus = Step("plus", Names.More, 1);
        text = NthUiText.Block("", NthUiTokens.Numbers.FontWeight(SwiftFont.Weight.Medium), NthUiTokens.Text1, wrap: false);
        text.MinWidth = 30;
        text.TextAlignment = TextAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(2, 0);
        capsule = new Border
        {
            Height = NthUiTokens.ControlHeight,
            CornerRadius = new CornerRadius(NthUiTokens.ControlRadius),
            Background = NthUiTokens.ControlFill.Brush(this),
            BorderBrush = NthUiTokens.ControlRim.Brush(this),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { minus, text, plus } },
        };
        Changed();
        Ring();
        return capsule;
    }

    private Button Step(string icon, string name, int direction)
    {
        var button = NthUiPress.Plain(new Button());
        var face = new NthUiFace(button, NthUiFaceStyle.Ghost, NthUiTokens.ControlRadius - 1) { Width = 30, Height = 30 };
        var glyph = (Control?)NthUiText.Icon(icon, 14) ?? NthUiText.Block(direction < 0 ? "−" : "+", NthUiTokens.BodyStrong, null, wrap: false);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        face.Child = glyph;
        button.Content = face;
        button.Focusable = false;
        button.Click += (_, _) =>
        {
            Request(Value + direction * SmallChange);
            Focus(NavigationMethod.Pointer);
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>Presses minus (-1) or plus (+1), as a pointer would.</summary>
    public void Press(int direction) => (direction < 0 ? minus : plus)?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    protected override void Changed()
    {
        if (text is null) return;
        var shown = format(Value) + (unit.Length > 0 ? " " + unit : "");
        if (text.Text != shown) text.Text = shown;
        if (minus is not null) minus.IsEnabled = Value > Minimum + 1e-9;
        if (plus is not null) plus.IsEnabled = Value < Maximum - 1e-9;
    }

    private void Ring()
    {
        if (capsule is null) return;
        capsule.BoxShadow = Classes.Contains(":focus-visible")
            ? new BoxShadows(new BoxShadow { Spread = 2, Color = NthUiTokens.Accent.Resolve(ActualThemeVariant) })
            : default;
    }
}

/// <summary>`stepper`: minus and plus around the value (canvas .stp).</summary>
public sealed class NthUiStepperView : NthUiInputView
{
    private readonly NthUiStepper stepper = new();

    public NthUiStepperView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        stepper.Names = (NthUiStrings.Fewer(Name), NthUiStrings.More(Name));
        stepper.Configure(v => NthUiValue.FormatNumber(v, Runtime.Culture), node.Arg("unit") is null ? null : Runtime.Display(node, "unit"));
        AutomationProperties.SetName(stepper, Name);
        stepper.Requested += v => Write(NthUiValue.Of(v));
        SetField(stepper);
        Sync();
    }

    public NthUiStepper Stepper => stepper;

    protected override void Sync()
    {
        var min = Runtime.Value(Node, "min");
        var max = Runtime.Value(Node, "max");
        var value = Current;
        stepper.Show(min.IsNumber ? min.AsNumber : -1e15, max.IsNumber ? max.AsNumber : 1e15, value.IsNumber ? value.AsNumber : 0, Runtime.InputStep(Node));
    }
}

/// <summary>The slider: a thin track, the accent fill up to the value, a round thumb; drag, click or keys.</summary>
public sealed class NthUiSlider : NthUiRange
{
    private const double Thumb = 9;
    private bool dragging;

    public NthUiSlider()
    {
        Focusable = true;
        Height = 24;
        MinWidth = 140;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Classes.CollectionChanged += (_, _) => InvalidateVisual();
    }

    protected override Type StyleKeyOverride => typeof(NthUiSlider);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? Math.Max(MinWidth, availableSize.Width) : MinWidth, Height);

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    private double Fraction => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    /// <summary>The thumb's centre for the current value.</summary>
    public double ThumbX => Thumb + Fraction * Math.Max(0, Bounds.Width - 2 * Thumb);

    private double ValueAt(double x)
    {
        var span = Math.Max(1, Bounds.Width - 2 * Thumb);
        return Minimum + Math.Clamp((x - Thumb) / span, 0, 1) * (Maximum - Minimum);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true;
        e.Pointer.Capture(this);
        Focus(NavigationMethod.Pointer);
        Request(ValueAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging) return;
        Request(ValueAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var cy = Bounds.Height / 2;
        var left = Thumb;
        var right = Math.Max(left, Bounds.Width - Thumb);
        var x = ThumbX;
        context.DrawRectangle(new ImmutableSolidColorBrush(NthUiTokens.Track.Resolve(theme)), null, new RoundedRect(new Rect(left - 2, cy - 2, right - left + 4, 4), 2));
        context.DrawRectangle(new ImmutableSolidColorBrush(NthUiTokens.Accent.Resolve(theme)), null, new RoundedRect(new Rect(left - 2, cy - 2, Math.Max(4, x - left + 2), 4), 2));
        var knob = new Point(x, cy);
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(theme == ThemeVariant.Dark ? (byte)110 : (byte)46, 0, 0, 0)), null, knob + new Vector(0, 1), Thumb + .5, Thumb + .5);
        var face = theme == ThemeVariant.Dark ? Color.FromRgb(0xF2, 0xF2, 0xF4) : Colors.White;
        context.DrawEllipse(new ImmutableSolidColorBrush(face), new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(30, 0, 0, 0)), .75), knob, Thumb, Thumb);
        if (Classes.Contains(":focus-visible"))
            context.DrawEllipse(null, new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.Accent.Resolve(theme)), 2), knob, Thumb + 3, Thumb + 3);
    }
}

/// <summary>`slider`: the slider and its value with its unit, in a readout as wide as the widest value.</summary>
public sealed class NthUiSliderView : NthUiInputView
{
    private readonly NthUiSlider slider = new();
    private readonly TextBlock readout;
    private readonly string unit;

    public NthUiSliderView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        unit = node.Arg("unit") is null ? "" : Runtime.Display(node, "unit");
        readout = NthUiText.Block("", NthUiTokens.Numbers, NthUiTokens.Text1, wrap: false);
        readout.TextAlignment = TextAlignment.Right;
        readout.VerticalAlignment = VerticalAlignment.Center;
        readout.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(slider, Name);
        slider.Requested += v => Write(NthUiValue.Of(v));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(slider);
        Grid.SetColumn(readout, 1);
        row.Children.Add(readout);
        SetField(row);
        Sync();
    }

    public NthUiSlider Slider => slider;

    public string Readout => readout.Text ?? "";

    private string Format(double value) => NthUiNumbers.Format(value, Runtime.Culture, unit);

    protected override void Sync()
    {
        var value = Current.IsNumber ? Current.AsNumber : 0;
        var min = Runtime.Value(Node, "min") is { IsNumber: true } lo ? lo.AsNumber : Math.Min(0, value);
        var max = Runtime.Value(Node, "max") is { IsNumber: true } hi ? hi.AsNumber : Math.Max(100, value);
        slider.Show(min, max, value, Runtime.InputStep(Node));
        readout.Text = Format(value);
        AutomationProperties.SetItemStatus(slider, readout.Text);
        var widest = new[] { Format(min), Format(max) }.MaxBy(s => s.Length) ?? "";
        if (readout.Tag as string != widest)
        {
            readout.Tag = widest;
            var probe = NthUiText.Block(widest, NthUiTokens.Numbers, null, wrap: false);
            probe.Measure(Size.Infinity);
            readout.MinWidth = Math.Ceiling(probe.DesiredSize.Width) + 12;
        }
    }
}

/// <summary>`segmented`: one choice of a few, on a segment track.</summary>
public sealed class NthUiSegmentedView : NthUiInputView
{
    private readonly NthUiSegments track;
    private readonly List<(string Value, ToggleButton Toggle)> options = [];

    public NthUiSegmentedView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        track = new NthUiSegments(context.Motion);
        var list = Runtime.Options(node);
        for (var i = 0; i < list.Count; i++)
        {
            var value = list[i].Value;
            var toggle = NthUiSegment.Create(list[i].Label, null, () => Write(NthUiValue.Of(value)), AutomationControlType.RadioButton);
            AutomationProperties.SetPositionInSet(toggle, i + 1);
            AutomationProperties.SetSizeOfSet(toggle, list.Count);
            options.Add((value, toggle));
            track.Add(toggle);
        }
        track.Picked += i => Write(NthUiValue.Of(options[i].Value));
        AutomationProperties.SetName(track, Name);
        AutomationProperties.SetControlTypeOverride(track, AutomationControlType.List);
        SetField(track);
        Sync();
    }

    public NthUiSegments Track => track;

    protected override void Sync()
    {
        var current = Current.Display(Runtime.Culture);
        var at = options.FindIndex(o => o.Value == current);
        for (var i = 0; i < options.Count; i++) options[i].Toggle.IsChecked = i == at;
        track.Selected = at;
    }
}

/// <summary>The switch: a track that fills with the accent when on and a knob that slides across.</summary>
public sealed class NthUiSwitch : ToggleButton, INthUiAnimated
{
    private readonly NthUiMotion motion;
    private NthUiTween knob = NthUiTween.At(0);

    internal NthUiSwitch(NthUiMotion motion)
    {
        this.motion = motion;
        Width = 38;
        Height = 22;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Classes.CollectionChanged += (_, _) => InvalidateVisual();
        IsCheckedChanged += (_, _) => Slide();
    }

    protected override Type StyleKeyOverride => typeof(NthUiSwitch);

    protected override AutomationPeer OnCreateAutomationPeer() => new ToggleButtonAutomationPeer(this);

    protected override Size MeasureOverride(Size availableSize) => new(38, 22);

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    /// <summary>The knob's travel now (0 off, 1 on).</summary>
    public double Travel => Math.Clamp(knob.Value(NthUiMotion.Now), 0, 1);

    private void Slide()
    {
        var target = IsChecked == true ? 1 : 0;
        if (!motion.CanAnimate || NthUiCalm.For(this)) knob = NthUiTween.At(target);
        else
        {
            knob = knob.Retarget(target, NthUiMotion.Now, .2, NthEase.Out);
            motion.Start(this);
        }
        InvalidateVisual();
    }

    bool INthUiAnimated.Step(double now)
    {
        InvalidateVisual();
        return !knob.Settled(now);
    }

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        var t = Travel;
        var off = NthUiTokens.Track.Resolve(theme);
        var on = NthUiTokens.Accent.Resolve(theme);
        var fill = Color.FromArgb(
            (byte)(off.A + (on.A - off.A) * t), (byte)(off.R + (on.R - off.R) * t), (byte)(off.G + (on.G - off.G) * t), (byte)(off.B + (on.B - off.B) * t));
        var track = new RoundedRect(new Rect(0, 0, 38, 22), 11);
        context.DrawRectangle(new ImmutableSolidColorBrush(fill), null, track);
        var center = new Point(11 + t * 16, 11);
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, center + new Vector(0, .75), 9.25, 9.25);
        context.DrawEllipse(Brushes.White, null, center, 9, 9);
        if (Classes.Contains(":focus-visible"))
            context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(on), 2), new RoundedRect(new Rect(-3, -3, 44, 28), 14));
    }
}

/// <summary>`toggle` (the input): a switch with its label beside it.</summary>
public sealed class NthUiToggleView : NthUiInputView
{
    private readonly NthUiSwitch toggle;
    private readonly TextBlock text;

    public NthUiToggleView(NthUiContext context, NthUiNode node) : base(context, node, labelAbove: false)
    {
        toggle = new NthUiSwitch(context.Motion) { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, Name);
        toggle.Click += (_, _) => Write(NthUiValue.Of(toggle.IsChecked == true));
        text = NthUiText.Block(Name, NthUiTokens.Body, NthUiTokens.Text1);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Cursor = new Cursor(StandardCursorType.Hand);
        text.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            Write(NthUiValue.Of(!Current.IsTruthy));
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { toggle, text } };
        row.MinHeight = NthUiTokens.ControlHeight;
        SetField(row);
        Sync();
    }

    public NthUiSwitch Switch => toggle;

    protected override void Sync()
    {
        toggle.IsChecked = Current.IsTruthy;
        if (text.Text != Name)
        {
            text.Text = Name;
            AutomationProperties.SetName(toggle, Name);
        }
    }
}

/// <summary>`textfield`: a tinted field (one line, or several with `multiline`) that sets its state as the user types.</summary>
public sealed class NthUiTextFieldView : NthUiInputView
{
    private readonly TextBox box;
    private readonly Border frame;
    private bool syncing;

    public NthUiTextFieldView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var multiline = node.Has("multiline");
        box = PlainControls.PlainTextField(new TextBox
        {
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
            VerticalAlignment = multiline ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = default,
        });
        NthUiTokens.Body.Apply(box);
        box.Foreground = NthUiTokens.Text1.Brush(box);
        if (node.Arg("placeholder") is not null) box.PlaceholderText = Runtime.Display(node, "placeholder");
        AutomationProperties.SetName(box, Name);
        frame = new Border
        {
            MinHeight = multiline ? 76 : NthUiTokens.ControlHeight,
            CornerRadius = new CornerRadius(NthUiTokens.ControlRadius),
            Background = NthUiTokens.ControlFill.Brush(box),
            BorderBrush = NthUiTokens.ControlRim.Brush(box),
            BorderThickness = new Thickness(1),
            Padding = multiline ? new Thickness(11, 7) : new Thickness(11, 0),
            Child = box,
            Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        frame.PointerPressed += (_, _) => box.Focus(NavigationMethod.Pointer);
        box.GotFocus += (_, _) => Ring(true);
        box.LostFocus += (_, _) => Ring(false);
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || syncing) return;
            Context.Changed(Runtime.SetInput(Node, NthUiValue.Of(box.Text ?? "")));
        };
        SetField(frame);
        Sync();
    }

    public TextBox Box => box;

    private void Ring(bool on) =>
        frame.BoxShadow = on ? new BoxShadows(new BoxShadow { Spread = 2, Color = NthUiTokens.Accent.Resolve(frame.ActualThemeVariant) }) : default;

    protected override void Sync()
    {
        var text = Current.Display(Runtime.Culture);
        if ((box.Text ?? "") == text) return;
        syncing = true;
        try
        {
            box.Text = text;
        }
        finally
        {
            syncing = false;
        }
    }
}

/// <summary>A menu under a field: a solid rounded surface with rows to pick from.</summary>
internal static class NthUiMenu
{
    public static Border Surface(Control owner, Control content) => new()
    {
        Background = NthUiTokens.Menu.Brush(owner),
        BorderBrush = NthUiTokens.CardRim.Brush(owner),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(4),
        Margin = new Thickness(0, 6, 0, 0),
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Spread = -6, Color = Color.FromArgb(70, 0, 0, 0) }),
        Child = content,
    };

    public static Popup Popup(Control anchor) => new()
    {
        PlacementTarget = anchor,
        Placement = PlacementMode.BottomEdgeAlignedLeft,
        IsLightDismissEnabled = true,
    };

    /// <summary>The closed field: the chosen text and a trailing glyph, on the tinted field face.</summary>
    public static Button FieldButton(string icon, out TextBlock text)
    {
        var button = NthUiPress.Plain(new Button());
        text = NthUiText.Block("", NthUiTokens.Body, null, wrap: false);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(text);
        if (NthUiText.Icon(icon, 13, NthUiTokens.Text2) is { } glyph)
        {
            glyph.Margin = new Thickness(10, 0, 0, 0);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(glyph, 1);
            row.Children.Add(glyph);
        }
        var face = new NthUiFace(button, NthUiFaceStyle.Plain, NthUiTokens.ControlRadius)
        {
            Height = NthUiTokens.ControlHeight,
            MinWidth = 140,
            Padding = new Thickness(12, 0, 10, 0),
            BorderBrush = NthUiTokens.ControlRim.Brush(button),
            BorderThickness = new Thickness(1),
            Child = row,
        };
        button.Content = face;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }
}

/// <summary>`select`: the chosen option in a field; pressing it opens the options under it.</summary>
public sealed class NthUiSelectView : NthUiInputView
{
    private readonly Button button;
    private readonly TextBlock text;
    private readonly Popup popup;
    private readonly List<(string Value, string Label)> options;

    public NthUiSelectView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        options = Runtime.Options(node).ToList();
        button = NthUiMenu.FieldButton("chevron-down", out text);
        AutomationProperties.SetName(button, Name);
        AutomationProperties.SetControlTypeOverride(button, AutomationControlType.ComboBox);
        popup = NthUiMenu.Popup(button);
        popup.Opened += (_, _) => FocusChosen();
        button.Click += (_, _) => Open();
        button.KeyDown += (_, e) =>
        {
            var at = options.FindIndex(o => o.Value == Current.Display(Runtime.Culture));
            var next = e.Key switch
            {
                Key.Down => Math.Min(options.Count - 1, at + 1),
                Key.Up => Math.Max(0, at < 0 ? 0 : at - 1),
                _ => -2,
            };
            if (next == -2 || options.Count == 0) return;
            e.Handled = true;
            Write(NthUiValue.Of(options[next].Value));
        };
        SetField(new Panel { Children = { button, popup } });
        Sync();
    }

    public Button Button => button;

    public Popup Menu => popup;

    public string Shown => text.Text ?? "";

    /// <summary>Opens the options under the field.</summary>
    public void Open()
    {
        if (options.Count == 0) return;
        popup.Child = NthUiMenu.Surface(button, BuildList());
        popup.IsOpen = true;
    }

    private Control BuildList()
    {
        var list = new StackPanel { Spacing = 1, MinWidth = Math.Max(140, button.Bounds.Width - 2) };
        var current = Current.Display(Runtime.Culture);
        foreach (var (value, label) in options)
        {
            var row = NthUiPress.Plain(new Button());
            var check = NthUiText.Icon("check", 13, NthUiTokens.Text1);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), VerticalAlignment = VerticalAlignment.Center };
            if (check is not null && value == current)
            {
                check.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(check);
            }
            var name = NthUiText.Block(label, NthUiTokens.Body, null, wrap: false);
            Grid.SetColumn(name, 1);
            line.Children.Add(name);
            var face = new NthUiFace(row, NthUiFaceStyle.Ghost, 8) { Height = 30, Padding = new Thickness(8, 0, 12, 0), Child = line };
            row.Content = face;
            row.Tag = value;
            row.Click += (_, _) =>
            {
                popup.IsOpen = false;
                Write(NthUiValue.Of(value));
                button.Focus(NavigationMethod.Tab);
            };
            AutomationProperties.SetName(row, label);
            AutomationProperties.SetControlTypeOverride(row, AutomationControlType.ListItem);
            list.Children.Add(row);
        }
        list.KeyDown += (_, e) =>
        {
            var rows = list.Children.OfType<Button>().ToList();
            var at = rows.FindIndex(r => r.IsFocused);
            switch (e.Key)
            {
                case Key.Down:
                    rows[Math.Min(rows.Count - 1, at + 1)].Focus(NavigationMethod.Directional);
                    e.Handled = true;
                    break;
                case Key.Up:
                    rows[Math.Max(0, at - 1)].Focus(NavigationMethod.Directional);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    button.Focus(NavigationMethod.Tab);
                    e.Handled = true;
                    break;
            }
        };
        AutomationProperties.SetControlTypeOverride(list, AutomationControlType.List);
        AutomationProperties.SetName(list, Name);
        return list;
    }

    private void FocusChosen()
    {
        if (popup.Child is not Border { Child: StackPanel list }) return;
        var current = Current.Display(Runtime.Culture);
        var row = list.Children.OfType<Button>().FirstOrDefault(r => r.Tag as string == current) ?? list.Children.OfType<Button>().FirstOrDefault();
        row?.Focus(NavigationMethod.Directional);
    }

    protected override void Sync()
    {
        var current = Current.Display(Runtime.Culture);
        var chosen = options.FirstOrDefault(o => o.Value == current);
        var shown = chosen.Label ?? current;
        text.Text = shown;
        text.Foreground = (shown.Length == 0 ? NthUiTokens.Text3 : NthUiTokens.Text1).Brush(text);
        AutomationProperties.SetItemStatus(button, shown);
    }
}

/// <summary>`date`: the chosen day in a field; pressing it opens a month to pick from. Up and Down move a day, Page Up
/// and Page Down a month, while closed.</summary>
public sealed class NthUiDateView : NthUiInputView
{
    private readonly Button button;
    private readonly TextBlock text;
    private readonly Popup popup;
    private DateTime month;

    public NthUiDateView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        button = NthUiMenu.FieldButton("calendar", out text);
        AutomationProperties.SetName(button, Name);
        AutomationProperties.SetHelpText(button, NthUiStrings.ChooseDate);
        popup = NthUiMenu.Popup(button);
        button.Click += (_, _) => Open();
        button.KeyDown += (_, e) =>
        {
            var step = e.Key switch
            {
                Key.Up => (Days: 1, Months: 0),
                Key.Down => (Days: -1, Months: 0),
                Key.PageUp => (Days: 0, Months: 1),
                Key.PageDown => (Days: 0, Months: -1),
                _ => (Days: 0, Months: 0),
            };
            if (step == (0, 0)) return;
            e.Handled = true;
            var from = Chosen ?? Today;
            Pick(from.AddDays(step.Days).AddMonths(step.Months));
        };
        SetField(new Panel { Children = { button, popup } });
        Sync();
    }

    public Button Button => button;

    public Popup Menu => popup;

    public string Shown => text.Text ?? "";

    /// <summary>Today, from the view's clock (so tests can fix it).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Today;

    private static DateTime Today => Clock().Date;

    private DateTime? Chosen => DateTime.TryParseExact(Current.Display(CultureInfo.InvariantCulture), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private DateTime? Bound(string arg) =>
        DateTime.TryParseExact(Runtime.Display(Node, arg), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private void Pick(DateTime day) => Write(NthUiValue.Of(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>Opens the month of the chosen day (or today) under the field.</summary>
    public void Open()
    {
        var from = Chosen ?? Today;
        month = new DateTime(from.Year, from.Month, 1);
        popup.Child = NthUiMenu.Surface(button, BuildMonth());
        popup.IsOpen = true;
    }

    private Control BuildMonth()
    {
        var culture = Runtime.Culture;
        var column = new StackPanel { Spacing = 4, Margin = new Thickness(4) };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var title = NthUiText.Block(month.ToString("MMMM yyyy", culture), NthUiTokens.BodyStrong, NthUiTokens.Text1, wrap: false);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(title, AutomationLiveSetting.Polite);
        var back = MonthButton("chevron-left", NthUiStrings.PreviousMonth, -1);
        var forward = MonthButton("chevron-right", NthUiStrings.NextMonth, 1);
        head.Children.Add(back);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        Grid.SetColumn(forward, 2);
        head.Children.Add(forward);
        column.Children.Add(head);
        var grid = new UniformGrid { Columns = 7 };
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            var name = NthUiText.Block(culture.DateTimeFormat.GetShortestDayName(day), NthUiTokens.Small, NthUiTokens.Text3, wrap: false);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 2, 0, 4);
            AutomationProperties.SetAccessibilityView(name, AccessibilityView.Raw);
            grid.Children.Add(name);
        }
        var lead = ((int)month.DayOfWeek - (int)first + 7) % 7;
        for (var i = 0; i < lead; i++) grid.Children.Add(new Border());
        var min = Bound("min");
        var max = Bound("max");
        var chosen = Chosen;
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        for (var d = 1; d <= days; d++)
        {
            var date = new DateTime(month.Year, month.Month, d);
            grid.Children.Add(DayButton(date, date == chosen, date == Today, (min is null || date >= min) && (max is null || date <= max)));
        }
        column.Children.Add(grid);
        column.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            button.Focus(NavigationMethod.Tab);
            e.Handled = true;
        };
        AutomationProperties.SetName(column, NthUiStrings.ChooseDate);
        return column;
    }

    private Button MonthButton(string icon, string name, int direction)
    {
        var b = NthUiPress.Plain(new Button());
        var glyph = (Control?)NthUiText.Icon(icon, 13) ?? NthUiText.Block(direction < 0 ? "‹" : "›", NthUiTokens.BodyStrong, null, wrap: false);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        b.Content = new NthUiFace(b, NthUiFaceStyle.Ghost, 14) { Width = 28, Height = 28, Child = glyph };
        b.Click += (_, _) =>
        {
            month = month.AddMonths(direction);
            if (popup.Child is Border surface) surface.Child = BuildMonth();
        };
        AutomationProperties.SetName(b, name);
        return b;
    }

    private Button DayButton(DateTime date, bool chosen, bool today, bool allowed)
    {
        var b = NthUiPress.Plain(new Button());
        var label = NthUiText.Block(date.Day.ToString(Runtime.Culture), NthUiTokens.Small.MonospacedDigit(), null, wrap: false);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = label, Margin = new Thickness(1) };
        if (chosen)
        {
            face.Background = NthUiTokens.Strong.Brush(b);
            label.Foreground = NthUiTokens.OnStrong.Brush(label);
        }
        else
        {
            label.Foreground = NthUiTokens.Text1.Brush(label);
            b.PointerEntered += (_, _) => face.Background = NthUiTokens.ControlHover.Brush(face);
            b.PointerExited += (_, _) => face.Background = null;
            if (today)
            {
                face.BorderBrush = NthUiTokens.Text3.Brush(face);
                face.BorderThickness = new Thickness(1);
            }
        }
        b.Content = face;
        b.IsEnabled = allowed;
        face.Opacity = allowed ? 1 : .35;
        b.Click += (_, _) =>
        {
            popup.IsOpen = false;
            Pick(date);
            button.Focus(NavigationMethod.Tab);
        };
        AutomationProperties.SetName(b, date.ToString("D", Runtime.Culture));
        return b;
    }

    protected override void Sync()
    {
        var chosen = Chosen;
        text.Text = chosen is { } d ? d.ToString("D", Runtime.Culture) : NthUiStrings.NoDate;
        text.Foreground = (chosen is null ? NthUiTokens.Text3 : NthUiTokens.Text1).Brush(text);
        AutomationProperties.SetItemStatus(button, text.Text);
        if (popup.IsOpen && popup.Child is Border surface && chosen is { } c)
        {
            month = new DateTime(c.Year, c.Month, 1);
            surface.Child = BuildMonth();
        }
    }
}
