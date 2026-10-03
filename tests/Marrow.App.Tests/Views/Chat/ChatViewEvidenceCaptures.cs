using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY evidence renders for the ChatView parity captures (compared side by side with the reference videos of the
// real Mac app 1.7.0). They write PNGs only when MARROW_CHATVIEW_EVIDENCE names a folder (outside git); otherwise they
// do nothing. The comparisons themselves are cited from the parity tests that use them.
[Collection("Headless")]
public sealed class ChatViewEvidenceCaptures(HeadlessSession headless)
{
    private static string? Folder => Environment.GetEnvironmentVariable("MARROW_CHATVIEW_EVIDENCE");

    private static void Save(AvaloniaWindow window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var stream = File.Create(Path.Combine(Folder!, name + ".png"));
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    // The Mac's state in 2-chat-use.mov at C 11 s: a project with no repository, Hydra on, the sidebar holding the window
    // buttons, the thread "Establish LifeOS repo baseline framework"; the dark appearance. 2290 = the content width there.
    [Fact]
    [Trait("Evidence", "ChatView")]
    public void Chrome_row_as_in_the_reference_still()
    {
        if (Folder is null) return;
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = true;
                var project = model.AddProject(new Project("LifeOS-Migration", h.NewFolder("project")).Url);
                var runtime = model.Runtime(model.NewThread(project)!.Id);
                model.Sidebar.NoteLaidOutWidth(400);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "Establish LifeOS repo baseline framework", project, project.Path, new GitStatusModel()) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }; // as ChatView places it
                var window = new AvaloniaWindow { Content = row, Width = 2290, Height = 80, RequestedThemeVariant = ThemeVariant.Dark, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(38, 44, 52)) };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Save(window, "chrome_row_port_dark");
            });
        });
    }

    // The terminal open of 2-chat-use.mov C 132.2-132.55 on the port: frames at the same times after the start.
    [Fact]
    [Trait("Evidence", "ChatView")]
    public void Terminal_open_frames()
    {
        if (Folder is null) return;
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
            var model = h.MakeModel();
            h.PrepareBootstrap(model);
            AvaloniaWindow window = null!;
            ThreadRuntime runtime = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
                window = new AvaloniaWindow { Content = new ChatView(runtime), Width = 1400, Height = 900, RequestedThemeVariant = ThemeVariant.Dark, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(38, 44, 52)) };
                AppEnvironment.SetAppModel(window, model);
                AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            void Drain() { h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); h.Main.RunUntilIdle(); }
            try
            {
                Drain();
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain();
                var elapsed = 0.0;
                foreach (var at in new[] { 0.0, 0.0317, 0.0533, 0.11, 0.145, 0.21, 0.338 })
                {
                    h.Main.Advance(TimeSpan.FromSeconds(at - elapsed)); elapsed = at;
                    frames.Advance(TimeSpan.Zero);
                    Drain();
                    Save(window, $"terminal_port_{at * 1000:000}ms");
                }
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(); }
        });
    }
}
