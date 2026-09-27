using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class AlertPresenterTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-216")] // App/Views/Settings/SettingsView.swift:920-934; cancellation is dialog close
    public void A_presenting_lifetime_ending_cancels_without_running_the_destructive_action() => headless.OnUiThread(() =>
    {
        using var lifetime = new CancellationTokenSource();
        var deleted = 0;
        var canceled = 0;
        var closed = 0;
        var request = AlertPresentation.Prepare(new AlertRequest("Delete?", null,
            [new AlertButton("Delete", () => deleted++) { Role = AlertButtonRole.Destructive },
             new AlertButton("Cancel", () => canceled++) { Role = AlertButtonRole.Cancel }], AlertKind.ConfirmationDialog));
        var session = new AlertSession(request, () => closed++);
        session.BindLifetime(lifetime.Token);
        lifetime.Cancel();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, deleted);
        Assert.Equal(1, canceled);
        Assert.Equal(1, closed);
        session.Closed();
        Assert.True(session.Completion.IsCompletedSuccessfully);
    });

    [Fact]
    [Trait("Decision", "ED-216")] // App/Views/Chat/TimelineRows.swift:191-196
    public void Confirmation_adds_Cancel_preserves_declared_order_and_defaults_to_the_first_roleless_action()
    {
        var request = AlertPresentation.Prepare(new AlertRequest("Edit?", "Message", [
            new AlertButton("Keep files", () => { }),
            new AlertButton("Revert files", () => { }) { Role = AlertButtonRole.Destructive }], AlertKind.ConfirmationDialog));
        Assert.Equal(["Keep files", "Revert files", "Cancel"], request.Buttons.Select(button => button.Title));
        Assert.Equal(AlertButtonRole.Cancel, request.Buttons[^1].Role);
        Assert.Same(request.Buttons[0], request.ReturnDefault);
    }

    [Fact]
    [Trait("Decision", "ED-216")] // App/Views/Settings/SettingsView.swift:920-934; App/Views/RootView.swift:80-91
    public void Explicit_Cancel_is_last_and_destructive_only_dialogs_have_no_Return_default()
    {
        var cancel = new AlertButton("Never mind", () => { }) { Role = AlertButtonRole.Cancel };
        var request = AlertPresentation.Prepare(new AlertRequest("Delete?", null,
            [cancel, new AlertButton("Delete", () => { }) { Role = AlertButtonRole.Destructive }], AlertKind.ConfirmationDialog));
        Assert.Equal(["Delete", "Never mind"], request.Buttons.Select(button => button.Title));
        Assert.Null(request.ReturnDefault);
        var soleCancel = AlertPresentation.Prepare(new AlertRequest("Notice", null, [cancel]));
        Assert.Same(cancel, soleCancel.ReturnDefault); // PR-WIN-066 windows_path, ED-216's sole-cancel exception
        var empty = AlertPresentation.Prepare(new AlertRequest("Notice", null, []));
        Assert.Equal("OK", Assert.Single(empty.Buttons).Title);
        Assert.Same(empty.Buttons[0], empty.ReturnDefault);
    }

    [Fact]
    [Trait("Decision", "ED-216")] // App/Views/Chat/TimelineRows.swift:191-196
    public void An_action_runs_before_close_and_completion_waits_for_close_not_the_button()
    {
        var order = new List<string>();
        var request = AlertPresentation.Prepare(new AlertRequest("Edit?", null,
            [new AlertButton("Revert", () => order.Add("action"))]));
        var session = new AlertSession(request, () => order.Add("close"));
        session.Press("Revert");
        Assert.Equal(["action", "close"], order);
        Assert.False(session.Completion.IsCompleted);
        session.Closed();
        Assert.True(session.Completion.IsCompletedSuccessfully);
        session.Press("Revert");
        Assert.Equal(["action", "close"], order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-216")] // App/Views/Chat/TimelineRows.swift:191-196; App/Views/Chat/ChatView.swift:1231-1237
    public void Escape_and_close_run_the_cancel_action_once_or_only_close_when_absent(bool hasCancel)
    {
        var canceled = 0;
        var closed = 0;
        var request = AlertPresentation.Prepare(new AlertRequest("Rename", null, hasCancel
            ? [new AlertButton("Cancel", () => canceled++) { Role = AlertButtonRole.Cancel }]
            : [new AlertButton("Rename", () => { })]));
        var session = new AlertSession(request, () => closed++);
        session.Cancel();
        session.Cancel();
        Assert.Equal(hasCancel ? 1 : 0, canceled);
        Assert.Equal(1, closed);
        session.Closed();
        Assert.True(session.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Decision", "ED-216")] // App/Views/Chat/ChatView.swift:1231-1237; App/Views/Settings/SettingsView.swift:920-934
    public void The_unshown_window_has_title_message_two_way_fields_roles_and_owner_centering() => headless.OnUiThread(() =>
    {
        var value = "Original";
        var request = AlertPresentation.Prepare(new AlertRequest("Rename thread", "Optional message",
            [new AlertButton("Delete", () => { }) { Role = AlertButtonRole.Destructive }, new AlertButton("Rename", () => { })],
            AlertKind.ConfirmationDialog, [new AlertTextField("Title", new Binding<string>(() => value, text => value = text))]));
        var session = new AlertSession(request, () => { });
        var window = WindowsAlertPresenter.BuildWindow(request, session); // Construct only: never Show/ShowDialog in tests.
        Assert.False(window.IsVisible);
        Assert.Equal("Rename thread", window.Title);
        Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        var content = Assert.IsType<StackPanel>(window.Content);
        var title = Assert.IsType<TextBlock>(content.Children[0]);
        Assert.Equal("Rename thread", title.Text);
        Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Equal("Optional message", Assert.IsType<TextBlock>(content.Children[1]).Text);
        var field = Assert.Single(content.Children.OfType<TextBox>());
        Assert.Equal("Title", field.PlaceholderText);
        Assert.Equal("Original", field.Text);
        field.Text = "Changed";
        Assert.Equal("Changed", value);
        var row = Assert.Single(content.Children.OfType<StackPanel>());
        Assert.Equal(HorizontalAlignment.Right, row.HorizontalAlignment);
        var buttons = row.Children.Cast<Button>().ToArray();
        Assert.Equal(["Delete", "Rename", "Cancel"], buttons.Select(button => Assert.IsType<TextBlock>(button.Content).Text));
        Assert.Equal(Chrome.Danger.Brush(buttons[0]).ToString(), Assert.IsType<TextBlock>(buttons[0].Content).Foreground!.ToString());
        Assert.False(buttons[0].IsDefault);
        Assert.True(buttons[1].IsDefault);
        Assert.True(buttons[2].IsCancel);
    });
}
