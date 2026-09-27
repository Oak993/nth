using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewPart3ReviewTests(HeadlessSession headless)
{
    private static Marrow.App.Runtime.ThreadRuntime Runtime(AppModelHarness h, AppModel model, string directory) =>
        model.Runtime(model.NewThread(model.AddProject(new Project("P", directory).Url))!.Id);

    [Fact]
    [Trait("Req", "UI-GIT-032")] // App/Views/Chat/ChatView.swift:1544-1546,1603-1604
    [Trait("Req", "UI-GIT-033")] // App/Views/Chat/ChatView.swift:1547-1556
    [Trait("Req", "UI-GIT-034")] // App/Views/Chat/ChatView.swift:1557-1571
    public void Commit_layout_keeps_twenty_point_padding_and_a_noninteractive_top_leading_placeholder()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var sheet = new CommitSheet(model, Runtime(h, model, h.NewFolder("repo")), h.Scratch, new GitStatusModel(), () => { });
                Assert.Equal(560, sheet.Width);
                var outer = Assert.IsType<Border>(sheet.Child);
                Assert.Equal(new Thickness(20), outer.Padding);
                Assert.Equal(14, Assert.IsType<StackPanel>(outer.Child).Spacing);
                var heading = LogicalQuery.SelfAndDescendants<TextBlock>(sheet).Single(t => t.Text == "Commit changes");
                Assert.Equal(17, heading.FontSize); // ED-059 table title2.
                Assert.Equal(Avalonia.Media.FontWeight.SemiBold, heading.FontWeight);
                Assert.Equal(new Thickness(10), sheet.SummaryFrame.Padding);
                Assert.Equal(110, Assert.IsType<ScrollViewer>(sheet.SummaryFrame.Child).MaxHeight);
                var summary = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(sheet.SummaryFrame));
                Assert.Equal(11, summary.FontSize); // ED-059 subheadline.
                Assert.Equal(HorizontalAlignment.Stretch, summary.HorizontalAlignment);
                var placeholder = LogicalQuery.SelfAndDescendants<TextBlock>(sheet.MessageFrame).Single(t => t.Text == "Commit message");
                Assert.False(placeholder.IsHitTestVisible);
                Assert.Equal(new Thickness(13, 8), placeholder.Margin);
                Assert.Equal(HorizontalAlignment.Left, placeholder.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Top, placeholder.VerticalAlignment);
                Assert.Equal(130, sheet.MessageFrame.MinHeight);
                var editor = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sheet.MessageFrame));
                Assert.Equal(13, editor.FontSize); // ED-059 body.
                Assert.Equal(new Thickness(8), editor.Margin);
                sheet.Message = "Message";
                Assert.False(placeholder.IsVisible);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1572-1592
    [Trait("Req", "UI-GIT-036")] // App/Views/Chat/ChatView.swift:1541,1593-1594
    [Trait("Req", "UI-GIT-037")] // App/Views/Chat/ChatView.swift:1596-1597
    [Trait("Req", "UI-GIT-038")] // App/Views/Chat/ChatView.swift:1598-1600
    public void The_action_row_has_drawable_glass_controls_a_flexible_gap_and_a_cancel_role()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                var dismissed = 0;
                var sheet = new CommitSheet(model, Runtime(h, model, h.NewFolder("repo")), h.Scratch, new GitStatusModel(), () => dismissed++);
                Assert.False(sheet.PushesAfterCommit);
                Assert.False(sheet.WriteForMeButton.IsEnabled);
                Assert.Equal("Turn on text generation in Settings > Source control", ToolTip.GetTip(sheet.WriteForMeButton));
                Assert.All(LogicalQuery.SelfAndDescendants<Button>(sheet), b => Assert.NotNull(b.Template)); // R-VIEW-1: no shared theme.
                Assert.NotNull(Assert.Single(LogicalQuery.SelfAndDescendants<CheckBox>(sheet)).Template);
                Assert.NotNull(Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sheet)).Template);
                var cancel = ChatGitFixtures.Button(sheet, "Cancel");
                Assert.True(cancel.IsCancel);
                Assert.Contains(LogicalQuery.SelfAndDescendants<Grid>(sheet), grid => grid.ColumnDefinitions.Any(c => c.Width.IsStar)); // :1595 Spacer.
                Assert.NotNull(SwiftGlass.GetEffect(sheet.CommitButton)); // ED-180 metadata follows the button's template layer.
                ChatGitFixtures.Click(cancel);
                Assert.Equal(1, dismissed);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-033")] // App/Views/Chat/ChatView.swift:1547,1605-1609
    public void Loading_waits_for_appearance_and_restarts_after_the_sheet_is_shown_again()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            CommitSheet sheet = null!;
            string directory = null!;
            Avalonia.Controls.Window window = null!;
            h.Main.OnMain(() =>
            {
                directory = ChatGitFixtures.Init(h.NewFolder("repo"));
                File.WriteAllText(Path.Combine(directory, "first.txt"), "one");
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                sheet = new CommitSheet(model, Runtime(h, model, directory), directory, new GitStatusModel(), () => { });
            });
            h.Main.RunUntilIdle();
            Assert.Equal("", sheet.Summary);
            h.Main.OnMain(() =>
            {
                // A lifecycle test does not require the Inter glyph cache shared by other headless windows.
                foreach (var text in LogicalQuery.SelfAndDescendants<TextBlock>(sheet)) text.FontFamily = Avalonia.Media.FontFamily.Default;
                window = new Avalonia.Controls.Window { Content = sheet }; window.Show();
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.Summary.Contains("first.txt", StringComparison.Ordinal)));
            h.Main.OnMain(() => { window.Content = null; File.WriteAllText(Path.Combine(directory, "second.txt"), "two"); window.Content = sheet; });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => sheet.Summary.Contains("second.txt", StringComparison.Ordinal)));
            h.Main.OnMain(() => window.Close());
            h.Main.RunUntilIdle();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-GIT-038")] // App/Views/Chat/ChatView.swift:1600,1633-1649
    [Trait("Req", "UI-GIT-060")] // App/Views/Chat/ChatView.swift:1645
    public void Commit_disables_while_running_trims_text_and_dismisses_only_after_success(bool pushes)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            CommitSheet sheet = null!;
            var git = new GitStatusModel();
            string directory = null!;
            Task work = null!;
            var dismissed = 0;
            bool wasCommitting = false, wasEnabled = true;
            string? activity = null;
            h.Main.OnMain(() =>
            {
                directory = ChatGitFixtures.Init(h.NewFolder("repo"));
                File.WriteAllText(Path.Combine(directory, "change.txt"), "payload");
                model.Settings.TextGeneration = TextGenerationChoice.Off;
                sheet = new CommitSheet(model, Runtime(h, model, directory), directory, git, () => dismissed++);
                sheet.Message = " \n Commit subject \n ";
                sheet.PushesAfterCommit = pushes; // no remote: optional push must fail, after the local commit.
                work = ChatGitFixtures.Invoke<Task>(sheet, "Commit", model, directory, git, (Action)(() => dismissed++));
                wasCommitting = sheet.IsCommitting;
                wasEnabled = sheet.CommitButton.IsEnabled;
                activity = git.Activity;
            });
            h.Main.Loop.Run(() => work);
            h.Main.OnMain(() =>
            {
                Assert.True(wasCommitting);
                Assert.False(wasEnabled);
                Assert.Equal("Committing", activity);
                Assert.False(sheet.IsCommitting);
                Assert.True(sheet.CommitButton.IsEnabled);
                Assert.Equal("Commit subject\n", ChatGitFixtures.Run(directory, "log", "-1", "--pretty=%s").Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Equal(pushes ? 0 : 1, dismissed);
                if (pushes) Assert.Equal("Commit failed", model.Alert!.Title);
                else Assert.Null(model.Alert);
                Assert.Equal(0, git.Status!.ChangedFiles);
                Assert.Null(git.Activity);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-043")] // App/Views/Chat/ChatView.swift:1398-1408
    [Trait("Req", "PR-GIT-130")] // App/Views/Chat/ChatView.swift:1398-1408
    public void New_branch_trims_and_replaces_spaces_then_refreshes_the_branch()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var git = new GitStatusModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            h.Main.OnMain(() => ChatGitFixtures.InvokeStatic(typeof(BranchMenu), "CreateBranch", model, git, directory, "  new branch  "));
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => git.Status?.Branch == "new-branch"));
            Assert.Null(model.Alert);
            h.Main.OnMain(() => ChatGitFixtures.InvokeStatic(typeof(BranchMenu), "CreateBranch", model, git, directory, " \n "));
            h.Main.RunUntilIdle();
            Assert.Equal("new-branch", git.Status!.Branch);
        });
    }

    [Theory]
    [InlineData("SwitchBranch", "Could not switch branch")]
    [InlineData("CreateBranch", "Could not create branch")]
    [InlineData("Push", "Push failed")]
    [InlineData("OpenPullRequest", "Could not open a pull request")]
    [Trait("Req", "UI-GIT-060")] // App/Views/Chat/ChatView.swift:1392,1404,1499,1521
    [Trait("Req", "PR-GIT-129")] // App/Views/Chat/ChatView.swift:1388-1396
    [Trait("Req", "PR-GIT-130")] // App/Views/Chat/ChatView.swift:1398-1408
    [Trait("Req", "PR-GIT-131")] // App/Views/Chat/ChatView.swift:1495-1527
    public void Git_failure_paths_alert_and_refresh_without_dismissing_the_error(string operation, string title)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var git = new GitStatusModel();
            h.Main.OnMain(() =>
            {
                var branch = operation is "SwitchBranch" or "CreateBranch";
                object?[] args = branch ? [model, git, directory, "invalid:name"] : [model, git, directory];
                ChatGitFixtures.InvokeStatic(branch ? typeof(BranchMenu) : typeof(GitActionsMenu), operation, args);
            });
            h.Main.Loop.Run(() => ChatGitFixtures.Until(() => model.Alert is not null && git.Status is not null));
            Assert.Equal(title, model.Alert!.Title);
            Assert.False(string.IsNullOrEmpty(model.Alert.Message));
            Assert.Null(git.Activity);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-031")] // App/Views/Chat/ChatView.swift:1484-1487
    public void Open_repository_uses_the_refreshed_remote_url()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            ChatGitFixtures.Run(directory, "remote", "add", "origin", "https://github.com/test/offline.git"); // never fetched or pushed.
            var git = new GitStatusModel();
            h.Main.Loop.Run(() => git.Refresh(directory));
            h.Main.OnMain(() =>
            {
                var menu = GitActionsMenu.BuildMenu(model, Runtime(h, model, directory), directory, git, 0);
                Assert.Contains(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(menu), t => t.FullText == "Open repository"); // pre-layout source label.
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverDivider>(menu).Count());
                Assert.Equal("https://github.com/test/offline", git.RemoteURL!.AbsoluteString);
            });
        });
    }
}
