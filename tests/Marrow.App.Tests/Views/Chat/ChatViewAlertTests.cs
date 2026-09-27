using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewAlertTests(HeadlessSession headless)
{
    private static void Click(Control menu, string title)
    {
        var row = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Single(item =>
            Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText == title);
        ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-CHAT-178")] // App/Views/Chat/ChatView.swift:1225-1237
    [Trait("Req", "UI-CHAT-078")] // App/Views/Chat/ChatView.swift:1231-1237
    [Trait("Decision", "ED-216")] // shared Return default and Escape cancel role
    public void Rename_uses_the_shared_bound_field_and_Return_or_Escape_changes_only_the_target_thread(bool cancels)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var dialog = new ChatAlertFixture();
            using var shared = AlertPresentation.Presenter.Override(dialog);
            var legacy = new RecordingPromptPresenter();
            using var oldRoute = PromptPresentation.Presenter.Override(legacy);
            Guid id = default, otherID = default;
            try
            {
                h.Main.OnMain(() =>
                {
                    var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                    id = model.NewThread(project)!.Id;
                    otherID = model.NewThread(project)!.Id;
                    model.Rename(id, "Current title");
                    model.Rename(otherID, "Other title");
                    Click(ChatChromeTitle.BuildMenu(model, model.Runtime(id), new Button(), "Current title"), "Rename…");
                });
                h.Main.RunUntilIdle();
                var request = Assert.Single(dialog.Shown);
                Assert.Empty(legacy.Shown);
                Assert.Equal("Rename thread", request.Title);
                Assert.Null(request.Message);
                Assert.Equal(AlertKind.Alert, request.Kind);
                var field = Assert.Single(request.TextFields!);
                Assert.Equal("Title", field.Label);
                Assert.Equal("Current title", field.Value.Value);
                Assert.Equal(["Rename", "Cancel"], request.Buttons.Select(button => button.Title));
                Assert.Equal([AlertButtonRole.None, AlertButtonRole.Cancel], request.Buttons.Select(button => button.Role));
                Assert.Same(request.Buttons[0], request.ReturnDefault);
                var window = Assert.Single(dialog.Windows);
                Assert.False(window.IsVisible);
                h.Main.OnMain(() =>
                {
                    var content = Assert.IsType<StackPanel>(window.Content);
                    var input = Assert.Single(content.Children.OfType<TextBox>());
                    Assert.Equal("Current title", input.Text);
                    Assert.Equal("Title", input.PlaceholderText);
                    var buttons = Assert.Single(content.Children.OfType<StackPanel>()).Children.Cast<Button>().ToArray();
                    Assert.True(buttons[0].IsDefault);
                    Assert.True(buttons[1].IsCancel);
                    input.Text = "Changed title";
                    Assert.Equal("Changed title", field.Value.Value);
                    Assert.False(dialog.Completions[0].IsCompleted);
                    dialog.Key(cancels ? Key.Escape : Key.Enter);
                });
                h.Main.RunUntilIdle();
                Assert.True(dialog.Completions[0].IsCompletedSuccessfully);
                Assert.Equal(cancels ? "Current title" : "Changed title", model.Thread(id)!.Title);
                Assert.Equal("Other title", model.Thread(otherID)!.Title);
            }
            finally
            {
                h.Main.OnMain(() => { dialog.CancelAll(); for (var index = 0; index < legacy.Shown.Count; index++) legacy.Dismiss(index, 1, ""); });
                h.Main.RunUntilIdle();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-GIT-043")] // App/Views/Chat/ChatView.swift:1371-1374,1381-1385,1398-1408
    [Trait("Req", "PR-GIT-130")] // App/Views/Chat/ChatView.swift:1398-1408
    [Trait("Decision", "ED-216")] // shared Return default and Escape cancel role
    public void New_branch_uses_the_shared_empty_field_and_Return_creates_the_trimmed_branch_while_Escape_keeps_main(bool cancels)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var git = new GitStatusModel();
            var dialog = new ChatAlertFixture();
            using var shared = AlertPresentation.Presenter.Override(dialog);
            var legacy = new RecordingPromptPresenter();
            using var oldRoute = PromptPresentation.Presenter.Override(legacy);
            try
            {
                h.Main.Loop.Run(() => git.Refresh(directory));
                h.Main.OnMain(() =>
                {
                    var project = model.AddProject(new Project("P", directory).Url);
                    var runtime = model.Runtime(model.NewThread(project)!.Id);
                    Click(BranchMenu.BuildMenu(model, runtime, directory, git), "New branch…");
                });
                h.Main.RunUntilIdle();
                var request = Assert.Single(dialog.Shown);
                Assert.Empty(legacy.Shown);
                Assert.Equal("New branch", request.Title);
                Assert.Null(request.Message);
                Assert.Equal(AlertKind.Alert, request.Kind);
                var field = Assert.Single(request.TextFields!);
                Assert.Equal("Branch name", field.Label);
                Assert.Equal("", field.Value.Value);
                Assert.Equal(["Create", "Cancel"], request.Buttons.Select(button => button.Title));
                Assert.Equal([AlertButtonRole.None, AlertButtonRole.Cancel], request.Buttons.Select(button => button.Role));
                Assert.Same(request.Buttons[0], request.ReturnDefault);
                var window = Assert.Single(dialog.Windows);
                Assert.False(window.IsVisible);
                h.Main.OnMain(() =>
                {
                    var content = Assert.IsType<StackPanel>(window.Content);
                    var input = Assert.Single(content.Children.OfType<TextBox>());
                    Assert.Equal("", input.Text);
                    Assert.Equal("Branch name", input.PlaceholderText);
                    var buttons = Assert.Single(content.Children.OfType<StackPanel>()).Children.Cast<Button>().ToArray();
                    Assert.True(buttons[0].IsDefault);
                    Assert.True(buttons[1].IsCancel);
                    input.Text = "  feature branch  "; // :1399 trimmed, internal spaces replaced with hyphens.
                    dialog.Key(cancels ? Key.Escape : Key.Enter);
                });
                h.Main.RunUntilIdle();
                if (!cancels) h.Main.Loop.Run(() => ChatGitFixtures.Until(() => git.Status?.Branch == "feature-branch"));
                Assert.True(dialog.Completions[0].IsCompletedSuccessfully);
                Assert.Equal(cancels ? "main" : "feature-branch", git.Status!.Branch);
                Assert.Equal(cancels ? "main" : "feature-branch", ChatGitFixtures.Run(directory, "branch", "--show-current").Trim());
                Assert.Equal(cancels ? "" : "feature-branch", ChatGitFixtures.Run(directory, "branch", "--list", "feature-branch").Trim().TrimStart('*').Trim());
                Assert.Null(model.Alert);
            }
            finally
            {
                h.Main.OnMain(() => { dialog.CancelAll(); for (var index = 0; index < legacy.Shown.Count; index++) legacy.Dismiss(index, 1, ""); });
                h.Main.RunUntilIdle();
            }
        });
    }
}
