// PORT-ONLY (ED-214)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Marrow.UI.Port;

/// <summary>SwiftUI minimumScaleFactor: reduce each one-line text's font during measure, before its own trimming.</summary>
public static class TextScaling
{
    public static readonly AttachedProperty<double> MinimumScaleFactorProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("MinimumScaleFactor", typeof(TextScaling), 1, inherits: true);

    public static double GetMinimumScaleFactor(Control view) => view.GetValue(MinimumScaleFactorProperty);

    /// <summary>Apply to a text or a composed view; every contained TextBlock retains its identity and its trimming.</summary>
    public static Control Apply(Control view, double factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(factor, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(factor, 1);
        view.SetValue(MinimumScaleFactorProperty, factor);
        return Modify(view, factor);
    }

    private static Control Modify(Control view, double factor)
    {
        view.SetValue(MinimumScaleFactorProperty, factor);
        if (view is MeasuringText) return view;
        if (view is TextBlock text) return new MeasuringText(text);
        if (view is Panel panel)
        {
            for (var i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i];
                panel.Children.RemoveAt(i); // Detach before putting the same text inside its measuring modifier.
                panel.Children.Insert(i, Modify(child, factor));
            }
            // Swift's HStack proposes bounded text slots. Avalonia's horizontal StackPanel instead measures
            // every child at infinity; preserve its natural spacing but share a finite proposal before measuring.
            if (panel is StackPanel { Orientation: Orientation.Horizontal } row) return new MeasuringRow(row, factor);
        }
        else if (view is Decorator decorator && decorator.Child is { } child)
        {
            decorator.Child = null;
            decorator.Child = Modify(child, factor);
        }
        else if (view is ContentControl content && content.Content is Control childContent)
        {
            content.Content = null;
            content.Content = Modify(childContent, factor);
        }
        return view;
    }

    private sealed class MeasuringText : Decorator
    {
        private readonly TextBlock text;
        private double size;
        private bool changingSize;

        public MeasuringText(TextBlock text)
        {
            this.text = text;
            size = text.FontSize;
            Child = text;
            UseLayoutRounding = text.UseLayoutRounding;
            text.PropertyChanged += (_, change) =>
            {
                if (!changingSize && change.Property == TextBlock.FontSizeProperty) size = text.FontSize;
            };
        }

        // ED-214: infinite proposals never scale; finite overflow uses max(factor, available / natural).
        protected override Size MeasureOverride(Size availableSize)
        {
            SetSize(size);
            text.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var natural = Math.Max(0, text.DesiredSize.Width - text.Margin.Left - text.Margin.Right);
            var available = Math.Max(0, availableSize.Width - text.Margin.Left - text.Margin.Right);
            if (double.IsFinite(available) && natural > available)
                SetSize(size * Math.Max(GetMinimumScaleFactor(text), available / natural));
            text.Measure(availableSize);
            return text.DesiredSize;
        }

        private void SetSize(double value)
        {
            changingSize = true;
            try { text.SetCurrentValue(TextBlock.FontSizeProperty, value); }
            finally { changingSize = false; }
        }
    }

    // Only the modifier's horizontal composition needs this adapter. Child controls (including buttons and
    // their text) are unchanged. Each measures in its own share of the row's finite width and scales independently.
    private sealed class MeasuringRow : Panel
    {
        private readonly double spacing;
        private double[] widths = [];

        public MeasuringRow(StackPanel source, double factor)
        {
            spacing = source.Spacing;
            HorizontalAlignment = source.HorizontalAlignment;
            VerticalAlignment = source.VerticalAlignment;
            Margin = source.Margin;
            UseLayoutRounding = source.UseLayoutRounding;
            SetValue(MinimumScaleFactorProperty, factor);
            while (source.Children.Count > 0)
            {
                var child = source.Children[0]; source.Children.RemoveAt(0); Children.Add(child);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            widths = new double[Children.Count];
            var natural = 0.0;
            for (var i = 0; i < Children.Count; i++)
            {
                Children[i].Measure(new Size(double.PositiveInfinity, availableSize.Height));
                widths[i] = Children[i].DesiredSize.Width;
                natural += widths[i];
            }
            var gaps = Math.Max(0, Children.Count - 1) * spacing;
            var available = Math.Max(0, availableSize.Width - gaps);
            var ratio = natural > available && natural > 0 ? available / natural : 1;
            double width = gaps, height = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                var slot = widths[i] * ratio;
                Children[i].Measure(new Size(slot, availableSize.Height));
                widths[i] = Math.Min(slot, Children[i].DesiredSize.Width);
                width += widths[i]; height = Math.Max(height, Children[i].DesiredSize.Height);
            }
            return new Size(Math.Min(availableSize.Width, width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var x = 0.0;
            for (var i = 0; i < Children.Count; i++)
            {
                Children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
                x += widths[i] + spacing;
            }
            return finalSize;
        }
    }
}
