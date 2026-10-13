using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W4, the terminal as a pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1 "Terminal | drawer |
// under the chat column | stays the chat's drawer by default; can be dragged into the grid", 3.6 "The terminal can be dragged
// out of its drawer into the grid and back", 3.7 "Terminal | Ctrl+J (existing)", content scope: "Ctrl+D (EOF in the terminal),
// Ctrl+\ (SIGQUIT in the terminal)"; the canvas's terminal cell: the kind glyph, the shell's title, Close; 14 W4 "terminal
// adapter in TerminalPanel.cs"; ED-NEW-web-w4-1). No Swift counterpart. The adapter hosts the thread's terminal sessions in a
// workspace cell. The shell is not restarted by a move: a session owns one long-lived view (TerminalSession.View), and the
// drawer and the cell take it from each other (TerminalHostContainer.HostedView). The drawer's header row (tabs, New terminal,
// Hide) becomes the pane's header (the shell's title, New terminal, Move to the drawer, Close) with a slim tab row under it
// when there is more than one shell.

namespace Marrow.App.Views.Terminal;

/// <summary>The terminal pane's content.</summary>
public static class TerminalPane
{
    /// <summary>Makes the terminal drawable in the workspace's grid (the app's start-up calls it; tests call it or replace the factory).</summary>
    public static void Register() => PaneContents.Register(PaneKind.Terminal, (_, runtime) => Create(runtime));

    /// <summary>The content of a thread's terminal pane: the shell's title, New terminal and Move to the drawer, and the shells.</summary>
    public static PaneContent Create(ThreadRuntime runtime)
    {
        var body = new TerminalPaneBody(runtime);
        var content = new PaneContent(body, PaneKinds.Title(PaneKind.Terminal), null,
        [
            new PaneHeaderAction(UiText.Get("panes.terminal.new_terminal"), body.NewTerminal, PaneGlyphKind.Plus),
            new PaneHeaderAction(UiText.Get("panes.terminal.move_to_drawer"), () => runtime.Workspace = runtime.Workspace.MoveTerminalToDrawer(), PaneGlyphKind.DockBottom),
        ]);
        body.TitleChanged += title => content.Update(string.IsNullOrWhiteSpace(title) ? PaneKinds.Title(PaneKind.Terminal) : title, null);
        return content;
    }

    /// <summary>The directory a thread's shells open in: its worktree, else its project, else the home folder (ChatView's rule).</summary>
    internal static string DirectoryOf(AppModel model, ThreadRuntime runtime)
    {
        var thread = model.Thread(runtime.ThreadID);
        return thread?.WorktreePath ?? (thread is null ? null : model.Project(thread.ProjectID)?.Path) ?? LoginEnvironment.HomeDirectory;
    }
}

/// <summary>The terminal pane's body: a slim tab row when there are several shells, and the selected shell.</summary>
public sealed class TerminalPaneBody : Decorator
{
    /// <summary>The slim tab row's height (the drawer's header is 38; this is its second row, under the pane's own header).</summary>
    public const double StripHeight = 30;

    private readonly ThreadRuntime runtime;
    private readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border strip;
    private readonly Border area = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private AppModel? model;
    private string directory = "";

    public TerminalPaneBody(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        strip = new Border
        {
            Height = StripHeight,
            Margin = new Thickness(8, 0, 6, 0),
            IsVisible = false,
            Child = new ScrollViewer
            {
                Content = tabStrip,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(strip, 0);
        Grid.SetRow(area, 1);
        layout.Children.Add(strip);
        layout.Children.Add(area);
        Child = layout;

        this.Track(() => AppEnvironment.GetAppModel(this), attached =>
        {
            model = attached;
            directory = TerminalPane.DirectoryOf(attached, runtime);
            attached.Terminals.EnsureTerminal(runtime.ThreadID, directory);
        });
        this.Track(
            () =>
            {
                var current = AppEnvironment.GetAppModel(this);
                return (current, Sessions: current.Terminals.Sessions(runtime.ThreadID).ToArray(), Selected: current.Terminals.Selected(runtime.ThreadID));
            },
            state => Rebuild(state.current, state.Sessions, state.Selected));
        this.Track(
            () => AppEnvironment.GetAppModel(this).Terminals.Selected(runtime.ThreadID)?.Title,
            title => TitleChanged?.Invoke(title));
        ActualThemeVariantChanged += (_, _) => ApplyArea();
    }

    /// <summary>The selected shell's title changed (or the selection did); null with no shell.</summary>
    public event Action<string?>? TitleChanged;

    /// <summary>The tab row (shown when there is more than one shell).</summary>
    internal Border Strip => strip;

    /// <summary>The terminal area under the tab row.</summary>
    internal Border Area => area;

    /// <summary>New terminal: another shell in the thread's directory, selected.</summary>
    public void NewTerminal() => model?.Terminals.Open(runtime.ThreadID, directory);

    private void Rebuild(AppModel current, TerminalSession[] sessions, TerminalSession? selected)
    {
        model = current;
        tabStrip.Children.Clear();
        foreach (var session in sessions)
            tabStrip.Children.Add(new TerminalTab(
                session,
                session.Id == selected?.Id,
                () => current.Terminals.Selection = current.Terminals.Selection.Mutating(map => map[runtime.ThreadID] = session.Id),
                () => current.Terminals.Restart(session),
                () => current.Terminals.Close(session)));
        strip.IsVisible = sessions.Length > 1;
        ApplyArea();
        if (selected is null)
        {
            area.Padding = new Thickness(0);
            area.Child = TerminalPanel.EmptyState(this, current, runtime, directory);
            return;
        }
        area.Padding = new Thickness(10, 4, 4, 6);
        selected.ApplyAppearance(isDark: SwiftTheme.IsDark(this));
        // A pane stays until it is closed, so Escape always reaches the shell (null: no "close at the prompt").
        area.Child = new TerminalHost(selected, runtime, onEscapeAtPrompt: null);
    }

    // The area is the terminal's own colour, edge to edge, so the shell's margin reads as part of the screen under the glass header.
    private void ApplyArea() => area.Background = new SolidColorBrush(TerminalPanel.PanelColor(SwiftTheme.IsDark(this)));
}
