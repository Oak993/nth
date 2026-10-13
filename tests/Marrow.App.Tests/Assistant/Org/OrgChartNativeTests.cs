using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.TeamSamples;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant.Org;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 16 "done when" for AS-17 ("60 fps gate") and 16.1 (p99 frame time under 16.7 ms and
// no two consecutive missed frames, on the owner's PC). (1) The Team tab on a real Win32 window with the real frame clock holds 60 frames a second for ten seconds while Dextra's mark lives at 64 px with six
// Asturs on its orbit in mixed states, the states change every half second, an Astur is made and another retired every two seconds (the rise, the fade, the glide, the connectors), the selection moves every
// three seconds, and the window is dragged across 640 px and back. (2) The sidebar's tree of six Asturs, whose marks spring on every change of state, holds 60 frames a second for six seconds.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-8")]
public sealed class OrgChartNativeTests(WindowManagerNativeSession native)
{
    // A theme-less window shows its content through a bare presenter (as WindowManagerNativeTests does); the frame source is bound before the content is built, so every mark and move takes the real one.
    private (Avalonia.Controls.Window Window, CountingFrameSource Frames) Host(Func<Control> content, double width = 1100, double height = 860)
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

    private static readonly AssistantProfile[] Six =
    [
        ChannelA, ChannelB, Store,
        Astur("p", "Desk", AsturShape.Star), Astur("q", "Ledger", AsturShape.Clover), Astur("r", "Inbox", AsturShape.RoundedTriangle),
    ];

    private static TeamSnapshot Mixed(int turn, int count)
    {
        var states = new[] { MissionState.Working, MissionState.NeedsYou, MissionState.Paused, MissionState.Planning, MissionState.Blocked, MissionState.Stopped };
        var team = Team([.. Six.Take(count)]);
        var jobs = team.Skip(1).Select((p, i) => Task(p.Id, "Task " + p.Name, states[(i + turn) % states.Length])).ToList();
        var cards = jobs.Where(j => j.Job.State == MissionState.NeedsYou).Select(j => Card(j.AssistantId, "Send it")).ToList();
        return TeamSnapshot.Build(team, jobs, cards);
    }

    [Fact]
    public void The_org_chart_holds_sixty_frames_a_second_with_six_Asturs_in_mixed_states_and_the_team_changing()
    {
        native.Run(_ =>
        {
            FakeTeamSource source = null!;
            TeamPanel panel = null!;
            var (window, frames) = native.OnUi(() =>
            {
                source = new FakeTeamSource { Current = Mixed(0, 6) };
                return Host(() => panel = new TeamPanel(source, new TeamActions { Add = () => { }, Talk = _ => { }, Edit = _ => { } }));
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);
            Thread.Sleep(1000); // first frames, JIT and the window's own appearance

            // A probe asks for every frame on the UI thread and stamps it itself, so a UI-thread stall shows as a gap; Dextra's mark draws on the render thread, so its own count says whether that thread kept up.
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
                return panel.Chart.Root!.DextraMark!.FramesDrawn;
            });
            var clock = Stopwatch.StartNew();
            var turn = 0;
            var count = 6;
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(500);
                var at = ++turn;
                native.OnUi(() =>
                {
                    if (at % 4 == 0) count = count == 6 ? 5 : 6;                                    // an Astur retired, then made again, every two seconds
                    source.Set(Mixed(at, count));
                    if (at % 6 == 0) panel.Chart.Select(panel.Chart.Snapshot.Members[at / 6 % panel.Chart.Snapshot.Members.Count].Id);
                    if (at == 10) window.Width = 600;                                                // across 640 px: the chart becomes the list
                    if (at == 16) window.Width = 1100;                                               // and back
                });
            }
            var seconds = clock.Elapsed.TotalSeconds;
            var (delivered, span, longest, gaps, rate) = native.OnUi(() =>
            {
                probing = false;
                var between = stamps.Zip(stamps.Skip(1), (a, b) => b - a).ToList();
                var histogram = string.Join(" ", between.GroupBy(g => (int)Math.Round(g)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key}:{g.Count()}"));
                return (stamps.Count, stamps[^1] - stamps[0], between.Max(), histogram, (panel.Chart.Root!.DextraMark!.FramesDrawn - drawnBefore) / seconds);
            });
            var diagnosis = $"UI frames {delivered} over {span:F0} ms, longest gap {longest:F1} ms, gaps ms {gaps}; Dextra's mark {rate:F1}/s";
            Assert.True(delivered * 1000.0 / span >= 58, diagnosis);
            Assert.True(longest < 2 * 1000.0 / 60, diagnosis); // zero stutter: never a dropped pair of frames
            Assert.True(rate >= 58, diagnosis);
        }, nameof(The_org_chart_holds_sixty_frames_a_second_with_six_Asturs_in_mixed_states_and_the_team_changing), typeof(OrgChartNativeTests));
    }

    [Fact]
    public void The_sidebar_tree_springs_its_marks_on_a_real_window_without_dropping_a_frame()
    {
        native.Run(_ =>
        {
            FakeTeamSource source = null!;
            AsturTree tree = null!;
            var (window, frames) = native.OnUi(() =>
            {
                source = new FakeTeamSource { Current = Mixed(0, 6) };
                return Host(() => new Border { Width = 260, Child = tree = new AsturTree(source) }, 300, 600);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Front(window);
            Thread.Sleep(1000);

            var stamps = new List<double>();
            var probing = true;
            void Probe(TimeSpan time)
            {
                stamps.Add(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                if (probing) frames.RequestFrame(Probe);
            }
            var before = native.OnUi(() =>
            {
                frames.RequestFrame(Probe);
                return tree.Rows.Sum(r => r.Mark.FramesDrawn);
            });
            var clock = Stopwatch.StartNew();
            var turn = 0;
            while (clock.Elapsed < TimeSpan.FromSeconds(6))
            {
                Thread.Sleep(250);
                var at = ++turn;
                native.OnUi(() => source.Set(Mixed(at, 6)));
            }
            var (delivered, span, longest, springs) = native.OnUi(() =>
            {
                probing = false;
                var between = stamps.Zip(stamps.Skip(1), (a, b) => b - a).ToList();
                return (stamps.Count, stamps[^1] - stamps[0], between.Max(), tree.Rows.Sum(r => r.Mark.FramesDrawn) - before);
            });
            var diagnosis = $"UI frames {delivered} over {span:F0} ms, longest gap {longest:F1} ms; the marks drew {springs} frames";
            Assert.True(delivered * 1000.0 / span >= 58, diagnosis);
            Assert.True(longest < 2 * 1000.0 / 60, diagnosis);
            Assert.True(springs > 0, diagnosis); // the springs were really drawn, not skipped
        }, nameof(The_sidebar_tree_springs_its_marks_on_a_real_window_without_dropping_a_frame), typeof(OrgChartNativeTests));
    }
}
