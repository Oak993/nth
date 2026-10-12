using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.11 "Migration and bad data" (web surfaces W3c; ED-NEW-web-w3c-1): how a thread's workspace starts. An old thread
// with no saved workspace maps the old ThreadRuntime flags in once (the terminal drawer, Changes) and starts from the
// ratios the user set last; a saved one is used as it is.
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class PaneWorkspaceMigrationTests
{
    [Fact]
    public void A_thread_with_no_saved_workspace_maps_the_old_flags_in_once()
    {
        var neither = PaneWorkspaceMigration.Initial(null, false, false, null);
        Assert.Equal(PaneWorkspaceState.Default, neither.State);
        Assert.True(neither.IsEmpty);

        var terminal = PaneWorkspaceMigration.Initial(null, true, false, null).State;
        Assert.True(terminal.IsTerminalInDrawer);
        Assert.False(terminal.IsOpen(PaneKind.Changes));

        var changes = PaneWorkspaceMigration.Initial(null, false, true, null).State;
        Assert.False(changes.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Changes }, changes.GridPanes);

        var both = PaneWorkspaceMigration.Initial(null, true, true, null).State;
        Assert.True(both.IsTerminalInDrawer);
        Assert.Equal(new[] { PaneKind.Changes }, both.GridPanes);
        Assert.Equal(PaneWorkspaceState.FromLegacyFlags(true, true), both);
    }

    [Fact]
    public void A_thread_with_no_saved_workspace_starts_from_the_ratios_the_user_set_last()
    {
        var last = new ValueMap<string, double>(new Dictionary<string, double> { ["chat"] = 0.42, ["cols"] = 0.6, ["bad"] = double.NaN });
        var state = PaneWorkspaceMigration.Initial(null, false, false, last).State;
        Assert.Equal(0.42, state.Ratios["chat"]);
        Assert.Equal(0.6, state.Ratios["cols"]);
        Assert.False(state.Ratios.ContainsKey("bad"));
        // Opening a pane keeps them: the layout solves with the user's last chat share.
        var layout = PaneLayout.Solve(state.Open(PaneKind.Browser), 1600, 900);
        Assert.InRange(layout.Chat.Width, 0.42 * (1600 - PaneKinds.Gap) - 1, 0.42 * (1600 - PaneKinds.Gap) + 1);
        Assert.Equal(PaneWorkspaceState.Default, PaneWorkspaceMigration.Initial(null, false, false, ValueMap<string, double>.Empty).State);
    }

    [Fact]
    public void A_saved_workspace_is_used_as_it_is_and_the_flags_and_last_ratios_are_ignored()
    {
        var saved = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(PaneKind.Artifacts).WithRatio("chat", 0.3) }
            .WithPaneData("artifact", "{\"id\":\"a1\"}");
        var last = new ValueMap<string, double>(new Dictionary<string, double> { ["chat"] = 0.9 });
        var started = PaneWorkspaceMigration.Initial(saved, true, true, last);
        Assert.Equal(saved, started);
        Assert.False(started.State.IsTerminalInDrawer);
        Assert.False(started.State.IsOpen(PaneKind.Changes));
        Assert.Equal(0.3, started.State.Ratios["chat"]);
    }

    [Fact]
    public void A_saved_workspace_with_bad_parts_starts_without_them()
    {
        // A state built by hand with a repeated pane and the chat in the order (a file could be edited into it).
        var bad = new StoredWorkspace
        {
            State = PaneWorkspaceState.Default with
            {
                Order = new ValueList<PaneSlot>([PaneSlot.Of(PaneKind.Chat), PaneSlot.Of(PaneKind.Browser), PaneSlot.Of(PaneKind.Browser)]),
                Expanded = PaneKind.Artifacts,
            },
        };
        var started = PaneWorkspaceMigration.Initial(bad, false, false, null).State;
        Assert.Equal(new[] { PaneKind.Browser }, started.GridPanes);
        Assert.Null(started.Expanded);
    }
}
