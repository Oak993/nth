using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-12")]
public sealed class DextraSmoothRestTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void Visible_sleep_settles_continuously_and_wake_continues_the_presented_frame(int hz) => headless.OnUiThread(() =>
    {
        using var scene = new Scene(hz);
        foreach (var state in new[] { ArmillaState.Thinking, ArmillaState.Working, ArmillaState.Idle, ArmillaState.Listening })
        {
            scene.Send(ArmillaHandler.Sleep.Off);
            scene.Mark.State = state;
            var prior = scene.Frame(); long ownStep = 0;
            for (var i = 0; i < hz * 3; i++) { var next = scene.Frame(); ownStep = Math.Max(ownStep, Distance(prior, next)); prior = next; }
            Assert.True(ownStep > 0, "the frame witness did not see moving ink");
            var before = scene.Mark.FramesDrawn;
            scene.Send(ArmillaHandler.Sleep.On);
            for (var i = 0; i < hz * 18; i++)
            {
                var next = scene.Frame();
                Assert.True(Distance(prior, next) <= ownStep, $"{state} rest step {Distance(prior, next)} exceeds its own spring/motion step {ownStep} at {hz} Hz");
                prior = next;
            }
            Assert.True(scene.Mark.FramesDrawn > before + 2, "visible sleep froze instead of continuously reaching rest");
            Assert.False(scene.Mark.Ticking);
            before = scene.Mark.FramesDrawn;
            for (var i = 0; i < hz; i++) Assert.Equal(prior, scene.Frame());
            Assert.Equal(before, scene.Mark.FramesDrawn);
            // A second, focused window advances the SAME compositor hub during this sleep.
            before = scene.Mark.FramesDrawn;
            scene.Send(ArmillaHandler.Sleep.Off);
            for (var i = 0; i < 8 && scene.Mark.FramesDrawn == before; i++) Assert.Equal(prior, scene.Frame());
            Assert.True(scene.Mark.FramesDrawn > before, "wake equality must witness the first actual drawing");
            for (var i = 0; i < hz * 2; i++)
            {
                var next = scene.Frame();
                Assert.True(Distance(prior, next) <= ownStep, $"{state} wake step {Distance(prior, next)} exceeds own step {ownStep} at {hz} Hz, input frame {i}");
                prior = next;
            }
        }
    });

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void Hidden_sleeping_changes_resume_still_without_replaying_unseen_motion(int hz) => headless.OnUiThread(() =>
    {
        using var scene = new Scene(hz);
        scene.Send(ArmillaHandler.Sleep.On);
        for (var i = 0; i < hz * 18; i++) scene.Frame();
        scene.Mark.IsVisible = false;
        for (var i = 0; i < 4; i++) scene.Frame();
        var before = scene.Mark.FramesDrawn;
        scene.Mark.State = ArmillaState.Thinking; scene.Mark.Glyph = ArmillaGlyph.Done;
        for (var i = 0; i < hz; i++) scene.Frame();
        Assert.Equal(before, scene.Mark.FramesDrawn); Assert.False(scene.Mark.Ticking);
        scene.Mark.IsVisible = true;
        byte[]? first = null;
        for (var i = 0; i < 8 && first is null; i++)
        {
            var frame = scene.Frame(); if (scene.Mark.FramesDrawn > before) first = frame;
        }
        Assert.NotNull(first);
        for (var i = 0; i < hz * 2; i++) Assert.Equal(first, scene.Frame());
        Assert.False(scene.Mark.Ticking);
    });

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void Sleeping_repaints_are_identical_to_the_frame_already_presented(int hz) => headless.OnUiThread(() =>
    {
        using var scene = new Scene(hz);
        scene.Mark.State = ArmillaState.Thinking;
        for (var i = 0; i < hz * 2; i++) scene.Frame();
        var before = scene.Mark.FramesDrawn;
        scene.Send(ArmillaHandler.Sleep.On);
        for (var i = 0; i < hz * 18; i++) scene.Frame();
        Assert.True(scene.Mark.FramesDrawn > before + 2, "the sleeping repaint must preserve a continuously settled frame");
        var prior = scene.Frame();
        for (var i = 0; i < 6; i++)
        {
            // Invalidate the surrounding surface, without changing the mark's look.
            scene.Window.Background = i % 2 == 0 ? Brushes.DimGray : Brushes.Gray;
            scene.Frame();
            scene.Window.Background = Brushes.Black;
            Assert.Equal(prior, scene.Frame());
            var beforeRepaint = scene.Mark.FramesDrawn;
            scene.Repaint();
            for (var n = 0; n < 8 && scene.Mark.FramesDrawn == beforeRepaint; n++) scene.Frame();
            Assert.True(scene.Mark.FramesDrawn > beforeRepaint, "the equality witness must include an actual new drawing");
            Assert.Equal(prior, scene.Frame());
        }
    });

    internal sealed class Scene : IDisposable
    {
        internal readonly ManualTimeProvider Clock = new();
        private readonly IDisposable time;
        private readonly DisplayPeriod display;
        private readonly int hz;
        internal readonly ArmillaMark Mark = new() { MarkSize = 176, Kind = ArmillaKind.Live, State = ArmillaState.Working,
            Nodes = Enum.GetValues<ArmillaShape>().Select(s => new ArmillaNode(s, true)).ToArray() };
        internal readonly Window Window;
        private readonly Window peer;
        internal Scene(int hz)
        {
            this.hz = hz; time = SwiftClock.Provider.Override(Clock); display = new DisplayPeriod(hz);
            Window = new Window { Width = 176, Height = 176, Background = Brushes.Black, Content = Mark };
            peer = new Window { Width = 24, Height = 24, Content = new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Live } };
            Window.Show(); peer.Show();
            ArmillaWindowWatch.For(Window).Focused(true); ArmillaWindowWatch.For(peer).Focused(true);
            for (var i = 0; i < 8; i++) Frame();
        }
        internal void Send(object message) => Visual(Mark).SendHandlerMessage(message);
        internal void Repaint() => Send(ArmillaHandler.Shown.Yes);
        internal byte[] Frame() { Pump(Clock, hz); return Pixels(Window); }
        public void Dispose() { Window.Close(); peer.Close(); Pump(Clock, hz); display.Dispose(); time.Dispose(); }
    }

    internal sealed class DisplayPeriod : IDisposable
    {
        private readonly Func<TimeSpan?> previous = RenderFrameSource.PeriodSource;
        internal DisplayPeriod(int hz) => RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / hz);
        public void Dispose() => RenderFrameSource.PeriodSource = previous;
    }

    internal static CompositionCustomVisual Visual(Control control) => (CompositionCustomVisual)control.GetType()
        .GetField("visual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;

    internal static void Pump(ManualTimeProvider clock, int hz)
    {
        clock.Advance(TimeSpan.FromSeconds(1.0 / hz));
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }

    internal static byte[] Pixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static long Distance(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        long total = 0;
        for (var i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
        return total;
    }
}
