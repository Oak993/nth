// PORT-ONLY: web surfaces W19, the App pane (DESIGN.md 7.6 "Expanded: the widget moves into an App pane in the workspace (3.1),
// with a header (server name, title, Pop out, Close)"; "Fullscreen ... maps to Expand: the App pane fills the workspace with the
// trust strip still visible (8.2)"; canvas "App · shipyard", Back inline; ED-NEW-web-w19-4). No Swift counterpart. A thread has
// one App pane; it shows the app the thread last expanded. Under the pane's header sits the trust strip, "Sandboxed · from
// shipyard", outside the web rectangle. The header offers Pop out and Back inline; its close button closes the pane and the
// app goes back to its card. A pane opened with no app to show (a thread whose app is gone) shows the thread's newest app, or
// closes itself.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Views.Panes;

public sealed class AppPane : Decorator
{
    public const double TrustHeight = 28;
    public const string PopOutText = "Pop out";
    public const string BackInlineText = "Back inline";

    private readonly ThreadRuntime runtime;
    private readonly Decorator slot = new() { Name = "WebSlot" };
    private readonly TextBlock trustText;
    private PaneContent? content;
    private McpAppSession? shown;

    public AppPane(ThreadRuntime runtime)
    {
        this.runtime = runtime;
        var shield = new PaneGlyph(PaneGlyphKind.Shield, 12) { VerticalAlignment = VerticalAlignment.Center };
        shield.Foreground = PaneTokens.Text3.Brush(shield);
        trustText = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        trustText.Foreground = PaneTokens.Text3.Brush(trustText);
        var trust = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Height = TrustHeight,
            Margin = new Thickness(12, 0, 12, 0),
            Children = { shield, trustText },
        };
        var body = new Border { Margin = new Thickness(6, 0, 6, 6), CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = slot };
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { trust, body } };
        Grid.SetRow(body, 1);
        Child = column;
        slot.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) shown?.UpdateContext(slot);
        };
    }

    /// <summary>Registers the App pane's factory (start-up).</summary>
    public static void Register() => PaneContents.Register(PaneKind.App, (_, runtime) =>
    {
        var pane = new AppPane(runtime);
        pane.content = new PaneContent(pane, PaneKinds.Title(PaneKind.App), null, pane.Actions());
        pane.Show(McpAppSessions.Expanded(runtime.ThreadID) ?? McpAppSessions.In(runtime.ThreadID).LastOrDefault());
        return pane.content;
    });

    /// <summary>The app the pane shows.</summary>
    public McpAppSession? Shown => shown;

    /// <summary>The trust strip's words.</summary>
    public string TrustText => trustText.Text ?? "";

    /// <summary>Where the app's host goes.</summary>
    public Decorator Slot => slot;

    /// <summary>The header's actions: Pop out, Back inline.</summary>
    internal IReadOnlyList<PaneHeaderAction> Actions() =>
    [
        new PaneHeaderAction(PopOutText, () => shown?.SetMode(McpAppDisplayMode.PopOut), PaneGlyphKind.PopOut),
        new PaneHeaderAction(BackInlineText, () => shown?.SetMode(McpAppDisplayMode.Inline), PaneGlyphKind.Inline),
    ];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        McpAppSessions.ExpandedChanged += OnExpandedChanged;
        Show(McpAppSessions.Expanded(runtime.ThreadID) ?? shown);
        if (shown is null)
        {
            // Nothing to show: the pane goes (a card with an empty body never stays).
            MainActor.Post(() =>
            {
                if (shown is null && runtime.Workspace.IsOpen(PaneKind.App)) runtime.Workspace = runtime.Workspace.Close(PaneKind.App);
            });
            return;
        }

        shown.AttachPane(slot);
        if (shown.Mode != McpAppDisplayMode.Pane) shown.SetMode(McpAppDisplayMode.Pane);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        McpAppSessions.ExpandedChanged -= OnExpandedChanged;
        shown?.DetachPane(slot);
    }

    private void OnExpandedChanged(Guid thread)
    {
        if (thread != runtime.ThreadID) return;
        if (McpAppSessions.Expanded(thread) is not { } next || next == shown) return;
        shown?.DetachPane(slot);
        Show(next);
        next.AttachPane(slot);
    }

    private void Show(McpAppSession? session)
    {
        shown = session;
        trustText.Text = session is null ? "" : McpAppTrust.Sandboxed(session.Server);
        content?.Update(PaneKinds.Title(PaneKind.App), session?.Server, Actions());
    }
}
