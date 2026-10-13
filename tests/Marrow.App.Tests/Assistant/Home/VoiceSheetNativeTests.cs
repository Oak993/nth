using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant.Home;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 16.1 "60 fps motion: ... the voice sheet", and
// AS-18 "the sheet's Armilla follows the mic level while listening and the speech level while speaking". The home with its voice sheet open, on a real Win32 window with the real frame clock,
// while a driver thread plays a whole conversation into it on real time (the owner speaks, Dextra thinks and speaks, the owner speaks over it), holds 60 frames a second: the UI frames
// arrive at 58 or more a second with no gap of two frames, and the sheet's own mark draws at 58 or more a second on the render thread. To be read beside the diagnosis the failure prints.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-10")]
public sealed class VoiceSheetNativeTests(WindowManagerNativeSession native)
{
    private (Avalonia.Controls.Window Window, CountingFrameSource Frames) Host(Func<Control> content, double width = 1280, double height = 860)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, ShowInTaskbar = false };
        native.Register(window);
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        var frames = new CountingFrameSource(new AvaloniaFrameSource(window));
        FrameClock.Source.SetProcessDefault(frames);
        window.Content = content();
        window.Show();
        window.Activate();
        return (window, frames);
    }

    private void Front(Avalonia.Controls.Window window) => native.OnUi(() =>
        WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: the test window has no HWND."), native.ThreadId));

    [Fact]
    public void The_voice_sheet_holds_sixty_frames_a_second_while_it_listens_thinks_and_speaks()
    {
        native.Run(_ =>
        {
            VoiceRig rig = null!;
            AssistantHomeView home = null!;
            var (window, frames) = native.OnUi(() =>
            {
                rig = new VoiceRig(post: action => Dispatcher.UIThread.Post(action));
                var parts = new AssistantHomeParts(rig.Jobs, rig.Decisions, rig.Controls) { Name = "Dextra", Voice = rig.Conversation, TalkChord = () => "Ctrl+Alt+Enter" };
                var hosted = Host(() => home = new AssistantHomeView(parts));
                rig.Conversation.Start(VoiceStart.Toggle);
                return hosted;
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);
            Thread.Sleep(1000);

            var running = true;
            var driver = new Thread(() =>
            {
                void Pace(float[]? voice, double seconds)
                {
                    var steps = (int)Math.Round(seconds * 100);
                    var clock = Stopwatch.StartNew();
                    for (var n = 0; n < steps && running; n++)
                    {
                        rig.Step(voice is null ? default : voice.AsSpan(n * VoiceRig.Chunk, Math.Min(VoiceRig.Chunk, Math.Max(0, voice.Length - n * VoiceRig.Chunk))));
                        var due = TimeSpan.FromMilliseconds((n + 1) * 10) - clock.Elapsed;
                        if (due > TimeSpan.Zero) Thread.Sleep(due);
                    }
                }
                while (running)
                {
                    rig.Heard.Enqueue("what is running");
                    Pace(SyntheticSpeech.Voice(2, 1.6, 0.12), 1.6);
                    Pace(null, 3.5);
                    rig.Heard.Enqueue("stop everything");
                    Pace(SyntheticSpeech.Voice(3, 1.2, 0.15), 1.2);
                    Pace(null, 3.0);
                    native.OnUi(() => rig.Conversation.Start(VoiceStart.Toggle));
                }
            }) { IsBackground = true, Name = "voice native driver" };

            var stamps = new List<double>();
            var probing = true;
            void Probe(TimeSpan time)
            {
                stamps.Add(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                if (probing) frames.RequestFrame(Probe);
            }
            var drawnBefore = native.OnUi(() =>
            {
                frames.RequestFrame(Probe);
                return home.VoiceSheet!.Mark.FramesDrawn;
            });
            driver.Start();
            var clockWatch = Stopwatch.StartNew();
            Thread.Sleep(TimeSpan.FromSeconds(10));
            running = false;
            driver.Join(TimeSpan.FromSeconds(5));
            var seconds = clockWatch.Elapsed.TotalSeconds;
            var (delivered, span, longest, gaps, rate, phase) = native.OnUi(() =>
            {
                probing = false;
                var between = stamps.Zip(stamps.Skip(1), (a, b) => b - a).ToList();
                var histogram = string.Join(" ", between.GroupBy(g => (int)Math.Round(g)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key}:{g.Count()}"));
                return (stamps.Count, stamps[^1] - stamps[0], between.Max(), histogram, (home.VoiceSheet!.Mark.FramesDrawn - drawnBefore) / seconds, rig.Conversation.Phase);
            });
            var diagnosis = $"UI frames {delivered} over {span:F0} ms, longest gap {longest:F1} ms, gaps ms {gaps}; the sheet's mark drew {rate:F1}/s; last phase {phase}";
            Assert.True(delivered * 1000.0 / span >= 58, diagnosis);
            Assert.True(longest < 2 * 1000.0 / 60, diagnosis); // zero stutter: never a dropped pair of frames
            Assert.True(rate >= 58, diagnosis);
            native.OnUi(() => rig.Dispose());
        }, nameof(The_voice_sheet_holds_sixty_frames_a_second_while_it_listens_thinks_and_speaks), typeof(VoiceSheetNativeTests));
    }
}
