using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraButton.swift (B3 52bc792), :1-711.

/// <summary>:4-198 <c>struct HydraButton: View</c>: the chat's Hydra mark, first in the chrome row.</summary>
public sealed class HydraButton : Decorator
{
    // :144 private static var delegationChecks = RecentCache<String, Bool>(limit: 32)
    private static readonly RecentCache<string, bool> DelegationChecks = new(32);

    // :159 private static var streamingScans: [String: (scanned: Int, found: Bool)] = [:]
    private static readonly Dictionary<string, (int Scanned, bool Found)> StreamingScans = new(SwiftStringComparer.Instance);

    // :15-24 the @State of the view
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;
        private bool isExplaining;
        private bool isSending;
        private bool isHydraSwitchShown;
        private bool isIntroShown;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

        /// <summary>:17 With Hydra on and nothing out yet, the button says so rather than doing nothing.</summary>
        public bool IsExplaining { get => Read(isExplaining); set => Set(ref isExplaining, value); }

        /// <summary>:20 Briefly true after heads go out, so the badge first shows the sending pulse.</summary>
        public bool IsSending { get => Read(isSending); set => Set(ref isSending, value); }

        /// <summary>:22 Right-tapping the mark opens the per-chat Hydra switch in place.</summary>
        public bool IsHydraSwitchShown { get => Read(isHydraSwitchShown); set => Set(ref isHydraSwitchShown, value); }

