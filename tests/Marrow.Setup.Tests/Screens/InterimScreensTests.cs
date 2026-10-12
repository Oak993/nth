using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, unit U1 (ED-NEW-inst-u1-1). The interim screens (STUB(U2): unit U2
// builds the canvas's screens) still say each screen's words from the string table, carry the Close-paused line where
// the plan puts one (Installing, Update, Uninstall) and Welcome's signature line, and their one button works by click,
// Enter and Space and shows a focus ring only for keyboard focus.
[Collection("SetupHeadless")]
public sealed class InterimScreensTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Every_screen_is_built_with_its_title_and_its_lines()
    {
        headless.OnUiThread(() =>
        {
            var session = new WindowRig().Session(SetupScreen.Welcome, installed: "1.0.0");
            var titles = new Dictionary<SetupScreen, string>
            {
                [SetupScreen.Welcome] = SetupStrings.WelcomeTitle,
                [SetupScreen.Options] = SetupStrings.OptionsTitle,
                [SetupScreen.Installing] = SetupStrings.InstallingTitle,
                [SetupScreen.Ready] = SetupStrings.ReadyTitle,
                [SetupScreen.Update] = SetupStrings.UpdateTitle,
                [SetupScreen.UpToDate] = SetupStrings.CurrentTitle,
                [SetupScreen.NewerInstalled] = SetupStrings.NewerTitle,
                [SetupScreen.Problem] = SetupStrings.ProblemTitle,
                [SetupScreen.Uninstall] = SetupStrings.UninstallTitle,
            };
            foreach (var kind in Enum.GetValues<SetupScreen>())
            {
                var screen = new InterimScreens().Create(kind, session);
                var texts = screen.View.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

                Assert.Equal(kind, screen.Kind);
                Assert.Contains(titles[kind], texts);
                Assert.Equal(kind is SetupScreen.Installing or SetupScreen.Update or SetupScreen.Uninstall, screen.Hold is not null);
                Assert.Equal(kind == SetupScreen.Welcome, screen.Signature is not null);
            }
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Up_to_date_and_newer_name_the_versions()
    {
        headless.OnUiThread(() =>
        {
            var current = new WindowRig().Session(SetupScreen.UpToDate, installed: "1.1.0");
            var newer = new WindowRig().Session(SetupScreen.NewerInstalled, installed: "1.2.0");

            Assert.Contains("nth 1.1.0 is already installed and up to date.", Texts(new InterimScreens().Create(SetupScreen.UpToDate, current)));
            Assert.Contains("A newer version of nth (1.2.0) is already installed. Nothing was changed.", Texts(new InterimScreens().Create(SetupScreen.NewerInstalled, newer)));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_button_works_by_Enter_and_Space_and_shows_its_ring_only_for_the_keyboard()
    {
        headless.OnUiThread(() =>
        {
            var runs = 0;
            var button = new InterimButton(SetupStrings.WelcomeInstall, () => runs++);
            var window = new Window { Content = button };
            window.Show();

            Assert.Equal("Install", button.Label);
            Assert.True(button.Focusable);
            button.Focus(NavigationMethod.Tab);
            Assert.True(button.ShowsFocusRing);
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
            Assert.Equal(2, runs);
            window.Close();
        });
    }

    private static List<string?> Texts(ISetupScreen screen) => screen.View.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
}
