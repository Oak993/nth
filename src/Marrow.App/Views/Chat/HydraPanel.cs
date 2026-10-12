using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraPanel.swift (B3 52bc7926), :1-552.

/// <summary>:4-201 <c>struct HydraPanel: View</c>: the lead's team as a floating glass panel.</summary>
public sealed class HydraPanel : Decorator
{
    public const double CornerRadius = 22; // :33

    // :37 static let stripHeight: CGFloat = Chrome.chromeTopPadding + Chrome.capsuleHeight + 6
    public static double StripHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    // :41 static let finishHold: TimeInterval = 2.0
    public const double FinishHold = 2.0;

    private readonly Grid stageHost = new();
    private readonly Grid waveHost = new() { IsHitTestVisible = false };
    private readonly Grid nameHost = new();
    private readonly StackPanel popOutHost = new();
    private readonly HoldState state = new();
    private readonly ThreadRuntime runtime;
    private readonly IReadOnlyList<ChatThread> initialHeads;
    private readonly Size size;
    private readonly string? workingDirectory;
    private readonly string? projectName;
    private readonly bool isPoppedOut;
    private readonly Action<Guid>? popOut;
    private readonly Action<Size> onDrag;
    private readonly Action onDragEnd;
    private readonly HydraHeadsButton? headsButton;
    private ChatThread? selectedHead;
    private Control? stage;
    private Control? name;
    private StageCapsule? capsule;
    private Control? popButton;
    private Guid? popId;
    private Border? wave;
    private StageHold? waveKey;
    private StageMark mark = new();
    private bool rendered;

    // PORT-ONLY: the stage's contents are independently owned; tests isolate the as-yet-unported
    // JumpToLatestButton dependency while exercising this unit's real lifetime and transitions.
    internal Func<ChatThread, Control>? StageFactory { get; set; }