        /// <summary>:24 The first chat's intro, opened by the mark itself until it has been read.</summary>
        public bool IsIntroShown { get => Read(isIntroShown); set => Set(ref isIntroShown, value); }
    }

    // What body (:26-101) reads, so that it is re-applied once per main-actor turn after any of it changes.
    private readonly record struct Look(
        bool IsOn, long Running, bool HasHeads, bool IsDelegating, bool IsSending, bool IsHovering, string Help, string PanelHelp);

    private ChatThread thread;
    private readonly ThreadRuntime runtime;
    private readonly AppModel model;
    private readonly ViewState state = new();
    private readonly Button button;
    private readonly HydraMarkView mark;
    private readonly Panel chargeSlot = new() { IsHitTestVisible = false };
    private readonly Panel badgeSlot = new();
    private HydraCharge? charge;
    private HydraSendWorkingBadge? badge;
    private Look? shown;
    private Tracked<Look>? tracked;
    private OnChange<long>? runningChange;

    // :12-13, :26-101
    public HydraButton(ChatThread thread, ThreadRuntime runtime)
    {
        this.thread = thread;
        this.runtime = runtime;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; // the chrome row's HStack centres its children
        model = FindAppModel(runtime) ?? throw new InvalidOperationException("The Hydra button requires its application model.");

        mark = new HydraMarkView(false, false); // :44
        // :39-58 ZStack { charge, mark }.frame(32, 32).contentShape(Circle()).overlay(alignment: .topTrailing) { badge }
        var face = new Panel
        {
            Width = Chrome.CapsuleHeight, // :46
            Height = Chrome.CapsuleHeight,
            Children =
            {
                new Ellipse { Fill = Brushes.Transparent }, // :47 .contentShape(Circle())
                chargeSlot,
                mark,
                badgeSlot,
            },
        };

        button = PlainControls.Button(face, OnClick); // :36-38, :75 .buttonStyle(.plain)
        var surface = button.ChromeGlassCircle(); // :76
        surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :80-82

        var explaining = new Binding<bool>(() => state.IsExplaining, value => state.IsExplaining = value);
        var switching = new Binding<bool>(() => state.IsHydraSwitchShown, value => state.IsHydraSwitchShown = value);
        var introducing = new Binding<bool>(() => state.IsIntroShown, value => state.IsIntroShown = value);

        // :83-85
        var idle = PlainControls.Popover(button, explaining, () =>
        {
            var pair = model.HydraPair(thread);
            return new HydraIdlePopover(thread, pair, pair is null ? null : model.Providers);
        });
        // :86-88
        var switchPopover = PlainControls.Popover(button, switching, () =>
            new HydraSwitchPopover(thread, model.HydraIsOn(thread), switching, model));
        // ED-NEW-T45, owner override: the intro can dismiss without acknowledging it.
        var intro = PlainControls.Popover(button, introducing, () => new HydraIntroPopover(() =>
        {
            model.Settings.HasSeenHydraIntro = true;
            state.IsIntroShown = false;
        }));

        // :77-79 .background { HydraRightTap { isHydraSwitchShown.toggle() } }
        var tap = new HydraRightTap(() => state.IsHydraSwitchShown = !state.IsHydraSwitchShown);
        Child = new Panel { Children = { PlainControls.Background(surface, tap), idle, switchPopover, intro } };

        Apply(Evaluate(), initial: true);

        AttachedToVisualTree += (_, _) =>
        {
            tracked ??= new Tracked<Look>(Evaluate, Apply); // body re-applied after anything it read changes
            runningChange ??= new OnChange<long>(RunningCount, OnRunningChanged); // :64-67
        };
        DetachedFromVisualTree += (_, _) =>
        {
            tracked?.Dispose();
            tracked = null;
            runningChange?.Dispose();
            runningChange = null;
        };
        // :68-73 .task(id: isSending) { guard isSending; sleep 0.9 s; guard !Task.isCancelled; isSending = false }
        this.Task(() => state.IsSending, async sending =>
        {
            if (!sending) return;
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(0.9));
            if (SwiftTask.IsCancelled) return;
            state.IsSending = false;
        });
    }

    /// <summary>
    /// The parent passing a new <c>thread</c> (R-VIEW-1: :12 <c>let thread</c> changes while the view keeps its identity and its
    /// state): the body is evaluated again with it, so switching Hydra on or off for the chat animates the charge in or out
    /// rather than replacing the whole button.
    /// </summary>
    internal void Update(ChatThread thread)
    {
        if (this.thread == thread) return;
        this.thread = thread;
        if (tracked is null)
        {
            Apply(Evaluate());
            return;
        }

        tracked.Dispose();
        tracked = new Tracked<Look>(Evaluate, Apply);
    }

    // :93 .onAppear { if !model.settings.hasSeenHydraIntro, isOn { isIntroShown = true } }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!model.Settings.HasSeenHydraIntro && model.HydraIsOn(thread)) state.IsIntroShown = true;
    }

    // :36-37
    private void OnClick()
    {
        if (!model.Settings.HasSeenHydraIntro) state.IsIntroShown = !state.IsIntroShown;
        else if (model.PanelHeads(thread.Id).Count == 0) state.IsExplaining = !state.IsExplaining;
        else TogglePanel();
    }

    // :27-35, :98-100
    private Look Evaluate()
    {
        var isOn = model.HydraIsOn(thread); // :27
        var heads = model.PanelHeads(thread.Id); // :30
        var running = RunningOf(heads); // :31
        var hasHeads = heads.Count > 0;
        var isDelegating = isOn && running == 0 && runtime.IsRunning && IsLeadDelegating(runtime.Entries); // :35
        return new Look(
            isOn, running, hasHeads, isDelegating, state.IsSending, state.IsHovering,
            Help(isOn, running, hasHeads), PanelHelp(running, hasHeads));
    }

    // :64 onChange(of: running)
    private long RunningCount() => RunningOf(model.PanelHeads(thread.Id));

    // :31
    private static long RunningOf(IReadOnlyList<ChatThread> heads) =>
        heads.LongCount(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);

    // :64-67
    private void OnRunningChanged(long old, long current)
    {
        if (current > old) state.IsSending = true;
    }

    // :59-63 .animation(spring, value: running), .animation(panelSlide, value: isSending / isDelegating / isOn): the
    // outermost modifier whose value changed supplies the animation.
    private static Animation? AnimationFor(Look? previous, Look next)
    {
        if (previous is not { } before) return null;
        if (before.IsOn != next.IsOn || before.IsDelegating != next.IsDelegating || before.IsSending != next.IsSending)
            return Chrome.PanelSlide;
        if (before.Running != next.Running) return Animation.Spring(response: 0.3, dampingFraction: 0.7);
        return null;
    }

    // :39-100
    private void Apply(Look look) => Apply(look, initial: false);

    // `initial`: the view's first body, in which what is already there arrives without a transition.
    private void Apply(Look look, bool initial)
    {
        var animation = AnimationFor(shown, look) ?? Transaction.Current;
        shown = look;
        Transaction.WithAnimation(initial ? null : animation, () =>
        {
            mark.Update(look.IsOn, look.IsHovering);
            UpdateCharge(look, initial);
            UpdateBadge(look, initial);
        });

        ToolTip.SetTip(button, look.Help); // :98
        AutomationProperties.SetName(button, look.PanelHelp); // :99
        AutomationProperties.SetItemStatus(button, look.IsOn ? "On" : "Off"); // :100 .accessibilityValue, as the port maps it (ChatBadge.cs)
    }

    // :40-43
    private void UpdateCharge(Look look, bool initial)
    {
        if (look.IsOn)
        {
            if (charge is null)
            {
                charge = new HydraCharge(look.Running > 0).Transition(SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity));
                if (initial) SwiftTransition.Place(chargeSlot, 0, charge);
                else SwiftTransition.Insert(chargeSlot, 0, charge);
            }
            else
            {
                charge.Update(look.Running > 0);
            }
        }
        else if (charge is { } leaving)
        {
            charge = null;
            SwiftTransition.Remove(chargeSlot, leaving);
        }
    }

    // :52-58
    private void UpdateBadge(Look look, bool initial)
    {
        var present = look.Running > 0 || look.IsSending || look.IsDelegating;
        var sending = look.IsSending || look.IsDelegating;
        if (present)
        {
            if (badge is null)
            {
                badge = new HydraSendWorkingBadge(look.Running, sending)
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, // :52 .topTrailing
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Margin = new Thickness(0, -3, -3, 0), // :55 .offset(x: 3, y: -3)
                };
                badge.Transition(SwiftTransition.Scale().Combined(SwiftTransition.Opacity)); // :56
                if (initial) SwiftTransition.Place(badgeSlot, 0, badge);
                else SwiftTransition.Insert(badgeSlot, 0, badge);
            }
            else
            {
                badge.Update(look.Running, sending);
            }
        }
        else if (badge is { } leaving)
        {
            badge = null;
            SwiftTransition.Remove(badgeSlot, leaving);
        }
    }

    // :106-108
    private void TogglePanel() =>
        Transaction.WithAnimation(Chrome.PanelSlide, () => runtime.IsHydraPanelHidden = !runtime.IsHydraPanelHidden);

    // :182-197
    private string Help(bool isOn, long running, bool hasHeads)
    {
        if (!isOn) return "Hydra is on in Settings"; // :183

        var parts = new List<string> { "Hydra is on" };
        if (model.HydraPair(thread) is { } pair)
            parts.Add(HydraPairSummary.Workers(pair, model.Providers)); // :185-186
        else
            parts.Add("Heads run on this chat's model"); // :188

        if (running > 0)
            parts.Add(running == 1 ? "1 head working" : $"{running.ToString(CultureInfo.InvariantCulture)} heads working"); // :190

        if (!hasHeads)
        {
            parts.Add("No heads out yet"); // :192
            return string.Join(" \U000000B7 ", parts);
        }

        parts.Add(runtime.IsHydraPanelHidden ? "Show the panel" : "Hide the panel"); // :195
        return string.Join(" \U000000B7 ", parts);
    }

    // :176-180
    private static string PanelHelp(long running, bool hasHeads)
    {
        if (!hasHeads) return "Hydra is on, no heads out yet";
        if (running == 0) return "Hydra is on, show the panel";

        return running == 1
            ? "Hydra is on, 1 head working, show or hide the panel"
            : $"Hydra is on, {running.ToString(CultureInfo.InvariantCulture)} heads working, show or hide the panel";
    }

    // :10 @Environment(AppModel.self): the model the runtime belongs to, which the runtime holds weakly (ThreadRuntime.App).
    private static AppModel? FindAppModel(ThreadRuntime runtime) => runtime.App;

    // :113-136
    private static bool IsLeadDelegating(IReadOnlyList<TimelineEntry> entries)
    {
        if (entries.Count == 0) return false; // :115

        var lastTurnID = entries[^1].TurnID; // :118
        for (var index = entries.Count - 1; index >= 0; index--) // :119
        {
            var entry = entries[index];
            if (entry.TurnID != lastTurnID) break; // :120

            switch (entry.Item.TimelineItemContent) // :121
            {
                // :122
                case TimelineItem.Content.Tool
                {
                    Value: { ToolCallKind: ToolCall.Kind.Agent, ToolCallStatus: ToolCall.Status.Running },
                }:
                    return true;
                // :124-130
                case TimelineItem.Content.Assistant { Value: var message }:
                    var hasBlock = message.IsStreaming
                        ? HasDelegationBlockWhileStreaming(message.Text, entry.Id)
                        : HasDelegationBlock(message.Text, entry.Id);
                    if (hasBlock) return true;
                    break;
            }
        }

        return false;
    }

    // :146-152
    private static bool HasDelegationBlock(string text, string rowID)
    {
        if (DelegationChecks.TryGetValue(rowID, out var known)) return known;

        var found = HydraPrompts.HasDelegationBlock(text);
        DelegationChecks.Insert(found, rowID);
        return found;
    }

    // :161-174
    private static bool HasDelegationBlockWhileStreaming(string text, string rowID)
    {
        if (StreamingScans.TryGetValue(rowID, out var known) && known.Found) return true; // :163

        var bytes = Encoding.UTF8.GetBytes(text);
        var scanned = StreamingScans.TryGetValue(rowID, out known) ? known.Scanned : 0; // :164
        if (bytes.Length == scanned) return false; // :166

        var from = bytes.Length < scanned ? 0 : Math.Max(0, scanned - 32); // :168
        var tail = Encoding.UTF8.GetString(bytes, from, bytes.Length - from);
        var found = HydraPrompts.HasDelegationOpener(tail); // :170

        if (StreamingScans.Count > 64) StreamingScans.Clear(); // :171
        StreamingScans[rowID] = (bytes.Length, found); // :172
        return found;
    }
}

