using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

/// <summary>
/// The composer's outline never snaps when the text wraps to a second line and back (owner bar: no snaps, no overlaps).
/// Swift grows and shrinks the pill's clip on smooth(0.28) (ComposerView.swift:604); the port's 1 pt placement of the
/// single-line outline (ED-NEW-T46-centre) must ride the same transition instead of stepping when the state flips.
/// </summary>
[Collection("Headless")]
[Trait("Decision", "ED-NEW-gate-fixes-6")]
public sealed class ComposerWrapTransitionT46Tests(HeadlessSession headless)
{
    private const string Short = "Single line with a descender: gyp";
    private const string Wrapped = "A long first line of words that has to wrap onto a second line because it is much wider than the composer text column, "
        + "so the composer grows by one line and the outline follows it. A long first line of words that has to wrap onto a second line.";

    private sealed record Sample(double Top, double Bottom, double Left, double Right, double Orb, double Height, double Text, double Send, double Attach, double Chip);

    private static Sample Read(ComposerView view, Avalonia.Controls.Window window)
    {
        // The outline is the plate the composer draws (its glass shape), which is what glides; the view itself only carries the contents' place.
        var rect = Frame(view.Child!, window);
        var slot = Slot(view);
        var orb = slot.TranslatePoint(new Point(slot.Diameter / 2, slot.Diameter / 2), window)!.Value.Y;
        var controls = LogicalQuery.SelfAndDescendants<ComposerControls>(view).Single().GetVisualDescendants().OfType<Control>().ToList();
        var send = Frame(controls.OfType<SendButton>().Single(), window).Center.Y;
        var chip = Frame(controls.OfType<ModelEffortSlot>().Single(), window).Center.Y;
        var attach = Frame(LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single(a => a.Place == ComposerAttachPlace.Inside), window).Center.Y;
        var text = Frame(DictationProbe.Box(view), window).Top;
        return new Sample(rect.Top, rect.Bottom, rect.Left, rect.Right, orb, rect.Height, text, send, attach, chip);
    }

    private static List<Sample> Transition(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window, string from, string to, out Sample before, out Sample after)
    {
        rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = from });
        Run(rig, window, .6);
        before = rig.Main.OnMain(() => Read(view, window));
        rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = to });
        var samples = Frames(rig, window, 40, () => Read(view, window));
        after = samples[^1];
        return samples;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void The_outline_glides_when_the_text_wraps_and_unwraps_and_the_resting_rectangles_are_unchanged(double scale) => headless.OnUiThread(() =>
    {
        var errors = new List<string>();
        foreach (var dark in new[] { false, true })
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default);
            var (view, window) = rig.Open(900);
            try
            {
                // The app's window carries its live-resize state; without it the height never glides (ComposerView.ApplyHeight).
                rig.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window)));
                rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, dark));
                rig.PumpWindow(window);
                rig.Main.OnMain(() => DictationProbe.Blur(view));
                var up = Transition(rig, view, window, Short, Wrapped, out var single, out var wrapped);
                var down = Transition(rig, view, window, Wrapped, Short, out var wrappedAgain, out var singleAgain);
                // The resting rectangles are as they were: the single line's outline is placed 1 pt down, the wrapped one is not.
                if (Math.Abs(wrapped.Bottom - wrappedAgain.Bottom) > .001) errors.Add($"{scale} dark={dark}: wrapped bottom {wrapped.Bottom} vs {wrappedAgain.Bottom}");
                if (Math.Abs(single.Bottom - singleAgain.Bottom) > .001) errors.Add($"{scale} dark={dark}: single bottom {single.Bottom} vs {singleAgain.Bottom}");
                if (Math.Abs(wrapped.Bottom - single.Bottom + 1) > .001) errors.Add($"{scale} dark={dark}: resting bottoms {single.Bottom} and {wrapped.Bottom} should differ by exactly the 1 pt placement");
                if (wrapped.Height <= single.Height + 10) errors.Add($"{scale} dark={dark}: the text did not wrap ({single.Height} to {wrapped.Height})");
                foreach (var (name, samples) in new[] { ("wrap", up), ("unwrap", down) })
                {
                    // A smooth interpolation of the pill's height moves no edge by more than a fraction of the whole move in a frame. The bottom edge is
                    // pinned (the pill grows upward), so only the 1 pt placement moves it: it must spread over the transition, never step in one frame.
                    var bottomSteps = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b.Bottom - a.Bottom)).ToList();
                    if (bottomSteps.Max() > .2) errors.Add($"{scale} dark={dark} {name}: the outline's bottom edge stepped {bottomSteps.Max():0.###} pt in one frame");
                    // The same for the top edge, net of the height's own glide: top + height is the bottom edge.
                    var edgeSteps = samples.Zip(samples.Skip(1), (a, b) => Math.Abs((b.Top + b.Height) - (a.Top + a.Height))).ToList();
                    if (edgeSteps.Max() > .2) errors.Add($"{scale} dark={dark} {name}: the outline's top edge stepped {edgeSteps.Max():0.###} pt beyond its height in one frame");
                    // The controls keep their approved window places through the whole transition (the outline alone glides): no control steps by more
                    // than half a device pixel (the layout rounding of its centre-to-bottom alignment), and each ends where it began.
                    foreach (var (control, read) in new (string, Func<Sample, double>)[] { ("orb", s => s.Orb), ("send", s => s.Send), ("attach", s => s.Attach), ("model chip", s => s.Chip) })
                    {
                        var steps = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(read(b) - read(a))).Max();
                        if (steps > .5 / scale + .01) errors.Add($"{scale} dark={dark} {name}: the {control} stepped {steps:0.###} pt in one frame");
                        if (Math.Abs(read(samples[^1]) - read(samples[0])) > .5 / scale + .01) errors.Add($"{scale} dark={dark} {name}: the {control} ended {read(samples[^1]) - read(samples[0]):0.###} pt from where it began");
                    }
                    // The outline moves the 1 pt over the height's own transition (not at once, not after it): monotone, and the last step is a small fraction.
                    var travelled = samples.Select(s => s.Bottom).ToList();
                    var sign = Math.Sign(travelled[^1] - travelled[0]);
                    if (sign != 0 && travelled.Zip(travelled.Skip(1), (a, b) => (b - a) * sign).Any(step => step < -.001)) errors.Add($"{scale} dark={dark} {name}: the outline's bottom edge doubled back");
                }
            }
            finally { rig.Close(window); }
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    });
}
