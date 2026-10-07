// PORT-ONLY RR-37, locked Settings Rise SPEC sections 5 and 7.
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsClose
{
    private const double Duration = .160;
    private readonly HostWindow window;
    private readonly Control content;
    private readonly ChromeCard? card;
    private readonly IFrameSource frames = FrameClock.Source.Current;
    private readonly Action<TimeSpan> frame;
    private readonly ContinuousInstant start = SwiftClock.Now;
    private readonly SettingsNativePresentation? native;
    private readonly List<IDisposable> bindings = [];
    private readonly SettingsRise.Feed<double> opacity = new(), cardOpacity = new();
    private readonly SettingsRise.Feed<ITransform?> transform = new();
    private readonly double contentBase, cardBase;
    private readonly ITransform? baseTransform;
    private readonly bool? fullOpacity;
    private bool requested;
    public bool Done { get; private set; }

    public SettingsClose(HostWindow window, SettingsView view)
    {
        this.window = window;
        content = (Control)window.Content!;
        card = SettingsRiseLayout.Gather(view).FirstCard;
        frame = OnFrame;
        contentBase = content.GetBaseValue(Visual.OpacityProperty) is { HasValue: true } value ? value.Value : 1;
        cardBase = card?.GetBaseValue(Visual.OpacityProperty) is { HasValue: true } own ? own.Value : 1;
        baseTransform = content.RenderTransform;
        native = SettingsNativePresentation.Attach.Current(window);
        fullOpacity = RenderOptions.GetRequiresFullOpacityHandling(window);
        // The card itself must composite before its extra alpha, on both render paths.
        RenderOptions.SetRequiresFullOpacityHandling(window, true);
        if (native is null)
        {
            SettingsNativePresentation.ReportUnavailable(window);
            // Parent render option makes the framebuffer fallback one composited alpha layer too.
            bindings.Add(content.Bind(Visual.OpacityProperty, opacity, BindingPriority.Animation));
            bindings.Add(content.Bind(Visual.RenderTransformProperty, transform, BindingPriority.Animation));
            var origin = new SettingsRise.Feed<RelativePoint>(); origin.Push(RelativePoint.Center);
            bindings.Add(content.Bind(Visual.RenderTransformOriginProperty, origin, BindingPriority.Animation));
        }
        if (card is not null) bindings.Add(card.Bind(Visual.OpacityProperty, cardOpacity, BindingPriority.Animation));
        window.PropertyChanged += OnWindowChanged;
        Apply(0);
        Request();
    }

    public void Stop()
    {
        if (Done) return;
        Done = true;
        window.PropertyChanged -= OnWindowChanged;
        if (requested) { requested = false; frames.CancelFrame(frame); }
        foreach (var binding in bindings) binding.Dispose();
        bindings.Clear();
        RenderOptions.SetRequiresFullOpacityHandling(window, fullOpacity);
        native?.Dispose();
    }

    private void OnWindowChanged(object? _, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && !window.IsVisible) Stop();
    }
    private void Request() { requested = true; frames.RequestFrame(frame); }
    private void OnFrame(TimeSpan _)
    {
        requested = false;
        if (Done) return;
        var elapsed = (SwiftClock.Now - start).TotalSeconds;
        if (EnvironmentValues.GetAccessibilityReduceMotion(window) || elapsed >= Duration)
        {
            Apply(1);
            window.Hide(); // hide the zero-alpha frame before releasing overrides
            Stop();
            return;
        }
        Apply(CometEasing.Bezier(Math.Clamp(elapsed / Duration, 0, 1), .4, 0, 1, 1).Value);
        Request();
    }
    private void Apply(double progress)
    {
        var factor = 1 - progress;
        var scale = 1 - .015 * progress;
        if (native is not null)
            native.Close(factor, scale, new Vector2((float)(window.ClientSize.Width * window.RenderScaling), (float)(window.ClientSize.Height * window.RenderScaling)));
        else
        {
            opacity.Push(contentBase * factor);
            var shrink = new ScaleTransform(scale, scale);
            transform.Push(baseTransform is Transform given ? new TransformGroup { Children = { given, shrink } } : shrink);
        }
        cardOpacity.Push(cardBase * factor); // the card's own fade under the gate gives the specified square
    }
}