/// <summary>:200-230 <c>private struct HydraSwitchPopover: View</c>.</summary>
internal sealed class HydraSwitchPopover : Decorator
{
    private readonly ChatThread thread;
    private readonly bool isOn;
    private readonly Binding<bool> isHydraSwitchShown;
    private readonly AppModel? model;

    // :203-206 `@Environment(AppModel.self) model`, `thread`, `isOn`, `@Binding isHydraSwitchShown`; the model has no
    // environment to read here and comes last. Without one the popover reads Hydra as on in Settings and only closes.
    public HydraSwitchPopover(ChatThread thread, bool isOn, Binding<bool> isHydraSwitchShown, AppModel? model = null)
    {
        this.thread = thread;
        this.isOn = isOn;
        this.isHydraSwitchShown = isHydraSwitchShown;
        this.model = model;
        Build();
    }

    // :208-229
    private void Build()
    {
        var content = new StackPanel { Spacing = 4 };
        var settingsEnabled = model?.Settings.HydraEnabled ?? true;

        if (!settingsEnabled)
        {
            content.Children.Add(new PopoverSectionHeader("Hydra is off in Settings")); // :211
            content.Children.Add(new PopoverItem("Turn Hydra on", () => // :212-216
            {
                if (model is not null)
                {
                    model.Settings.HydraEnabled = true;
                    model.SetHydra(true, thread.Id);
                }
                isHydraSwitchShown.Value = false;
            }));
        }
        else
        {
            content.Children.Add(new PopoverSectionHeader(isOn ? "Hydra is on for this chat" : "Hydra is off for this chat")); // :218
            content.Children.Add(new PopoverItem("Turn Hydra on for this chat", () => // :219-222
            {
                model?.SetHydra(true, thread.Id);
                isHydraSwitchShown.Value = false;
            }, isChecked: isOn));
            content.Children.Add(new PopoverItem("Turn Hydra off for this chat", () => // :223-226
            {
                model?.SetHydra(false, thread.Id);
                isHydraSwitchShown.Value = false;
            }, isChecked: !isOn));
        }

        Child = new PopoverMenu(() => content);
    }
}

/// <summary>
/// :232-250 <c>private struct HydraRightTap: NSViewRepresentable</c>: catches a right-tap, a right-click or a
/// control-click on the Hydra mark. A background with no drawing of its own, sized to the button, so the button's
/// left-click, badge, hover and help text are exactly as they were.
/// </summary>
internal sealed class HydraRightTap : Decorator
{
    // :241, :243-249
    public HydraRightTap(Action action)
    {
        Child = new HydraRightTapHost(action);
    }
}

/// <summary>
/// :252-294 <c>private final class HydraRightTapHost: NSView</c>. It watches the window's events rather than hanging a
/// recognizer on itself: a secondary click that lands inside this view's frame fires the action and goes no further;
/// every other event passes untouched. Control-click counts as a secondary click too.
/// </summary>
internal sealed class HydraRightTapHost : Control
{
    private static int installedMonitors;
    private TopLevel? monitoredWindow;

    // :253
    public Action Action { get; set; }

    /// <summary>PORT-ONLY: how many local mouse-down monitors are installed now, for the lifetime test (PR-HYDRA-244).</summary>
    internal static int InstalledMonitors => installedMonitors;

    // :256-259
    public HydraRightTapHost(Action action)
    {
        Action = action;
        IsHitTestVisible = false; // no drawing and no hits of its own
    }

