using Avalonia;
using HostWindow = Avalonia.Controls.Window;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Settings;

// Unit UE-4 (BUILD-PLAN.md 2.4 UE-4 "transition audit"; OD-0076 "nothing cuts"). The whole page is driven through a full story on the frame clock at 60 Hz (check, update everything with a
// running CLI, a failure, a download with a percentage, Retry, the restart offer, Later, Restart) and every animated quantity it exposes is sampled each frame: opacities, folds, the bar, the
// spinner's slot, the button's glass layers and its width. A cut is a step of the whole range in one frame; the quickest curve the page uses (the .1 s outgoing crossfade) moves 0.32 of its
// range in a 16 ms frame, so no quantity may move more than 0.4 of its range in one.
[Collection("Headless")]
public sealed class UpdatesTransitionAuditTests(HeadlessSession headless)
{
    private const double LargestStep = 0.4;
    private const double FrameMilliseconds = 16;

    [Fact]
    [Trait("Decision", "ED-237")] // OD-0076 / ED-237: nothing on the Updates page changes in one frame, through a whole check, run, failure, retry and restart
    public void No_animated_quantity_of_the_page_cuts_through_a_whole_check_run_failure_retry_and_restart() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cliGate = new TaskCompletionSource();
        var marrowGate = new TaskCompletionSource();
        IProgress<double?>? report = null;
        var claude = new ScriptedUpdateItem("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "2.1.283", "2.1.290") { OnUpdate = async _ => { await cliGate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); } };
        var attempts = 0;
        var codex = new ScriptedUpdateItem("cli:codex", "Codex", UpdateItemKind.ProviderCli, "0.157.1", "0.160.0")
        {
            OnUpdate = _ => Task.FromResult(++attempts == 1 ? new UpdateOutcome(false, null, false, false, "failed") : new UpdateOutcome(true, "0.160.0", true, false, null)),
        };
        var marrow = new ScriptedUpdateItem("marrow", "nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0")
        {
            OnUpdate = async progress => { report = progress; await marrowGate.Task; return new UpdateOutcome(true, "1.1.0", true, true, null); },
        };
        var page = new UpdatesSettingsPage(new OrchestratorUpdatesRunner(new UpdateOrchestrator([claude, codex, marrow]), () => { }));
        var window = new HostWindow { Width = 600, Height = 900, Content = new Border { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = page } };
        window.Show();
        runtime.RunUntilIdle();
        List<double[]> samples = [];
        List<string[]> identities = [];
        List<(string Name, Func<double> Value, Func<string> Identity)> series =
        [
            ("title in", () => page.TitleText.IncomingOpacity, () => page.TitleText.Text), ("title out", () => page.TitleText.OutgoingOpacity, () => "~" + page.TitleText.Text),
            ("detail in", () => page.DetailText.IncomingOpacity, () => page.DetailText.Text), ("detail out", () => page.DetailText.OutgoingOpacity, () => "~" + page.DetailText.Text),
            ("banner fold", () => page.BannerFold.Fraction, () => ""), ("banner title in", () => page.BannerTitleText.IncomingOpacity, () => page.BannerTitleText.Text),
            ("button primary", () => page.MainButton.PrimaryFill.Opacity, () => ""), ("button quiet", () => page.MainButton.QuietFill.Opacity, () => ""), ("button busy", () => page.MainButton.BusyFill.Opacity, () => ""),
            ("spinner slot", () => page.MainButton.SpinnerSlotWidth / 21, () => ""), ("button width", () => page.MainButton.Bounds.Width / 150, () => ""),
        ];
        foreach (var row in page.RowControls)
        {
            var id = row.RowId;
            series.Add(($"{id} status in", () => row.StatusCell.IncomingOpacity, () => row.StatusCell.Status.ToString()));
            series.Add(($"{id} status out", () => row.StatusCell.OutgoingOpacity, () => "~" + row.StatusCell.Status.ToString()));
            series.Add(($"{id} progress fold", () => row.ProgressFold.Fraction, () => ""));
            series.Add(($"{id} failure fold", () => row.FailureFold.Fraction, () => ""));
            series.Add(($"{id} installed in", () => row.InstalledText.IncomingOpacity, () => row.InstalledText.Text));
            series.Add(($"{id} latest in", () => row.LatestText.IncomingOpacity, () => row.LatestText.Text));
            series.Add(($"{id} bar fill", () => row.ProgressBar.Fraction, () => ""));
        }

        void Sample()
        {
            window.UpdateLayout();
            samples.Add(series.Select(entry => entry.Value()).ToArray());
            identities.Add(series.Select(entry => entry.Identity()).ToArray());
        }

        void Run(double milliseconds)
        {
            for (var elapsed = 0.0; elapsed < milliseconds; elapsed += FrameMilliseconds)
            {
                runtime.AdvanceMilliseconds(FrameMilliseconds);
                frames.Advance(TimeSpan.FromMilliseconds(FrameMilliseconds));
                runtime.RunUntilIdle();
                Sample();
            }
        }

        try
        {
            Run(700); // the opening check settles
            Assert.Equal("3 updates available", page.TitleText.Text);
            page.MainButton.Activate(); // Update everything: Claude Code runs (a sheen), Codex and Marrow wait
            runtime.RunUntilIdle();
            Run(500);
            cliGate.SetResult(); // Claude Code is done; Codex fails; Marrow begins its download
            runtime.RunUntilIdle();
            Run(400);
            report!.Report(.3);
            Run(400);
            report.Report(.8);
            Run(400);
            marrowGate.SetResult(); // Marrow is downloaded and waits for the restart
            runtime.RunUntilIdle();
            Run(700);
            Assert.Equal("Codex didn’t update", page.TitleText.Text);
            page.MainButton.Activate(); // Retry
            runtime.RunUntilIdle();
            Run(700);
            Assert.Equal("3 updated", page.TitleText.Text);
            page.LaterButton.Activate(); // Later
            Run(700);
            page.MainButton.Activate(); // Restart Marrow
            runtime.RunUntilIdle();
            Run(700);
            Assert.Equal("Restarting nth…", page.TitleText.Text);

            Assert.True(samples.Count > 20, "the story was sampled");
            // A line of text that is replaced hands over: its incoming copy must start low (it fades up) and the step is not counted across the hand-over, where the old copy becomes the outgoing one (series
            // marked "~" are those outgoing copies, which start high by nature).
            for (var index = 0; index < series.Count; index++)
            {
                var worst = 0.0;
                for (var frame = 1; frame < samples.Count; frame++)
                {
                    var step = Math.Abs(samples[frame][index] - samples[frame - 1][index]);
                    if (identities[frame][index] != identities[frame - 1][index])
                    {
                        Assert.True(identities[frame][index].StartsWith('~') || samples[frame][index] <= LargestStep, $"{series[index].Name} appeared at {samples[frame][index]:0.000} in the frame its text changed");
                        continue;
                    }
                    worst = Math.Max(worst, step);
                }
                Assert.True(worst <= LargestStep, $"{series[index].Name} moved {worst:0.000} of its range in one {FrameMilliseconds} ms frame");
            }
        }
        finally { window.Close(); }
    });
}
