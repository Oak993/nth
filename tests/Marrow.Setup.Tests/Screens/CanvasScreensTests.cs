using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, 8a and 10, unit U2 (ED-NEW-inst-u2-1). The canvas's screens say
// each screen's words from the string table word for word (lower-case "nth", OD-0072's sentences, "by oak993"), carry
// the Close-paused line where the plan puts one (Installing, Update, Uninstall) and Welcome's signature line, keep
// Welcome's link and the Options switch in step, and answer Enter, Esc and Tab as the canvas does.
[Collection("SetupHeadless")]
public sealed class CanvasScreensTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Every_screen_is_built_with_its_title_its_lines_and_its_Narrator_name()
    {
        headless.OnUiThread(() =>
        {
            var session = new WindowRig().Session(SetupScreen.Welcome, installed: "1.0.0");
            var titles = new Dictionary<SetupScreen, (string Title, string Name)>
            {
                [SetupScreen.Welcome] = (SetupStrings.WelcomeTitle, "Welcome"),
                [SetupScreen.Options] = (SetupStrings.OptionsTitle, "Install options"),
                [SetupScreen.Installing] = (SetupStrings.InstallingTitle, "Installing"),
                [SetupScreen.Ready] = (SetupStrings.ReadyTitle, "Ready"),
                [SetupScreen.Update] = (SetupStrings.UpdateTitle, "Update"),
                [SetupScreen.UpToDate] = (SetupStrings.CurrentTitle, "Already installed"),
                [SetupScreen.NewerInstalled] = (SetupStrings.NewerTitle, "Already installed"),
                [SetupScreen.Problem] = (SetupStrings.ProblemTitle, "Problem"),
                [SetupScreen.Uninstall] = (SetupStrings.UninstallTitle, "Uninstall"),
            };
            foreach (var kind in Enum.GetValues<SetupScreen>())
            {
                var screen = new CanvasScreens().Create(kind, session);
                screen.Entered(null);

                Assert.Equal(kind, screen.Kind);
                Assert.Contains(titles[kind].Title, Texts(screen));
                Assert.Equal(titles[kind].Name, Avalonia.Automation.AutomationProperties.GetName(screen.View));
                Assert.Equal(kind is SetupScreen.Installing or SetupScreen.Update or SetupScreen.Uninstall, screen.Hold is not null);
                Assert.Equal(kind == SetupScreen.Welcome, screen.Signature is not null);
                Assert.DoesNotContain(Texts(screen), t => t is not null && t.Contains("Nth", StringComparison.Ordinal));
            }
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Welcome_says_who_made_it_where_it_installs_and_what_it_adds_word_for_word()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.SizeMegabytes = 420;
            session.Start();
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);
            var texts = AllText(welcome);

            Assert.Contains("Welcome to nth", texts);
            Assert.Contains("by oak993", texts);
            Assert.Contains("Version 1.1.0 · 420 MB on disk", texts);
            Assert.Contains("For you only, no admin needed", texts);
            Assert.Contains("This setup works offline and sends nothing", texts);
            Assert.Contains(@"Installs to C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth · Options", texts);
            Assert.Contains("Adds nth to Start and your desktop · Don’t add to desktop", texts);
            Assert.Contains("Free software under the GNU AGPL v3.0 · View license", texts);
            Assert.Equal("Install", welcome.Install.Label);
            Assert.Equal(ButtonKind.Primary, welcome.Install.Kind);
            Assert.Equal(200, welcome.Install.MinWidth);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Welcome_hides_the_size_it_does_not_know_and_starts_without_the_desktop_when_asked()
    {
        headless.OnUiThread(() =>
        {
            var session = new WindowRig().Session(SetupScreen.Welcome, null, true, "--no-desktop-shortcut");
            session.Start();
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);

            Assert.DoesNotContain(AllText(welcome), t => t.StartsWith("Version", StringComparison.Ordinal));
            Assert.False(session.DesktopShortcut);
            Assert.Equal(("Adds nth to Start", "Add to desktop"), welcome.DeskLine);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Welcome_link_and_the_Options_switch_and_preview_stay_in_step()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);

            welcome.DeskLink.Invoke();
            Assert.False(session.DesktopShortcut);
            // the sentence and its link cross-fade together: out in 0.13 s, the words change, back in 0.13 s
            rig.Run(0.07);
            Assert.InRange(welcome.DeskLineOpacity, 0.05, 0.95);
            Assert.Equal(("Adds nth to Start and your desktop", "Don’t add to desktop"), welcome.DeskLine);
            rig.Run(0.3);
            Assert.Equal(("Adds nth to Start", "Add to desktop"), welcome.DeskLine);
            Assert.Equal(1, welcome.DeskLineOpacity);

            welcome.OptionsLink.Invoke();
            rig.Run(0.6);
            var options = Assert.IsType<OptionsScreen>(session.Current);
            Assert.False(options.Switch.IsOn);
            Assert.Equal(DeskState.Off, options.Preview.State);
            Assert.Equal(0, options.Preview.IconPose.Opacity);

            options.Preview.Tap();
            Assert.True(options.Switch.IsOn);
            Assert.True(session.DesktopShortcut);
            Assert.Equal(DeskState.Land, options.Preview.State);
            rig.Run(1.2);
            Assert.Equal((0, 1, 1, 1), options.Preview.IconPose);

            options.Switch.Toggle();
            Assert.False(session.DesktopShortcut);
            options.Done.Invoke();
            rig.Run(0.6);
            Assert.Same(welcome, session.Current);
            Assert.Equal(("Adds nth to Start", "Add to desktop"), welcome.DeskLine);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Options_Change_puts_nth_in_the_picked_folder_or_in_an_nth_folder_inside_a_full_one()
    {
        var empty = Directory.CreateTempSubdirectory("nth-u2-empty-");
        var full = Directory.CreateTempSubdirectory("nth-u2-full-");
        File.WriteAllText(Path.Combine(full.FullName, "other.txt"), "x");
        try
        {
            Assert.Equal(empty.FullName, SetupSession.RootFor(empty.FullName + Path.DirectorySeparatorChar));
            Assert.Equal(Path.Combine(full.FullName, "nth"), SetupSession.RootFor(full.FullName));
            Assert.Equal(Path.Combine(empty.FullName, "new"), SetupSession.RootFor(Path.Combine(empty.FullName, "new")));
            headless.OnUiThread(() =>
            {
                var rig = new WindowRig();
                var session = rig.Session(SetupScreen.Options);
                session.Start();
                rig.Window.Show();
                rig.Settle();
                var options = Assert.IsType<OptionsScreen>(session.Current);
                rig.Actions.NextFolder = full.FullName;

                options.Change.Invoke();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                rig.Run(0.8);

                Assert.Equal(Path.Combine(full.FullName, "nth"), session.InstallRoot);
                Assert.Equal(Path.Combine(full.FullName, "nth"), options.PathBlock.Text);
                rig.Window.CloseNow();
            });
        }
        finally
        {
            empty.Delete(true);
            full.Delete(true);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Up_to_date_and_newer_name_the_versions_and_both_offer_Open_nth()
    {
        headless.OnUiThread(() =>
        {
            var current = new WindowRig().Session(SetupScreen.UpToDate, installed: "1.1.0");
            var newer = new WindowRig().Session(SetupScreen.NewerInstalled, installed: "1.2.0");
            current.Start();
            newer.Start();

            Assert.Contains("nth 1.1.0 is already installed and up to date.", Texts(current.Current!));
            Assert.Contains("A newer version of nth (1.2.0) is already installed. Nothing was changed.", Texts(newer.Current!));
            Assert.Equal("Open nth", Assert.IsType<CurrentScreen>(current.Current).Open.Label);
            Assert.Equal("Open nth", Assert.IsType<CurrentScreen>(newer.Current).Open.Label);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Enter_presses_the_default_button_and_Esc_closes_or_goes_back()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();

            Key(rig, Avalonia.Input.Key.Escape);
            Assert.False(rig.Window.IsVisible);

            var second = new WindowRig();
            var other = second.Session(SetupScreen.Options);
            other.Start();
            // ED-NEW-inst-merge-3: Install takes Enter only once the signature has answered (ED-NEW-inst-cx-1)
            other.SignatureChecked(new Marrow.Setup.Platform.SignatureResult(Marrow.Setup.Platform.SignatureState.Unsigned, null, 0));
            second.Window.Show();
            second.Settle();
            Key(second, Avalonia.Input.Key.Escape);
            Assert.Equal(SetupScreen.Welcome, other.Screen);
            second.Run(0.6);
            Key(second, Avalonia.Input.Key.Enter);
            Assert.True(other.InstallChosen);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Uninstall_focuses_Cancel_and_Enter_and_Esc_both_cancel()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var screen = Assert.IsType<UninstallScreen>(session.Current);

            Assert.True(screen.Cancel.IsFocused);
            Assert.False(screen.Cancel.ShowsFocusRing); // as the canvas opens it: the ring comes with the keyboard
            Assert.Same(screen.Cancel, screen.DefaultButton);
            Assert.Equal(ButtonKind.Destructive, screen.Remove.Kind);
            Key(rig, Avalonia.Input.Key.Enter);
            Assert.False(rig.Window.IsVisible);

            var again = new WindowRig();
            var other = again.Session(SetupScreen.Uninstall, installed: "1.1.0");
            other.Start();
            again.Window.Show();
            again.Settle();
            Key(again, Avalonia.Input.Key.Escape);
            Assert.False(again.Window.IsVisible);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_buttons_work_by_Enter_and_Space_and_show_their_ring_only_for_the_keyboard()
    {
        headless.OnUiThread(() =>
        {
            var runs = 0;
            var button = new SetupButton(SetupStrings.WelcomeInstall, ButtonKind.Primary, () => runs++);
            var other = new SetupButton(SetupStrings.WelcomeOptions, ButtonKind.Secondary, () => { });
            var window = new Window { Content = new StackPanel { Children = { button, other } } };
            window.Show();

            Assert.Equal("Install", button.Label);
            Assert.True(button.Focusable);
            Assert.True(button.Focus(NavigationMethod.Pointer));
            Assert.False(button.ShowsFocusRing);
            Assert.True(other.Focus(NavigationMethod.Tab));
            Assert.True(other.ShowsFocusRing);
            Assert.True(button.Focus(NavigationMethod.Tab));
            Assert.True(button.ShowsFocusRing);
            Assert.False(other.ShowsFocusRing);
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Space });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.A });
            Assert.Equal(2, runs);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Tab_goes_through_the_links_and_buttons_in_reading_order_and_never_to_a_preview()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var options = Assert.IsType<OptionsScreen>(session.Current);

            var stops = options.View.GetLogicalDescendants().OfType<InputElement>().Where(e => e.Focusable && KeyboardNavigation.GetIsTabStop(e)).ToList();
            Assert.Equal(new object[] { options.Change, options.Switch, options.Done }, stops.Cast<object>().ToArray());
            Assert.False(options.Preview.Focusable);
            Assert.Equal(Avalonia.Automation.AccessibilityView.Raw, Avalonia.Automation.AutomationProperties.GetAccessibilityView(options.Preview));
            Assert.Equal("Desktop shortcut", Avalonia.Automation.AutomationProperties.GetName(options.Switch));
            Assert.Equal("You can change this later in nth’s Settings", Avalonia.Automation.AutomationProperties.GetHelpText(options.Switch));
        });
    }

    private static void Key(WindowRig rig, Avalonia.Input.Key key) =>
        rig.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = rig.Window });

    private static List<string?> Texts(ISetupScreen screen) => screen.View.GetLogicalDescendants().OfType<TextBlock>().Select(Plain).ToList();

    private static List<string> AllText(ISetupScreen screen) => [.. Texts(screen).OfType<string>()];

    /// <summary>A text block's words, with its inline runs and links joined as they read.</summary>
    private static string? Plain(TextBlock block)
    {
        if (block.Inlines is not { Count: > 0 } inlines) return block.Text;
        return string.Concat(inlines.Select(i => i switch
        {
            Run run => run.Text,
            InlineUIContainer { Child: LinkButton link } => link.Words,
            _ => "",
        }));
    }
}