    // :18-31, :46-122
    public HydraPanel(ThreadRuntime runtime, IReadOnlyList<ChatThread> heads, Size size,
        string? workingDirectory, string? projectName, Action<Size> onDrag, Action onDragEnd,
        Action dismiss, bool isPoppedOut = false, Action<Guid>? popOut = null)
    {
        this.runtime = runtime;
        initialHeads = heads;
        this.size = size;
        this.workingDirectory = workingDirectory;
        this.projectName = projectName;
        this.isPoppedOut = isPoppedOut;
        this.popOut = popOut;
        this.onDrag = onDrag;
        this.onDragEnd = onDragEnd;
        if (!isPoppedOut) headsButton = new HydraHeadsButton(runtime, heads, dismiss);
        // :99-121 Keep the shadow-bearing scrim outside the clipped glass surface.
        var shape = new Border { ClipToBounds = true, Child = stageHost }.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous); // :57 `shape`, the 22-point continuous rounded rectangle (ED-181, ED-275)
        SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous))); // :105
        SwiftGlass.SetCastsOwnShadow(shape, true); // OD-0147: the scrim below casts the card's one shadow
        var scrim = new Border { CornerRadius = new CornerRadius(CornerRadius) };
        // :120 `.shadow(color: .black.opacity(isDark ? 1 : 0.65), radius: 28, y: 10)` on the filled scrim: the scrim's own coverage, cast at
        // its fill's alpha and drawn under it, through it (SwiftShadow.Caster, ED-358; sigma 28, ED-327).
        var shadow = new SwiftShadow.Caster(scrim, Colors.Black, radius: 28, y: 10);
        // :102, :117 `let isDark = colorScheme == .dark` is read inside `body`, so the tint, the scrim and its shadow follow
        // the theme as it changes (the control is built before it has a theme, so this cannot be fixed at construction).
        void UpdateTheme()
        {
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            shape.Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(this); // :107
            scrim.Background = new SolidColorBrush(Color.FromArgb(isDark ? (byte)77 : (byte)87, // :119 black 0.3 / white 0.34
                isDark ? (byte)0 : (byte)255, isDark ? (byte)0 : (byte)255, isDark ? (byte)0 : (byte)255));
            shadow.Set(Color.FromArgb(isDark ? (byte)255 : (byte)166, 0, 0, 0), radius: 28, y: 10); // :120 black 1.0 / 0.65, cast by the 0.3 / 0.34 fill
        }
        UpdateTheme();
        ActualThemeVariantChanged += (_, _) => UpdateTheme();
        var panelContents = new Grid();
        panelContents.Children.Add(shape);
        panelContents.Children.Add(waveHost);
        panelContents.Children.Add(Strip(isPoppedOut ? heads.FirstOrDefault() : null, runtime, heads, isPoppedOut, popOut, dismiss, onDrag, onDragEnd));
        var panel = new Border { ClipToBounds=true, Child=panelContents }.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous); // :113 .clipShape(shape)
        Child = new Grid { Children = { shadow, scrim, panel } };
        ClipToBounds=false; Width=size.Width; Height=size.Height;
        Apply(heads, isPoppedOut ? null : runtime.HydraSelectedHeadID, false, false);
        // :43-91 Observation and task are registered before attachment. Detachment cancels both.
        this.Track(() =>
        {
            var model = GetValue(AppEnvironment.AppModelProperty)
                ?? (!isPoppedOut || initialHeads.Count>0 ? WindowManager.Shared.Model : null);
            IReadOnlyList<ChatThread> current = initialHeads;
            if (model is not null)
            {
                if (isPoppedOut)
                    current = initialHeads.Select(h => model.Thread(h.Id) ?? h).ToArray();
                else if (model.Thread(runtime.ThreadID) is not null)
                    current = model.HydraHeads(runtime.ThreadID);
                else
                    current = initialHeads.Select(h => model.Thread(h.Id) ?? h).ToArray();
            }
            _ = state.Hold;
            _ = isPoppedOut ? 0 : runtime.HydraWaitingCount;
            return (Heads:current, Selection:isPoppedOut ? null : runtime.HydraSelectedHeadID,
                Resize:GetValue(AppEnvironment.WindowLiveResizeProperty)?.IsActive ?? false,
                Reduce:EnvironmentValues.GetAccessibilityReduceMotion(this));
        }, input => Apply(input.Heads,input.Selection,input.Resize,input.Reduce));
        this.Task(() => state.Hold, async hold =>
        {
            if (hold is null || !await SwiftTask.TrySleep(TimeSpan.FromSeconds(FinishHold))) return;
            Transaction.WithAnimation(Chrome.PanelSlide, () => state.Hold = null);
        });
    }

    // :50-91 selected = held ?? wanted. Only a same-id running -> finished mark starts a hold.
    private void Apply(IReadOnlyList<ChatThread> heads, Guid? selection, bool resizing, bool reduceMotion)
    {
        var wanted = isPoppedOut ? heads.FirstOrDefault()
            : heads.FirstOrDefault(h => h.Id==selection)
                ?? heads.LastOrDefault(h => h.Hydra?.HydraHeadInfoStatus==HydraHeadInfo.Status.Running)
                ?? heads.LastOrDefault();
        var selected = heads.FirstOrDefault(h => h.Id==state.Hold?.Id) ?? wanted;
        var nextMark = new StageMark { Id=selected?.Id, Status=selected?.Hydra?.HydraHeadInfoStatus };
        var finished = nextMark.Id is not null && mark.Id==nextMark.Id
            && mark.Status==HydraHeadInfo.Status.Running && nextMark.Status!=HydraHeadInfo.Status.Running;
        mark=nextMark;
        if (finished)
            Transaction.WithAnimation(Chrome.PanelSlide, () => state.Hold = new StageHold { Id=selected!.Id, StartedAt=SwiftDate.Now });
        var animate = rendered && !resizing;
        Transaction.WithAnimation(animate ? Chrome.PanelSlide : null, () =>
        {
            if (selectedHead?.Id != selected?.Id)
            {
                Replace(stageHost, ref stage, selected is null ? null :
                    StageFactory?.Invoke(selected) ?? new HydraHeadTranscript(selected,size.Height,workingDirectory,projectName),
                    SwiftTransition.Opacity, animate);
            }
            else if (stage is HydraHeadTranscript transcript && selected is not null && selectedHead != selected)
                transcript.Update(selected);
            if (selectedHead != selected)
            {
                // :136-165 `if let selected, let info = selected.hydra { HStack ... .transition(.softAppear) }` carries no .id: a
                // head change from one head to another keeps the SAME capsule view and its contents change under :199
                // `.animation(liveResize.isActive ? nil : Chrome.panelSlide, value: selected?.id)`. Only a capsule that was
                // not there (no head on stage, or a head with no record) arrives by the soft appear, and one that goes leaves by it.
                var headChanged = selectedHead?.Id != selected?.Id;
                if (capsule is not null && selected?.Hydra is not null)
                    capsule.Update(selected, animate && headChanged && !reduceMotion ? Transaction.Current : null);
                else
                {
                    capsule = selected?.Hydra is not null ? new StageCapsule(this, selected, onDrag, onDragEnd) : null;
                    Replace(nameHost, ref name, capsule?.Root, CapsuleAppear, animate && headChanged);
                }
            }
            var nextPopId = !isPoppedOut && popOut is not null && heads.Count>1 ? selected?.Id : null;
            if (popId != nextPopId)
            {
                Replace(popOutHost, ref popButton, nextPopId is { } id
                    ? new ChromeCircleButton("arrow.up.right",$"Pop {selected?.Hydra?.DisplayName ?? "this head"} out into its own panel",()=>popOut!(id))
                    : null, CapsuleAppear,animate);
                popId=nextPopId;
            }
            var held = heads.FirstOrDefault(h => h.Id==state.Hold?.Id);
            var key = held is null ? null : state.Hold;
            if (waveKey != key)
            {
                Control? oldWave=wave;
                Border? next=null;
                if (key is not null)
                {
                    var color=(held!.Hydra?.Persona ?? HydraRoster.Persona(0)).Color;
                    var tint=new SwiftColor(color.Red,color.Green,color.Blue,color.Alpha);
                    var sweep=new DotFieldSweep { Tint=tint };
                    // DotFieldSweep constructs its field before object-initializer tint/environment are applied.
                    if (sweep.Child is DotFieldControl field) { field.UpdateColor(tint); field.UpdateAnimated(!reduceMotion); }
                    next=new Border { Child=sweep, ClipToBounds=true }.RoundedRectangle(CornerRadius, RoundedCornerStyle.Continuous); // :76 DotFieldSweep(...).clipShape(shape)
                }
                Replace(waveHost,ref oldWave,next,SwiftTransition.Opacity,animate);
                wave=next; waveKey=key;
            }
        });
        selectedHead=selected;
        headsButton?.Update(heads);
        rendered=true;
    }

    // UI/Chrome/Chrome.swift:934-960: softAppear's layer opacity and four-point
    // offset interpolate on the 0.32-second smooth curve; removal is a 0.12-second ease-out fade.
    // Express the layer properties directly: the shared Bool modifier has empty animatable data.
    private static SwiftTransition CapsuleAppear => SwiftTransition.Asymmetric(
        SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y:4)).Animation(Animation.SoftAppear),
        SwiftTransition.Opacity.Animation(Animation.EaseOut(duration:0.12)));

    private static void Replace(Panel host, ref Control? old, Control? next, SwiftTransition transition, bool animate)
    {
        if (old is not null)
        {
            if (!animate) old.Transition(SwiftTransition.Identity);
            SwiftTransition.Remove(host,old);
        }
        old=next;
        if (next is null) return;
        // softAppear owns an animation; identity must gate it explicitly during live resize.
        next.Transition(animate ? transition : SwiftTransition.Identity);
        SwiftTransition.Insert(host,host.Children.Count,next);
    }

    private sealed class HoldState : MainActorModel
    {
        private StageHold? hold;
        public StageHold? Hold { get => Read(hold); set => Set(ref hold,value); }
    }

    // :132-164 The selected head's name capsule: the contents of its HStack (the glyph, the name, the provider's mark, the status
    // word and the clock) for the head on stage, drawn into the given panel.
    private void FillCapsule(StackPanel content, ChatThread selected)
    {
        var info = selected.Hydra!;
        content.Children.Clear();
        // :132-135 The head's stored record, not its live one: the strip sits over a streaming chat
        // and shows only the name and the state.
        content.Children.Add(new HydraGlyph(info.Persona, 16, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, info.HydraHeadInfoStatus) { VerticalAlignment = VerticalAlignment.Center }); // :132 a 16 point frame centred in the HStack, not stretched to the row
        content.Children.Add(new HydraNameText(info.Persona, 12.5, SwiftFont.Weight.Medium, Chrome.PrimaryText.Opacity(0.92)));
        // :141-145 The provider's mark, so the strip says whose model is at work on stage.
        var providerIcon = new ProviderIcon { Provider = selected.Provider, Size = 12 };
        providerIcon.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.SecondaryText.Brush(providerIcon)); // :144 .foregroundStyle(Chrome.secondaryText)
        ToolTip.SetTip(providerIcon,
            WindowManager.Shared.Model.Providers.Model(selected.Model, selected.Provider)?.ShortName
            ?? selected.Provider.DisplayName);
        content.Children.Add(providerIcon);
        // :146-149 Text(verbatim: HydraStatusText.short(info)).font(.system(.callout)); ED-059: callout
        // is 12 points (B1:DroppyCode/UI/Common/ChatZoom.swift:52), in Inter (OD-0009).
        var status = new TextBlock
        {
            Text = HydraStatusText.Short(info),
            Foreground = Chrome.SecondaryText.Brush(this),
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(status);
        content.Children.Add(status);
        // :150-152 The head's own clock, so the strip itself is still drawn once.
        content.Children.Add(new HydraElapsedTime(info.StartedAt, info.FinishedAt, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running));
    }

    /// <summary>
    /// :136-165 the stage capsule as ONE view for as long as a head with a record is on stage (the source's `if let` has no .id): the glass shell, its
    /// padding and the drag handle stay, and a head change takes the contents in place under the panel slide (:199). ENGINEERING DECISION (OD-0076:
    /// content that swaps crossfades, a container whose content changes width eases to it): the contents dip out over the first half of the motion on its
    /// ease-in, the new head's come in over the second on its ease-out, and the capsule's width glides from the old width to the new one over the second half, once the new contents' width is known,
    /// as the working line's clock does (HydraElapsedTime.Update). With no animation (a live resize, the same head's record changing, reduce motion) it is at once.
    /// </summary>
    private sealed class StageCapsule
    {
        private readonly HydraPanel owner;
        private readonly StackPanel content;
        private readonly Border padded;
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        public Grid Root { get; }

        public StageCapsule(HydraPanel owner, ChatThread selected, Action<Size> onDrag, Action onDragEnd)
        {
            this.owner = owner;
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            owner.FillCapsule(content, selected);
            // :154-158 horizontal 12, content height 28, then vertical 2, fixed size, chromeGlassCapsule (the same surface the
            // heads button wears: a continuous capsule of glass, flat on a glass panel, a shade brighter under the pointer).
            padded = new Border
            {
                Child = content,
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
                Height = Chrome.CapsuleHeight,
            };
            // :159-163 The name is a label, not a control, so it drags the panel too: it is where the hand
            // goes when there is little room beside it.
            Root = new Grid();
            Root.Children.Add(padded.ChromeGlassCapsule());
            Root.Children.Add(new PanelDragHandle(onDrag, onDragEnd));
        }

        public void Update(ChatThread selected, Animation? change)
        {
            Stop();
            if (change is null || change.Duration <= 0 || TopLevel.GetTopLevel(padded) is null)
            {
                owner.FillCapsule(content, selected);
                content.Opacity = 1;
                padded.Width = double.NaN;
                return;
            }
            var from = padded.Bounds.Width;
            var to = from;
            var fromOpacity = content.Opacity;
            var half = change.Duration / 2;
            var frames = FrameClock.Source.Current;
            var since = SwiftClock.Now;
            var swapped = false;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var elapsed = (SwiftClock.Now - since).TotalSeconds;
                var done = elapsed + 0.001 >= change.Duration || TopLevel.GetTopLevel(padded) is null; // ED-117 (2); a capsule that left the tree finishes at once
                var at = done ? change.Duration : elapsed;
                if (at >= half && !swapped)
                {
                    swapped = true;
                    owner.FillCapsule(content, selected);
                    content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); // the new contents, as they will be drawn (the clock has its words once attached)
                    to = content.DesiredSize.Width + 2 * Chrome.CapsuleHorizontalPadding;
                }
                content.Opacity = done ? 1 : at < half ? fromOpacity * (1 - Animation.EaseIn(half).Progress(at)) : Animation.EaseOut(half).Progress(at - half);
                padded.Width = done ? double.NaN : at < half ? from : from + (to - from) * Animation.EaseOut(half).Progress(at - half);
                if (done) { pending = null; return; }
                frames.RequestFrame(frame!);
            };
            source = frames;
            pending = frame;
            frames.RequestFrame(frame);
        }

        private void Stop()
        {
            if (pending is { } frame) source?.CancelFrame(frame);
            pending = null;
        }
    }

    private Control Strip(
        ChatThread? selected,
        ThreadRuntime runtime,
        IReadOnlyList<ChatThread> heads,
        bool isPoppedOut,
        Action<Guid>? popOut,
        Action dismiss,
        Action<Size> onDrag,
        Action onDragEnd)
    {
        // :128-131
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!isPoppedOut) left.Children.Add(headsButton!);
        left.Children.Add(nameHost);
        // :169-175 The handle. Invisible, like a window's title bar with no title; the cursor says what it
        // does. It (and the one over the stage capsule, :162) is PanelDragHandle (SubagentPanel.swift:646-666): the open
        // hand over it, the closed hand while held, 2 points of slop and a first click that works when the window is not
        // key. This file used to track the press itself with none of those; Marrow.App.Views.Chat.SubagentPanel.PanelDragHandle
        // has since been re-pinned against B3 and wraps the shared Marrow.UI.Chrome.DragHandleView, so it is used here
        // as the source does.
        var drag = new PanelDragHandle(onDrag, onDragEnd)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = StripHeight
        };
        ToolTip.SetTip(drag, "Drag to move");
        AutomationProperties.SetName(drag, "Drag to move");

        // :176-194
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (isPoppedOut)
        {
            // :177-180
            right.Children.Add(new ChromeCircleButton(
                "arrow.down.left",
                $"Put {selected?.Hydra?.DisplayName ?? "the head"} back in the team panel",
                dismiss));
        }
        else
        {
            right.Children.Add(popOutHost);
            // :190-192
            right.Children.Add(new ChromeCircleButton("xmark", "Dismiss the panel; heads keep working", dismiss));
        }

        // :127, :167-168, :195-196 The strip's ends, padded 12 at the top and 14 at the outer edge.
        // :127 HStack(alignment: .top, spacing: 8): the three groups are 8 points apart, and the strip is an overlay
        // aligned to the panel's top (:92), so it takes the top of the panel rather than the middle of it.
        var row = new Grid { Height = StripHeight, ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Top };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var leftHolder = new Border
        {
            Child = left,
            Padding = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var rightHolder = new Border
        {
            Child = right,
            Padding = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(leftHolder, 0);
        Grid.SetColumn(drag, 1);
        Grid.SetColumn(rightHolder, 2);
        row.Children.Add(leftHolder);
        row.Children.Add(drag);
        row.Children.Add(rightHolder);
        return row;
    }
}

