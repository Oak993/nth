using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Panes;

// PORT-ONLY: web surfaces W3a, what a pane kind puts in its workspace card (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 3.1, 3.12 "Header (36 DIP): kind glyph, title, optional title menu (version, tabs), then trailing actions";
// ED-NEW-web-w3a-1). No Swift counterpart. The workspace draws the card, its header and its motion; each pane unit
// (Visus W8a, Tabula W14, Side chat W6, Background tasks W5, Changes and Terminal W4, the App pane W19) registers a
// factory for its kind. A kind with no factory cannot be opened and is never drawn, so a thread saved with a pane this
// build does not have opens without it, and no empty card ever shows.

namespace Marrow.App.Views.Panes;

/// <summary>A pane's body and its header's words and actions. The pane updates them as it changes (a new version chip).</summary>
public sealed class PaneContent
{
    public PaneContent(Control body, string? title = null, string? subtitle = null, IReadOnlyList<PaneHeaderAction>? actions = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
        Title = title;
        Subtitle = subtitle;
        Actions = actions ?? [];
    }

    /// <summary>The pane's body, under the header.</summary>
    public Control Body { get; }

    /// <summary>The header title; null uses the kind's title ("Background tasks", "Visus").</summary>
    public string? Title { get; private set; }

    /// <summary>The title's detail after a middle dot ("Markdown", "3 files").</summary>
    public string? Subtitle { get; private set; }

    /// <summary>The pane's own header actions, in reading order (Expand and Close are the workspace's).</summary>
    public IReadOnlyList<PaneHeaderAction> Actions { get; private set; }

    /// <summary>A menu the header's title opens (Tabula's artifacts, web W14); null for a plain title.</summary>
    public Action? TitleMenu { get; private set; }

    /// <summary>The control that takes keyboard focus when the person opens the pane (Side chat's composer); null: focus stays where it was.</summary>
    public Control? FocusTarget { get; set; }

    /// <summary>Raised after <see cref="Update"/>.</summary>
    public event Action? Changed;

    public void Update(string? title, string? subtitle, IReadOnlyList<PaneHeaderAction>? actions = null)
    {
        Title = title;
        Subtitle = subtitle;
        if (actions is not null) Actions = actions;
        Changed?.Invoke();
    }

    /// <summary>Gives the title a menu (or takes it away with null).</summary>
    public void SetTitleMenu(Action? menu)
    {
        TitleMenu = menu;
        Changed?.Invoke();
    }
}

/// <summary>Makes a pane kind's content for a thread.</summary>
public delegate PaneContent? PaneContentFactory(PaneKind kind, ThreadRuntime runtime);

/// <summary>The pane kinds this build can draw, each with its factory.</summary>
public static class PaneContents
{
    private static readonly Dictionary<PaneKind, PaneContentFactory> Factories = [];
    private static readonly object Gate = new();

    /// <summary>Registers (or replaces) the factory of <paramref name="kind"/>. Pane units call it at start-up.</summary>
    public static void Register(PaneKind kind, PaneContentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!PaneKinds.IsGridKind(kind) && kind != PaneKind.SideChat) throw new ArgumentException("The chat is not a pane.", nameof(kind));
        lock (Gate) Factories[kind] = factory;
    }

    /// <summary>Whether <paramref name="kind"/> can be opened in this build.</summary>
    public static bool IsAvailable(PaneKind kind)
    {
        lock (Gate) return Factories.ContainsKey(kind);
    }

    /// <summary>The content of <paramref name="kind"/> for a thread, or null when the kind has no factory.</summary>
    public static PaneContent? Create(PaneKind kind, ThreadRuntime runtime)
    {
        PaneContentFactory? factory;
        lock (Gate) Factories.TryGetValue(kind, out factory);
        return factory?.Invoke(kind, runtime);
    }
}