    // :266-269 viewDidMoveToWindow: a window installs the monitor, none removes it
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InstallMonitor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        RemoveMonitor();
    }

    // :271-278 NSEvent.addLocalMonitorForEvents(matching: [.rightMouseDown, .leftMouseDown])
    private void InstallMonitor()
    {
        if (monitoredWindow is not null) return; // :272 guard monitor == nil
        if (TopLevel.GetTopLevel(this) is not { } window) return;
        monitoredWindow = window;
        installedMonitors++;
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // :280-284
    private void RemoveMonitor()
    {
        if (monitoredWindow is not { } window) return; // :281 guard let monitor
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        monitoredWindow = null;
        installedMonitors--;
    }

    // :273-277
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Catches(e)) return;
        Action();
        e.Handled = true; // :276 return nil: the event goes no further
    }

    // :288-293 Whether the event is a secondary click on this view: in its window, over its frame, and either the right
    // button or the left one with Control held.
    private bool Catches(PointerPressedEventArgs e)
    {
        if (monitoredWindow is not { } window || !ReferenceEquals(e.Source is Visual source ? TopLevel.GetTopLevel(source) : null, window)) return false;
        if (!IsEffectivelyVisible || Bounds.Width <= 0 || Bounds.Height <= 0) return false; // :289
        var kind = e.GetCurrentPoint(window).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return false; // :273
        var isSecondary = kind == PointerUpdateKind.RightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control); // :290
        if (!isSecondary) return false;
        return new Rect(Bounds.Size).Contains(e.GetPosition(this)); // :292
    }
}

/// <summary>:296-319 <c>private struct HydraIdlePopover: View</c>.</summary>
internal sealed class HydraIdlePopover : Decorator
{
    // :301, :303-318
    public HydraIdlePopover(ChatThread thread) : this(thread, null, null)
    {
    }

    // :301, :303-318
    public HydraIdlePopover(ChatThread thread, HydraPair? pair, ProviderRegistry? registry)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new PopoverSectionHeader("Hydra is on"));

        if (pair is not null && registry is not null)
        {
            content.Children.Add(new PopoverNote(HydraPairSummary.Title(pair, registry)));
            content.Children.Add(new PopoverNote(HydraPairSummary.Workers(pair, registry)));
        }
        else
        {
            content.Children.Add(new PopoverNote("Heads run on this chat's model and effort."));
        }

        content.Children.Add(new PopoverDivider());
        content.Children.Add(new PopoverNote("No heads are out yet. Ask for something big enough to split up and this chat sends them out, each with a panel of its own."));
        content.Children.Add(new PopoverDivider());
        content.Children.Add(new PopoverNote("Right-click the mark to switch Hydra off for this chat."));

        Child = new PopoverMenu(() => content);
        Width = 290;
    }
}

/// <summary>:321-331 <c>private struct HydraMarkView: View</c>: quiet when Hydra is off, full when it is on or under the pointer.</summary>
internal sealed class HydraMarkView : Decorator
{
    private readonly HydraMarkImage image;
    private readonly AnimatedColor foreground;

    // :323-324, :326-330
    public HydraMarkView(bool isOn, bool isHovering)
    {
        image = new HydraMarkImage
        {
            Width = 18, // :329 .frame(width: 18, height: 18)
            Height = 18,
        };
        foreground = new AnimatedColor(image);
        image.SetValue(TextElement.ForegroundProperty, foreground.Brush);
        Child = image;
        Update(isOn, isHovering);
    }

    /// <summary>The parent passing new inputs (R-VIEW-1): :328 <c>.foregroundStyle(Chrome.primaryText.opacity(...))</c>.</summary>
    public void Update(bool isOn, bool isHovering) =>
        foreground.Set(Chrome.PrimaryText.Opacity(isOn || isHovering ? 1 : 0.6), Transaction.Current);
}

/// <summary>
/// :333-387 <c>struct HydraCharge: View</c>: the charge around an active Hydra button: a ring of the roster's colours and
/// a glow behind the mark. While a head works the ring keeps turning and the glow breathes; with no head out the ring
/// stands still, and so does it under Reduce Motion.
/// </summary>
public sealed class HydraCharge : Decorator
{
    private bool isWorking;
    private HydraChargeMotion? motion;

    // :344
    internal static ValueList<SwiftColor> Colors => new(HydraRoster.Personas.Take(6).Select(persona =>
        new SwiftColor(persona.Color.Red, persona.Color.Green, persona.Color.Blue, persona.Color.Alpha)));

    // :346
    internal static ValueList<SwiftColor> LayerColors => Colors;

    // :342, :348-358
    public HydraCharge(bool isWorking = true)
    {
        this.isWorking = isWorking;
        IsHitTestVisible = false; // :357 .allowsHitTesting(false)
        Rebuild();
    }

    /// <summary>The moving form's layer view, while the charge is in motion; null when it stands still.</summary>
    internal HydraChargeLayerView? LayerView => motion?.LayerView;

    /// <summary>The parent passing new inputs (R-VIEW-1): the body runs again with the current <c>isWorking</c>.</summary>
    internal void Update(bool isWorking)
    {
        if (this.isWorking != isWorking)
        {
            this.isWorking = isWorking;
            Rebuild();
            return;
        }

        motion?.Refresh(); // :403-405 updateNSView: configure(colors:accent:) on every re-render of the button
    }

    // :350 `isWorking, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion`
    private void Rebuild()
    {
        if (isWorking && !Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion)
        {
            motion = new HydraChargeMotion();
            Child = motion;
        }
        else
        {
            motion = null;
            Child = new Grid
            {
                IsHitTestVisible = false,
                Children =
                {
                    Glow(false), // :353
                    Ring(false), // :354
                },
            };
        }
    }

    // :360-372. Fill: accent at 0.46 fading to none from a start radius of 2 to an end radius of 15 points, whatever the
    // circle's size, so the gradient is rebuilt for the size the circle is laid out at.
    public static Control Glow(bool breathing)
    {
        var glow = new Ellipse
        {
            Margin = new Thickness(2), // :370 .padding(2)
            Opacity = breathing ? 1 : 0.6, // :371
        };
        void Fill() => glow.Fill = GlowBrush(Chrome.Accent.Resolve(glow.ActualThemeVariant), glow.Bounds.Size);
        glow.SizeChanged += (_, _) => Fill();
        glow.ActualThemeVariantChanged += (_, _) => Fill();
        Fill();
        return glow;
    }