/// <summary>:203-208 <c>private struct StageHold: Hashable</c>.</summary>
internal sealed record StageHold
{
    public required Guid Id { get; init; } // :206
    public required DateTimeOffset StartedAt { get; init; } // :207
}

/// <summary>:210-214 <c>private struct StageMark: Equatable</c>.</summary>
internal sealed record StageMark
{
    public Guid? Id { get; init; } // :212
    public HydraHeadInfo.Status? Status { get; init; } // :213
}

/// <summary>:216-288 <c>private struct HydraHeadsButton: View</c>.</summary>
internal sealed class HydraHeadsButton : Decorator
{
    // :227-228 @State private var isPresented = false, isHovering = false
    private sealed class ViewState : MainActorModel
    {
        private bool isPresented;
        private bool isHovering;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    /// <summary>
    /// :250, :255, :257 The capsule itself: not a Button, a tap that rides along with the glass, with the button trait and
    /// an action that toggles the popover for accessibility.
    /// </summary>
    private sealed class Capsule : Border
    {
        private readonly Action toggle;

        public Capsule(Action toggle)
        {
            this.toggle = toggle;
            // :250 .simultaneousGesture(TapGesture().onEnded { isPresented.toggle() }): a press and a release on the capsule, each
            // one a tap (Avalonia's Tapped reads the second of two quick clicks as a double tap, which would drop a toggle).
            var pressed = false;
            PointerPressed += (_, e) => pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
            PointerExited += (_, _) => pressed = false;
            PointerCaptureLost += (_, _) => pressed = false;
            PointerReleased += (_, _) =>
            {
                if (!pressed) return;
                pressed = false;
                toggle();
            };
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this, toggle);

