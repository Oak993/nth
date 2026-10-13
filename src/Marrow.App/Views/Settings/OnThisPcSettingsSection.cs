using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Views.Settings;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8a "nth's Settings" and section 8 "Open at sign-in", unit U8,
/// ED-NEW-inst-u8-1 and -2). The "On this PC" section at the end of Settings > General: two rows in the app's own card,
/// "Desktop shortcut" (nth's icon on the desktop) and "Open at sign-in", each a <see cref="SettingsSwitch"/> like every
/// other row.
/// <list type="bullet">
/// <item><b>Only in a Velopack install.</b> <see cref="IsAvailable"/> is false in a dev build, a portable copy and a test
/// (<see cref="OnThisPcPlatform.Slot"/> is bound only in Program.cs for a Velopack install), and then the page does not add
/// the section and the search does not offer its words (<see cref="WithKeywords"/>).</item>
/// <item><b>The machine is the state</b> (<see cref="OnThisPcModel"/>): the switches show the last known answer at once and
/// update in place, without animation, when a scan finds the machine changed outside nth. The scan runs on a background
/// thread when the page opens and when the window is activated, at most once every 2 s.</item>
/// <item><b>The detail line says why</b> when a tap did not hold: the switch springs back and the row's second line reads the
/// reason until the next tap or the next time the page opens.</item>
/// <item><b>Narrator:</b> each switch is named by its row ("Desktop shortcut", "Open at sign-in") with the detail line as its
/// help text.</item>
/// </list>
/// The page hooks this in with one line (<c>GeneralSettingsPage</c> in SettingsView.cs) and the search with one wrapper
/// around General's keyword list; nothing else of the page changes.
/// </summary>
internal sealed class OnThisPcSettingsSection : Decorator
{
    internal const string Title = "On this PC";
    internal const string DesktopShortcutTitle = "Desktop shortcut";
    internal const string SignInTitle = "Open at sign-in";

    /// <summary>The words the plan adds to General's search (PLAN.md 8a).</summary>
    internal static readonly string[] Keywords = ["desktop", "shortcut", "sign-in", "startup"];

    private readonly OnThisPcModel model;
    private AvaloniaWindow? window;

    /// <summary>Whether this run is a Velopack install, where nth's shortcut and sign-in entry exist to manage.</summary>
    public static bool IsAvailable => OnThisPcPlatform.Slot.Current.IsAvailable;

    /// <summary>General's keyword list, with the section's own words added when the section is shown.</summary>
    public static string[] WithKeywords(string[] general) => IsAvailable ? [.. general, .. Keywords] : general;

    public OnThisPcSettingsSection() : this(OnThisPcModel.For(OnThisPcPlatform.Slot.Current)) { }

    internal OnThisPcSettingsSection(OnThisPcModel model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        var desktop = Row(DesktopShortcutTitle, OnThisPcModel.DesktopShortcutDetail,
            () => model.DesktopShortcutOn, on => model.RequestDesktopShortcut(on), out var desktopSwitch);
        desktop.Track(() => model.DesktopShortcutNote, note =>
        {
            var detail = note ?? OnThisPcModel.DesktopShortcutDetail;
            SettingsRowDetails.Update(desktop, detail);
            AutomationProperties.SetHelpText(desktopSwitch, detail);
        });
        var signIn = Row(SignInTitle, OnThisPcModel.SignInDetail,
            () => model.SignInOn, on => model.RequestSignIn(on), out var signInSwitch);
        signIn.Track(() => model.SignInNote, note =>
        {
            var detail = note ?? OnThisPcModel.SignInDetail;
            SettingsRowDetails.Update(signIn, detail);
            AutomationProperties.SetHelpText(signInSwitch, detail);
        });
        Child = new ChromeSection(Title, () => new ChromeCard(() =>
        {
            var stack = new StackPanel();
            stack.Children.Add(desktop);
            stack.Children.Add(new ChromeRowDivider());
            stack.Children.Add(signIn);
            return stack;
        }));
        AttachedToVisualTree += (_, _) =>
        {
            model.Refresh(pageOpened: true);
            window = TopLevel.GetTopLevel(this) as AvaloniaWindow;
            if (window is not null) window.Activated += OnActivated;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (window is not null) window.Activated -= OnActivated;
            window = null;
        };
    }

    private void OnActivated(object? sender, EventArgs e) => model.Refresh();

    private ChromeRow Row(string title, string detail, Func<bool> read, Action<bool> write, out SmallSwitch small)
    {
        var settingsSwitch = new SettingsSwitch(new Binding<bool>(read, write));
        small = (SmallSwitch)settingsSwitch.Child!;
        small.StillWhen = () => model.ChangeIsExternal;
        AutomationProperties.SetName(small, title);
        AutomationProperties.SetHelpText(small, detail);
        return new ChromeRow(title, () => settingsSwitch, detail);
    }
}
