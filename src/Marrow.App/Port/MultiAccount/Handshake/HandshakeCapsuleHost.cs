// PORT-ONLY: MA-4-3, page :3293-3394; actual engine events drive the view, no demo or routing policy here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Runtime;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed class HandshakeCapsuleHost : Panel
{
    private const double CanvasWidth = 230, CanvasHeight = 128;
    private readonly Control body;
    private readonly ComposerCapsuleSlot slot;
    private readonly Guid thread;
    private readonly ICardPort port;
    private readonly Func<IReadOnlyList<ThreadAccount>> accounts;
    private readonly Func<Guid, CapsuleContent?> ghostContent;
    private readonly HandshakePresentationContext context;
    private readonly IReadOnlyList<IThreadAccountPort> accountSources;
    private readonly CrossfadeSlot status = new(), ready = new(), tick = new();
    private readonly MatrixTransform ghostTransform = new();
    private readonly Action<TimeSpan> frame;
    private HandshakeMotionModel? motion;
    private IReadOnlyList<ThreadAccount> snapshot = [];
    private IFrameSource? frames;
    private bool requested, attached, running, wasRunning;
    private Guid? incoming, shownGhost, setStandby;
    private string? shownStatus;
    private bool shownTick;
    private DateTimeOffset last;
    private Point canvasOrigin;
    private HandshakeFrameSnapshot? liveFrame;
    // Inspection only, as in Core. Advance and Render never call the allocating Plan projection.
    internal HandshakeMotionPlan? Plan => liveFrame is null ? null : motion?.Plan(CanvasWidth, CanvasHeight, ActualThemeVariant == ThemeVariant.Dark);
    internal HandshakeCanvas Canvas { get; } = new() { Width = CanvasWidth, Height = CanvasHeight };
    internal AccountCapsule? Ghost { get; private set; }
    internal bool TickShown => shownTick;
    internal double GhostScale { get; private set; } = 1;
    internal HandshakeCapsuleHost(Control body, ComposerCapsuleSlot slot, Guid thread, ICardPort port, Func<IReadOnlyList<ThreadAccount>> accounts, Func<Guid, CapsuleContent?> ghostContent, HandshakePresentationContext? context = null, IReadOnlyList<IThreadAccountPort>? accountSources = null)
    {
        this.body = body; this.slot = slot; this.thread = thread; this.port = port; this.accounts = accounts; this.ghostContent = ghostContent; this.context = context ?? new();
        this.accountSources = accountSources ?? [];
        ClipToBounds = false; Children.Add(body); Children.Add(Canvas); Children.Add(status); Children.Add(ready); Children.Add(tick);
        status.IsHitTestVisible = ready.IsHitTestVisible = tick.IsHitTestVisible = false; Canvas.IsVisible = false;
        status.Show(new Border(), null); ready.Show(new Border(), null); tick.Show(new Border(), null);
        frame = _ => { requested = false; if (!attached) return; var now = SwiftClock.Provider.Current.GetUtcNow(); if ((now - last).TotalSeconds >= 1.0 / 60 - .0005) Advance(); Request(); };
        AttachedToVisualTree += (_, _) =>
        {
            attached = true; snapshot = accounts(); port.Changed += Snapshot;
            foreach (var source in this.accountSources) source.Changed += AccountsChanged;
            motion = new HandshakeMotionModel(thread, port, () => snapshot, SwiftClock.Provider.Current, () => CapsuleMotion.Reduce(this), this.context.Readiness); motion.Changed += Changed; Changed();
        };
        DetachedFromVisualTree += (_, _) => { attached = false; port.Changed -= Snapshot; foreach (var source in this.accountSources) source.Changed -= AccountsChanged; if (requested) frames?.CancelFrame(frame); requested = false; if (motion is { } m) { m.Changed -= Changed; m.Dispose(); motion = null; } slot.SetHandshake(false, null); };
    }
    private void Snapshot(object? sender, HandshakeEvent state)
    {
        if (state.ThreadId != thread) return;
        if (Dispatcher.UIThread.CheckAccess()) UpdateAccounts(); else Dispatcher.UIThread.Post(UpdateAccounts);
    }
    private void UpdateAccounts() { snapshot = accounts(); motion?.RefreshAccounts(); }
    private void AccountsChanged()
    {
        void Apply() { UpdateAccounts(); if (incoming is { } id && Ghost is { } ghost && ghostContent(id) is { } content) ghost.Apply(content); Advance(); }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    private void Changed() { if (Dispatcher.UIThread.CheckAccess()) Advance(); else Dispatcher.UIThread.Post(Advance); }
    internal void Advance()
    {
        if (motion is null) return; last = SwiftClock.Provider.Current.GetUtcNow(); var dark = ActualThemeVariant == ThemeVariant.Dark;
        motion.SoundEnabled = context.SoundEnabled?.Invoke() == true;
        var plan = motion.ReadFrame(dark); liveFrame = plan.Phase == HandshakePhase.Idle ? null : plan;
        running = plan.Phase is HandshakePhase.Standby or HandshakePhase.Linking or HandshakePhase.InSync || plan.Phase == HandshakePhase.Switch && (plan.VirtualMilliseconds < 5700 || plan.ShowTick);
        incoming = Guid.TryParse(port.State(thread)?.IncomingAccountId, out var id) ? id : null;
        if (running != wasRunning || incoming != setStandby) { slot.SetHandshake(running, running ? incoming : null); wasRunning = running; setStandby = incoming; }
        Canvas.IsVisible = liveFrame is not null; if (liveFrame is not null) Canvas.Apply(plan, dark);
        if (incoming != shownGhost || Ghost?.Style != slot.Capsule?.Style)
        {
            if (Ghost is not null) Children.Remove(Ghost); Ghost = null; shownGhost = incoming;
            if (incoming is { } next && ghostContent(next) is { } content)
            {
                Ghost = new AccountCapsule(content.Pill is null ? "plain" : slot.Capsule?.Style ?? "1") { IsHitTestVisible = false, RenderTransform = ghostTransform, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute) };
                Ghost.Face.Focusable = false; Ghost.Apply(content, animate: false); Children.Add(Ghost);
            }
        }
        PlaceGhost();
        if (shownStatus != plan.Status) { shownStatus = plan.Status; status.Show(LibraryUi.Text(plan.Status, 11.5, secondary: true), Fade()); }
        if (shownTick != plan.ShowTick)
        {
            shownTick = plan.ShowTick; ready.Show(shownTick ? LibraryUi.Text("In sync", 10.5, secondary: true) : new Border(), Fade());
            tick.Show(shownTick ? LibraryUi.Glyph("checkmark", 9, ChromeTokens.Accent) : new Border(), Fade());
        }
        if (motion.SoundEnabled) { var tones = motion.TakeSounds(); if (tones.Count > 0) (context.Play ?? HandshakeSound.Play)(tones); }
        InvalidateArrange(); Request();
    }
    private Animation? Fade() => FrameClock.IsBound ? Animation.EaseOut(duration: CapsuleMotion.Reduce(this) ? .22 : .25) : null;
    private void PlaceGhost()
    {
        if (Ghost is not { } ghost) return;
        if (liveFrame is not { } plan) { ghost.Opacity = 0; return; }
        var g = HandshakeGhostLayout.Read(CanvasWidth, CanvasHeight, plan); var breath = plan.UseCrossfade ? 1 : .8 + .2 * Math.Sin(plan.VirtualMilliseconds / 260);
        ghost.Opacity = g.Alpha * breath; GhostScale = plan.UseCrossfade ? 1 : g.Scale;
        var mx = ghost.Style == "7" ? 17 : ghost.FaceSize.Width / 2; var my = 11 + (ghost.Style == "1" ? 1.5 : 0);
        ghostTransform.Matrix = Matrix.CreateTranslation(-mx, -my) * Matrix.CreateScale(GhostScale, GhostScale) * Matrix.CreateTranslation(canvasOrigin.X + g.X, canvasOrigin.Y + g.Y);
    }
    private void Request() { if (!attached || !running || requested || !FrameClock.IsBound) return; frames = FrameClock.Source.Current; requested = true; frames.RequestFrame(frame); }
    protected override Size MeasureOverride(Size available)
    {
        body.Measure(available); Canvas.Measure(new Size(CanvasWidth, CanvasHeight)); status.Measure(new Size(500, 40)); ready.Measure(new Size(60, 22)); tick.Measure(new Size(10, 10)); Ghost?.Measure(new Size(100, 50)); return body.DesiredSize;
    }
    protected override Size ArrangeOverride(Size final)
    {
        body.Arrange(new Rect(final)); var cx = slot.Capsule?.Style == "7" ? 17 : final.Width / 2; var cy = final.Height / 2 + (slot.Capsule?.Style == "1" ? 1.5 : 0); canvasOrigin = new Point(cx - (CanvasWidth - 46), cy - (CanvasHeight - 26));
        Canvas.Arrange(new Rect(canvasOrigin, new Size(CanvasWidth, CanvasHeight))); status.Arrange(new Rect(cx - 400, cy - 46, 400, 22)); ready.Arrange(new Rect(cx + 21, cy - 11, 60, 22)); tick.Arrange(new Rect(cx + 8, cy - 4, 9, 9)); Ghost?.Arrange(new Rect(default(Point), Ghost.DesiredSize)); PlaceGhost(); return final;
    }
    internal static Control With(Control body, ComposerCapsuleSlot slot, ThreadRuntime runtime)
    {
        var ports = ThreadPortSeams.Offer.Current; if (ports.Cards is not { } port) return body;
        var sources = SwiftEnum<ProviderKind>.AllCases.Select(service => ports.Account(runtime.ThreadID, service)).OfType<IThreadAccountPort>().ToArray();
        return new HandshakeCapsuleHost(body, slot, runtime.ThreadID, port, () => sources.SelectMany(source => source.Accounts).ToArray(), HandshakeGhostFactory.For(runtime), HandshakePresentationSeams.Offer.Current(runtime.ThreadID), sources);
    }
}