        private sealed class Peer(Control owner, Action toggle) : ControlAutomationPeer(owner), IInvokeProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button; // :255
            void IInvokeProvider.Invoke() => toggle(); // :257
        }
    }

    private readonly ViewState state = new();
    private readonly ThreadRuntime runtime;
    private readonly Action dismiss;
    private readonly Capsule capsule;
    private readonly AnimatedColor foreground;
    private readonly Popup popover;
    private IReadOnlyList<ChatThread> heads = [];

    // :223-225, :230-287
    public HydraHeadsButton(ThreadRuntime runtime, IReadOnlyList<ChatThread> heads, Action dismiss)
    {
        this.runtime = runtime;
        this.dismiss = dismiss;
        capsule = new Capsule(() => state.IsPresented = !state.IsPresented)
        {
            // :244-246 padded horizontally by capsuleHorizontalPadding, framed to capsuleContentHeight, padded vertically by
            // capsuleVerticalPadding; :247 hit-shaped as a continuous capsule, so the whole of it takes the tap.
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
            Height = Chrome.CapsuleHeight,
            CornerRadius = new CornerRadius(Chrome.CapsuleHeight / 2),
            Background = Brushes.Transparent,
        };
        foreground = new AnimatedColor(capsule);
        foreground.Set(Chrome.PrimaryText.Opacity(0.92), null);

        var surface = capsule.ChromeGlassCapsule(); // :249
        // :251-253 .onHover { hovering in withAnimation(Chrome.hover) { isHovering = hovering } }
        surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering));
        // :258 .popover(isPresented: $isPresented, arrowEdge: .bottom)
        popover = PlainControls.Popover(surface, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), Menu, PlacementMode.Bottom);
        Child = new Panel { Children = { surface, popover } };

        // :243 .foregroundStyle(Chrome.primaryText.opacity(isHovering || isPresented ? 1 : 0.92))
        this.Track(() => state.IsHovering || state.IsPresented,
            lit => foreground.Set(Chrome.PrimaryText.Opacity(lit ? 1 : 0.92), Transaction.Current));
        Update(heads);
    }

    // :230-287 The count, selection, waiting count and head records all follow observation.
    internal void Update(IReadOnlyList<ChatThread> heads)
    {
        this.heads = heads;
        // :231 let running = heads.count { $0.hydra?.status == .running }
        var running = heads.Count(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);

        // :232-242 HStack(spacing: 6), its parts centred in the capsule as an HStack centres them.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Centred(new HydraSpokesMark()));
        var count = new TextBlock { Text = heads.Count.ToString(CultureInfo.InvariantCulture), Foreground = foreground.Brush };
        SwiftFont.System(12.5, SwiftFont.Weight.Semibold).MonospacedDigit().Apply(count); // :235
        row.Children.Add(Centred(count));
        if (running > 0) row.Children.Add(Centred(new MiniSpinner())); // :236-238
        row.Children.Add(Centred(SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText))); // :239-241
        capsule.Child = row;

        // :254-257 the tooltip and the accessibility label are "1 head" or "N heads"
        var label = heads.Count == 1 ? "1 head" : $"{heads.Count.ToString(CultureInfo.InvariantCulture)} heads";
        ToolTip.SetTip(capsule, label);
        AutomationProperties.SetName(capsule, label);

        // :258-286 the popover reads the heads, the selection and the waiting count: while it is open it follows them.
        if (state.IsPresented && popover.Child is not null) popover.Child = Menu();
    }

    private static T Centred<T>(T control) where T : Control
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    // :259-285
    private Control Menu() => new PopoverMenu(() =>
    {
        var running = heads.Count(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);
        var menu = new StackPanel();
        menu.Children.Add(new PopoverSectionHeader(Header(running, runtime.HydraWaitingCount)));
        foreach (var head in heads)
        {
            menu.Children.Add(new HydraHeadRow(head, head.Id == runtime.HydraSelectedHeadID, () =>
            {
                runtime.HydraSelectedHeadID = head.Id;
                state.IsPresented = false;
            }));
        }

        var finished = heads.Any(head => head.Hydra?.IsFinished == true);
        if (running > 1 || finished) menu.Children.Add(new PopoverDivider());
        if (running > 1)
        {
            menu.Children.Add(new PopoverItem("Stop all heads", () =>
            {
                InvokeModelAction(WindowManager.Shared.Model, "StopAllHydraHeads", runtime.ThreadID);
                state.IsPresented = false;
            }, symbol: "stop.circle"));
        }
        if (finished)
        {
            // :280-282 inside a panel slide; the only action here that leaves the popover open (upstream defect 31).
            menu.Children.Add(new PopoverItem("Clear finished heads", () =>
                Transaction.WithAnimation(Chrome.PanelSlide, () => InvokeModelAction(WindowManager.Shared.Model, "ClearFinishedHydraHeads", runtime.ThreadID)),
                symbol: "checkmark.circle"));
        }
        return menu;
    });

    // :274, :280-281. The Swift methods are on AppModel; resolve their C# names at the call boundary.
    private static void InvokeModelAction(AppModel model, string methodName, Guid threadID)
    {
        var method = typeof(AppModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(Guid)]);
        if (method is null) throw new MissingMethodException(typeof(AppModel).FullName, methodName);
        method.Invoke(model, [threadID]);
    }

    // :259-262
    private static string Header(int running, long waiting)
    {
        var header = running == 0 ? "Heads" : running == 1 ? "1 head working" : $"{running.ToString(CultureInfo.InvariantCulture)} heads working";
        return waiting > 0 ? header + (waiting == 1 ? ", 1 waiting" : $", {waiting.ToString(CultureInfo.InvariantCulture)} waiting") : header;
    }
}

