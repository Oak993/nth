using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test rig for NewThreadPrompt's project picker (App/Views/Chat/NewThreadPrompt.swift:25-34, :57-80).
// It builds the prompt in a headless window, opens the picker popover through the R-VIEW-9 presenter seam
// (PopoverRecorder, a real Popup is never opened) and exposes the presented menu, so a test clicks the rows exactly
// as a user would. The caller runs every step on the harness's main loop; the view's own
// reactive updates and PopoverItem's deferred action need later turns, so a test makes each step in its own
// OnMain call (a nested OnMain throws).
public sealed class NewThreadPickerRig : IDisposable
{
    private readonly IDisposable seam;
    private AvaloniaWindow? window;
    private Control? host;

    /// <summary>Installs the presenter seam. Make the rig in the test body, outside OnMain: the seam is an ambient value
    /// that only reaches the loop's later turns when it is set on the calling flow.</summary>
    public NewThreadPickerRig()
    {
        seam = Presentation.Presenter.Override(Recorder);
    }

    /// <summary>Builds the prompt for a thread in a headless window. Call it inside OnMain.</summary>
    public void Show(AppModel model, Guid threadID, string projectName)
    {
        var border = new Border();
        AppEnvironment.SetAppModel(border, model);
        border.Child = new NewThreadPrompt(threadID, projectName);
        host = border;
        window = new AvaloniaWindow { Content = host };
        window.Show();
    }

    public PopoverRecorder Recorder { get; } = new();

    /// <summary>Clicks the project name (the button tipped "Choose another project"). The presenter hears of it on a
    /// later turn of the loop, so read <see cref="Popup"/> from the next OnMain call.</summary>
    public void ClickName() => Click(LogicalQuery.SelfAndDescendants<Button>(host!)
        .Single(button => (ToolTip.GetTip(button) as string) == "Choose another project"));

    /// <summary>The popup the picker asked the presenter to show.</summary>
    public Popup Popup => Recorder.Calls.Last(call => call.Presented).Popup;

    /// <summary>The presented menu's content.</summary>
    public Control Menu => Popup.Child ?? throw new Xunit.Sdk.XunitException("the picker hosts no content");

    /// <summary>Whether the picker is still showing: the presenter's last word on it was to present it, and the prompt still
    /// holds that popup. A move re-renders the prompt for the new project (its body reads the thread), which drops the
    /// old popup from the tree and so takes the picker away without a presenter call.</summary>
    public bool IsPresented => Recorder.Calls.Count > 0 && Recorder.Calls[^1].Presented &&
        LogicalQuery.SelfAndDescendants<Popup>(host!).Contains(Recorder.Calls[^1].Popup);

    /// <summary>The stack holding the section header, the project rows, the divider and the Add project row, in order.</summary>
    public IReadOnlyList<Control> Rows =>
        LogicalQuery.SelfAndDescendants<StackPanel>(Menu).Single(stack => stack.Children.OfType<PopoverSectionHeader>().Any()).Children.ToArray();

    /// <summary>The row whose accessibility name (its title) is <paramref name="title"/>.</summary>
    public PopoverItem Row(string title) =>
        Rows.OfType<PopoverItem>().Single(row => AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(row).First()) == title);

    /// <summary>Clicks a row's button; what the click defers (the action, the close) runs by the end of the OnMain call.</summary>
    public void ClickRow(string title) => Click(LogicalQuery.SelfAndDescendants<Button>(Row(title)).First());

    public void Dispose()
    {
        window?.Close();
        seam.Dispose();
    }

    private static void Click(Control button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
