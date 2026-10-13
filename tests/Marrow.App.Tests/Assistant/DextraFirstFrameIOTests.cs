using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.App.Tests.Assistant;
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-open-6")]
public sealed class DextraFirstFrameIOTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_first_headless_frame_can_render_during_a_foreground_or_hidden_start_and_all_service_IO_stays_off_UI(bool hidden)
    {
        headless.OnUiThread(() =>
        {
            using var temp = new AssistantTempRoot();
            DextraFixStartupTests.DutyFile(temp);
            using var harness = new AppModelHarness(withRuntime: false);
            var model = new AppModel();
            using var chord = new Marrow.App.Safety.GlobalStop(new FakeStopChordHost(), () => null);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var io = new ConcurrentBag<bool>();
            var seams = new BackgroundStartSeams
            {
                Roots = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("worker release"); return temp.Roots; },
                Keys = () => temp.Keys, StopKey = chord, FileIO = _ => io.Add(MainActor.IsIsolated || Dispatcher.UIThread.CheckAccess()),
                Host = new DutySeams { RunKey = new FakeRunKey(), TaskHost = new FakeTaskSchedulerHost(),
                    Processes = new FakeProcessRunner((_, _) => new ProcessOutcome(0, "", "")), Desktop = new FakeDesktop(),
                    Time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero)) },
            };
            var settings = new DextraSettings(new InMemorySettingsStore()); settings.Update(p => p with { FirstRunDone = true, Id = Guid.NewGuid() });
            using var host = new DextraHost(model, settings, voice: (_, _, _) => null, startup: seams);
            model.AssistantHost = host;
            Task starting = hidden ? BackgroundStart.RunAsync(model, new StartupMode(true, null, null), () => { }, seams) : host.EnsureStartedAsync(seams);
            var window = new Avalonia.Controls.Window { Width = 320, Height = 180, Content = new TextBlock { Text = "First frame" } };
            try
            {
                HeadlessSession.RunDispatcherUntil(entered.Task, TimeSpan.FromSeconds(20));
                Assert.False(starting.IsCompleted);
                // The headless window is only a frame probe; neither start opens the owner's application or a native window.
                window.Show(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.False(starting.IsCompleted);
                Assert.DoesNotContain(true, io);
                release.Set();
                HeadlessSession.RunDispatcherUntil(starting, TimeSpan.FromSeconds(20));
                Assert.True(starting.IsCompletedSuccessfully);
                Assert.NotNull(model.Dextra);
                Assert.True(io.Count >= 12);
                Assert.DoesNotContain(true, io);
            }
            finally { release.Set(); host.Stop(); window.Content = null; window.Hide(); }
        });
    }
}
