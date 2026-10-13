using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 2, 12.1, 12.3 point 4; ED-NEW-dextra-as19-3): which recorded steps ask the owner. Code decides it,
// from the label of the control (English, Hindi and Urdu), the command typed into a terminal, a shortcut, a password field or a window
// nth does not record. Asking too often is the safe side; asking too little is the failure.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-3")]
public sealed class TeachGuardTests
{
    [Theory]
    [InlineData("Send", ActionClass.Outward)]
    [InlineData("&Send now", ActionClass.Outward)]
    [InlineData("Reply all", ActionClass.Outward)]
    [InlineData("Post", ActionClass.Outward)]
    [InlineData("Pay now", ActionClass.Money)]
    [InlineData("Place your order", ActionClass.Money)]
    [InlineData("Buy now", ActionClass.Money)]
    [InlineData("Delete", ActionClass.Destructive)]
    [InlineData("Empty Recycle Bin", ActionClass.Destructive)]
    [InlineData("Uninstall", ActionClass.Destructive)]
    [InlineData("Install", ActionClass.Install)]
    [InlineData("Book now", ActionClass.Commitment)]
    [InlineData("I agree", ActionClass.Commitment)]
    [InlineData("Accept all", ActionClass.Commitment)]
    [InlineData("Sign in", ActionClass.Login)]
    public void A_label_that_names_a_guarded_action_asks_in_the_class_of_that_action(string label, ActionClass expected)
    {
        var guard = TeachGuards.ForLabel(label);
        Assert.NotNull(guard);
        Assert.Equal(expected, guard!.Class);
    }

