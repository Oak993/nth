using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewFollowup2Tests(HeadlessSession headless)
{
    private static PopoverItem[] Rows(Control menu) => LogicalQuery.SelfAndDescendants<PopoverItem>(menu).ToArray();
    private static string Title(PopoverItem item) => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText;
    private static void Click(PopoverItem item) => ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));

    [Fact]
    [Trait("Req", "PR-CHAT-180")] // App/Views/Chat/ChatView.swift:1416-1429
    [Trait("Req", "UI-CHAT-080")] // App/Views/Chat/ChatView.swift:1416-1429; UI/Common/Components.swift:109-117,143-163; OD-0012 G4 / ED-087.
    public void Installed_editor_rows_keep_catalogue_order_icons_and_open_the_exact_directory_through_the_workspace()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var main = new TestRuntime();
            using var workspace = new ChatWorkspaceFixture();
            workspace.Icon = new DrawingImage(new GeometryDrawing { Brush = Brushes.Red, Geometry = new RectangleGeometry(new Rect(0, 0, 32, 32)) });
            workspace.Paths.Add("code", @"C:\fixture\Code.exe");
            workspace.Paths.Add("cursor", @"C:\fixture\Cursor.exe");
            workspace.Paths.Add("zed", @"C:\fixture\Zed.exe");
            workspace.Paths.Add("wt.exe", @"C:\fixture\wt.exe");
            const string directory = @"C:\work with spaces\project";
            PopoverItem[] rows = [];
            main.OnMain(() =>
            {
                var menu = OpenInMenu.BuildMenu(directory);
                rows = Rows(menu);
                Assert.Equal(["File Explorer", "Visual Studio Code", "Cursor", "Zed", "Terminal", "Copy path"], rows.Select(Title));
                Assert.Same(workspace.Icon, Assert.Single(LogicalQuery.SelfAndDescendants<Image>(rows[0])).Source);
                foreach (var row in rows.Skip(1).Take(4))
                    Assert.Equal(new Size(16, 16), Assert.Single(LogicalQuery.SelfAndDescendants<Image>(row)).Source!.Size); // Components.swift:158
                Assert.Equal([ChatWorkspaceFixture.Explorer, @"C:\fixture\Code.exe", @"C:\fixture\Cursor.exe", @"C:\fixture\Zed.exe", @"C:\fixture\wt.exe"], workspace.IconReads);
                Assert.Single(LogicalQuery.SelfAndDescendants<PopoverDivider>(menu));
            });
            main.OnMain(() => Click(rows[0]));
            Assert.Equal([directory], workspace.Reveals);
            main.OnMain(() => { foreach (var row in rows.Skip(1).Take(4)) Click(row); });
                Assert.Equal([@"C:\fixture\Code.exe", @"C:\fixture\Cursor.exe", @"C:\fixture\Zed.exe", @"C:\fixture\wt.exe"], workspace.Launches.Select(call => call.Executable));
                Assert.Equal(new[] { directory }, workspace.Launches[0].Arguments);
                Assert.Equal(new[] { directory }, workspace.Launches[1].Arguments);
                Assert.Equal(new[] { directory }, workspace.Launches[2].Arguments);
                Assert.Equal(new[] { "-d", directory }, workspace.Launches[3].Arguments); // Components' ED-087 Windows Terminal adaptation.
                Assert.All(workspace.Launches, call => { Assert.Null(call.Directory); Assert.False(call.Console); });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-180")] // App/Views/Chat/ChatView.swift:1420-1424
    [Trait("Req", "UI-CHAT-080")] // App/Views/Chat/ChatView.swift:1420-1424
    public void A_missing_editor_icon_uses_the_source_code_symbol_and_uninstalled_editors_have_no_rows()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var main = new TestRuntime();
            using var workspace = new ChatWorkspaceFixture();
            workspace.Paths.Add("cursor", @"C:\fixture\Cursor.exe");
            PopoverItem[] rows = [];
            main.OnMain(() =>
            {
                rows = Rows(OpenInMenu.BuildMenu(@"C:\project"));
                Assert.Equal(["File Explorer", "Cursor", "Copy path"], rows.Select(Title));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Image>(rows[1]));
                Assert.Equal("chevron.left.forwardslash.chevron.right", Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(rows[1])).SymbolName);
            });
            main.OnMain(() => Click(rows[1]));
            Assert.Equal(@"C:\fixture\Cursor.exe", Assert.Single(workspace.Launches).Executable);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-180")] // App/Views/Chat/ChatView.swift:1416-1429
    [Trait("Req", "UI-CHAT-080")] // App/Views/Chat/ChatView.swift:1416-1429
    public void With_no_installed_editors_the_original_reveal_divider_and_copy_rows_remain()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var main = new TestRuntime();
            using var workspace = new ChatWorkspaceFixture();
            Control menu = null!;
            main.OnMain(() =>
            {
                menu = OpenInMenu.BuildMenu(@"C:\project");
                Assert.Equal(["File Explorer", "Copy path"], Rows(menu).Select(Title));
                Assert.Single(LogicalQuery.SelfAndDescendants<PopoverDivider>(menu));
            });
            main.OnMain(() => Click(Rows(menu)[0]));
            Assert.Equal([@"C:\project"], workspace.Reveals);
            Assert.Empty(workspace.Launches);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-035")] // App/Views/Chat/ChatView.swift:1575,1591-1592; Services/Providers/ProviderRegistry.swift:358-377,788-790,806-809
    public void Real_provider_refresh_disables_on_sign_out_and_reenables_on_sign_in_without_replacing_the_sheet()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
            var project = new Project("P", directory);
            h.WriteLibrary([project], []);
            var model = h.MakeModel();
            var folder = h.NewFolder("refresh-stand-in");
            var executable = ChatProviderFixture.Create(folder, "{\"loggedIn\":false,\"version\":\"1.7.0\"}");
            using var cli = ProviderRegistry.SetCLIUpdaterExecutableForTesting(provider => provider == ProviderKind.Claude
                ? SwiftURL.Create("marrow-native-path:" + executable.Replace('\\', '/')) : null);
            CommitSheet sheet = null!;
            Avalonia.Controls.Window window = null!;
            try
            {
                h.Main.OnMain(() =>
                {
                    model.Settings.SetBinaryPath(executable, ProviderKind.Claude);
                    model.Settings.TextGeneration = TextGenerationChoice.Claude;
                    var runtime = model.Runtime(model.NewThread(project)!.Id);
                    sheet = new CommitSheet(model, runtime, directory, new GitStatusModel(), () => { }) { IsVisible = false };
                    sheet.Message = "Typed"; // :1608 do not auto-generate on appearance.
                    window = new Avalonia.Controls.Window { Content = sheet }; window.Show();
                    Assert.True(sheet.WriteForMeButton.IsEnabled);
                });
                h.Main.Loop.Run(() => model.Providers.Refresh(ProviderKind.Claude));
                h.Main.RunUntilIdle();
                Assert.IsType<ProviderStatus.Auth.SignedOut>(h.Main.OnMain(() => model.Providers.Status(ProviderKind.Claude).ProviderStatusAuth));
                Assert.False(sheet.WriteForMeButton.IsEnabled);
                Assert.Equal("Turn on text generation in Settings > Source control", ToolTip.GetTip(sheet.WriteForMeButton));
                File.WriteAllText(Path.Combine(folder, "stdout.txt"), "{\"loggedIn\":true,\"version\":\"1.7.0\"}");
                h.Main.Loop.Run(() => model.Providers.Refresh(ProviderKind.Claude));
                h.Main.RunUntilIdle();
                Assert.IsType<ProviderStatus.Auth.SignedIn>(h.Main.OnMain(() => model.Providers.Status(ProviderKind.Claude).ProviderStatusAuth));
                Assert.True(sheet.WriteForMeButton.IsEnabled);
                Assert.Equal("Write a commit message from these changes", ToolTip.GetTip(sheet.WriteForMeButton));
                Assert.Same(sheet, window.Content);
                Assert.Equal("Typed", sheet.Message);
                Assert.Equal("--version\0", File.ReadAllText(Path.Combine(folder, "call-0.args")));
                Assert.Equal("auth\0status\0", File.ReadAllText(Path.Combine(folder, "call-1.args")));
                Assert.Equal("--version\0", File.ReadAllText(Path.Combine(folder, "call-2.args")));
                Assert.Equal("auth\0status\0", File.ReadAllText(Path.Combine(folder, "call-3.args")));
            }
            finally
            {
                h.Main.OnMain(() => window?.Close());
                h.Main.RunUntilIdle();
            }
        });
    }
}
