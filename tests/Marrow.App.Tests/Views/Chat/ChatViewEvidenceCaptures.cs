using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

    [Fact]
    [Trait("Evidence", "ChatView")]
    public void Chrome_row_with_a_project_and_no_repository()
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
                model.Sidebar.NoteLaidOutWidth(400); // the sidebar holds the window buttons, as in the video.
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "Establish LifeOS repo baseline framework", project, project.Path, new GitStatusModel());
                var window = new AvaloniaWindow { Content = row, Width = 2290, Height = 80, Background = Avalonia.Media.Brushes.Black };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Save(window, "chrome_row_port_2290");
            });
        });
    }
}