    [Theory]
    [InlineData("भेजें", ActionClass.Outward)]
    [InlineData("पोस्ट करें", ActionClass.Outward)]
    [InlineData("भुगतान करें", ActionClass.Money)]
    [InlineData("अभी खरीदें", ActionClass.Money)]
    [InlineData("हटाएं", ActionClass.Destructive)]
    [InlineData("इंस्टॉल करें", ActionClass.Install)]
    [InlineData("बुक करें", ActionClass.Commitment)]
    [InlineData("لاگ ان", ActionClass.Login)]
    [InlineData("بھیجیں", ActionClass.Outward)]
    [InlineData("ادائیگی کریں", ActionClass.Money)]
    [InlineData("ابھی خریدیں", ActionClass.Money)]
    [InlineData("حذف کریں", ActionClass.Destructive)]
    [InlineData("انسٹال کریں", ActionClass.Install)]
    [InlineData("بک کریں", ActionClass.Commitment)]
    public void Hindi_and_Urdu_labels_ask_the_same_way(string label, ActionClass expected)
    {
        var guard = TeachGuards.ForLabel(label);
        Assert.NotNull(guard);
        Assert.Equal(expected, guard!.Class);
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Open")]
    [InlineData("Bookmarks")]
    [InlineData("Paycheck calendar")]
    [InlineData("Sender list")]
    [InlineData("Format Cells")]
    [InlineData("")]
    [InlineData(null)]
    public void A_label_that_only_looks_like_one_does_not_ask(string? label) => Assert.Null(TeachGuards.ForLabel(label));

    [Fact]
    public void A_label_naming_two_actions_takes_the_stricter_class()
    {
        Assert.Equal(ActionClass.Money, TeachGuards.ForLabel("Pay and send")!.Class);
        Assert.Equal(ActionClass.Money, TeachGuards.ForLabel("Send money")!.Class);
    }

    [Fact]
    public void A_bare_yes_after_a_guarded_step_takes_its_guard_and_a_bare_yes_alone_does_not()
    {
        var delete = TeachGuards.ForEvent(TeachSamples.Click(1, 5, TeachSamples.Button("Delete")));
        Assert.Equal(ActionClass.Destructive, delete!.Class);
        var yes = TeachSamples.Click(2, 6, TeachSamples.Button("Yes"));
        var inherited = TeachGuards.ForEvent(yes, delete);
        Assert.Equal(ActionClass.Destructive, inherited!.Class);
        Assert.StartsWith("It confirms the step before", inherited.Reason, StringComparison.Ordinal);
        Assert.Null(TeachGuards.ForEvent(yes));
        Assert.True(TeachGuards.IsConfirmLabel("हाँ"));
        Assert.True(TeachGuards.IsConfirmLabel("ہاں"));
        Assert.False(TeachGuards.IsConfirmLabel("Save as"));
    }

    [Theory]
    [InlineData("git push origin main", true)]
    [InlineData("Remove-Item -Recurse C:\\Users\\sam\\Documents\\old", true)]
    [InlineData("npm publish", true)]
    [InlineData("winget install foo", true)]
    [InlineData("some-unknown-tool --go", true)]
    [InlineData("echo hello", false)]
    [InlineData("git status", false)]
    public void A_command_typed_into_a_terminal_asks_unless_the_classifier_lets_it_run_alone(string command, bool asks)
    {
        var typed = TeachSamples.Type(1, 3, new TeachElement("powershell", "Windows PowerShell", "edit", "Terminal"), command);
        Assert.Equal(asks, TeachGuards.ForEvent(typed) is not null);
    }

    [Fact]
    public void The_same_text_typed_into_an_ordinary_field_is_not_a_command()
    {
        Assert.Null(TeachGuards.ForEvent(TeachSamples.Type(1, 3, TeachSamples.Field("Subject"), "git push origin main")));
    }

    [Fact]
    public void A_password_or_secret_is_a_step_the_owner_does_himself()
    {
        var secret = TeachGuards.ForEvent(TeachSamples.Type(1, 3, TeachSamples.Field("Password", password: true), null, 12, secret: true));
        Assert.Equal(ActionClass.HardDeny, secret!.Class);
        Assert.True(secret.OwnerDoesIt);
        var flagged = TeachSamples.Type(2, 4, TeachSamples.Field("Password", password: true), "hunter2");
        Assert.True(TeachGuards.ForEvent(flagged)!.OwnerDoesIt);
    }

    [Theory]
    [InlineData("1Password", true)]
    [InlineData("Bitwarden.exe", true)]
    [InlineData("consent", true)]
    [InlineData("Marrow.App", true)]
    [InlineData("outlook", false)]
    [InlineData("", false)]
    public void A_password_manager_the_prompt_for_admin_rights_and_nth_itself_are_private_apps(string app, bool isPrivate) =>
        Assert.Equal(isPrivate, TeachGuards.IsPrivateApp(app));

    [Theory]
    [InlineData("Sam - Private browsing", true)]
    [InlineData("InPrivate - Microsoft Edge", true)]
    [InlineData("User Account Control", true)]
    [InlineData("Inbox - Outlook", false)]
    public void A_private_browser_window_and_the_security_prompts_are_private_windows(string title, bool isPrivate) =>
        Assert.Equal(isPrivate, TeachGuards.IsPrivateWindow(title));

    [Fact]
    public void A_click_in_a_private_window_and_a_private_event_are_steps_the_owner_does_himself()
    {
        var inManager = TeachSamples.Click(1, 3, TeachSamples.Button("Copy password", app: "bitwarden"));
        Assert.True(TeachGuards.ForEvent(inManager)!.OwnerDoesIt);
        var incognito = TeachSamples.Click(2, 4, TeachSamples.Button("Login", window: "Bank - Incognito"));
        Assert.True(TeachGuards.ForEvent(incognito)!.OwnerDoesIt);
        Assert.True(TeachGuards.ForEvent(TeachSamples.Private(3, 5))!.OwnerDoesIt);
    }

    [Theory]
    [InlineData("Ctrl+Enter", ActionClass.Outward)]
    [InlineData("Shift+Delete", ActionClass.Destructive)]
    public void A_shortcut_that_sends_or_deletes_asks(string chord, ActionClass expected) => Assert.Equal(expected, TeachGuards.ForShortcut(chord)!.Class);

    [Fact]
    public void An_ordinary_shortcut_does_not_ask() => Assert.Null(TeachGuards.ForShortcut("Ctrl+S"));

    [Fact]
    public void A_browser_form_submit_asks_and_a_page_open_does_not()
    {
        Assert.Equal(ActionClass.Outward, TeachGuards.ForEvent(TeachSamples.Browser(1, 3, "submit", url: "https://shop.example/form"))!.Class);
        Assert.Null(TeachGuards.ForEvent(TeachSamples.Browser(2, 4, "open", url: "https://shop.example/")));
        Assert.Equal(ActionClass.Money, TeachGuards.ForEvent(TeachSamples.Browser(3, 5, "click", element: TeachSamples.Button("Place order", "msedge")))!.Class);
    }

    [Fact]
    public void Each_finding_names_the_action_in_words_the_owner_reads()
    {
        Assert.Equal("sends", TeachGuards.ForLabel("Send")!.Word);
        Assert.Equal("pays", TeachGuards.ForLabel("Pay")!.Word);
        Assert.Equal("deletes", TeachGuards.ForLabel("Delete")!.Word);
        Assert.Contains("“Send”", TeachGuards.ForLabel("Send")!.Reason, StringComparison.Ordinal);
    }
}
