using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Assistant.Views;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-21): the Activity view in a headless window over a throwaway audit log, with a manual frame clock, the
/// recording popover presenter (a headless window cannot open a popup) and a clock fixed at 2026-10-13 12:00 UTC (so the
/// dataset's 12th is "Yesterday" and its 11th "Sun 11 Oct 2026"). Made inside <c>HeadlessSession.OnUiThread</c>.
/// </summary>
public sealed class ActivityViewStage : IDisposable
{
    private readonly IDisposable frames = FrameClock.Source.Override(new ManualFrameSource());
    private readonly IDisposable presentation;
    private readonly Dictionary<Avalonia.Controls.Primitives.Popup, Control> menus = [];
    private readonly List<AvaloniaWindow> menuWindows = [];

    public ActivityViewStage(ActivityLogRig rig, double width = 1200, double height = 760, Func<ActivityExportFormat, string, Task<string?>>? pickPath = null,
        ActivityLabels? labels = null, ActivityLogSource? source = null, IActivityWork? work = null, bool show = true)
    {
        Popups = new RecordingPresenter();
        presentation = Presentation.Presenter.Override(Popups);
        Clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Model = new ActivityLogModel(source ?? rig.Source, work ?? new InlineActivityWork(), Clock, labels);
        View = new ActivityLogView(Model, TimeZoneInfo.Utc, pickPath);
        Window = new AvaloniaWindow { Content = View, Width = width, Height = height };
        if (show) Show();
    }

    public RecordingPresenter Popups { get; }

    public ManualTimeProvider Clock { get; }

    public ActivityLogModel Model { get; }

    public ActivityLogView View { get; }

    public AvaloniaWindow Window { get; }

    public void Show()
    {
        Window.Show();
        Layout();
    }

    /// <summary>Runs queued work and lays the window out (twice: the list fills its cells once it knows its viewport).</summary>
    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public static T[] Nodes<T>(Control root) where T : Control => root.GetSelfAndLogicalDescendants().OfType<T>().ToArray();

    public static string[] Texts(Control root) => Nodes<TextBlock>(root).Select(t => t.Text ?? "").ToArray();

    /// <summary>Presses a button the way its click does.</summary>
    public static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// The items of the popup the last press presented. A popover's view comes off the card the presenter hung it on and is put
    /// in a window of its own (a headless run cannot open a popup, and an item builds its body when it is shown); the same
    /// popup gives the same view each time.
    /// </summary>
    public PopoverItem[] OpenedItems(Control anchor)
    {
        Dispatcher.UIThread.RunJobs(); // a press shows its popover when the queued work has run
        var popup = Popups.Calls.Last(call => call.Presented).Popup;
        if (!menus.TryGetValue(popup, out var menu))
        {
            menu = (Control)PopoverHost.Content(popup)!;
            PopoverCardContent.Lift(menu);
            var window = new AvaloniaWindow { Content = menu, Width = 420, Height = 700 };
            window.Show();
            menuWindows.Add(window);
            menus[popup] = menu;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        return Nodes<PopoverItem>(menu);
    }

    public void Dispose()
    {
        foreach (var window in menuWindows) window.Close();
        Window.Close();
        Model.Dispose();
        presentation.Dispose();
        frames.Dispose();
    }
}
