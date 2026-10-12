// PORT-ONLY: Tactus, the apps' own icons from the shell (ED-NEW-pcctl-46; DESIGN.md 4.7 "Apps: list with icon", 10.7 the
// strip's "app chips (icon, state dot, x)"). An "exe:" key is its exe; an "aumid:" key is its entry in the shell's Apps
// folder, spelled as an open window of that app spells its AUMID (the key is lower-cased), and as the key spells it when no
// window is open. Each app's icon is read once: an icon that cannot be read is remembered as none, except an app known by
// its id with no window open, which is asked again; a key the shell cannot name is never read. The chat's strip shows each
// app's icon at 16 DIP before its state dot, and Settings shows each allowed app's icon at 32 DIP before its row. The shell
// on fakes; nothing native runs.
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-46")]
public sealed class TactusAppIconsTests(HeadlessSession headless)
{
    private const string Excel = "exe:c:\\office\\excel.exe";
    private const string Calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private static PcWindowInfo CalculatorWindow() =>
        new(1, "Calculator", "ApplicationFrameWindow", 10, "c:\\windows\\system32\\applicationframehost.exe") { Aumid = Calculator };

    [Fact]
    public void The_shell_s_name_for_an_app_is_its_exe_or_its_Apps_folder_entry_spelled_as_its_open_window_spells_it()
    {
        var asked = 0;
        IEnumerable<PcWindowInfo> None() { asked++; return []; }
        IEnumerable<PcWindowInfo> Open() => [new(2, "Book1 - Excel", "XLMAIN", 11, "c:\\office\\excel.exe"), CalculatorWindow()];
        var key = new PcAppIdentity("c:\\windows\\system32\\applicationframehost.exe", Calculator).Key;

        Assert.Equal(("c:\\office\\excel.exe", true), TactusAppIcons.Where(Excel, None));
        Assert.Equal(0, asked);
        Assert.Equal((TactusAppIcons.AppsFolder + Calculator, true), TactusAppIcons.Where(key, Open));
        Assert.Equal((TactusAppIcons.AppsFolder + Calculator.ToLowerInvariant(), false), TactusAppIcons.Where(key, None));
        Assert.Equal("shell:AppsFolder\\", TactusAppIcons.AppsFolder);
        Assert.Null(TactusAppIcons.Where("excel", Open));
        Assert.Null(TactusAppIcons.Where("aumid:", Open));
        Assert.Null(TactusAppIcons.Shell("c:\\no\\such\\folder\\app.exe"));
    }

    [Fact]
    public void Each_app_s_icon_is_read_once_and_an_app_known_by_its_id_with_no_window_is_asked_again()
    {
        var excel = new PcFakeIcon("excel");
        var calculator = new PcFakeIcon("calculator");
        var loads = new List<string>();
        var open = false;
        var key = new PcAppIdentity("c:\\windows\\system32\\applicationframehost.exe", Calculator).Key;
        var icons = new TactusAppIcons(
            k => TactusAppIcons.Where(k, () => open ? [CalculatorWindow()] : []),
            name =>
            {
                loads.Add(name);
                return name == "c:\\office\\excel.exe" ? excel : name == TactusAppIcons.AppsFolder + Calculator ? calculator : null;
            });

        Assert.Same(excel, icons.Icon(Excel));
        Assert.Same(excel, icons.Icon(Excel));
        Assert.Null(icons.Icon("exe:c:\\tools\\remote.exe"));
        Assert.Null(icons.Icon("exe:c:\\tools\\remote.exe"));
        Assert.Null(icons.Icon("excel"));
        Assert.Equal(["c:\\office\\excel.exe", "c:\\tools\\remote.exe"], loads);

        // the key's own spelling finds nothing while no window is open: asked again, and found once a window is
        Assert.Null(icons.Icon(key));
        Assert.Null(icons.Icon(key));
        open = true;
        Assert.Same(calculator, icons.Icon(key));
        open = false;
        Assert.Same(calculator, icons.Icon(key));
        Assert.Equal(5, loads.Count);
        Assert.Equal(TactusAppIcons.AppsFolder + Calculator, loads[^1]);
    }

    [Fact]
    public void The_strip_s_chips_show_each_app_s_icon_before_its_state_dot() => headless.OnUiThread(() =>
    {
        var excel = new PcFakeIcon("excel");
        using var scope = PcAppIcons.Source.Override(new TactusAppIcons(k => (k, true), name => name == Excel ? excel : null));
        var clock = new PcManualClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var broker = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), broker, clock);
        var strip = new TactusStrip("chat-1", () => feed, reduceMotion: () => true);
        var window = new Avalonia.Controls.Window { Width = 800, Height = 200, Content = strip };
        window.Show();
        try
        {
            using var excelJoined = pause.Join("chat-1", Excel, new PcWaitState());
            using var remoteJoined = pause.Join("chat-1", "exe:c:\\tools\\remote.exe", new PcWaitState());
            Dispatcher.UIThread.RunJobs();
            var chips = strip.AppButtons.Select(b => Assert.IsType<StackPanel>(b.Parent)).ToList();
            Assert.Equal(2, chips.Count);
            var drawn = Assert.IsType<Image>(chips[0].Children[0]);
            Assert.Same(excel, drawn.Source);
            Assert.Equal(16, drawn.Width);
            Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(chips[0].Children[1]);
            var lettered = Assert.IsType<Border>(chips[1].Children[0]);
            Assert.Equal(16, lettered.Width);
            Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(chips[1].Children[1]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Settings_shows_each_allowed_app_s_icon_before_its_row() => headless.OnUiThread(() =>
    {
        var excel = new PcFakeIcon("excel");
        using var scope = PcAppIcons.Source.Override(new TactusAppIcons(k => (k, true), name => name == Excel ? excel : null));
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var grants = new PcGrants(new InMemorySettingsStore());
        var value = PcSettings.Default;
        var page = new PcControlSettingsPage(new PcSettingsPageParts { Read = () => value, Write = next => value = next, Grants = grants, TypesCharacter = _ => false });
        var window = new Avalonia.Controls.Window { Width = 600, Height = 2400, Content = new ScrollViewer { Content = new Border { Width = 520, Child = page } } };
        window.Show();
        try
        {
            grants.Grant("chat-1", new PcAppIdentity("c:\\office\\excel.exe"), "Excel", use: true, PcGrantScope.Always);
            grants.Grant("chat-1", new PcAppIdentity("c:\\office\\outlook.exe"), "Outlook", use: false, PcGrantScope.Chat);
            for (var i = 0; i < 4; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }

            var rows = page.GetVisualDescendants().OfType<DockPanel>().Where(d => d.Children.Count == 2 && d.Children[1] is ChromeRow).ToList();
            Assert.Equal(2, rows.Count);
            var byName = rows.ToDictionary(r => r.Children[1].GetVisualDescendants().OfType<TextBlock>().First().Text!);
            var drawn = Assert.IsType<Image>(byName["Excel"].Children[0]);
            Assert.Same(excel, drawn.Source);
            Assert.Equal(32, drawn.Width);
            Assert.Equal(Dock.Left, DockPanel.GetDock(drawn));
            var lettered = Assert.IsType<Border>(byName["Outlook"].Children[0]);
            Assert.Equal(32, lettered.Width);
            Assert.Equal("O", Assert.IsType<TextBlock>(lettered.Child).Text);
        }
        finally
        {
            window.Close();
        }
    });
}
