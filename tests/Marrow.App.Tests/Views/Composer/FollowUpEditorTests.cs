using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/FollowUpEditor.swift (B2 1fbf81e), :1-37.

[Collection("Headless")]
public sealed class FollowUpEditorTests(HeadlessSession headless)
{
    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static Button ButtonNamed(Control host, string text) =>
        LogicalQuery.SelfAndDescendants<Button>(host)
            .First(button => Texts(button).Contains(text));

    // A real offline ThreadRuntime inside a headless UI test, built on the UI thread like every Avalonia object here.
    // The harness's PrepareBootstrap calls LoginEnvironment.Load().Wait(): Load awaits with the Avalonia dispatcher's
    // context captured, so waiting for it on that thread deadlocks (FollowUpQueueTabTests' note). The load is
    // process-wide and cached (Shell.cs LoginEnvironment.Loader), so it is completed once first on a pool thread, which
    // has no synchronization context; the harness's own Load().Wait() then returns at once.
    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    private static Control Build(ThreadRuntime runtime, PromptEdit edit)
    {
        var host = new Border { Child = new FollowUpEditor(edit, runtime) };
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        Settle(window);
        return host;
    }

    // The editor's TextBox raises TextChanged from posted dispatcher jobs, and each layout pass can post more; they
    // write through the text binding into the MainActorModel PromptEdit, so they must all run inside h.Main.OnMain
    // (ComposerTextViewHarness drains the same way). Anything left for the dispatcher's reset between tests would run
    // off the main actor and trap.
    private static void Settle(Avalonia.Controls.TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    // Closes the test's window on the main actor and drains what closing posts.
    private static void Close(Control host)
    {
        if (Avalonia.Controls.TopLevel.GetTopLevel(host) is AvaloniaWindow window)
        {
            Settle(window);
            window.Close();
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-174")] // App/Views/Composer/FollowUpEditor.swift:10-24
    public void The_title_is_exactly_Edit_follow_up()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var edit = h.Main.OnMain(() => new PromptEdit(System.Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
            h.Main.OnMain(() =>
            {
                var host = Build(h.Runtime, edit);
                Assert.Contains("Edit follow-up", Texts(host));
                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-062")] // App/Views/Composer/FollowUpEditor.swift:21-23
    public void Save_is_disabled_while_the_edit_holds_no_attachment_and_no_non_whitespace_text()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var edit = h.Main.OnMain(() => new PromptEdit(System.Guid.NewGuid(), "   ", ValueList<Attachment>.Empty));
            h.Main.OnMain(() =>
            {
                var host = Build(h.Runtime, edit);
                var save = ButtonNamed(host, "Save");
                Assert.False(save.IsEnabled);
                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-062")] // App/Views/Composer/FollowUpEditor.swift:21-23, :27-31
    public void Save_writes_the_text_back_to_the_queued_prompt_and_closes()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.EnqueueFollowUp("original text", []));
            var id = h.Main.OnMain(() => h.Runtime.FollowUps.Single().Id);
            var edit = h.Main.OnMain(() => new PromptEdit(id, "hello world", ValueList<Attachment>.Empty));
            h.Main.OnMain(() =>
            {
                h.Runtime.FollowUpEdit = edit;
                var host = Build(h.Runtime, edit);
                var save = ButtonNamed(host, "Save");
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(h.Runtime.FollowUpEdit);
                var stored = h.Runtime.FollowUps.Single(prompt => prompt.Id == id);
                Assert.Equal("hello world", stored.Text);
                Assert.Empty(stored.Attachments);
                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-174")] // App/Views/Composer/FollowUpEditor.swift:27-31
    public void Save_writes_the_attachments_back_with_the_text()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var attachment = new Attachment { Name = "a.txt", Path = @"C:
o-such-folder.txt", MimeType = "text/plain" };
            h.Main.OnMain(() => h.Runtime.EnqueueFollowUp("original text", []));
            var id = h.Main.OnMain(() => h.Runtime.FollowUps.Single().Id);
            var edit = h.Main.OnMain(() => new PromptEdit(id, "with a file", new ValueList<Attachment>([attachment])));
            h.Main.OnMain(() =>
            {
                h.Runtime.FollowUpEdit = edit;
                var host = Build(h.Runtime, edit);
                ButtonNamed(host, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var stored = h.Runtime.FollowUps.Single(prompt => prompt.Id == id);
                Assert.Equal("with a file", stored.Text);
                Assert.Equal([attachment], stored.Attachments);
                Close(host);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-061")] // App/Views/Composer/FollowUpEditor.swift:19-20, :33-35
    public void Cancel_discards_the_edit_and_closes_without_writing_it_back()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.EnqueueFollowUp("original text", []));
            var id = h.Main.OnMain(() => h.Runtime.FollowUps.Single().Id);
            var edit = h.Main.OnMain(() => new PromptEdit(id, "should not be saved", ValueList<Attachment>.Empty));
            h.Main.OnMain(() =>
            {
                h.Runtime.FollowUpEdit = edit;
                var host = Build(h.Runtime, edit);
                var cancel = ButtonNamed(host, "Cancel");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(h.Runtime.FollowUpEdit);
                Assert.Equal("original text", h.Runtime.FollowUps.Single(prompt => prompt.Id == id).Text);
                Close(host);
            });
        });
    }
}
