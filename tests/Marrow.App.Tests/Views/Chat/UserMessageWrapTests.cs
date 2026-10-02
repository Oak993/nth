using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Defect: a long sent message drew as one clipped line in the real window ("...Follow the existing Switch co").
// Source: App/Views/Chat/TimelineRows.swift:118-129 (the bubble is InlineText(hugsContent: true), padded 14 / 14 + tail, 9),
// :163-164 (.frame(maxWidth: .infinity, alignment: .trailing), .padding(.leading, 96)): a long message wraps inside the
// column less 96 points, and the bubble grows in height with it. The timeline hosts the rows in a scroll viewer.
[Collection("Headless")]
public sealed class UserMessageWrapTests(HeadlessSession headless)
{
    private const string GalleryMessage =
        "Add a dark-mode toggle to the settings page, keep the choice in localStorage and cover it with a test. Follow the existing Switch component.";

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void Settle(Avalonia.Controls.Window window, ManualFrameSource frames)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            frames.Advance(TimeSpan.FromMilliseconds(50));
            window.UpdateLayout();
        }
    }

    // The timeline as the main window hosts it, 820 points wide, with the message as the thread's first row.
    private void InTimeline(string message, Action<ThreadTimeline, Avalonia.Controls.Window> check) => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = new Marrow.App.AppModel();
        var id = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
        DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult();
        var runtime = new ThreadRuntime(id, model);
        var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, false, 600);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 600 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show();
            Settle(window, frames);
            var turn = Guid.NewGuid();
            SetEntries(runtime, [Entry("user", new TimelineItem.Content.User(new UserMessage { Text = message }), turn)]);
            Settle(window, frames);
            check(view, window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:118-129, :163-164
    public void A_long_sent_message_in_the_timeline_wraps_inside_the_column_less_96_points_and_the_bubble_grows_taller()
    {
        double oneLine = 0;
        InTimeline("hello there", (view, _) => oneLine = Assert.Single(view.GetVisualDescendants().OfType<InlineText>()).Bounds.Height);
        Assert.InRange(oneLine, 8, 40);
        InTimeline(GalleryMessage, (view, _) =>
        {
            var text = Assert.Single(view.GetVisualDescendants().OfType<InlineText>());
            var bubble = Assert.IsType<Border>(text.Parent);
            var column = view.GetVisualDescendants().OfType<StackPanel>().First(stack => stack.Spacing == 6 && stack.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right);
            // :163-164 the bubble stays inside the column less the 96 points of leading padding.
            Assert.True(bubble.Bounds.Width <= column.Bounds.Width + 0.5, $"bubble {bubble.Bounds.Width} is wider than its column {column.Bounds.Width}");
            // :122 the words wrap inside the bubble's 14 + 18 point insets and none is cut off at its edge.
            Assert.True(text.Bounds.Width <= bubble.Bounds.Width - 32 + 0.5, $"text {text.Bounds.Width} is wider than its bubble {bubble.Bounds.Width}");
            // The 140-character message needs more than the 700 or so points the column leaves: two lines at least.
            Assert.True(text.Bounds.Height >= 2 * oneLine - 1, $"the message is one line: its text is {text.Bounds.Height} high against {oneLine} for one line");
        });
    }
}