    // :364-368 RadialGradient(colors: [accent.opacity(0.46), accent.opacity(0)], center: .center, startRadius: 2, endRadius: 15)
    internal static RadialGradientBrush GlowBrush(Color accent, Size size)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        return new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(HydraChargeLayerView.GlowEndRadius / width, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(HydraChargeLayerView.GlowEndRadius / height, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(WithAlpha(accent, 0.46), 0),
                new GradientStop(WithAlpha(accent, 0.46), HydraChargeLayerView.GlowStartRadius / HydraChargeLayerView.GlowEndRadius), // flat inside the start radius
                new GradientStop(WithAlpha(accent, 0), 1),
            ],
        };
    }

    // :374-386
    public static Control Ring(bool turned, bool bright = false)
    {
        var ring = new Ellipse
        {
            Margin = new Thickness(1), // :383 .padding(1)
            StrokeThickness = HydraChargeLayerView.LineWidth, // :381 lineWidth: 2
            Opacity = bright ? 1 : 0.85, // :385
            RenderTransformOrigin = RelativePoint.Center,
        };
        void Stroke() => ring.Stroke = RingBrush(ring.ActualThemeVariant);
        ring.ActualThemeVariantChanged += (_, _) => Stroke();
        Stroke();
        if (turned) ring.RenderTransform = new RotateTransform(360); // :384
        return ring;
    }

    // :376-381 AngularGradient(gradient: Gradient(colors: colors + [colors[0]]), center: .center): the colours spread
    // evenly from 3 o'clock round clockwise to the first again.
    internal static ConicGradientBrush RingBrush(ThemeVariant theme) => HydraChargeLayerView.RingGradient(Colors, theme);

    // :364-368, :576
    internal static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
}

/// <summary>
/// :389-411 <c>private struct HydraChargeMotion: NSViewRepresentable</c>: the charge in motion. A view of its own, so the
/// repeating animations start when a head goes out and are torn down with it when the last one is back.
/// </summary>
internal sealed class HydraChargeMotion : Decorator
{
    // :397-401 makeNSView
    public HydraChargeMotion()
    {
        LayerView = new HydraChargeLayerView();
        Child = LayerView;
        Refresh();
    }

    internal HydraChargeLayerView LayerView { get; }

    // :399, :403-405 configure(colors: HydraCharge.layerColors, accent: Chrome.accentNSColor)
    internal void Refresh() => LayerView.Configure(HydraCharge.LayerColors, Chrome.AccentNSColor);
}

/// <summary>
/// :413-613 <c>final class HydraChargeLayerView: NSView</c>: the moving charge as layers: the glow, a radial gradient
/// breathing between the still glow's opacity and full, and the ring, a conic gradient turning once every four seconds
/// and brightening in step with the glow. The geometry is the still charge's exactly. Every animation runs from one
/// fixed origin on the shared clock, so putting it back on wherever it may have been dropped changes nothing on screen.
/// </summary>
/// <remarks>
/// Core Animation has no Avalonia twin. What <c>installAnimations</c> adds to the two layers is kept as data
/// (<see cref="Animations"/>); the frame the render server would draw is a pure function of the shared clock
/// (<see cref="Phase"/>), played from the frame source (ED-117), as HydraBreathRingView does. A conic gradient grows
/// clockwise here, so the ring's colours go in as they are, where the layer's grew counter-clockwise and needed them
/// reversed (:568-579).
/// </remarks>
public sealed class HydraChargeLayerView : Control
{
    internal const string TurnKey = "turn"; // :428
    internal const string BreathKey = "breathe"; // :429
    internal const float RingRestOpacity = 0.85f; // :431
    internal const float GlowRestOpacity = 0.6f; // :432
    internal const double TurnDuration = 4; // :433
    internal const double BreathDuration = 1.4; // :434
    internal const double LineWidth = 2; // :435
    internal const double GlowStartRadius = 2; // :437
    internal const double GlowEndRadius = 15; // :438

    /// <summary>:586 <c>ring.convertTime(1, from: nil)</c>: one second on the shared clock, the one origin of every animation.</summary>
    internal const double Origin = 1;

    /// <summary>The two layers.</summary>
    internal enum ChargeLayer
    {
        Ring,
        Glow,
    }

    /// <summary>One <c>CABasicAnimation</c> a layer holds, as its properties.</summary>
    internal sealed record LayerAnimation(
        ChargeLayer Layer,
        string Key,
        string KeyPath,
        double From,
        double To,
        double Duration,
        bool AutoReverses,
        bool RepeatsForever,
        string Timing,
        double BeginTime,
        bool IsRemovedOnCompletion);

    // :425-426
    private IReadOnlyList<SwiftColor> colors = Array.Empty<SwiftColor>();
    private SwiftColor accent = SwiftColor.ControlAccentColor;

    // :422-424 the layers' resolved colours (applyColors) and what installAnimations has put on them
    private Color glowAccent;
    private Color[] ringColors = [];
    private readonly List<LayerAnimation> animations = [];

    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool waiting;
    private Marrow.UI.Common.DecorationPace pace;

    // ED-NEW-lean-8: the glow's gradient and the ring's pen, made again only when their colours or size change, not once per
    // frame. The same brushes, so the same pixels.
    private RadialGradientBrush? glowBrush;
    private Color glowBrushAccent;
    private Size glowBrushSize;
    private Pen? ringPen;
    private Color[]? ringPenColors;

    // :474-479 the three NotificationCenter observers, added in init and removed in deinit: kept weakly, so a view that is
    // let go is let go (R-NOTE-1; the platform has no deinit to unsubscribe in).
    private static readonly List<WeakReference<HydraChargeLayerView>> Live = [];

    static HydraChargeLayerView()
    {
        ThemeManager.DidChange += () => Notify(view => view.OnThemeChanged()); // :479
    }

    // :440-480
    public HydraChargeLayerView()
    {
        Focusable = false;
        IsHitTestVisible = false; // :491 override func hitTest(_:) -> NSView? { nil }
        frame = OnFrame;
        RingOpacity = RingRestOpacity;
        GlowOpacity = GlowRestOpacity;
        ActualThemeVariantChanged += (_, _) => ApplyColors(); // :512-516 viewDidChangeEffectiveAppearance
        lock (Live) Live.Add(new WeakReference<HydraChargeLayerView>(this));
    }

