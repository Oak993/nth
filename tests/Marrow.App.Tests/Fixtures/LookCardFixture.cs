using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// OD-0088 (ED-250): the Appearance page in a 520 wide column (the page column of the Settings window, SPEC 1), in a scope
/// whose appearance follows the theme as the window root's does (WindowManager.swift:151, HostedRoot), with the Windows
/// accent standing for the board's preview colour #0067C0, on the manual frame clock. A frame is 1/60 s.
/// </summary>
public abstract class LookCardFixture(HeadlessSession headless) : Marrow.App.Tests.Views.Settings.SettingsRestFixture(headless)
{
    public const double FrameMs = 1000.0 / 60;

    /// <summary>
    /// Runs <paramref name="check"/> on a Look card. <paramref name="systemDark"/> is the system appearance System follows.
    /// The card's state starts settled.
    /// </summary>
    protected void Card(Action<LookCard, AppModel, AppModelHarness, HostWindow> check, AppTheme theme = AppTheme.Dark, string accent = "windows", bool systemDark = true, bool reduceMotion = false, string windowsAccent = "#0067C0")
    {
        IDisposable? source = null;
        With(() =>
        {
            source = SwiftColor.AccentSource.Override(() => Color.Parse(windowsAccent));
            var made = new ThemeVariantScope { Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = new AppearanceSettingsPage() };
            EnvironmentValues.SetAccessibilityReduceMotion(made, reduceMotion);
            made.Track(() => Marrow.App.Port.AppEnvironment.GetAppModel(made).Settings.Theme, value => made.RequestedThemeVariant = value.Spec().Scheme ?? (systemDark ? ThemeVariant.Dark : ThemeVariant.Light));
            return made;
        }, (view, model, h, w) =>
        {
            Drain(w);
            var card = Assert.Single(Nodes<LookCard>(view));
            Settle(h, w);
            try { check(card, model, h, w); }
            finally { source?.Dispose(); }
        }, seed: model => { model.Settings.Theme = theme; model.Settings.Accent = accent; }, height: 1200);
    }

    /// <summary>Runs frames until nothing moves (at most 2 s).</summary>
    protected static void Settle(AppModelHarness h, HostWindow w)
    {
        for (var i = 0; i < 120; i++)
        {
            Tick(h, FrameMs);
            Drain(w);
        }
    }

    /// <summary>One 1/60 s frame.</summary>
    protected static void Frame(AppModelHarness h) => Tick(h, FrameMs);

    protected static void Press(HostWindow window, Key key) { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); Drain(window); }

    protected static Point Centre(Control control, HostWindow window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    protected static Rect In(Control control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    /// <summary>
    /// A row's height (LEAD RULING, ED-290 (1)): a ChromeRow, its padding + its title's line + 2 + the board's subheadline line (16) +
    /// its padding: the source's 11 + 18 + 2 + 16 + 11 = 58, at the Settings sizes of ED-NEW-textsize-2.
    /// </summary>
    protected static double RowHeight(LookCard card)
    {
        var title = Assert.IsType<Avalonia.Controls.TextBlock>(((StackPanel)((Grid)((Border)card.ThemeRow.Child!).Child!).Children[0]).Children[0]);
        Assert.Equal(Marrow.UI.Chrome.Chrome.SettingsRowTitleSize, title.FontSize);
        Assert.Equal("Theme", title.Text);
        return 2 * Marrow.UI.Chrome.Chrome.SettingsRowVerticalPadding + title.Bounds.Height + 2 + 16;
    }
}

/// <summary>
/// OD-0088 (ED-250): one transition sampled frame by frame: the spec's 60 Hz value (SPEC 6.10, within 0.02 of the travel)
/// and the named CSS curve's exact value (within 0.002 of the travel) at each frame from the action.
/// </summary>
public sealed class LookTrack(string name, Func<double> read, double[] table, Func<double, double> exact, double travel, double quantum = 0)
{
    public string Name { get; } = name;

    public List<double> Seen { get; } = [];

    public void Sample(int k)
    {
        var value = read();
        Seen.Add(value);
        if (k < table.Length)
            Assert.True(Math.Abs(value - table[k]) <= 0.02 * Math.Abs(travel) + 1e-9, $"{Name} frame {k} ({k * LookCardFixture.FrameMs:F0} ms): {value:F4}, spec {table[k]:F4}");
        var t = k * LookCardFixture.FrameMs;
        Assert.True(Math.Abs(value - exact(t)) <= 0.002 * Math.Abs(travel) + quantum + 1e-9, $"{Name} frame {k}: {value:F5}, curve {exact(t):F5}");
    }
}
