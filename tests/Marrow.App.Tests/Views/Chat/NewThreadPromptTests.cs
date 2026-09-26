using System.Linq;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/NewThreadPrompt.swift (B2 1fbf81e), :1-81.
[Collection("Headless")]
public sealed class NewThreadPromptTests(HeadlessSession headless)
{
    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static Control Build(AppModel model, System.Guid threadID, string? projectName)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new NewThreadPrompt(threadID, projectName);
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return host;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:39-40
    public void With_no_project_name_the_prompt_reads_the_plain_question()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, System.Guid.NewGuid(), null);
                Assert.Contains("What should we build?", Texts(host));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:23-38
    public void With_a_project_name_the_prompt_assembles_the_three_literal_parts()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, System.Guid.NewGuid(), "Marrow");
                var texts = Texts(host);
                Assert.Contains("What should we build in ", texts);
                Assert.Contains("Marrow", texts);
                Assert.Contains("?", texts);
                // The space before the name and no space before "?" (PR-CHAT-139's own citation).
                Assert.DoesNotContain("What should we build in Marrow?", texts);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // App/Views/Chat/NewThreadPrompt.swift:17-21
    public void The_app_icon_is_hidden_from_accessibility()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var host = Build(model, System.Guid.NewGuid(), null);
                var icon = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(host));
                Assert.Equal(72d, icon.Width);
                Assert.Equal(72d, icon.Height);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // App/Views/Chat/NewThreadPrompt.swift:25-34
    public void Clicking_the_project_name_toggles_the_picker_popover()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recorder = new PopoverRecorder();
            using var presenter = Presentation.Presenter.Override(recorder);
            Button button = null!;

            // Tracked<T>'s reactive update (PlainControls.Popover's own popup.Track) is posted via MainActor.Post
            // for a LATER turn, not applied inline, so the click and each assertion need their own OnMain call.
            harness.Main.OnMain(() =>
            {
                var host = Build(model, System.Guid.NewGuid(), "Marrow");
                button = LogicalQuery.SelfAndDescendants<Button>(host)
                    .Single(b => (ToolTip.GetTip(b) as string) == "Choose another project");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            harness.Main.OnMain(() =>
            {
                var (_, firstPresented) = Assert.Single(recorder.Calls);
                Assert.True(firstPresented);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, recorder.Calls.Count);
                Assert.False(recorder.Calls[1].Presented);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:57-72
    public void The_project_picker_lists_every_project_with_the_current_one_checked_plus_add_project()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directoryA = harness.NewFolder("a");
            var directoryB = harness.NewFolder("b");
            harness.Main.OnMain(() =>
            {
                // AppModel.AddProject derives the project's own Name from the folder's basename, not from the
                // throwaway Project passed in just to get a SwiftURL - read the names back from what it returns.
                var projectA = model.AddProject(new Project("Alpha", directoryA).Url);
                var projectB = model.AddProject(new Project("Beta", directoryB).Url);
                var thread = model.NewThread(projectA)!;

                var host = Build(model, thread.Id, projectA.Name);
                var prompt = LogicalQuery.SelfAndDescendants<NewThreadPrompt>(host).Single();
                var makeChoices = typeof(NewThreadPrompt).GetMethod("MakeProjectChoices", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var choices = (Control)makeChoices.Invoke(prompt, new object?[] { model, thread.Id, (System.Action)(() => { }) })!;

                Assert.Contains("Projects", Texts(choices));

                // PopoverItem's title is a MiddleTruncatingText (a TextBlock subclass whose displayed .Text is only
                // computed during layout, not set eagerly); its accessibility name and its own FullText are set
                // directly in the constructor, so those are what a test not attached to a real layout pass can read.
                var names = LogicalQuery.SelfAndDescendants<Button>(choices).Select(AutomationProperties.GetName).ToArray();
                Assert.Contains(projectA.Name, names);
                Assert.Contains(projectB.Name, names);
                Assert.Contains("Add project…", names); // U+2026 HORIZONTAL ELLIPSIS

                // :276-281 the leading checkmark is always present once isChecked is non-null, its opacity 1 or 0.
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(choices).ToArray();
                var alphaRow = rows.Single(row => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(row).Single().FullText == projectA.Name);
                var betaRow = rows.Single(row => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(row).Single().FullText == projectB.Name);
                var alphaCheck = LogicalQuery.SelfAndDescendants<SwiftImage>(alphaRow).Single(image => image.SymbolName == "checkmark");
                var betaCheck = LogicalQuery.SelfAndDescendants<SwiftImage>(betaRow).Single(image => image.SymbolName == "checkmark");
                Assert.Equal(1, alphaCheck.Opacity);
                Assert.Equal(0, betaCheck.Opacity);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:74-80
    public void Choosing_a_different_project_moves_the_thread_and_closes_the_popover()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directoryA = harness.NewFolder("a");
            var directoryB = harness.NewFolder("b");
            harness.Main.OnMain(() =>
            {
                var projectA = model.AddProject(new Project("Alpha", directoryA).Url);
                var projectB = model.AddProject(new Project("Beta", directoryB).Url);
                var thread = model.NewThread(projectA)!;

                Build(model, thread.Id, projectA.Name);
                var move = typeof(NewThreadPrompt).GetMethod("Move", BindingFlags.Static | BindingFlags.NonPublic)!;
                var closed = false;

                move.Invoke(null, new object?[] { model, thread.Id, projectB, (System.Action)(() => closed = true) });

                Assert.Equal(projectB.Id, model.Thread(thread.Id)!.ProjectID);
                Assert.True(closed);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:75 "does nothing when the thread is already in that project"
    public void Choosing_the_current_project_does_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directoryA = harness.NewFolder("a");
            harness.Main.OnMain(() =>
            {
                var projectA = model.AddProject(new Project("Alpha", directoryA).Url);
                var thread = model.NewThread(projectA)!;

                Build(model, thread.Id, projectA.Name);
                var move = typeof(NewThreadPrompt).GetMethod("Move", BindingFlags.Static | BindingFlags.NonPublic)!;
                var closed = false;

                move.Invoke(null, new object?[] { model, thread.Id, projectA, (System.Action)(() => closed = true) });

                Assert.Equal(projectA.Id, model.Thread(thread.Id)!.ProjectID);
                Assert.False(closed);
            });
        });
    }
}