    // ThemeManager is set from any actor and posts DidChange on the setter's thread: each view takes the notification on its own
    // thread (now if this is it, else posted), as every other theme observer does.
    private static void Notify(Action<HydraChargeLayerView> post)
    {
        WeakReference<HydraChargeLayerView>[] live;
        lock (Live) live = Live.ToArray();
        foreach (var weak in live)
        {
            if (!weak.TryGetTarget(out var view)) { lock (Live) Live.Remove(weak); continue; }
            if (view.CheckAccess()) post(view);
            else view.Dispatcher.Post(() => post(view));
        }
    }

    /// <summary>The ring's turn now, in degrees clockwise on screen (a model value of 0 while no turn is attached).</summary>
    internal double TurnDegrees { get; private set; }

    internal double RingOpacity { get; private set; }

    internal double GlowOpacity { get; private set; }

    /// <summary>The animations the layers hold now.</summary>
    internal IReadOnlyList<LayerAnimation> Animations => animations;

    /// <summary>How many times installAnimations has run on this view.</summary>
    internal int InstallCount { get; private set; }

    /// <summary>How many times applyColors has resolved the layers' colours on this view.</summary>
    internal int RecolorCount { get; private set; }

    /// <summary>The colours and the accent as last stored (:425-426).</summary>
    internal IReadOnlyList<SwiftColor> StoredColors => colors;

    internal SwiftColor StoredAccent => accent;

    /// <summary>The glow's gradient stops and the ring's conic colours, as last resolved.</summary>
    internal Color ResolvedGlowAccent => glowAccent;

    internal IReadOnlyList<Color> ResolvedRingColors => ringColors;

    internal bool HasAnimation(ChargeLayer layer, string key) => animations.Any(item => item.Layer == layer && item.Key == key);

    /// <summary><c>layer.removeAnimation(forKey:)</c>: what puts a lost animation back is the lifecycle, not the render.</summary>
    internal void RemoveAnimation(ChargeLayer layer, string key)
    {
        animations.RemoveAll(item => item.Layer == layer && item.Key == key);
        Refresh();
        InvalidateVisual();
    }

    // :493-501
    public void Configure(IReadOnlyList<SwiftColor> colors, SwiftColor accent)
    {
        var recolor = !SameColors(colors, this.colors) || !SameColor(accent, this.accent);
        this.colors = colors.ToArray();
        this.accent = accent;
        if (recolor) ApplyColors();
        // The animations go on once; the lifecycle puts them back wherever they can be lost. A re-render of the button
        // (every hover) is not one of those.
        if (!HasAnimation(ChargeLayer.Ring, TurnKey)) InstallAnimations();
    }

