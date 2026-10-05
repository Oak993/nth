// PORT-ONLY, first-release UE-4 (OD-0100 place A, OD-0076). One line of text that, when it changes, crossfades with the old line sinking 4 pt as it fades (.22 s) and the new one rising after a short wait
// (the design's .xt: opacity .22 s, translate .3 s ez, .06 s delay) while the width of the box eases to the new line (.3 s ez), so a button around it grows or shrinks with its label and nothing cuts.
// Each line carries its own colour. The motion runs on the frame clock (CzMotion) and lands at once under Reduce Motion.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesFadeText : Panel
{
    private const double Sink = 4;

    private readonly CzMotion motion;
    private readonly CzNumber width;
    private readonly double size;
    private readonly SwiftFont.Weight weight;
    private Line current;
    private bool measured;

    public UpdatesFadeText(string text, SwiftColor ink, double size, SwiftFont.Weight weight)
    {
        this.size = size;
        this.weight = weight;
        motion = new CzMotion(this);
        width = new CzNumber(motion, 0, _ => InvalidateMeasure());
        ClipToBounds = true;
        current = Make(text, ink, shown: true);
    }

    public string Text => current.Block.Text ?? "";

    public double IncomingOpacity => current.Opacity;

    public void SetText(string text, SwiftColor ink)
    {
        if (current.Block.Text == text) return;
        foreach (var line in Children.OfType<Line>().Where(l => !ReferenceEquals(l, current)).ToArray()) Children.Remove(line);
        var outgoing = current;
        current = Make(text, ink, shown: false);
        outgoing.Hide();
        current.Show();
        InvalidateMeasure();
    }

    private Line Make(string text, SwiftColor ink, bool shown)
    {
        var line = new Line(this, text);
        SwiftFont.System(size, weight).Apply(line.Block);
        line.Block.Foreground = ink.Brush(line.Block);
        Children.Add(line);
        line.Bind(motion, shown);
        return line;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var line in Children.OfType<Line>()) line.Measure(Size.Infinity);
        var natural = current.DesiredSize;
        if (!measured)
        {
            width.Jump(natural.Width);
            measured = true;
        }
        else width.To(natural.Width, CzMotion.Ez(.3));
        return new Size(width.Value, natural.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var line in Children.OfType<Line>())
            line.Arrange(new Rect(0, Math.Max(0, (finalSize.Height - line.DesiredSize.Height) / 2), line.DesiredSize.Width, line.DesiredSize.Height));
        return finalSize;
    }

    private sealed class Line : Decorator
    {
        private readonly UpdatesFadeText owner;
        private CzNumber? fade, lift;
        private bool hidden;

        public Line(UpdatesFadeText owner, string text)
        {
            this.owner = owner;
            Block = new TextBlock { Text = text, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            Child = Block;
            IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Left;
        }

        public TextBlock Block { get; }

        public void Bind(CzMotion motion, bool shown)
        {
            fade = new CzNumber(motion, shown ? 1 : 0, v => Opacity = v);
            lift = new CzNumber(motion, shown ? 0 : Sink, v => RenderTransform = new TranslateTransform(0, v));
            Opacity = shown ? 1 : 0;
            RenderTransform = new TranslateTransform(0, shown ? 0 : Sink);
        }

        public void Show()
        {
            fade!.To(1, CzMotion.Ease(.22), .06);
            lift!.To(0, CzMotion.Ez(.3), .06);
        }

        public void Hide()
        {
            hidden = true; // taken out of the tree once it is invisible (OnPropertyChanged)
            fade!.Jump(Opacity);
            fade.To(0, CzMotion.Ease(.22));
            lift!.To(Sink, CzMotion.Ez(.3));
            if (Opacity <= 0 && !ReferenceEquals(owner.current, this)) owner.Children.Remove(this); // it was never shown: no fade to wait for
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == OpacityProperty && hidden && Opacity <= 0 && !ReferenceEquals(owner.current, this)) owner.Children.Remove(this);
        }
    }
}
