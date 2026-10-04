using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// UI-COMPOSER-033's "click toggles fast mode inside .spring(response: 0.3, dampingFraction: 0.7)" (EffortSlider.swift:665-672,
// :673 the plate is filled by the toggled value). The plate's colour is sampled against the spring's own curve, the overshoot
// of an under-damped spring included: damping 0.7 overshoots its target by about 4.6 percent at a quarter of the response.
[Collection("Headless")]
public sealed class EffortSliderFastModeSpringTests(HeadlessSession headless)
{
    private static byte Channel(byte from, byte to, double progress) => (byte)Math.Clamp(Math.Round(from + (to - from) * progress, MidpointRounding.AwayFromZero), 0, 255);

    [Fact]
    [Trait("Req", "UI-COMPOSER-033")] // App/Views/Composer/EffortSlider.swift:665-673 withAnimation(.spring(response: 0.3, dampingFraction: 0.7)) { isOn.toggle() }: the plate's colour follows that spring, overshoot included
    public void The_toggles_plate_follows_a_spring_of_response_point_three_and_damping_point_seven_through_its_overshoot() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var box = new ObservableBoolBox();
        var control = new FastModeButton(box.AsBinding());
        var window = new Avalonia.Controls.Window { Width = 120, Height = 80, Content = control };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var circle = Assert.Single(control.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());
            var theme = window.ActualThemeVariant;
            var from = Chrome.Overlay(0.06).Resolve(theme); // the off plate at rest
            var to = SwiftColor.Yellow.Opacity(0.18).Resolve(theme);
            var spring = Animation.Spring(response: 0.3, dampingFraction: 0.7); // the source's literals (:665)
            Color Plate() => Assert.IsAssignableFrom<ISolidColorBrush>(circle.Fill).Color;
            Assert.Equal(from, Plate());
            var button = Assert.Single(control.GetVisualDescendants().OfType<Button>());
            var peak = 0.0;
            var overshoot = false;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // the click toggles inside Transaction.WithAnimation(spring) (:665)
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.Value);
            for (var ms = 10; ms <= 700; ms += 10)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10)); frames.Advance(TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.RunJobs();
                var seconds = ms / 1000.0;
                var progress = ms / 1000.0 >= spring.Duration ? 1 : spring.Progress(seconds);
                var plate = Plate();
                var expected = Color.FromArgb(Channel(from.A, to.A, progress), Channel(from.R, to.R, progress), Channel(from.G, to.G, progress), Channel(from.B, to.B, progress));
                Assert.True(Math.Abs(plate.A - expected.A) <= 1 && Math.Abs(plate.R - expected.R) <= 1 && Math.Abs(plate.G - expected.G) <= 1 && Math.Abs(plate.B - expected.B) <= 1,
                    $"{ms} ms into the spring (progress {progress:F3}) the plate is {plate}, the curve gives {expected}");
                peak = Math.Max(peak, progress);
                // past its end value in the direction of travel, on the overshoot, in any of the channels that move
                if (progress > 1.02 && ((plate.A - to.A) * (to.A - from.A) > 0 || (plate.R - to.R) * (to.R - from.R) > 0 || (plate.G - to.G) * (to.G - from.G) > 0 || (plate.B - to.B) * (to.B - from.B) > 0)) overshoot = true;
            }
            Assert.True(peak > 1.03 && peak < 1.06, $"the spring's own overshoot is about 4.6 percent, here {peak:F3}");
            Assert.True(overshoot, "the plate's colour overshoots its target as the spring does");
            Assert.Equal(to, Plate()); // settled on the yellow at 18 percent
        }
        finally { window.Close(); }
    });
}