    // :503-510 viewDidMoveToWindow. Joining a window late (a button built off screen), the layers need their colours
    // resolved for its appearance and their animations running from the shared clock. Leaving one does nothing.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        source = FrameClock.Source.Current;
        ApplyColors();
        InstallAnimations();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (waiting) source?.CancelFrame(frame);
        waiting = false;
        source = null;
    }

    /// <summary>
    /// PORT-ONLY seam for <c>NSApplication.didBecomeActiveNotification</c> (:475; R-NOTE-1): the application shell posts it
    /// when the application is activated, and every live charge view hears it (the observer has no object filter).
    /// </summary>
    public static void PostApplicationDidBecomeActive() => Notify(view => view.OnApplicationActive());

    /// <summary>
    /// PORT-ONLY seam for <c>NSWindow.didChangeOcclusionStateNotification</c> (:476): the window that changed and whether it
    /// is now visible. Every window ordered in posts it, popovers and tooltips included.
    /// </summary>
    public static void PostWindowDidChangeOcclusionState(TopLevel window, bool isVisible) =>
        Notify(view => view.OnOcclusionChanged(window, isVisible));

    // :518-521
    private void OnApplicationActive()
    {
        if (TopLevel.GetTopLevel(this) is null) return; // guard window != nil
        InstallAnimations();
    }

    // :523-528 Every window ordered in posts this, popovers and tooltips included: only this view's own window coming back
    // into view concerns it.
    private void OnOcclusionChanged(TopLevel window, bool isVisible)
    {
        if (TopLevel.GetTopLevel(this) is not { } own || !ReferenceEquals(own, window) || !isVisible) return;
        InstallAnimations();
    }

    // :530-533 The accent is set on the glow once, not resolved on every draw, so a theme change has to reach it here.
    private void OnThemeChanged()
    {
        accent = Chrome.AccentNSColor;
        ApplyColors();
    }

    // :568-579 The accent, resolved in this view's appearance, into the glow's stops; the ring's colours into the
    // gradient.
    private void ApplyColors()
    {
        if (colors.Count == 0) return; // :569 guard let first = colors.first
        var theme = ActualThemeVariant;
        glowAccent = accent.Resolve(theme);
        ringColors = colors.Append(colors[0]).Select(color => color.Resolve(theme)).ToArray();
        RecolorCount++;
        InvalidateVisual();
    }

    // :583-612 Puts the turn and the two breaths on afresh, each from the one origin on the shared clock, so however often
    // they are put back the phase is the one the clock says.
    private void InstallAnimations()
    {
        animations.Clear(); // :584-585 removeAllAnimations
        // :588-598 the turn: a negative angle, because the layer is y-up: it turns clockwise on screen
        animations.Add(new LayerAnimation(
            ChargeLayer.Ring, TurnKey, "transform.rotation.z", 0, -2 * Math.PI, TurnDuration,
            AutoReverses: false, RepeatsForever: true, Timing: "linear", BeginTime: Origin, IsRemovedOnCompletion: false));
        // :600-611 the breaths, from the rest opacity to full
        foreach (var (layer, rest) in new[] { (ChargeLayer.Ring, (double)RingRestOpacity), (ChargeLayer.Glow, (double)GlowRestOpacity) })
        {
            animations.Add(new LayerAnimation(
                layer, BreathKey, "opacity", rest, 1, BreathDuration,
                AutoReverses: true, RepeatsForever: true, Timing: "easeInEaseOut", BeginTime: Origin, IsRemovedOnCompletion: false));
        }

        InstallCount++;
        Refresh();
        InvalidateVisual();
        Request();
    }

    /// <summary>
    /// :588-611 the three animations as a pure function of the seconds since their shared origin: the ring's turn
    /// (degrees clockwise, linear, once every 4 s) and, for the ring and the glow, the breath (the rest opacity up to full
    /// over 1.4 s and back, ease-in ease-out), without end.
    /// </summary>
    internal static (double TurnFraction, double BreathProgress) Phase(double elapsedSeconds)
    {
        var elapsed = Math.Max(0, elapsedSeconds);
        var turn = elapsed % TurnDuration / TurnDuration;
        var leg = elapsed % (2 * BreathDuration); // autoreverses
        var forward = leg <= BreathDuration ? leg : 2 * BreathDuration - leg;
        return (turn, BreathCurve.Progress(forward));
    }

    // ED-NEW-lean-8 review: the breath's curve, made once (an immutable timing curve; a new one each frame was the frame's
    // only allocation of its own).
    private static readonly Animation BreathCurve = Animation.EaseInOut(BreathDuration);

    // What the layers show now: each attached animation's value at the shared clock, the model value where none is attached.
    private void Refresh()
    {
        var elapsed = (SwiftClock.Now - default(ContinuousInstant)).TotalSeconds - Origin;
        var (turn, breath) = Phase(elapsed);
        var turning = animations.FirstOrDefault(item => item.Layer == ChargeLayer.Ring && item.Key == TurnKey);
        TurnDegrees = turning is null ? 0 : -turning.To * 180 / Math.PI * turn;
        var ringBreath = animations.FirstOrDefault(item => item.Layer == ChargeLayer.Ring && item.Key == BreathKey);
        RingOpacity = ringBreath is null ? RingRestOpacity : ringBreath.From + (ringBreath.To - ringBreath.From) * breath;
        var glowBreath = animations.FirstOrDefault(item => item.Layer == ChargeLayer.Glow && item.Key == BreathKey);
        GlowOpacity = glowBreath is null ? GlowRestOpacity : glowBreath.From + (glowBreath.To - glowBreath.From) * breath;
    }

    private void Request()
    {
        if (waiting || source is null) return;
        pace.Reset(); // a new run of frames repaints at its first
        waiting = true;
        source.RequestFrame(frame);
    }

    /// <summary>ED-NEW-lean-8 (tests): how many frames have repainted the charge.</summary>
    internal int Invalidations { get; private set; }

    // ED-NEW-lean-8: the turn and the breaths repaint at the 60 Hz cap (DecorationPace); the frames between only ask again.
    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (source is null) return;
        if (pace.Due(SwiftClock.Now))
        {
            Refresh();
            Invalidations++;
            InvalidateVisual();
        }
        waiting = true;
        source.RequestFrame(frame);
    }

    // :535-562 layout: the still charge's geometry exactly
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 4 || bounds.Height <= 4) return; // :538

        // :543-550 a circle two points in, its gradient running from two to fifteen points out of the centre
        var glowRect = bounds.Deflate(2);
        if (glowBrush is null || glowBrushAccent != glowAccent || glowBrushSize != glowRect.Size)
        {
            glowBrush = HydraCharge.GlowBrush(glowAccent, glowRect.Size);
            glowBrushAccent = glowAccent;
            glowBrushSize = glowRect.Size;
        }
        var glow = glowBrush;
        using (context.PushOpacity(GlowOpacity))
            context.DrawEllipse(glow, null, glowRect.Center, glowRect.Width / 2, glowRect.Height / 2);

        // :553-560 two points of stroke just inside a circle one point in
        if (ringColors.Length == 0) return;
        var ringRect = bounds.Deflate(1);
        var radiusX = ringRect.Width / 2 - LineWidth / 2;
        var radiusY = ringRect.Height / 2 - LineWidth / 2;
        var centre = ringRect.Center;
        var turn = Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(TurnDegrees * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y);
        if (ringPen is null || !ReferenceEquals(ringPenColors, ringColors)) // ApplyColors stores a new array whenever it recolours
        {
            ringPen = new Pen(RingGradient(ringColors), LineWidth);
            ringPenColors = ringColors;
        }
        using (context.PushOpacity(RingOpacity))
        using (context.PushTransform(turn))
            context.DrawEllipse(null, ringPen, centre, radiusX, radiusY);
    }

    // The ring's conic gradient from resolved colours: evenly spread from 3 o'clock, clockwise.
    internal static ConicGradientBrush RingGradient(IReadOnlyList<SwiftColor> colors, ThemeVariant theme) =>
        RingGradient(colors.Append(colors[0]).Select(color => color.Resolve(theme)).ToArray());

    private static ConicGradientBrush RingGradient(Color[] closed)
    {
        var stops = new GradientStops();
        for (var index = 0; index < closed.Length; index++)
            stops.Add(new GradientStop(closed[index], index / (double)(closed.Length - 1)));
        return new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Angle = 90, // Avalonia's sweep starts at 12 o'clock; SwiftUI's and the layer's at 3
            GradientStops = stops,
        };
    }

    // :494 colors != self.colors, by value
    private static bool SameColors(IReadOnlyList<SwiftColor> first, IReadOnlyList<SwiftColor> second)
    {
        if (first.Count != second.Count) return false;
        for (var index = 0; index < first.Count; index++)
            if (!SameColor(first[index], second[index])) return false;
        return true;
    }

    // :494 accent != self.accent, by value in both appearances
    private static bool SameColor(SwiftColor first, SwiftColor second) =>
        first.Resolve(ThemeVariant.Light) == second.Resolve(ThemeVariant.Light) &&
        first.Resolve(ThemeVariant.Dark) == second.Resolve(ThemeVariant.Dark);
}

