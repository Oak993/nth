using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port.Ignis.Settings;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-ignis-guards: the idle-limit pair ("Cool idle chats after", "Learn when to cool") in Settings › Ignis and in the Kindle
// popover (owner 2026-10-10: both places, one source of truth, both views update live). Headless, a fake controller; no engine.
[Collection("Headless")]
public sealed class IgnisCoolingSettingsTests(HeadlessSession headless)
{
    private static AvaloniaWindow Show(Control content)
    {
        var window = new AvaloniaWindow { Content = new ScrollViewer { Content = new Border { Child = content, Width = 520, HorizontalAlignment = HorizontalAlignment.Left } }, Width = 600, Height = 1600 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }

    private static IToggleProvider Toggle(SettingsSwitch host) =>
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(host.GetVisualDescendants().OfType<SmallSwitch>().Single()));

    private static (IgnisSettingsPage Page, KindlePopover Popover, AvaloniaWindow Window) Open(TestRuntime runtime, IgnisSettings settings) => runtime.OnMain(() =>
    {
        var controller = new KindleFakeController(settings);
        var page = new IgnisSettingsPage(settings, null, null, () => new DateOnly(2026, 10, 10), () => null);
        var popover = new KindlePopover(new KindleCapsule(controller, KindleFakeController.Chat));
        var window = Show(new StackPanel { Children = { page, popover } });
        return (page, popover, window);
    });

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void Both_views_show_the_defaults_with_the_same_words_and_choices() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var (page, popover, window) = Open(runtime, new IgnisSettings(runtime.Defaults));
        try
        {
            runtime.OnMain(() =>
            {
                string[] choices = ["1 hour", "2 hours", "4 hours", "6 hours", "8 hours", "12 hours", "24 hours", "Never"];
                foreach (var menu in new[] { page.CoolAfterMenu, popover.CoolAfterMenu })
                {
                    Assert.Equal("12", menu.SelectedId);
                    Assert.Equal(choices, menu.Items.Select(item => item.Label.Text));
                    Assert.Equal(IgnisSettingsWords.RowCoolAfter, AutomationProperties.GetName(menu));
                }
                foreach (var toggle in new[] { page.LearnCoolingSwitch, popover.LearnCoolingSwitch })
                {
                    Assert.Equal(ToggleState.On, Toggle(toggle).ToggleState);
                    Assert.Equal(IgnisSettingsWords.RowLearnCooling, AutomationProperties.GetName(toggle));
                }
                var pageTexts = page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
                foreach (var word in new[] { IgnisSettingsWords.RowCoolAfter, IgnisSettingsWords.RowCoolAfterDetail, IgnisSettingsWords.RowLearnCooling, IgnisSettingsWords.RowLearnCoolingDetail })
                    Assert.Contains(word, pageTexts);
                var popoverTexts = popover.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
                Assert.Contains(IgnisSettingsWords.RowCoolAfter, popoverTexts);
                Assert.Contains(IgnisSettingsWords.RowLearnCooling, popoverTexts);
                Assert.Equal(KindlePopover.CompactRowHeight, popover.CoolAfterMenu.Height);
                Assert.Null(runtime.Defaults.Object(IgnisSettings.SettingsKey)); // reading never writes
            });
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void A_change_in_either_view_shows_in_the_other_and_persists() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var settings = new IgnisSettings(runtime.Defaults);
        var (page, popover, window) = Open(runtime, settings);
        try
        {
            // the page's menu, seen in the popover
            runtime.OnMain(() => page.CoolAfterMenu.Select("4"));
            runtime.OnMain(() => Settle(window));
            runtime.OnMain(() =>
            {
                Assert.Equal(4, settings.Value.CoolAfterHours);
                Assert.Equal("4", popover.CoolAfterMenu.SelectedId);
                Assert.Equal("4 hours", popover.CoolAfterMenu.ValueLabel.Text);
            });

            // the popover's menu ("Never"), seen in the page
            runtime.OnMain(() => popover.CoolAfterMenu.Select(IgnisCoolingControls.NeverId));
            runtime.OnMain(() => Settle(window));
            runtime.OnMain(() =>
            {
                Assert.Null(settings.Value.CoolAfterHours);
                Assert.Equal(IgnisCoolingControls.NeverId, page.CoolAfterMenu.SelectedId);
            });

            // the popover's switch, seen in the page; and the page's switch, seen in the popover
            runtime.OnMain(() => Toggle(popover.LearnCoolingSwitch).Toggle());
            runtime.OnMain(() => Settle(window));
            runtime.OnMain(() =>
            {
                Assert.False(settings.Value.LearnCooling);
                Assert.Equal(ToggleState.Off, Toggle(page.LearnCoolingSwitch).ToggleState);
            });
            runtime.OnMain(() => Toggle(page.LearnCoolingSwitch).Toggle());
            runtime.OnMain(() => Settle(window));
            runtime.OnMain(() =>
            {
                Assert.True(settings.Value.LearnCooling);
                Assert.Equal(ToggleState.On, Toggle(popover.LearnCoolingSwitch).ToggleState);
            });

            // a change from elsewhere (the settings themselves) reaches both
            runtime.OnMain(() => settings.Update(p => p with { CoolAfterHours = 1, LearnCooling = false }));
            runtime.OnMain(() => Settle(window));
            runtime.OnMain(() =>
            {
                Assert.Equal(("1", "1"), (page.CoolAfterMenu.SelectedId, popover.CoolAfterMenu.SelectedId));
                Assert.Equal((ToggleState.Off, ToggleState.Off), (Toggle(page.LearnCoolingSwitch).ToggleState, Toggle(popover.LearnCoolingSwitch).ToggleState));
            });

            var read = new IgnisSettings(runtime.Defaults).Value; // a fresh reader of the one "ignis" key
            Assert.Equal((1, false), (read.CoolAfterHours, read.LearnCooling));
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void A_value_from_a_settings_file_that_is_not_a_choice_is_listed_in_its_place()
    {
        Assert.Equal(["1", "2", "3", "4", "6", "8", "12", "24", "never"], IgnisCoolingControls.Options(3).Select(o => o.Id));
        Assert.Equal("3 hours", IgnisCoolingControls.Options(3)[2].Text);
        Assert.Equal(["1", "2", "4", "6", "8", "12", "24", "never"], IgnisCoolingControls.Options(null).Select(o => o.Id));
        Assert.True(IgnisCoolingControls.TryHours("never", out var never) && never is null);
        Assert.True(IgnisCoolingControls.TryHours("24", out var day) && day == 24);
        Assert.False(IgnisCoolingControls.TryHours("0", out _));
        Assert.False(IgnisCoolingControls.TryHours("soon", out _));
    }
}
