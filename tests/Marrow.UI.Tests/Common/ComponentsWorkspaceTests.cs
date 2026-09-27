using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

[Collection("Headless")]
public sealed class ComponentsWorkspaceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-181")] // DroppyCode/UI/Common/Components.swift:109-117
    [Trait("Req", "PR-CHAT-182")] // DroppyCode/UI/Common/Components.swift:137-147
    [Trait("Decision", "ED-087")]
    public void Installed_editors_are_resolved_once_registry_then_PATH_Ghostty_absent_and_Open_re_resolves()
    {
        using var runtime = new TestRuntime();
        using var fake = new WorkspaceTestPlatform();
        fake.Registry[("Code.exe", false)] = @"C:\Apps\Code.exe"; fake.Existing.Add(@"C:\Apps\Code.exe");
        fake.Registry[("Cursor.exe", true)] = @"C:\Apps\Cursor.exe"; fake.Existing.Add(@"C:\Apps\Cursor.exe");
        fake.Path["zed"] = @"C:\Apps\Zed.exe";
        fake.Path["wt.exe"] = @"C:\Apps\wt.exe";
        Assert.Equal(new[] { "Visual Studio Code", "Cursor", "Zed", "Terminal" }, Workspace.InstalledEditors.Select(e => e.Name));
        var reads = fake.RegistryReads.Count + fake.PathReads.Count;
        Assert.DoesNotContain("ghostty", fake.PathReads);
        Assert.Same(Workspace.InstalledEditors, Workspace.InstalledEditors);
        Assert.Equal(reads, fake.RegistryReads.Count + fake.PathReads.Count);
        var folder = @"C:\a folder\project";
        Workspace.Open(folder, Workspace.Editors[0]);
        Assert.Equal(new[] { folder }, Assert.Single(fake.Launches).Arguments);
        fake.Existing.Clear();
        Workspace.Open(folder, Workspace.Editors[0]); // Uninstalled: no launch, stale menu unchanged.
        Assert.Single(fake.Launches);
        Workspace.Open(folder, Workspace.Editors[3]);
        Assert.Equal(new[] { "-d", folder }, fake.Launches[1].Arguments);
        Assert.Equal(4, Workspace.InstalledEditors.Count);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-182")] // DroppyCode/UI/Common/Components.swift:149-167
    public void Icons_are_cached_per_identity_at_16_and_reveal_reuses_ShellWorkspace()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var fake = new WorkspaceTestPlatform();
            using var bitmap = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96));
            fake.Image = bitmap;
            fake.Path["code"] = @"C:\Apps\Code.exe";
            var image = Assert.IsAssignableFrom<IImage>(Workspace.Icon(Workspace.Editors[0]));
            Assert.Equal(new Size(16, 16), image.Size);
            Assert.Same(image, Workspace.Icon(Workspace.Editors[0] with { Name = "Other display name" }));
            Assert.Single(fake.IconReads);
            var explorer = Assert.IsAssignableFrom<IImage>(Workspace.FileExplorerIcon);
            Assert.Same(explorer, Workspace.FileExplorerIcon);
            Assert.Equal(2, fake.IconReads.Count);
            Assert.Equal(@"C:\Windows\explorer.exe", fake.IconReads[1]);
            var original = ShellWorkspace.RevealInFileExplorer;
            string? revealed = null;
            try { ShellWorkspace.RevealInFileExplorer = path => revealed = path; fake.Reveal = path => ShellWorkspace.RevealInFileExplorer(path); Workspace.RevealInFileExplorer(@"C:\a folder\file.txt"); Assert.Equal(@"C:\a folder\file.txt", revealed); }
            finally { ShellWorkspace.RevealInFileExplorer = original; }
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-233")] // DroppyCode/UI/Common/Components.swift:119-135
    [Trait("Decision", "ED-089")]
    public void Login_uses_a_new_Terminal_window_PowerShell_7_home_and_the_verbatim_command()
    {
        using var runtime = new TestRuntime();
        using var fake = new WorkspaceTestPlatform();
        fake.Path["wt.exe"] = @"C:\Apps\wt.exe";
        fake.Path["pwsh.exe"] = @"C:\Apps\pwsh.exe";
        Workspace.OpenTerminal("cursor-agent login");
        var launch = Assert.Single(fake.Launches);
        Assert.Equal(@"C:\Apps\wt.exe", launch.Executable);
        Assert.Equal(new[] { "-w", "new", "new-tab", "-d", @"C:\fixture home", @"C:\Apps\pwsh.exe", "-NoExit", "-Command", "cursor-agent login" }, launch.Arguments);
        Assert.Equal(@"C:\fixture home", launch.Directory);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-233")] // DroppyCode/UI/Common/Components.swift:119-135
    [Trait("Decision", "ED-089")]
    public void Login_falls_back_to_Windows_PowerShell_in_its_own_console_and_preserves_each_command()
    {
        using var runtime = new TestRuntime();
        using var fake = new WorkspaceTestPlatform();
        fake.Path["powershell.exe"] = @"C:\Windows\powershell.exe";
        Workspace.OpenTerminal("claude auth login");
        Workspace.OpenTerminal("codex login");
        Assert.Equal(2, fake.Launches.Count);
        Assert.All(fake.Launches, l => { Assert.Equal(@"C:\Windows\powershell.exe", l.Executable); Assert.True(l.Console); Assert.Equal(@"C:\fixture home", l.Directory); });
        Assert.Equal(new[] { "-NoExit", "-Command", "claude auth login" }, fake.Launches[0].Arguments);
        Assert.Equal(new[] { "-NoExit", "-Command", "codex login" }, fake.Launches[1].Arguments);
    }
}
