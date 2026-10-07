// PORT-ONLY: .btn.sm at :1308-1318. Explicit painted template; PlainControls' content-only template cannot paint Button.Background.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Marrow.Core.Port;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal static class OpinionCombineChrome
{
    internal static void Install(Button button)
    {
        button.Template = new FuncControlTemplate<Button>((owner, scope) =>
        {
            var label = new ContentPresenter { Name = "PART_ContentPresenter", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope);
            var surface = new Face(owner) { Child = label, Padding = new Thickness(11, 0) };
            return surface;
        });
    }
    private sealed class Face : Decorator
    {
        private readonly Button owner;
        private bool keyboard;
        private double fill, fromFill, targetFill, scale = 1, fromScale = 1, targetScale = 1, alpha = 1, fromAlpha = 1, targetAlpha = 1;
        private double focus, fromFocus, targetFocus;
        private ContinuousInstant start;
        private IFrameSource? frames;
        private Action<TimeSpan>? pending;
        private static readonly Animation FillCurve = Animation.EaseOut(.1), PressCurve = Animation.TimingCurve(.25, .1, .25, 1, .12), AlphaCurve = Animation.TimingCurve(.25, .1, .25, 1, .1), ShadowCurve = Animation.TimingCurve(.25, .1, .25, 1, .2);
        internal Face(Button owner)
        {
            this.owner = owner; fill = fromFill = targetFill = Fill();
            owner.PropertyChanged += (_, change) =>
            {
                if (change.Property == Button.IsPressedProperty || change.Property == IsPointerOverProperty || change.Property == IsEffectivelyEnabledProperty) Change();
                else if (change.Property == IsFocusedProperty || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Change();
            };
            owner.GotFocus += (_, e) => { keyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional; Change(); };
            owner.ActualThemeVariantChanged += (_, _) => { Stop(); fill = Fill(); InvalidateVisual(); };
            AttachedToVisualTree += (_, _) => { fill = Fill(); InvalidateVisual(); };
            DetachedFromVisualTree += (_, _) => Stop();
        }
        private double Fill()
        {
            var dark = owner.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var press = owner.IsPressed && owner.IsEffectivelyEnabled; var hover = owner.IsPointerOver && owner.IsEffectivelyEnabled;
            return dark ? press ? .055 : hover ? .12 : .075 : press ? .5 : hover ? .86 : .62;
        }
        private void Change()
        {
            Sample(); Stop(); fromFill = fill; targetFill = Fill(); fromScale = scale; fromAlpha = alpha;
            var pressed = owner.IsPressed && owner.IsEffectivelyEnabled;
            targetScale = pressed && !CapsuleMotion.Reduce(owner) ? .97 : 1; targetAlpha = pressed ? .86 : 1;
            fromFocus = focus; targetFocus = keyboard && owner.IsFocused ? 1 : 0;
            if (CapsuleMotion.Reduce(owner)) { scale = fromScale = targetScale = 1; PaintState(); }
            if (!FrameClock.IsBound) { fill = targetFill; scale = targetScale; alpha = targetAlpha; focus = targetFocus; PaintState(); return; }
            start = SwiftClock.Now; frames = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null; tick = _ => { Sample(); if ((SwiftClock.Now - start).TotalSeconds >= .2) pending = null; else frames.RequestFrame(tick!); };
            pending = tick; frames.RequestFrame(tick);
        }
        private void Sample()
        {
            if (pending is null) return;
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            fill = fromFill + (targetFill - fromFill) * FillCurve.Progress(Math.Min(seconds, .1));
            scale = fromScale + (targetScale - fromScale) * PressCurve.Progress(Math.Min(seconds, .12));
            alpha = fromAlpha + (targetAlpha - fromAlpha) * AlphaCurve.Progress(Math.Min(seconds, .1));
            focus = fromFocus + (targetFocus - fromFocus) * ShadowCurve.Progress(Math.Min(seconds, .2)); PaintState();
        }
        private void PaintState()
        {
            Opacity = alpha; RenderTransformOrigin = RelativePoint.Center;
            RenderTransform = CapsuleMotion.Reduce(owner) || Math.Abs(scale - 1) < .00001 ? null : new ScaleTransform(scale, scale); InvalidateVisual();
        }
        private void Stop() { if (pending is { } frame) frames?.CancelFrame(frame); pending = null; }
        public override void Render(DrawingContext context)
        {
            var dark = owner.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var rect = new RoundedRect(new Rect(Bounds.Size), 13);
            var background = Rgba(255, 255, 255, fill);
            var rim = dark ? Rgba(255, 255, 255, .13) : Rgba(0, 0, 0, .09);
            var high = Rgba(255, 255, 255, dark ? .14 : .95);
            var low = Rgba(0, 0, 0, dark ? .22 : .035);
            var shadow = dark ? Rgba(0, 0, 0, .30) : Rgba(30, 40, 60, .10);
            var depth = dark ? Rgba(0, 0, 0, .40) : Rgba(30, 40, 60, .16);
            var shadows = new List<BoxShadow>
            {
                new() { IsInset = true, Spread = .5, Color = rim }, new() { IsInset = true, OffsetY = 1, Color = high },
                new() { OffsetY = 1, Blur = 2, Color = shadow }, new() { OffsetY = 2, Blur = 8, Spread = -4, Color = depth },
            };
            var ring = CapsuleTokens.For(dark).FocusRing;
            shadows.Insert(2, new BoxShadow { Spread = 2, Color = Color.FromArgb((byte)Math.Round(ring.A * focus), ring.R, ring.G, ring.B) });
            shadows.Insert(3, new BoxShadow { IsInset = true, OffsetY = -1, Color = Color.FromArgb((byte)Math.Round(low.A * (1 - focus)), low.R, low.G, low.B) });
            context.Custom(new OpinionButtonBackdrop(new Rect(Bounds.Size)));
            context.DrawRectangle(new SolidColorBrush(background), null, rect, new BoxShadows(shadows[0], shadows.Skip(1).ToArray()));
            base.Render(context);
        }
        private static Color Rgba(byte r, byte g, byte b, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b);
    }
}
