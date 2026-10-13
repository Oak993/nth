using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.HomeSamples;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant.Home;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 16
// "done when" for AS-8. (1) The home on a real Win32 window with the real frame clock holds 60 frames a second with four working rows
// in the Result list, six marks in mixed states on the Board, the header and hero marks moving, and a pause morph with its label roll
// started on one row every half second, measured over ten seconds; no gap between frames reaches two frames. (2) On a real keyboard,
// Enter and a tap of Space on an irreversible card's Send never answer it, and a full one-second hold of Space does.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-7")]
public sealed class AssistantHomeNativeTests(WindowManagerNativeSession native)
{
    private const byte VkReturn = 0x0D;
    private const byte VkSpace = 0x20;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    // A theme-less window shows its content through a bare presenter (as WindowManagerNativeTests does); the frame source is bound
    // before the content is built, so every mark and morph takes the real one.
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
    public void The_home_holds_sixty_frames_a_second_with_four_working_rows_six_marks_and_the_morphs_running()
    {
        native.Run(_ =>
        {
            FakeMissionJobs jobs = null!;
            AssistantHomeView home = null!;
            var (window, frames) = native.OnUi(() =>
            {
                jobs = new FakeMissionJobs();
                jobs.Set(
                    Job("Fix the login timeout", MissionState.Working),
                    Job("Write the release notes", MissionState.Working),
                    Job("Next video: Why lenses fog up", MissionState.Working),
                    Job("Compare espresso machines", MissionState.Working),
                    Job("Reply to Sam", MissionState.NeedsYou),
                    Job("Plan the week", MissionState.Planning));
                var hosted = Host(() => home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls()) { Name = "Dextra" }));
                home.Result.Open(true);
                return hosted;
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);
            Thread.Sleep(1000); // first frames, JIT and the window's own appearance

            // A probe asks for every frame on the UI thread and stamps it itself, so a UI-thread stall shows as a gap; the moving marks
            // draw on the render thread, so each one's own count says whether the render thread kept up.
            var stamps = new List<double>();
            var probing = true;
            void Probe(TimeSpan time)
            {
                stamps.Add(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                if (probing) frames.RequestFrame(Probe);
            }
            IEnumerable<ArmillaMark> Moving() => home.GetVisualDescendants().OfType<ArmillaMark>().Where(m => m.Moves && !m.Calm && m.IsEffectivelyVisible);
            var drawnBefore = native.OnUi(() =>
            {
                frames.RequestFrame(Probe);
                return Moving().ToDictionary(m => m, m => m.FramesDrawn);
            });
            var clock = Stopwatch.StartNew();
            var turn = 0;
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(500);
                var at = turn++;
                native.OnUi(() =>
                {
                    // the pause morph of slot A and the label's roll on one working row, and back on the next turn
                    var job = jobs.Jobs[at % 4];
                    jobs.Replace(Moved(job, job.State == MissionState.Working ? MissionState.Paused : MissionState.Working));
                });
            }
            var seconds = clock.Elapsed.TotalSeconds;
            var (delivered, span, longest, gaps, marks) = native.OnUi(() =>
            {
                probing = false;
                var between = stamps.Zip(stamps.Skip(1), (a, b) => b - a).ToList();
                var histogram = string.Join(" ", between.GroupBy(g => (int)Math.Round(g)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key}:{g.Count()}"));
                var drawn = drawnBefore.Select(p => (p.Key.Kind, p.Key.MarkSize, Rate: (p.Key.FramesDrawn - p.Value) / seconds)).ToList();
                return (stamps.Count, stamps[^1] - stamps[0], between.Max(), histogram, drawn);
            });
            var diagnosis = $"UI frames {delivered} over {span:F0} ms, longest gap {longest:F1} ms, gaps ms {gaps}; moving marks " +
                string.Join(", ", marks.Select(m => $"{m.Kind} {m.MarkSize:F0} px {m.Rate:F1}/s"));
            Assert.True(marks.Count >= 1, diagnosis); // the header mark and the working cards move
            Assert.True(delivered * 1000.0 / span >= 58, diagnosis);
            Assert.True(longest < 2 * 1000.0 / 60, diagnosis); // zero stutter: never a dropped pair of frames
            Assert.All(marks, m => Assert.True(m.Rate >= 58, diagnosis));
        }, nameof(The_home_holds_sixty_frames_a_second_with_four_working_rows_six_marks_and_the_morphs_running), typeof(AssistantHomeNativeTests));
    }

    [Fact]
    public void On_a_real_keyboard_Enter_and_a_tap_of_Space_never_send_and_a_held_Space_does()
    {
        native.Run(_ =>
        {
            FakeDecisions decisions = null!;
            HoldToActButton send = null!;
            var (window, _) = native.OnUi(() =>
            {
                decisions = new FakeDecisions();
                decisions.Add(Irreversible("Send the reply to Sam"));
                var list = new DecisionList(decisions, new DecisionContext(decisions.Answer));
                var hosted = Host(() => list, 640, 420);
                send = list.Cards[0].AnswerButtons.OfType<HoldToActButton>().Single();
                return hosted;
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);
            native.OnUi(() => send.Focus(NavigationMethod.Tab));
            N.Until(() => native.OnUi(() => send.IsFocused), "Send never took focus");

            keybd_event(VkReturn, 0, 0, 0);
            keybd_event(VkReturn, 0, KeyUp, 0);
            Thread.Sleep(300);
            Assert.Empty(native.OnUi(() => decisions.Answers.ToList()));
            Assert.Equal(HoldToActButton.EnterHint, native.OnUi(() => send.Hint));

            keybd_event(VkSpace, 0, 0, 0);
            Thread.Sleep(400);
            keybd_event(VkSpace, 0, KeyUp, 0);
            Thread.Sleep(300);
            Assert.Empty(native.OnUi(() => decisions.Answers.ToList()));

            keybd_event(VkSpace, 0, 0, 0);
            Thread.Sleep(1300); // the keyboard's own repeat keeps arriving; the hold is one
            keybd_event(VkSpace, 0, KeyUp, 0);
            Thread.Sleep(300);
            var answer = Assert.Single(native.OnUi(() => decisions.Answers.ToList()));
            Assert.Equal(DecisionAnswerKind.DoIt, answer.Answer.Kind);
        }, nameof(On_a_real_keyboard_Enter_and_a_tap_of_Space_never_send_and_a_held_Space_does), typeof(AssistantHomeNativeTests));
    }
}
