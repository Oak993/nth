// PORT-ONLY: web surfaces W19, the App pane (DESIGN.md 7.6 "Expanded: the widget moves into an App pane in the workspace (3.1),
// with a header (server name, title, Pop out, Close)"; "Fullscreen ... maps to Expand: the App pane fills the workspace with the
// trust strip still visible (8.2)"; canvas "App · shipyard", Back inline; ED-NEW-web-w19-4). No Swift counterpart. A thread has
// one App pane; it shows the app the thread last expanded. Under the pane's header sits the trust strip, "Sandboxed · from
// shipyard", outside the web rectangle. The header offers Pop out and Back inline; its close button closes the pane and the
// app goes back to its card. A pane opened with no app to show (a thread whose app is gone) shows the thread's newest app, or
// closes itself.
//
// Motion (DESIGN 2.4, 2.6): the pane springs open, reflows and closes in the workspace with opacity and scale, which a web view's
// own window cannot follow. So while the workspace moves, and from the moment the app arrives until two frames after it stops,
// the app's host shows its newest frame; the page goes live once the pane stands still.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Web;

namespace Marrow.App.Views.Panes;

public sealed class AppPane : Decorator
{
    public const double TrustHeight = 28;
    public static string PopOutText => UiText.Get("panes.app.pop_out");

    public static string BackInlineText => UiText.Get("panes.app.back_inline");

    private readonly ThreadRuntime runtime;
    private readonly Decorator slot = new() { Name = "WebSlot" };
    private readonly TextBlock trustText;
    // ED-NEW-web-sec-res-2: a link the user clicked in the app is offered under the trust strip, its whole address shown.
    private readonly McpAppLinkOffer offer = new(new Thickness(6, 0, 6, 6));
    private PaneContent? content;
    private McpAppSession? shown;
    private McpAppSession? watched;
    private (WebViewHost Host, IDisposable Lease)? motionHold;
    private Action<TimeSpan>? pendingFrame;
    private int stillFrames;

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
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = { trust, offer.Notice, body } };
        Grid.SetRow(offer.Notice, 1);
        Grid.SetRow(body, 2);
        Child = column;
        slot.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;
            shown?.UpdateContext(slot);
            if (Workspace()?.IsMoving == true) HoldWhileMoving();
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

    /// <summary>ED-NEW-web-sec-res-2: the offer of a link the user clicked in the app, with its whole address.</summary>
    internal Marrow.App.Views.Artifacts.TabulaNotice LinkNotice => offer.Notice;

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
        Watch(shown);
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
        HoldWhileMoving();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        McpAppSessions.ExpandedChanged -= OnExpandedChanged;
        Watch(null);
        shown?.DetachPane(slot);
        EndMotionHold();
    }

    private void OnExpandedChanged(Guid thread)
    {
        if (thread != runtime.ThreadID) return;
        if (McpAppSessions.Expanded(thread) is not { } next || next == shown) return;
        shown?.DetachPane(slot);
        Show(next);
        Watch(next);
        next.AttachPane(slot);
        HoldWhileMoving();
    }

    private PaneWorkspace? Workspace() => this.FindAncestorOfType<PaneWorkspace>();

    /// <summary>True while the pane keeps the app on its snapshot because the workspace moves (tests).</summary>
    internal bool HoldsForMotion => motionHold is not null;

    // The app's page shows its newest frame until the workspace has stood still for two frames.
    private void HoldWhileMoving()
    {
        if (shown is null) return;
        if (motionHold is { } held && held.Host != shown.Host) EndMotionHold();
        motionHold ??= (shown.Host, shown.Host.HoldSnapshot());
        stillFrames = 0;
        if (pendingFrame is not null) return;
        pendingFrame = OnFrame;
        FrameClock.Source.Current.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        if (motionHold is null) return;
        stillFrames = Workspace()?.IsMoving == true ? 0 : stillFrames + 1;
        if (stillFrames >= 2)
        {
            EndMotionHold();
            return;
        }

        pendingFrame = OnFrame;
        FrameClock.Source.Current.RequestFrame(pendingFrame);
    }

    private void EndMotionHold()
    {
        if (pendingFrame is { } pending) FrameClock.Source.Current.CancelFrame(pending);
        pendingFrame = null;
        if (motionHold is not { } held) return;
        motionHold = null;
        held.Lease.Dispose();
    }

    private void Show(McpAppSession? session)
    {
        shown = session;
        trustText.Text = session is null ? "" : McpAppTrust.Sandboxed(session.Server);
        content?.Update(PaneKinds.Title(PaneKind.App), session?.Server, Actions());
        offer.Show(session);
    }

    // While the pane is in the window it follows the shown app's changes (its offered link).
    private void Watch(McpAppSession? next)
    {
        if (watched == next) return;
        if (watched is not null) watched.Changed -= OnShownChanged;
        watched = next;
        if (watched is not null) watched.Changed += OnShownChanged;
    }

    private void OnShownChanged() => offer.Show(shown);
}
