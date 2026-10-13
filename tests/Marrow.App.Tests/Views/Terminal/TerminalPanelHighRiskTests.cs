using System.Reflection;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), :199-222,60.
// The fake-shell half of the high-risk terminal panel checks (TerminalPanelRig.Run, no real shell), kept in a managed class so the
// ordinary !~Native sweeps still run it; the real-shell facts stay in TerminalPanelNativeHighRiskTests (ED-NEW-web-review-30).
[Collection("Headless")]
public sealed class TerminalPanelHighRiskTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-TERM-032")] // App/Views/Terminal/TerminalPanel.swift:199-222,60: one monitor per hosted session
    public void Switching_tabs_repeatedly_keeps_exactly_one_escape_listener_and_removing_the_panel_removes_it() => TerminalPanelRig.Run(headless, rig =>
    {
        var baseline = StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount");
        var directory = TerminalPanelRig.Folder("escape listeners");
        rig.Step(() =>
        {
            for (var i = 0; i < 3; i++) rig.App.Terminals.Open(rig.Thread.Id, directory);
        });
        var (host, _, window) = rig.Show(directory, 600, 300);
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id).ToArray();
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        // The host's counter is its own bookkeeping; what proves the removal is what the views really carry. Exactly one
        // monitor exists, on the hosted session's view, and none on a view whose host was released.
        void AssertOneMonitorOn(TerminalSession hosted)
        {
            foreach (var session in sessions)
                Assert.Equal(session.Id == hosted.Id ? 1 : 0, RealEscapeSubscriptions(session));
        }
        AssertOneMonitorOn(rig.App.Terminals.Selected(rig.Thread.Id)!);

        // :60 .id(selected.id): every selection change makes a new host and releases the old one, so the count never grows.
        for (var turn = 0; turn < 6; turn++)
        {
            var next = sessions[turn % 3];
            rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = next.Id));
            Assert.Equal(next.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
            Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
            AssertOneMonitorOn(next);
        }

        // A tap on a tab switches the same way.
        var tabs = LogicalQuery.SelfAndDescendants<TerminalTab>(host).ToArray();
        var target = tabs[0];
        var at = target.TranslatePoint(new Point(4, target.Bounds.Height / 2), window)!.Value;
        rig.Step(() => window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(at, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        AssertOneMonitorOn(sessions[0]);

        // Closing the selected tab hosts another session, still one listener; closing the others leaves the empty state.
        rig.Step(() => rig.App.Terminals.Close(sessions[0]));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(0, RealEscapeSubscriptions(sessions[0]));
        Assert.Equal(1, sessions.Sum(RealEscapeSubscriptions));
        rig.Step(() => rig.App.Terminals.Close(sessions[1]));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(1, sessions.Sum(RealEscapeSubscriptions));
        rig.Step(() => rig.App.Terminals.Close(sessions[2]));
        Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalHost>(host));
        Assert.Equal(baseline, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount")); // no hosted session, no monitor
        Assert.Equal(0, sessions.Sum(RealEscapeSubscriptions));

        rig.Step(() => rig.App.Terminals.Open(rig.Thread.Id, directory));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        var reopened = rig.App.Terminals.Sessions(rig.Thread.Id).Single();
        Assert.Equal(1, RealEscapeSubscriptions(reopened));
        rig.Close(window); // the drawer is gone: the host is destroyed
        Assert.Equal(baseline, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(0, RealEscapeSubscriptions(reopened));
    });

    private static int StaticMetric(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(null));
    }

    // The KeyDown subscriptions that a TerminalHost really holds on the session's view, read from Avalonia's own handler
    // table (not from the host's bookkeeping counter): it finds the table by its type, so a changed layout fails loudly.
    private static int RealEscapeSubscriptions(TerminalSession session)
    {
        var table = typeof(Interactive)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && field.FieldType.GetGenericArguments()[0] == typeof(RoutedEvent))
            .ToArray();
        Assert.Single(table);
        var handlers = table[0].GetValue(session.View) as System.Collections.IDictionary;
        if (handlers is null || !handlers.Contains(InputElement.KeyDownEvent)) return 0;
        var count = 0;
        foreach (var subscription in (System.Collections.IEnumerable)handlers[InputElement.KeyDownEvent]!)
        {
            var delegates = subscription.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(subscription) as Delegate)
                .ToArray();
            Assert.NotEmpty(delegates);
            if (delegates.Any(handler => handler?.Target is TerminalHost)) count++;
        }
        return count;
    }
}
