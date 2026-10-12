using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Detail views (web W3c) on their own headless windows over one model, each pinned to a thread or following the selection.</summary>
public static class DetailWindows
{
    public static void Pump(AppModelHarness h, params Avalonia.Controls.Window[] windows)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            h.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                foreach (var window in windows) window.UpdateLayout();
            });
            h.Main.AdvanceMilliseconds(1);
        }
        h.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var window in windows) window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
    }

    public static (DetailView Detail, Avalonia.Controls.Window Window) Show(AppModelHarness h, AppModel model, Guid? pinned, double width, double height = 900)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height };
        var detail = new DetailView { PaneFactory = (kind, _) => new PaneContent(new Border(), PaneKinds.Title(kind)) };
        h.Main.OnMain(() =>
        {
            var resize = new WindowLiveResize(window);
            AppEnvironment.SetAppModel(detail, model);
            AppEnvironment.SetWindowLiveResize(detail, resize);
            detail.PinnedThreadID = pinned;
            window.Content = detail;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        return (detail, window);
    }

    public static void Close(AppModelHarness h, (DetailView Detail, Avalonia.Controls.Window Window) shown) => h.Main.OnMain(() =>
    {
        shown.Window.Hide();
        shown.Window.Content = null;
        Dispatcher.UIThread.RunJobs();
    });
}