/// <summary>:290-297 <c>private struct HydraSpokesMark: View</c>.</summary>
internal sealed class HydraSpokesMark : Decorator
{
    // :292-296
    public HydraSpokesMark()
    {
        var mark = new HydraMarkImage
        {
            Width = 15,
            Height = 15
        };
        // :294 .foregroundStyle(Chrome.primaryText.opacity(0.9))
        mark.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.PrimaryText.Opacity(0.9).Brush(mark));
        Child = mark;
    }
}

/// <summary>:299-363 <c>private struct HydraHeadRow: View</c>.</summary>
internal sealed class HydraHeadRow : Decorator
{
    // :304-306, :310-362
    public HydraHeadRow(ChatThread head, bool isOnStage, Action select)
    {
        if (head.Hydra is not { } stored) return;

        // :313-314 A running head's note and counts live on its runtime, off the thread record.
        var info = HydraLiveInfo.Shown(stored, WindowManager.Shared.Model.ExistingRuntime(head.Id));

        // :320-336
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new HydraNameText(info.Persona, 13, SwiftFont.Weight.Medium, Chrome.PrimaryText));
        var providerIcon = new ProviderIcon { Provider = head.Provider, Size = 11 };
        providerIcon.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.SecondaryText.Brush(providerIcon)); // :323 .foregroundStyle(Chrome.secondaryText)
        ToolTip.SetTip(providerIcon,
            WindowManager.Shared.Model.Providers.Model(head.Model, head.Provider)?.ShortName
            ?? head.Provider.DisplayName);
        label.Children.Add(providerIcon);
        // :327-330 .font(.system(.subheadline)); ED-059: subheadline is 11 points
        // (B1:DroppyCode/UI/Common/ChatZoom.swift:53).
        var status = new TextBlock
        {
            Text = HydraStatusText.Long(info),
            Foreground = (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed ? Chrome.Danger : Chrome.SecondaryText).Brush(this),
            TextWrapping = TextWrapping.NoWrap
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(status);
        label.Children.Add(status);

        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(label);
        // :332-336 Text(verbatim: info.task.isEmpty ? "Waiting for a task" : info.task).font(.system(.callout))
        var task = new TextBlock
        {
            Text = info.Task.Length == 0 ? "Waiting for a task" : info.Task,
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(task);
        details.Children.Add(task);

        // :315-347
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var selectButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HydraGlyph(info.Persona, 22, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, info.HydraHeadInfoStatus),
                    details,
                    // :338 Spacer(minLength: 8)
                    new Border { Width = 8 }
                }
            },
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        selectButton.Click += (_, _) => select();
        row.Children.Add(selectButton);
        if (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running && info.CanStop)
            row.Children.Add(new HydraStopButton(info.DisplayName, () => InvokeModelAction(WindowManager.Shared.Model, "StopHydraHead", head.Id)));

        // :349-360
        var background = new Border
        {
            Child = row,
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(6),
            Background = isOnStage ? Chrome.Overlay(0.14).Brush(this) : Brushes.Transparent
        };
        AutomationProperties.SetName(background,
            $"{info.DisplayName}, {HydraStatusText.Long(info)}, {info.Task}{(isOnStage ? ", on stage" : "")}");
        Child = background;
    }

    private static void InvokeModelAction(AppModel model, string methodName, Guid threadID)
    {
        var method = typeof(AppModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(Guid)]);
        if (method is null) throw new MissingMethodException(typeof(AppModel).FullName, methodName);
        method.Invoke(model, [threadID]);
    }
}

