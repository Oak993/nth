using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// nth web surfaces, unit W3a: the chat chrome row's trailing end (work/design/web-surfaces/DESIGN.md 3.2, 3.12). With no
// pane open it keeps clear of the Windows caption buttons (OD-0044); when panes take the window's right edge the row runs
// to the chat column's own end, keeping only the chrome padding, and a live reflow moves it on the panel slide.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3a-1")]
public sealed class ChatChromeTrailingReserveTests(HeadlessSession headless)
{
    [Fact]
    public void The_row_keeps_clear_of_the_caption_buttons_only_while_it_runs_under_them() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        Panel slot = null!;
        Func<double> trailing = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = false;
            var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("project")).Url))!.Id);
            var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "Fix it", null, h.Scratch, new GitStatusModel())
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            slot = new Panel { Children = { row } };
            var window = new AvaloniaWindow { Content = slot, Width = 1400, Height = 300 };
            ChatHeadlessFixtures.Track(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            trailing = () =>
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var last = LogicalQuery.SelfAndDescendants<ChromeCapsule>(row).Last();
                return row.Bounds.Width - last.TranslatePoint(new Point(last.Bounds.Width, 0), row)!.Value.X;
            };

            Assert.Equal(WindowChrome.CaptionTrailingReserve, trailing(), 1);
            ChatView.SetChromeTrailingReserve(slot, -400); // the column ends 556 DIP left of the window's edge
            Assert.Equal(Chrome.ChromeHorizontalPadding, trailing(), 1);
            ChatView.SetChromeTrailingReserve(slot, 60); // it ends 96 DIP short: the buttons still overhang 60
            Assert.Equal(60, trailing(), 1);
            ChatView.SetChromeTrailingReserve(slot, double.NaN);
            Assert.Equal(WindowChrome.CaptionTrailingReserve, trailing(), 1);

            // A live reflow moves it on Chrome.PanelSlide instead of jumping.
            ChatView.SetChromeTrailingReserveAnimates(slot, true);
            ChatView.SetChromeTrailingReserve(slot, -400);
            Assert.Equal(WindowChrome.CaptionTrailingReserve, trailing(), 1);
        });
        h.Main.AdvanceMilliseconds(100);
        h.Main.OnMain(() =>
        {
            frames.Advance(TimeSpan.FromMilliseconds(100));
            Assert.InRange(trailing(), Chrome.ChromeHorizontalPadding + 1, WindowChrome.CaptionTrailingReserve - 1);
        });
        h.Main.AdvanceMilliseconds(400);
        h.Main.OnMain(() =>
        {
            frames.Advance(TimeSpan.FromMilliseconds(400));
            Assert.Equal(Chrome.ChromeHorizontalPadding, trailing(), 1);
        });
    });
}
