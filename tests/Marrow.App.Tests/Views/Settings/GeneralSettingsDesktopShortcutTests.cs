using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using HostWindow = Avalonia.Controls.Window;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a "nth's Settings" and section 8 "Open at sign-in", unit U8
// (ED-NEW-inst-u8-1 and -2). The "On this PC" section of Settings > General in Avalonia.Headless, on FakeShellLinks and
// FakeUserRegistry: hidden outside a Velopack install, the fake's truth on open and after a simulated deletion plus
// activation, the scan never on the UI thread, on and off calling Create and RemoveMade, a refusal springing the switch back
// with the right detail line, and a Settings search for "desktop" finding General. No test touches the real Desktop or HKCU.
[Collection("Headless")]
public sealed class GeneralSettingsDesktopShortcutTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private const string Root = OnThisPcRig.Root;
    private const string Run = UninstallEntry.RunKeyPath;
    private const string OursCommand = "\"" + OnThisPcRig.CurrentExe + "\" --at-sign-in";
    private const int AccessDenied = unchecked((int)0x80070005);
    private static readonly string DesktopLink = FakeShellLinks.DesktopLink;

    private static bool IsOn(Control toggle) => ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(toggle)!).ToggleState == ToggleState.On;

    // the knob's left edge: 2 at rest off, 16 at rest on (SmallSwitch: 44 wide track, 26 wide knob, 2 in from each edge)
    private static double KnobLeft(SmallSwitch toggle) => toggle.KnobNow().X;

    private static SmallSwitch Switch(Control view, string title) => Assert.Single(Nodes<SmallSwitch>(Row(view, title)));

    private static string Detail(Control view, string title) => Text(Row(view, title))[1];

    /// <summary>The section alone, on a model the test drives step by step.</summary>
    private void WithSection(Action<OnThisPcRig>? arrange, Action<OnThisPcRig, Control, AppModelHarness, HostWindow> check)
    {
        OnThisPcRig rig = null!;
        With(() =>
        {
            rig = new OnThisPcRig();
            arrange?.Invoke(rig);
            return new OnThisPcSettingsSection(rig.Model);
        }, (view, _, h, w) => check(rig, view, h, w));
    }

    // ---- what it is ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_section_is_one_On_this_PC_card_of_two_switch_rows_with_their_detail_lines()
    {
        WithSection(null, (rig, view, h, w) =>
        {
            var texts = Text(view);
            Assert.Equal(["On this PC", "Desktop shortcut", "nth's icon on your desktop", "Open at sign-in", "Starts nth when you sign in to Windows"], texts);
            Assert.Single(Nodes<ChromeSection>(view));
            Assert.Single(Nodes<ChromeCard>(view));
            Assert.Equal(2, Nodes<ChromeRow>(view).Length);
            Assert.Single(Nodes<ChromeRowDivider>(view));
            foreach (var title in new[] { "Desktop shortcut", "Open at sign-in" })
            {
                var row = Row(view, title);
                Assert.Single(Nodes<SettingsSwitch>(row));
                Assert.Empty(Text(Assert.Single(Nodes<SettingsSwitch>(row)))); // the switch itself carries no label
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Narrator_names_each_switch_by_its_row_with_the_detail_line_as_help()
    {
        WithSection(null, (rig, view, h, w) =>
        {
            Assert.Equal("Desktop shortcut", AutomationProperties.GetName(Switch(view, "Desktop shortcut")));
            Assert.Equal("nth's icon on your desktop", AutomationProperties.GetHelpText(Switch(view, "Desktop shortcut")));
            Assert.Equal("Open at sign-in", AutomationProperties.GetName(Switch(view, "Open at sign-in")));
            Assert.Equal("Starts nth when you sign in to Windows", AutomationProperties.GetHelpText(Switch(view, "Open at sign-in")));
        });
    }

    // ---- showing the machine's truth ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Opening_the_page_asks_for_a_background_scan_and_the_switches_settle_on_the_truth_without_animation()
    {
        WithSection(rig =>
        {
            rig.Shell.Seed(DesktopLink, OnThisPcRig.Made());
            rig.Registry.Seed(Run, "nth", OursCommand);
        }, (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            var signIn = Switch(view, "Open at sign-in");
            Assert.Single(rig.Work); // the page opening asked; the UI thread read nothing
            Assert.Equal(0, rig.Shell.Reads);
            Assert.False(IsOn(desktop));
            Assert.Equal(2, KnobLeft(desktop), 3);

            rig.Pump();
            Drain(w);

            Assert.True(IsOn(desktop));
            Assert.True(IsOn(signIn));
            Assert.Equal(16, KnobLeft(desktop), 3); // already at rest, with no frame ticked: it did not slide
            Assert.Equal(16, KnobLeft(signIn), 3);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Activating_the_window_scans_again_and_a_deleted_shortcut_turns_the_switch_off_in_place()
    {
        WithSection(rig => rig.Shell.Seed(DesktopLink, OnThisPcRig.Made()), (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Drain(w);
            Assert.True(IsOn(desktop));

            rig.Shell.DeleteLink(DesktopLink); // deleted in Explorer while Settings was open
            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            w.Activate();
            Drain(w);
            Assert.Single(rig.Work); // the activation asked for another scan

            rig.Pump();
            Drain(w);
            Assert.False(IsOn(desktop));
            Assert.Equal(2, KnobLeft(desktop), 3); // in place, no slide
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Activating_the_window_again_within_two_seconds_does_not_scan_again()
    {
        WithSection(null, (rig, view, h, w) =>
        {
            rig.Pump();
            Drain(w);
            rig.Clock.Advance(TimeSpan.FromSeconds(1));

            w.Activate();
            Drain(w);

            Assert.Empty(rig.Work);
        });
    }

    // ---- a tap ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_tap_flips_the_switch_at_once_slides_it_and_then_the_shortcut_exists()
    {
        WithSection(null, (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Drain(w);

            Click(w, desktop);

            Assert.True(IsOn(desktop));
            Assert.Empty(rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop)); // the worker has not run yet
            Tick(h, 50);
            Assert.InRange(KnobLeft(desktop), 2.5, 15.5); // sliding: the user's own tap animates
            Tick(h, 300);
            Assert.Equal(16, KnobLeft(desktop), 3);

            rig.Pump();
            Drain(w);
            Assert.True(IsOn(desktop));
            Assert.Equal(new[] { DesktopLink }, rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
            Assert.Equal("nth's icon on your desktop", Detail(view, "Desktop shortcut"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_the_switch_off_removes_the_shortcut_nth_made()
    {
        WithSection(rig => rig.Shell.Seed(DesktopLink, OnThisPcRig.Made()), (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Drain(w);
            Assert.True(IsOn(desktop));

            Click(w, desktop);
            Assert.False(IsOn(desktop));
            rig.Pump();
            Drain(w);
            Tick(h, 300);

            Assert.False(IsOn(desktop));
            Assert.Empty(rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
            Assert.Equal(2, KnobLeft(desktop), 3);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_refusal_springs_the_switch_back_and_the_detail_line_says_why_until_the_next_tap()
    {
        WithSection(rig => rig.Shell.Fail(FakeShellLinks.Call.WriteLink, AccessDenied, times: 1), (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Drain(w);

            Click(w, desktop);
            Tick(h, 300);
            Assert.True(IsOn(desktop));
            Assert.Equal(16, KnobLeft(desktop), 3);

            rig.Pump();
            Drain(w);
            Assert.False(IsOn(desktop));
            Tick(h, 60);
            Assert.InRange(KnobLeft(desktop), 2.5, 15.5); // it slides back, it does not snap
            Tick(h, 300);
            Assert.Equal(2, KnobLeft(desktop), 3);
            Assert.Equal("Windows didn't allow it. Try again.", Detail(view, "Desktop shortcut"));
            Assert.Equal("Windows didn't allow it. Try again.", AutomationProperties.GetHelpText(desktop));

            Click(w, desktop); // the next tap clears the reason at once; the refusal was only for one call
            Assert.Equal("nth's icon on your desktop", Detail(view, "Desktop shortcut"));
            rig.Pump();
            Drain(w);
            Assert.True(IsOn(desktop));
            Assert.Equal("nth's icon on your desktop", Detail(view, "Desktop shortcut"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_foreign_nth_lnk_springs_the_switch_back_with_the_name_taken_line()
    {
        WithSection(rig => rig.Shell.Seed(DesktopLink, new ShellLink(@"C:\Old\nth\nth.exe")), (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Drain(w);

            Click(w, desktop);
            rig.Pump();
            Drain(w);
            Tick(h, 300);

            Assert.False(IsOn(desktop));
            Assert.Equal("Another \"nth\" shortcut is already on your desktop.", Detail(view, "Desktop shortcut"));
            Assert.Equal(@"C:\Old\nth\nth.exe", rig.Shell.Peek(DesktopLink)!.Target);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_page_opening_again_brings_back_the_normal_detail_line()
    {
        WithSection(rig => rig.Shell.Seed(DesktopLink, new ShellLink(@"C:\Old\nth\nth.exe")), (rig, view, h, w) =>
        {
            var desktop = Switch(view, "Desktop shortcut");
            rig.Pump();
            Click(w, desktop);
            rig.Pump();
            Drain(w);
            Assert.NotEqual("nth's icon on your desktop", Detail(view, "Desktop shortcut"));

            rig.Model.Refresh(pageOpened: true);
            Drain(w);

            Assert.Equal("nth's icon on your desktop", Detail(view, "Desktop shortcut"));
        });
    }

    // ---- open at sign-in ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_switch_writes_and_removes_the_run_value()
    {
        WithSection(null, (rig, view, h, w) =>
        {
            var signIn = Switch(view, "Open at sign-in");
            rig.Pump();
            Drain(w);

            Click(w, signIn);
            Assert.True(IsOn(signIn));
            rig.Pump();
            Drain(w);
            Assert.Equal(OursCommand, rig.Registry.GetValue(Run, "nth"));

            Click(w, signIn);
            Assert.False(IsOn(signIn));
            rig.Pump();
            Drain(w);
            Assert.Null(rig.Registry.GetValue(Run, "nth"));
            Assert.Equal("Starts nth when you sign in to Windows", Detail(view, "Open at sign-in"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_value_named_nth_that_is_not_nths_springs_the_sign_in_switch_back_with_its_line()
    {
        WithSection(rig => rig.Registry.Seed(Run, "nth", @"C:\Old\nth\nth.exe"), (rig, view, h, w) =>
        {
            var signIn = Switch(view, "Open at sign-in");
            rig.Pump();
            Drain(w);
            Assert.False(IsOn(signIn));

            Click(w, signIn);
            rig.Pump();
            Drain(w);
            Tick(h, 300);

            Assert.False(IsOn(signIn));
            Assert.Equal("Another start-up entry named \"nth\" is already set.", Detail(view, "Open at sign-in"));
            Assert.Equal(@"C:\Old\nth\nth.exe", rig.Registry.GetValue(Run, "nth"));
        });
    }

    // ---- the General page: the hook ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void General_has_no_On_this_PC_section_outside_a_Velopack_install_and_search_does_not_offer_its_words()
    {
        Assert.False(OnThisPcSettingsSection.IsAvailable);
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            Assert.Empty(Nodes<OnThisPcSettingsSection>(view));
            Assert.DoesNotContain("On this PC", Text(view));
            Assert.DoesNotContain("Desktop shortcut", Text(view));
        });
        foreach (var word in new[] { "desktop", "shortcut", "sign-in", "startup" })
            Assert.DoesNotContain(SettingsPage.General, SettingsSidebar.Pages(word));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void In_a_Velopack_install_General_ends_with_the_section_and_search_finds_General_by_its_words()
    {
        var rig = new OnThisPcRig();
        OnThisPcPlatform.Slot.SetProcessDefault(rig.Platform);
        try
        {
            Assert.True(OnThisPcSettingsSection.IsAvailable);
            With(() => new GeneralSettingsPage(), (view, model, h, w) =>
            {
                var titles = Nodes<ChromeSection>(view).Select(section => Text(section).First()).ToArray();
                Assert.Equal("On this PC", titles[^1]);
                Assert.Equal("Finished threads", titles[^2]);
                Assert.Single(Nodes<OnThisPcSettingsSection>(view));
                Assert.Equal(2, Nodes<SmallSwitch>(Assert.Single(Nodes<OnThisPcSettingsSection>(view))).Length);
            });
            foreach (var word in new[] { "desktop", "shortcut", "sign-in", "startup" })
                Assert.Contains(SettingsPage.General, SettingsSidebar.Pages(word));
            Assert.Equal(new[] { "desktop", "shortcut", "sign-in", "startup" }, SettingsPage.General.Keywords.TakeLast(4));
            Assert.Contains("permissions", SettingsPage.General.Keywords); // the page's own words are all still there
        }
        finally
        {
            OnThisPcPlatform.Slot.SetProcessDefault(OnThisPcPlatform.Unavailable);
        }
    }
}
