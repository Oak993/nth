// PORT-ONLY: page enter/Scene.add :1670-1699, :2899. Exact sampled CSS spring(200,22); RM is 260ms CSS ease opacity only.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionArrival
{
    private readonly Control owner;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;
    private static readonly Animation Soft = new CssSoftSpring();
    internal OpinionArrival(Control owner) { this.owner = owner; owner.DetachedFromVisualTree += (_, _) => Finish(); }
    internal void Enter()
    {
        Finish(); if (!FrameClock.IsBound) return;
        var reduce = CapsuleMotion.Reduce(owner);
        var curve = reduce ? Animation.TimingCurve(.25, .1, .25, 1, .26) : Soft;
        var start = SwiftClock.Now;
        frames = FrameClock.Source.Current;
        Apply(0);
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            if (seconds + .001 >= curve.Duration) { pending = null; Apply(1); return; }
            // A live accessibility change stills transforms, preserving the fade.
            reduce |= CapsuleMotion.Reduce(owner);
            Apply(curve.Progress(seconds)); frames.RequestFrame(tick!);
        };
        pending = tick; frames.RequestFrame(tick);
        void Apply(double progress)
        {
            owner.Opacity = Math.Clamp(progress, 0, 1);
            owner.RenderTransformOrigin = RelativePoint.Center;
            owner.RenderTransform = reduce || progress == 1 ? null : new TransformGroup { Children =
                { new ScaleTransform(.985 + .015 * progress, .985 + .015 * progress), new TranslateTransform(0, 10 * (1 - progress)) } };
        }
    }
    private void Finish() { if (pending is { } tick) frames?.CancelFrame(tick); pending = null; owner.Opacity = 1; owner.RenderTransform = null; }
    private sealed record CssSoftSpring : Animation
    {
        private readonly double[] samples;
        public override double Duration { get; }
        internal CssSoftSpring()
        {
            double x = 0, v = 0, t = 0; const double dt = 1.0 / 1200; var raw = new List<double> { 0 };
            while (t < 3)
            {
                for (var i = 0; i < 10; i++) { v += (-200 * (x - 1) - 22 * v) * dt; x += v * dt; t += dt; }
                raw.Add(x); if (t > .2 && Math.Abs(x - 1) < .0015 && Math.Abs(v) < .02) break;
            }
            Duration = Math.Round(t * 1000, MidpointRounding.AwayFromZero) / 1000;
            samples = Enumerable.Range(0, 45).Select(i => i == 44 ? 1 : Math.Round(raw[(int)Math.Floor((double)i / 44 * (raw.Count - 1) + .5)], 4, MidpointRounding.AwayFromZero)).ToArray();
        }
        public override double Progress(double seconds)
        {
            var position = Math.Clamp(seconds / Duration, 0, 1) * 44; var i = Math.Min(43, (int)position);
            return samples[i] + (samples[i + 1] - samples[i]) * (position - i);
        }
    }
}