/// <summary>:365-383 <c>private struct HydraStopButton: View</c>.</summary>
internal sealed class HydraStopButton : Decorator
{
    // :368-369, :371-382
    public HydraStopButton(string name, Action action)
    {
        var button = new Button
        {
            Content = SwiftImage.SystemName("stop.fill")
                .Font(Chrome.IconFont)
                .ForegroundStyle(SwiftColor.White),
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Chrome.Accent.Brush(this),
            CornerRadius = new CornerRadius(14)
        };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, $"Stop {name}");
        AutomationProperties.SetName(button, $"Stop {name}");
        Child = button;
    }
}

/// <summary>:385-437 <c>private struct HydraHeadTranscript: View</c>.</summary>
internal sealed class HydraHeadTranscript : Decorator
{
    private readonly Border progressHost = new();
    private readonly Border footerHost = new();
    private readonly ThreadRuntime runtime;
    internal void Update(ChatThread head)
    {
        if (progressHost.Child is not null) progressHost.Child=new HydraHeadProgress(head,runtime);
        if (footerHost.Child is not null && head.Hydra is { } info)
            footerHost.Child=new HydraHeadFooter(HydraLiveInfo.Shown(info,runtime));
    }
    // :388-391, :396-436
    public HydraHeadTranscript(ChatThread head, double height, string? workingDirectory, string? projectName)
    {
        var model = WindowManager.Shared.Model;
        runtime = model.Runtime(head.Id);
        var scrollChrome = new ChromeScrollModel();
        var scrollState = new TimelineScrollState();
        Control body;
        if (model.Settings.HydraShowsHeadDetails)
        {
            // :400-411 A panel's transcript needs no rail.
            body = new ThreadTimeline(
                runtime,
                scrollChrome,
                scrollState,
                projectName,
                workingDirectory,
                false,
                height,
                false);
        }
        else
        {
            // :413-417 The task and the progress bar stand in for the steps.
            progressHost.Child = new HydraHeadProgress(head, runtime);
            body = progressHost;
        }

        // :419-432
        // :419 .safeAreaInset(edge: .bottom): the inset is docked first and the body, added last, takes what is left (a DockPanel's
        // last child fills, so a docked child added after the body would fill the whole panel instead of the bottom edge).
        var content = new DockPanel();
        if (head.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            // :421-425 A child Droppy Code runs itself can be steered and answered like any chat.
            var composer = new Grid();
            composer.Children.Add(new ComposerArea(runtime, workingDirectory, compactModelChip: true, takesFocusOnAppear: false, showsChanges: false));
            composer.Children.Add(new JumpToLatestButton(scrollState) { VerticalAlignment = VerticalAlignment.Top }); // :425-427 overlay(alignment: .top)
            DockPanel.SetDock(composer, Dock.Bottom);
            content.Children.Add(composer);
        }
        else if (head.Hydra is { } info)
        {
            var footer = new Grid();
            footerHost.Child = new HydraHeadFooter(HydraLiveInfo.Shown(info, runtime));
            footer.Children.Add(footerHost);
            footer.Children.Add(new JumpToLatestButton(scrollState) { VerticalAlignment = VerticalAlignment.Top }); // :430-432 overlay(alignment: .top)
            DockPanel.SetDock(footer, Dock.Bottom);
            content.Children.Add(footer);
        }
        content.Children.Add(body);

        // :433-435
        var transcript = new Grid();
        transcript.Children.Add(content);
        transcript.Children.Add(new PaneTopVeil(scrollChrome));
        Child = transcript;
    }
}

