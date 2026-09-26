using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EditorAttachments.swift (B2 1fbf81e), :1-53.
// Not claimed here: the arrival flight for a newly picked file (AttachmentFlight's own tests cover AttachmentArrival;
// this strip only wraps each cell in one) and the preview panel opening on a tap (a Popup the headless host cannot open).
[Collection("Headless")]
public sealed class EditorAttachmentsTests(HeadlessSession headless)
{
    private static Attachment File(string name) =>
        new() { Name = name, Path = @"C:\no-such-folder\" + name, MimeType = "text/plain" };

    // The real caller's source: PromptEditor binds the strip to a PromptEdit's attachments (FollowUpEditor.swift:14).
    private static (PromptEdit Edit, EditorAttachments Strip, AvaloniaWindow Window) Build(params Attachment[] files)
    {
        var edit = new PromptEdit(System.Guid.NewGuid(), "", new ValueList<Attachment>(files));
        var strip = new EditorAttachments(new Binding<ValueList<Attachment>>(() => edit.Attachments, value => edit.Attachments = value));
        var window = new AvaloniaWindow { Content = strip };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (edit, strip, window);
    }

    private static Button Badge(Control strip, string name) =>
        LogicalQuery.SelfAndDescendants<Button>(strip).Single(button => AutomationProperties.GetName(button) == "Remove " + name);

    private void OnMain(System.Action body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(body);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-180")] // App/Views/Composer/EditorAttachments.swift:22-24
    public void Each_attachment_is_a_48_point_thumbnail_in_an_arrival_cell()
    {
        OnMain(() =>
        {
            var (_, strip, window) = Build(File("a.txt"), File("b.txt"));
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<AttachmentArrival>(strip).Count());
            foreach (var thumbnail in LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip))
            {
                var plate = LogicalQuery.SelfAndDescendants<Border>(thumbnail).First(border => border.ClipToBounds);
                Assert.Equal(48, plate.Width);
                Assert.Equal(48, plate.Height);
            }
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-060")] // App/Views/Composer/EditorAttachments.swift:25-36
    public void The_remove_badge_is_named_for_its_file_sits_top_trailing_offset_6_by_minus_6_and_padded_4()
    {
        OnMain(() =>
        {
            var (_, strip, window) = Build(File("notes.txt"));
            var badge = Badge(strip, "notes.txt");
            Assert.Equal(HorizontalAlignment.Right, badge.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, badge.VerticalAlignment);
            var offset = Assert.IsType<TranslateTransform>(badge.RenderTransform);
            Assert.Equal(6, offset.X);
            Assert.Equal(-6, offset.Y);
            var padded = LogicalQuery.SelfAndDescendants<Border>(badge).First(border => border.Child is SwiftImage);
            Assert.Equal(new Thickness(4), padded.Padding);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-060")] // App/Views/Composer/EditorAttachments.swift:38-39
    public void Cells_are_padded_2_on_top_and_8_trailing_in_a_row_with_no_spacing()
    {
        OnMain(() =>
        {
            var (_, strip, window) = Build(File("a.txt"), File("b.txt"));
            var row = LogicalQuery.SelfAndDescendants<StackPanel>(strip).First();
            Assert.Equal(0, row.Spacing);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            foreach (var thumbnail in LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip))
                Assert.Equal(new Thickness(0, 2, 8, 0), Assert.IsType<Panel>(thumbnail.Parent).Margin);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-060")] // App/Views/Composer/EditorAttachments.swift:12
    public void The_strip_scrolls_horizontally_without_indicators()
    {
        OnMain(() =>
        {
            var (_, strip, window) = Build(File("a.txt"));
            var scroller = LogicalQuery.SelfAndDescendants<ScrollViewer>(strip).Single();
            Assert.Equal(ScrollBarVisibility.Hidden, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-180")] // App/Views/Composer/EditorAttachments.swift:26-27
    public void Clicking_a_badge_drops_only_that_attachment_from_the_edit_and_its_cell_from_the_strip()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var a = File("a.txt");
            var b = File("b.txt");
            var c = File("c.txt");
            var (edit, strip, window) = runtime.OnMain(() => Build(a, b, c));
            runtime.OnMain(() => Badge(strip, "b.txt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            // A second main turn: the strip's rows follow the binding through Track, which reacts on a later turn.
            runtime.OnMain(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal([a.Id, c.Id], edit.Attachments.Select(item => item.Id));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).Count());
                window.Close();
            });
        });
    }
}
