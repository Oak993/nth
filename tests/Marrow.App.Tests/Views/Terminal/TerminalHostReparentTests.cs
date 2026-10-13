using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792) TerminalHost :191-251 (attach, updateNSView) and
// TerminalStore.swift:44-57 applyAppearance. Real sessions and real windows; the shell behind a session is a stand-in,
// except in the scrollback test, which reads the output of a real isolated PowerShell in a ConPTY.
[Collection("Headless")]
public sealed class TerminalHostReparentTests(HeadlessSession headless)
{
    private static TerminalHostContainer[] Containers(Control host) =>
        LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).ToArray();

    private static Color Resolved(SwiftColor? color, ThemeVariant theme) => Assert.IsType<SwiftColor>(color).Resolve(theme);

    private static void SelectTab(TerminalPanelRig rig, AvaloniaWindow window, TerminalTab tab)
    {
        var padding = tab.TranslatePoint(new Point(4, tab.Bounds.Height / 2), window)!.Value;
        rig.Step(() => window.MouseDown(padding, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(padding, MouseButton.Left, RawInputModifiers.None));
    }

    [Fact]
    [Trait("Req", "PR-TERM-028")] // App/Views/Terminal/TerminalPanel.swift:188,225-250
    public void Switching_tabs_moves_each_sessions_one_view_into_the_host_container_and_keeps_its_scrollback_Native() => TerminalPanelRig.RunWithRealShell(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("reparent tabs");
        TerminalSession one = null!;
        TerminalSession two = null!;
        rig.Step(() =>
        {
            one = rig.App.Terminals.Open(rig.Thread.Id, directory, command: "Write-Output ('ONE' + 'MARK'); Start-Sleep -Seconds 60");
            two = rig.App.Terminals.Open(rig.Thread.Id, directory, command: "Write-Output ('TWO' + 'MARK'); Start-Sleep -Seconds 60");
        });
        var (host, _, window) = rig.Show(directory, 600, 400);
        var oneView = one.View;
        var twoView = two.View;
        Assert.True(UiPump.Until(() => one.View.ScrollbackText.Contains("ONEMARK", StringComparison.Ordinal)
            && two.View.ScrollbackText.Contains("TWOMARK", StringComparison.Ordinal), TimeSpan.FromSeconds(30)), "the shells did not print their markers");

        var first = Assert.Single(Containers(host));
        Assert.Same(twoView, first.HostedView); // the selected (last opened) session

        // Select the first tab: ITS view, the object the store already owns, is now hosted; nothing is rebuilt.
        SelectTab(rig, window, LogicalQuery.SelfAndDescendants<TerminalTab>(host).First());
        var second = Assert.Single(Containers(host));
        Assert.Same(oneView, second.HostedView);
        Assert.Same(oneView, one.View);
        Assert.Contains("ONEMARK", one.View.ScrollbackText, StringComparison.Ordinal); // scrollback survives
        // :243-249 sized to the container's bounds and autoresizing in both axes.
        Assert.Equal(second.Bounds.Size, ((Control)oneView).Bounds.Size);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, ((Control)oneView).HorizontalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, ((Control)oneView).VerticalAlignment);

        // And back: the second session's same view moves into the newest container; the container that held it
        // before has lost its subview, so the view is never in two places.
        SelectTab(rig, window, LogicalQuery.SelfAndDescendants<TerminalTab>(host).Last());
        var third = Assert.Single(Containers(host));
        Assert.Same(twoView, third.HostedView);
        Assert.Same(twoView, two.View);
        Assert.Null(first.HostedView); // the previous container loses its subview
        Assert.Contains("TWOMARK", two.View.ScrollbackText, StringComparison.Ordinal);
        Assert.Contains("ONEMARK", one.View.ScrollbackText, StringComparison.Ordinal);

        // A third time, the other way: still the same two view objects, each in exactly one container.
        SelectTab(rig, window, LogicalQuery.SelfAndDescendants<TerminalTab>(host).First());
        var fourth = Assert.Single(Containers(host));
        Assert.Same(oneView, fourth.HostedView);
        Assert.Null(second.HostedView); // the container that held it before loses it
        Assert.Equal(fourth.Bounds.Size, ((Control)oneView).Bounds.Size);
    });

    [Fact]
    [Trait("Req", "PR-TERM-028")] // App/Views/Terminal/TerminalPanel.swift:225-228,242-250: removeFromSuperview before addSubview
    public void A_second_panel_for_the_same_thread_takes_the_view_from_the_first_instead_of_failing_or_cloning_it() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("reparent panels");
        var (firstHost, _, _) = rig.Show(directory);
        var session = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        var view = session.View;
        var firstContainer = Assert.Single(Containers(firstHost));
        Assert.Same(view, firstContainer.HostedView);

        // The drawer is hidden and shown again, or the thread is switched away and back: a fresh panel is built for
        // the same session while the old one is still around.
        var (secondHost, _, _) = rig.Show(directory);
        var secondContainer = Assert.Single(Containers(secondHost));
        Assert.Same(view, secondContainer.HostedView);
        Assert.Same(view, session.View);
        Assert.Null(firstContainer.HostedView);
        Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
    });

    [Fact]
    [Trait("Req", "PR-TERM-029")] // App/Views/Terminal/TerminalStore.swift:43-57
    public void Applying_the_appearance_does_nothing_unless_dark_or_light_actually_changed_and_the_first_call_always_applies() => TerminalPanelRig.Run(headless, rig =>
    {
        var light = ThemeVariant.Light;
        var dark = ThemeVariant.Dark;
        var sentinel = new SwiftColor(0.5, 1);
        foreach (var firstIsDark in new[] { false, true })
        {
            var session = new TerminalSession(rig.Thread.Id, TerminalPanelRig.Folder("appearance guard"), "Terminal", null);
            try
            {
                var first = firstIsDark ? dark : light;
                Assert.Null(session.View.NativeBackgroundColor); // nothing applied yet: appliedDark starts nil
                session.ApplyAppearance(firstIsDark);
                // :50-51 the first call applies whichever value it is.
                Assert.Equal(Resolved(firstIsDark ? new SwiftColor(0.08, 1) : new SwiftColor(0.985, 1), first), Resolved(session.View.NativeBackgroundColor, first));
                Assert.Equal(Resolved(firstIsDark ? new SwiftColor(0.9, 1) : new SwiftColor(0.12, 1), first), Resolved(session.View.NativeForegroundColor, first));

                // Same value again: the guard returns first, so a colour set behind its back survives (nothing is re-applied).
                session.View.NativeBackgroundColor = sentinel;
                session.View.NativeForegroundColor = sentinel;
                session.View.CaretColor = sentinel;
                session.View.SelectedTextBackgroundColor = sentinel;
                session.ApplyAppearance(firstIsDark);
                session.ApplyAppearance(firstIsDark);
                Assert.Same(sentinel, session.View.NativeBackgroundColor);
                Assert.Same(sentinel, session.View.NativeForegroundColor);
                Assert.Same(sentinel, session.View.CaretColor);
                Assert.Same(sentinel, session.View.SelectedTextBackgroundColor);

                // A real change applies all four colours (:50-56).
                var next = firstIsDark ? light : dark;
                session.ApplyAppearance(!firstIsDark);
                Assert.Equal(Resolved(firstIsDark ? new SwiftColor(0.985, 1) : new SwiftColor(0.08, 1), next), Resolved(session.View.NativeBackgroundColor, next));
                Assert.Equal(Resolved(firstIsDark ? new SwiftColor(0.12, 1) : new SwiftColor(0.9, 1), next), Resolved(session.View.NativeForegroundColor, next));
                Assert.Same(Chrome.AccentNSColor, session.View.CaretColor);
                Assert.Equal(Resolved(Chrome.AccentNSColor.Opacity(0.3), next), Resolved(session.View.SelectedTextBackgroundColor, next));

                // And the new value is then guarded in turn.
                session.View.NativeBackgroundColor = sentinel;
                session.ApplyAppearance(!firstIsDark);
                Assert.Same(sentinel, session.View.NativeBackgroundColor);
            }
            finally { rig.Step(() => session.Terminate()); }
        }
    });

    [Fact]
    [Trait("Req", "PR-TERM-029")] // App/Views/Terminal/TerminalPanel.swift:225-227 updateNSView, :242-250
    public void A_hosted_terminal_takes_the_new_colours_when_the_window_changes_between_light_and_dark() => TerminalPanelRig.Run(headless, rig =>
    {
        var (_, _, window) = rig.Show(TerminalPanelRig.Folder("appearance live"));
        var session = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        rig.Step(() => window.RequestedThemeVariant = ThemeVariant.Light);
        rig.Step(() => TerminalPanelRig.Settle(window));
        Assert.Equal(Resolved(new SwiftColor(0.985, 1), ThemeVariant.Light), Resolved(session.View.NativeBackgroundColor, ThemeVariant.Light));

        rig.Step(() => window.RequestedThemeVariant = ThemeVariant.Dark);
        rig.Step(() => TerminalPanelRig.Settle(window));
        Assert.Equal(Resolved(new SwiftColor(0.08, 1), ThemeVariant.Dark), Resolved(session.View.NativeBackgroundColor, ThemeVariant.Dark));
        Assert.Equal(Resolved(new SwiftColor(0.9, 1), ThemeVariant.Dark), Resolved(session.View.NativeForegroundColor, ThemeVariant.Dark));

        rig.Step(() => window.RequestedThemeVariant = ThemeVariant.Light);
        rig.Step(() => TerminalPanelRig.Settle(window));
        Assert.Equal(Resolved(new SwiftColor(0.985, 1), ThemeVariant.Light), Resolved(session.View.NativeBackgroundColor, ThemeVariant.Light));
        Assert.Equal(Resolved(new SwiftColor(0.12, 1), ThemeVariant.Light), Resolved(session.View.NativeForegroundColor, ThemeVariant.Light));
    });

    [Fact]
    [Trait("Req", "PR-TERM-028")] // App/Views/Terminal/TerminalPanel.swift:248 session.applyAppearance(isDark:) on attach
    public void The_appearance_is_applied_the_moment_a_tab_s_view_is_attached() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("appearance on attach");
        rig.Step(() =>
        {
            rig.App.Terminals.Open(rig.Thread.Id, directory);
            rig.App.Terminals.Open(rig.Thread.Id, directory);
        });
        var (host, _, window) = rig.Show(directory);
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id);
        var theme = host.ActualThemeVariant;
        Assert.Null(sessions[0].View.NativeBackgroundColor); // never hosted yet, so never themed
        SelectTab(rig, window, LogicalQuery.SelfAndDescendants<TerminalTab>(host).First());
        var expected = theme == ThemeVariant.Dark ? new SwiftColor(0.08, 1) : new SwiftColor(0.985, 1);
        Assert.Equal(Resolved(expected, theme), Resolved(sessions[0].View.NativeBackgroundColor, theme));
    });
}
