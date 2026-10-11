using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 4.2 hard denies and 4.3 guarded words; ED-NEW-pcctl-2 (legacy Marrow
// pcTools.ts:39-45): the tools go only to a chat the user is watching, and only when PC control is on.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-2")]
public sealed class PcPolicyTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", PcDenyCategory.Terminal)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", PcDenyCategory.Terminal)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\PowerShell.exe", PcDenyCategory.Terminal)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.21\WindowsTerminal.exe", PcDenyCategory.Terminal)]
    [InlineData(@"C:\Windows\System32\conhost.exe", PcDenyCategory.Terminal)]
    [InlineData(@"C:\Program Files\KeePassXC\KeePassXC.exe", PcDenyCategory.PasswordManager)]
    [InlineData(@"C:\Users\u\AppData\Local\1Password\app\8\1Password.exe", PcDenyCategory.PasswordManager)]
    [InlineData(@"C:\Windows\System32\CredentialUIBroker.exe", PcDenyCategory.PasswordManager)]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.SecHealthUI_8wekyb3d8bbwe\SecHealthUI.exe", PcDenyCategory.Security)]
    [InlineData(@"C:\Windows\System32\consent.exe", PcDenyCategory.System)]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.LockApp_cw5n1h2txyewy\LockApp.exe", PcDenyCategory.System)]
    [InlineData(@"C:\Users\u\AppData\Local\Marrow Code\current\Marrow.App.exe", PcDenyCategory.AgentApp)]
    [InlineData(@"C:\Users\u\AppData\Local\AnthropicClaude\claude.exe", PcDenyCategory.AgentApp)]
    public void Terminals_password_managers_security_apps_protected_screens_and_agent_apps_are_denied_by_exe(string path, PcDenyCategory category)
    {
        var verdict = PcPolicy.Classify(new PcAppIdentity(path));
        Assert.True(verdict.Denied);
        Assert.Equal(category, verdict.Category);
        Assert.False(string.IsNullOrEmpty(verdict.Reason));
    }

    [Theory]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", PcDenyCategory.Terminal)]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App", PcDenyCategory.AgentApp)]
    [InlineData("Microsoft.SecHealthUI_8wekyb3d8bbwe!SecHealthUI", PcDenyCategory.Security)]
    public void Packaged_apps_are_denied_by_aumid(string aumid, PcDenyCategory category)
    {
        var verdict = PcPolicy.Classify(new PcAppIdentity(@"C:\Program Files\WindowsApps\x\host.exe", aumid));
        Assert.Equal(category, verdict.Category);
    }

    [Fact]
    public void Ordinary_apps_are_allowed()
    {
        Assert.False(PcPolicy.Classify(new PcAppIdentity(@"C:\Windows\System32\notepad.exe")).Denied);
        Assert.False(PcPolicy.Classify(new PcAppIdentity(@"C:\Windows\System32\mspaint.exe", "Microsoft.Paint_8wekyb3d8bbwe!App")).Denied);
    }

    [Fact]
    public void The_user_can_deny_more_apps_by_exe_name_path_or_aumid()
    {
        var notepad = new PcAppIdentity(@"C:\Windows\System32\notepad.exe");
        Assert.Equal(PcDenyCategory.User, PcPolicy.Classify(notepad, ["NOTEPAD.EXE"]).Category);
        Assert.Equal(PcDenyCategory.User, PcPolicy.Classify(notepad, [@"c:\windows\system32\notepad.exe"]).Category);
        var paint = new PcAppIdentity(@"C:\x\paint.exe", "Microsoft.Paint_8wekyb3d8bbwe!App");
        Assert.Equal(PcDenyCategory.User, PcPolicy.Classify(paint, ["microsoft.paint_8wekyb3d8bbwe!app"]).Category);
        Assert.False(PcPolicy.Classify(notepad, ["", "  ", "wordpad.exe"]).Denied);
    }

    [Fact]
    public void The_run_dialog_is_recognised_as_a_shell()
    {
        Assert.True(PcPolicy.IsRunDialog("explorer.exe", "#32770", "Run"));
        Assert.False(PcPolicy.IsRunDialog("explorer.exe", "#32770", "Properties"));
        Assert.False(PcPolicy.IsRunDialog("notepad.exe", "#32770", "Run"));
    }

    [Fact]
    public void Elevated_high_integrity_ui_access_or_unreadable_tokens_are_refused()
    {
        Assert.False(PcPolicy.IsElevated(new PcProcessSecurity(false, false, false)));
        Assert.True(PcPolicy.IsElevated(new PcProcessSecurity(true, false, false)));
        Assert.True(PcPolicy.IsElevated(new PcProcessSecurity(false, true, false)));
        Assert.True(PcPolicy.IsElevated(new PcProcessSecurity(false, false, true)));
        Assert.True(PcPolicy.IsElevated(new PcProcessSecurity(false, false, false, Readable: false)));
    }

    [Fact]
    public void Text_is_refused_for_password_fields_only()
    {
        Assert.True(PcPolicy.RefusesText(new PcUiNode("1", "edit", "Password") { States = PcStates.Password }));
        Assert.False(PcPolicy.RefusesText(new PcUiNode("1", "edit", "User name")));
        Assert.False(PcPolicy.RefusesText(null));
    }

    [Theory]
    [InlineData("Send", "send")]
    [InlineData("Send now", "send")]
    [InlineData("DELETE", "delete")]
    [InlineData("Delete account", "delete account")]
    [InlineData("Sign  out", "sign out")]
    [InlineData("Pay $12.00", "pay")]
    [InlineData("Place order", "order")]
    [InlineData("Install update", "install")]
    [InlineData("Uninstall", "uninstall")]
    [InlineData("Publish post", "publish")]
    public void Guarded_words_are_found_as_whole_words_in_any_case(string name, string word)
    {
        Assert.Equal(word, PcPolicy.GuardedWord(name));
    }

    [Theory]
    [InlineData("Sender")]
    [InlineData("Postpone")]
    [InlineData("Payment history")]
    [InlineData("Ordered list")]
    [InlineData("Open")]
    [InlineData("")]
    [InlineData(null)]
    public void Words_that_only_contain_a_guarded_word_are_not_guarded(string? name)
    {
        Assert.Null(PcPolicy.GuardedWord(name));
    }

    [Fact]
    public void Tools_are_offered_only_when_pc_control_is_on_and_the_user_is_watching()
    {
        Assert.False(PcPolicy.OffersTools(PcSettings.Default, attended: true));
        var on = PcSettings.Default with { Enabled = true };
        Assert.True(PcPolicy.OffersTools(on, attended: true));
        Assert.False(PcPolicy.OffersTools(on, attended: false));
    }

    [Fact]
    public void Browsers_mail_chat_and_file_managers_carry_the_risk_warning()
    {
        foreach (var exe in new[] { "chrome.exe", "msedge.exe", "outlook.exe", "slack.exe", "explorer.exe" })
            Assert.True(PcPolicy.IsRisky(new PcAppIdentity(@"C:\x\" + exe)), exe);
        Assert.False(PcPolicy.IsRisky(new PcAppIdentity(@"C:\Windows\notepad.exe")));
    }
}