/// <summary>
/// :615-646 <c>private struct HydraSendWorkingBadge: View</c>: the count of heads at work, above the button's top-right.
/// While the lead delegates it first shows the sending pulse, then morphs in place into the working count: the same
/// capsule at the same size, so nothing jumps and no second row ever appears.
/// </summary>
internal sealed class HydraSendWorkingBadge : Decorator
{
    private readonly Border content = new() { MinWidth = 14, MinHeight = 14 }; // :635 .frame(minWidth: 14, minHeight: 14)
    private readonly Border capsule = new();
    private readonly AnimatedColor tint;
    private readonly TextBlock count = new();
    private readonly NthBeadsSlot spinner = new(NthBeadsPlace.HydraBadge, "hydra-send-badge", running: false); // :627, the quiet Tripus beads in place of the accent MiniSpinner (Calculi WU6)
    private long running;
    private bool isSending;

    // :621-622, :624-645
    public HydraSendWorkingBadge(long running, bool isSending)
    {
        this.running = running;
        this.isSending = isSending;

        // :630-632 .font(.system(.caption2, weight: .bold).monospacedDigit()).foregroundStyle(.white)
        SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold).MonospacedDigit().Apply(count);
        count.Foreground = Brushes.White;
        count.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        count.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        spinner.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        spinner.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        capsule.Padding = new Thickness(4, 1); // :636-637 .padding(.horizontal, 4).padding(.vertical, 1)
        capsule.CornerRadius = new CornerRadius(999); // :640 Capsule()
        capsule.Child = content;
        tint = new AnimatedColor(capsule);
        capsule.Background = tint.Brush;
        Child = capsule;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :644 .accessibilityHidden(true)
        Show(animated: false);
    }

    /// <summary>The parent passing new inputs (R-VIEW-1).</summary>
    public void Update(long running, bool isSending)
    {
        var changed = this.isSending != isSending;
        this.running = running;
        this.isSending = isSending;
        // :642 .animation(Chrome.panelSlide, value: isSending): the tint crossfades, never a jump
        Transaction.WithAnimation(changed ? Chrome.PanelSlide : Transaction.Current, () => Show(animated: true));
    }

    private void Show(bool animated)
    {
        // :626-633 Group { if isSending { MiniSpinner } else { Text } }
        if (isSending)
        {
            count.Text = null;
            content.Child = spinner;
            spinner.Begin();
        }
        else
        {
            spinner.End();
            count.Text = running.ToString(CultureInfo.InvariantCulture); // :629 Text(verbatim: "\(running)")
            content.Child = count;
        }

        // :640 .background(isSending ? Chrome.glassTint : Chrome.accent, in: Capsule())
        tint.Set(isSending ? Chrome.GlassTint : Chrome.Accent, animated ? Transaction.Current : null);
        // :643
        ToolTip.SetTip(this, running == 1
            ? "1 head working \U000000B7 Show or hide the Hydra panel"
            : $"{running.ToString(CultureInfo.InvariantCulture)} heads working \U000000B7 Show or hide the Hydra panel");
    }
}

/// <summary>:648-711 <c>enum HydraPairSummary</c>: one-line summaries of a pair.</summary>
public static class HydraPairSummary
{
    // :652-657
    public static string? WorkerModel(HydraPair pair, ProviderRegistry registry)
    {
        if (pair.WorkerModel is { } model)
            return registry.Model(model, pair.HeadsProvider)?.ShortName ?? model;

        if (!pair.SendsHeadsElsewhere) return null;
        return registry.DefaultModel(pair.HeadsProvider)?.ShortName;
    }

    // :661-673
    public static string Workers(HydraPair pair, ProviderRegistry registry)
    {
        var parts = new List<string>();
        if (WorkerModel(pair, registry) is { } model)
            parts.Add("Heads on " + model + (pair.SendsHeadsElsewhere ? $" ({pair.HeadsProvider.DisplayName})" : ""));
        else if (pair.SendsHeadsElsewhere)
            parts.Add($"Heads on {pair.HeadsProvider.DisplayName}");
        else
            parts.Add("Heads on the chat's model");

        if (pair.WorkerEffort is { } effort)
            parts.Add(ModelOption.EffortTitle(effort) + " effort");

        return string.Join(" \U000000B7 ", parts);
    }

    // :676-678
    public static string Providers(HydraPair pair) =>
        pair.SendsHeadsElsewhere
            ? $"{pair.Provider.DisplayName} lead, {pair.HeadsProvider.DisplayName} heads"
            : pair.Provider.DisplayName;

    // :683-693
    public static string? Efforts(HydraPair pair)
    {
        var lead = pair.OrchestratorEffort is { } leadEffort
            ? SwiftString.Lowercased(ModelOption.EffortTitle(leadEffort))
            : null;
        var heads = pair.WorkerEffort is { } headsEffort
            ? SwiftString.Lowercased(ModelOption.EffortTitle(headsEffort))
            : null;

        if (lead is not null && heads is not null)
        {
            if (SwiftString.Equal(lead, heads))
                // :687 lead.capitalized: each word of the lowercased effort title (including "extra high").
                // UNVERIFIED O-07: invariant TextInfo word boundaries outside the pinned effort titles may
                // differ from NSString.capitalized; all catalog titles, including "extra high", agree.
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lead) + " effort";
            return $"Lead {lead}, heads {heads} effort";
        }

        if (lead is not null) return $"Lead {lead} effort";
        if (heads is not null) return $"Heads {heads} effort";
        return null;
    }

    // :697-704
    public static string ModelsTitle(HydraPair pair, ProviderRegistry registry)
    {
        var lead = pair.OrchestratorModel is { } model
            ? registry.Model(model, pair.Provider)?.ShortName ?? model
            : $"Any {pair.Provider.DisplayName} model";

        var worker = WorkerModel(pair, registry) ?? (pair.SendsHeadsElsewhere ? pair.HeadsProvider.DisplayName : null);
        return worker is null ? $"{lead} + itself" : $"{lead} + {worker}";
    }

    // :707-710
    public static string Title(HydraPair pair, ProviderRegistry registry) =>
        pair.CustomName ?? ModelsTitle(pair, registry);
}