/// <summary>:439-473 <c>private struct HydraHeadFooter: View</c>.</summary>
internal sealed class HydraHeadFooter : Decorator
{
    // :443, :445-472
    public HydraHeadFooter(HydraHeadInfo info)
    {
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        // :447-453
        Control leading;
        if (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running)
        {
            leading = new MiniSpinner();
        }
        else
        {
            var symbol = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed
                ? "checkmark.circle.fill"
                : info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed
                    ? "xmark.circle.fill"
                    : "stop.circle.fill";
            var color = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed
                ? Chrome.Success
                : info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed
                    ? Chrome.Danger
                    : Chrome.SecondaryText;
            leading = SwiftImage.SystemName(symbol).Font(SwiftFont.System(SwiftFont.TextStyle.Callout)).ForegroundStyle(color);
        }

        leading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(leading);

        // :454-458 Text(verbatim: HydraStatusText.footer(info)).font(.system(.callout))
        var status = new TextBlock
        {
            Text = HydraStatusText.Footer(info),
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(status);
        Grid.SetColumn(status, 1);
        row.Children.Add(status);

        // :460-464
        if (info.ToolCalls > 0)
        {
            var steps = new TextBlock
            {
                Text = info.ToolCalls == 1 ? "1 step" : $"{info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps",
                Foreground = Chrome.SecondaryText.Brush(this),
                VerticalAlignment = VerticalAlignment.Center
            };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit().Apply(steps);
            Grid.SetColumn(steps, 2);
            row.Children.Add(steps);
        }

        // :469 Chrome.panelControlFill(isDark: colorScheme == .dark): Chrome.glassTint mixed toward white in dark by 0.12 or
        // toward black in light by 0.04, at 0.55 opacity in dark and 0.42 in light. The colour scheme is read where the footer
        // is drawn, so the fill is a pair that follows the theme (the control has none yet when it is built).
        var fill = SwiftColor.Dynamic(light: Chrome.PanelControlFill(isDark: false), dark: Chrome.PanelControlFill(isDark: true));

        // :466-471 padded 14 horizontally and 10 vertically, backed by a 16-point continuous rounded
        // rectangle, then padded 20 horizontally and 14 at the bottom.
        Child = new Border
        {
            Child = row,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(16),
            Background = fill.Brush(this),
            Margin = new Thickness(20, 0, 20, 14)
        }.RoundedRectangle(16, RoundedCornerStyle.Continuous); // one line tall the bar is 37 pt: ED-275 (2)'s band, OD-0102 (a plain Border fills the circle of its CornerRadius)
    }
}

/// <summary>:475-487 <c>@MainActor enum HydraLiveInfo</c>, a caseless enum used as a namespace.</summary>
public static class HydraLiveInfo
{
    // :480-486
    public static HydraHeadInfo Shown(HydraHeadInfo stored, ThreadRuntime? runtime)
    {
        if (stored.HydraHeadInfoStatus != HydraHeadInfo.Status.Running || runtime is null) return stored;
        var info = stored;
        if (runtime.HydraActivity is { } activity) info = info with { Activity = activity };
        info = info with { ToolCalls = Math.Max(info.ToolCalls, runtime.HydraToolCalls) };
        return info;
    }
}

/// <summary>:489-552 <c>enum HydraStatusText</c>, a caseless enum used as a namespace: the words for a head's state.</summary>
public static class HydraStatusText
{
    // :491-498
    public static string Short(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => "working",
        HydraHeadInfo.Status.Completed => "done",
        HydraHeadInfo.Status.Failed => "failed",
        HydraHeadInfo.Status.Stopped => "stopped",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :500-511
    public static string Long(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => info.Activity is { } activity
            ? "\U000000B7 " + SingleLine(activity, 40) // U+00B7 MIDDLE DOT
            : "\U000000B7 working", // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Completed =>
            "\U000000B7 done" + Elapsed(info) + Steps(info) + (Landed(info) is { } landed ? " \U000000B7 " + landed : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Failed => "\U000000B7 failed", // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Stopped => "\U000000B7 stopped", // U+00B7 MIDDLE DOT
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :513-524
    public static string Footer(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => info.Activity is { } activity
            ? SingleLine(activity, 80)
            : "Working",
        HydraHeadInfo.Status.Completed =>
            "Done" + Elapsed(info) + Steps(info) + (Landed(info) is { } landed ? " \U000000B7 " + landed : "") + // U+00B7 MIDDLE DOT
            (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Failed =>
            "Failed" + (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Stopped => "Stopped",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :528-538 Where a Droppy-run head's work went, in a few words; nil for a head with no copy of its own.
    public static string? Landed(HydraHeadInfo info)
    {
        if (info.Landing is not { } landing) return null;
        if (landing.IsEmpty) return "changed nothing";
        var files = landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count.ToString(CultureInfo.InvariantCulture)} files";
        if (landing.PatchPath is not null) return "kept as a patch";
        if (landing.Error is not null) return "did not land";
        if (landing.Conflicts.Count != 0)
            return "landed " + files + ", " + (landing.Conflicts.Count == 1
                ? "1 conflict"
                : $"{landing.Conflicts.Count.ToString(CultureInfo.InvariantCulture)} conflicts");
        return "landed " + files;
    }

    // :540-543
    private static string Steps(HydraHeadInfo info)
    {
        if (info.ToolCalls <= 0) return "";
        return info.ToolCalls == 1
            ? " \U000000B7 1 step" // U+00B7 MIDDLE DOT
            : $" \U000000B7 {info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps"; // U+00B7 MIDDLE DOT
    }

    // :545-551. ED-197: Duration.UnitsFormatStyle(allowed: [.seconds, .minutes], width: .narrow, maximumUnitCount:
    // 2, fractionalPart: .hide(rounded: .down)), the same configuration as Hydra.swift:1420-1423's duration(_:).
    private static readonly SwiftFormatters.DurationUnit[] ElapsedUnits =
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes];

    private static string Elapsed(HydraHeadInfo info)
    {
        if (info.FinishedAt is not { } finished) return "";
        var seconds = SwiftNumber.TruncatingToInt(SwiftDate.TimeIntervalSince(finished, info.StartedAt));
        if (seconds <= 0) return "";
        return " in " + SwiftFormatters.DurationUnits(seconds, ElapsedUnits, SwiftFormatters.CurrentLocale.Current);
    }

    // Text.swift:63-73 TextCleanup.singleLine, at this file's own call sites (:505, :518, :520, :522).
    private static string SingleLine(string text, int limit) => TextCleanup.SingleLine(text, limit);
}
