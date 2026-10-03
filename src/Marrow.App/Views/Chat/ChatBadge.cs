using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/ChatBadge.swift (B2 1fbf81e), :1-200.

namespace Marrow.App.Views.Chat;

/// <summary>ChatBadge (:4-81).</summary>
public sealed class ChatBadge : Decorator
{
    private readonly Button button;
    // :16 the detail builder, read when the popover opens (:69): the latest render's, as SwiftUI's body would hand it.
    private Func<Control> detail;

    // :10-17. `isPresented`, `glyph` and `detail` have no default and move before the defaulted ones (ED-095).
    public ChatBadge(
        string title,
        Binding<bool> isPresented,
        Func<Control> glyph,
        Func<Control> detail,
        string? caption = null,
        bool showsChevron = true,
        bool needsAttention = false,
        bool isEnabled = true,
        BadgePopoverCoordinator? coordinator = null)
    {
        this.detail = detail;
        // :20-21 @State private var coordinator. PORT-ONLY (ED-NEW-parity-timelinerows, not ED-095, which covers only the order of parameters): a host whose own view identity outlives the badge
        // control (PlanCard, kept by ForEach(groups) in SwiftUI while the port rebuilds the block that holds it) hands in
        // the coordinator it keeps for that identity, so a popover that is open stays the same popover.
        var held = coordinator ?? new BadgePopoverCoordinator();

        button = PlainControls.Button(Label(title, glyph, caption, showsChevron, needsAttention),
            () => isPresented.Value = !isPresented.Value); // :23 Button { isPresented.toggle() }
        // :66 windowRectAnchor is applied here, BEFORE :76's outer .frame(maxWidth: .infinity, alignment: .leading):
        // the reported rect is the button's own natural/compact pill size, left-aligned inside the wider `aligned`
        // wrapper below, not stretched to it (R-VIEW review fix; was HorizontalAlignment.Stretch with no citation,
        // which inflated the anchor rect WindowRectAnchor reports to the full row width).
        button.HorizontalAlignment = HorizontalAlignment.Left;
        Describe(title, needsAttention, isEnabled);

        // :66
        button.WindowRectAnchor(held.SetAnchor);

        // :67-73
        button.Track(() => isPresented.Value, shown =>
        {
            if (shown)
            {
                if (!held.IsShown) held.Show(this.detail().PresentedChrome()); // :69
            }
            else
            {
                held.Close(); // :71
            }
        });

        // :74-75 .onAppear { coordinator.onClose = ... } and .onDisappear { coordinator.close() }
        button.AttachedToVisualTree += (_, _) => held.Appear(button, () => isPresented.Value = false);
        button.DetachedFromVisualTree += (_, _) => held.Disappear(button);

        // :76-79
        var aligned = new Border
        {
            Child = button,
            HorizontalAlignment = HorizontalAlignment.Stretch, // :76 .frame(maxWidth: .infinity, alignment: .leading)
            Padding = new Thickness(0, 0, 96, 0), // :77 .padding(.trailing, 96)
        };
        Child = aligned;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>PORT-ONLY (ED-NEW-parity-timelinerows, not ED-095, which covers only the order of parameters): the host's body ran again with new values for the same badge. In SwiftUI the badge
    /// keeps its identity across its host's re-renders, so its @State coordinator (:20) and an open popover survive while
    /// the label shows the new values; the popover keeps the content it was opened with (:138-147 setFixedContent) and
    /// the next opening builds the new detail. Hosts whose values change under an open badge call this instead of
    /// building a new badge (PlanCard, TimelineRows.swift:2263-2316; PR-CHAT-113).</summary>
    public void Update(string title, Func<Control> glyph, Func<Control> detail, string? caption = null, bool showsChevron = true,
        bool needsAttention = false, bool isEnabled = true)
    {
        this.detail = detail;
        button.Content = Label(title, glyph, caption, showsChevron, needsAttention);
        Describe(title, needsAttention, isEnabled);
    }

    private void Describe(string title, bool needsAttention, bool isEnabled)
    {
        button.IsEnabled = isEnabled; // :53 .disabled(!isEnabled)
        AutomationProperties.SetName(button, title); // :78 .accessibilityLabel(Text(title))
        AutomationProperties.SetItemStatus(button, needsAttention ? "Needs your attention" : ""); // :79 .accessibilityValue(...)
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetItemStatus(this, needsAttention ? "Needs your attention" : "");
    }

    // :23-65 the button's label: the pill and, above it, the attention dot field.
    private Control Label(string title, Func<Control> glyph, string? caption, bool showsChevron, bool needsAttention)
    {
        // :23-53
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8, // :24 HStack(spacing: 8)
            VerticalAlignment = VerticalAlignment.Center,
        };

        var glyphView = glyph(); // :25
        glyphView.Width = 18; // :26 .frame(width: 18, height: 18)
        glyphView.Height = 18;
        glyphView.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyphView);

        var titleText = new TextBlock
        {
            Text = title, // :27 Text(verbatim: title)
            MaxLines = 1, // :30 .lineLimit(1)
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, // :31 .truncationMode(.tail)
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.Callout.FontWeight(SwiftFont.Weight.Medium).Apply(titleText); // :28 .font(.chat(.callout, weight: .medium))
        titleText.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(titleText); // :29
        row.Children.Add(titleText);

        if (caption is { } captionText) // :32
        {
            var captionBlock = new TextBlock
            {
                Text = captionText, // :33 Text(verbatim: caption)
                MaxLines = 1, // :36 .lineLimit(1)
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, // :37 .truncationMode(.tail)
                VerticalAlignment = VerticalAlignment.Center,
            };
            SwiftFont.Caption.Apply(captionBlock); // :34 .font(.chat(.caption))
            captionBlock.Foreground = Chrome.SecondaryText.Brush(captionBlock); // :35
            row.Children.Add(captionBlock);
        }

        if (showsChevron) // :39
        {
            var chevron = SwiftImage.SystemName("chevron.right") // :40
                .Font(SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Semibold)) // :41 .font(.chat(.caption2, weight: .semibold))
                .ForegroundStyle(Chrome.SecondaryText); // :42
            chevron.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(chevron);
        }

        // :45-50 The whole pill takes the click, padding included, as the Hydra pills do.
        var pill = new Border
        {
            Padding = new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, // :46-48
                TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical),
            Background = Chrome.Accent.Opacity(0.1).Brush(this), // :49 .background(.tint.opacity(0.1), in: ...)
            Child = row,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        pill.CornerRadius = new CornerRadius(TimelineMetrics.PillRadius); // :49 RoundedRectangle(cornerRadius:, style: .continuous)

        // :54-65
        var layers = new BadgeLayers(pill); // :49-50 the pill and, above it, the attention dot field
        if (needsAttention) // :55
        {
            var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this); // :19 @Environment(\.accessibilityReduceMotion)
            var dotField = new DotFieldControl(Chrome.AccentNSColor, animated: !reduceMotion, sweeps: false); // :57 DotFieldLayerView.Representable(color:animated:), still rendered (static) under reduce motion
            dotField.RenderTransform = new ScaleTransform(1, -1); // :58 .scaleEffect(y: -1), on the dot field itself, inside its quarter frame
            var attentionLayer = new BadgeAttentionQuarter(dotField); // :59 .frame(height: proxy.size.height * 0.25)
            attentionLayer.IsHitTestVisible = false; // :61 .allowsHitTesting(false)
            AutomationProperties.SetAccessibilityView(attentionLayer, AccessibilityView.Raw); // :62 .accessibilityHidden(true)
            AutomationProperties.SetAccessibilityView(dotField, AccessibilityView.Raw);
            layers.Children.Add(attentionLayer);

            layers.Track(
                () => EnvironmentValues.GetAccessibilityReduceMotion(layers),
                motion => dotField.UpdateAnimated(!motion));
        }
        return layers;
    }
}

/// <summary>`extension View` (:83-91). A static class named after the Swift file would clash with the view type
/// ChatBadge, so it takes the Extensions suffix R-NAME-4.</summary>
public static class ChatBadgeExtensions
{
    // :88-90 badgePopover(isPresented:detail:)
    public static Control BadgePopover(this Control view, Binding<bool> isPresented, Func<Control> detail)
    {
        var coordinator = new BadgePopoverCoordinator();
        var host = new Border { Child = view };

        // :100
        host.WindowRectAnchor(coordinator.SetAnchor);

        // :101-107
        host.Track(() => isPresented.Value, shown =>
        {
            if (shown)
            {
                if (!coordinator.IsShown) coordinator.Show(detail().PresentedChrome()); // :103
            }
            else
            {
                coordinator.Close(); // :105
            }
        });

        // :108-109 .onAppear { coordinator.onClose = { isPresented = false } } and .onDisappear { coordinator.close() }. The same
        // reading of onDisappear as ChatBadge's own (:74-75): gone only if still out of the tree once the pass is over, so a
        // row the timeline merely re-parents (ThreadTimeline.Draw; the Hydra report, heads and brief pills,
        // TimelineRows.swift:293, :470, :583) keeps its open popover (PR-CHAT-143). ENGINEERING DECISION
        // ED-NEW-parity-timelinerows.
        host.AttachedToVisualTree += (_, _) => coordinator.Appear(host, () => isPresented.Value = false);
        host.DetachedFromVisualTree += (_, _) => coordinator.Disappear(host);
        return host;
    }
}

/// <summary>BadgePopoverModifier (:93-111), `private struct` at file scope -> internal (R-NAME-6).</summary>
internal sealed record BadgePopoverModifier(Binding<bool> IsPresented, Func<Control> Detail) // :94-95
{
    // :98-110
    public Control Body(Control content) =>
        ChatBadgeExtensions.BadgePopover(content, IsPresented, Detail);
}

/// <summary>BadgePopoverCoordinator (:113-200). @MainActor final class, NSObject and NSPopoverDelegate: a plain
/// class; the delegate's popoverDidClose is a method the Popup's Closed event calls.</summary>
public sealed class BadgePopoverCoordinator
{
    private readonly Popup popover = new(); // :118
    private Rect? anchorRect; // :119
    // :121 private weak var shownIn: NSWindow?: weak, so a coordinator the host keeps (PlanCard's identity outlives the badge,
    // TimelineRows.cs) never keeps a closed window alive. ENGINEERING DECISION ED-NEW-parity-timelinerows (the source's weak).
    private WeakReference<Avalonia.Controls.Window>? shownInWindow;
    private Rect shownRect; // :122
    private bool isShown;
    private bool closeCallbackPending;
    private bool closeCallbackFired;

    // :123. A `(() -> Void)?` is an Action?.
    public Action? OnClose { get; set; }

    // The badge control that last appeared over this coordinator (the view whose onAppear/onDisappear it follows).
    private Control? presence;

    /// <summary>:74 <c>.onAppear { coordinator.onClose = { isPresented = false } }</c>, for the badge control now in the tree.</summary>
    internal void Appear(Control view, Action onClose)
    {
        presence = view;
        OnClose = onClose;
    }

    /// <summary>:75 <c>.onDisappear { coordinator.close() }</c>. ENGINEERING DECISION ED-NEW-parity-timelinerows: SwiftUI's onDisappear fires when the view
    /// has left the hierarchy; it never fires for a view that merely moved. The port's timeline lays its rows out again
    /// by taking each kept row out of its old stack and putting it into a new one (ThreadTimeline.Draw), so a badge is
    /// detached and attached again inside one synchronous pass. The badge has disappeared only if, once the pass is over,
    /// no control is attached over this coordinator any more: another badge that appeared over it (a host's rebuilt copy
    /// of the same identity) or the same badge back in the tree is the view still being there.</summary>
    internal void Disappear(Control view) => Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(presence, view) || view.IsAttachedToVisualTree()) return;
        presence = null; // the detached control is not kept alive by the identity's coordinator
        Close();
    });

    // :125-130
    public BadgePopoverCoordinator()
    {
        popover.IsLightDismissEnabled = false; // :127 popover.behavior = .applicationDefined
        popover.Closed += (_, _) => PopoverDidClose(); // :129 popover.delegate = self
    }

    // :132-134. Swift: setAnchor(windowRect:).
    public void SetAnchor(Rect windowRect) => anchorRect = windowRect;

    // :136
    public bool IsShown => isShown;

    // :138-152. Swift: show(_:), generic over Content: View.
    public void Show(Control content)
    {
        // :139
        if (anchorRect is not { } rect || WindowRectAnchor.Target(rect) is not { } anchor) return;

        // :140-147
        if (isShown) Close(); // :141 Opened again while the last one is still fading out: that one goes at once.
        var weakSelf = new WeakReference<BadgePopoverCoordinator>(this);
        Popovers.SetClosePopover(content, () =>
        {
            if (weakSelf.TryGetTarget(out var coordinator)) coordinator.Close();
        }); // :144 content.environment(\.closePopover, { [weak self] in self?.close() })
        // :145 NSHostingView(rootView: content).fittingSize: the size of a hosted view, so measured in a window, where the
        // content's buttons and fields have their templates (detached, the question sheet measured 246 pt, not 262).
        var size = StyledMeasure.Of(content, Size.Infinity);
        if (size.Width <= 0 || size.Height <= 0) size = new Size(460, 320); // :146
        popover.SetFixedContent(content, size); // :147 measured once and frozen

        var window = anchor.View as Avalonia.Controls.Window
            ?? TopLevel.GetTopLevel(anchor.View) as Avalonia.Controls.Window; // :148
        shownInWindow = window is null ? null : new WeakReference<Avalonia.Controls.Window>(window);
        var origin = window is not null
            ? anchor.View.TranslatePoint(anchor.Rect.TopLeft, window) ?? anchor.Rect.TopLeft
            : anchor.Rect.TopLeft;
        shownRect = new Rect(origin, anchor.Rect.Size); // :149 window coordinates for outside-click tests
        popover.PlacementTarget = anchor.View; // :150 popover.show(relativeTo: anchor.rect, of: anchor.view, preferredEdge: anchor.view.isFlipped ? .minY : .maxY)
        popover.PlacementRect = anchor.Rect;
        popover.Placement = PlacementMode.Bottom; // :150 Avalonia's top-left-origin window corresponds to a flipped anchor.
        isShown = true;
        closeCallbackPending = false;
        closeCallbackFired = false;

        // :150-151
        Presentation.Presenter.Current.SetPresented(popover, true);
        StartMonitors();
    }

    // :156-160
    public void Close()
    {
        if (!isShown) return;
        StopMonitors(); // :158
        isShown = false;
        closeCallbackPending = true;
        Presentation.Presenter.Current.SetPresented(popover, false); // :159 popover.performClose(nil)
    }

    private Avalonia.Controls.Window? ShownIn =>
        shownInWindow is not null && shownInWindow.TryGetTarget(out var window) ? window : null;

    // :162-167. Swift: popoverDidClose(_:), nonisolated.
    public void PopoverDidClose()
    {
        StopMonitors(); // :164
        // A closed NSPopover lets go of its content view controller and its positioning view; the Popup keeps its Child and
        // its PlacementTarget until told otherwise, and a coordinator a host keeps for longer than the badge (PlanCard's
        // identity, as long as the thread keeps the entry) would hold the window and the frozen detail tree. ENGINEERING
        // DECISION ED-NEW-parity-timelinerows. The next Show sets both again.
        popover.PlacementTarget = null;
        popover.Child = null;
        if (isShown)
        {
            isShown = false;
            closeCallbackPending = true;
        }

        // :165 once, whichever way it went.
        if (!closeCallbackPending || closeCallbackFired) return;
        closeCallbackFired = true;
        closeCallbackPending = false;
        OnClose?.Invoke();
    }

    // :169-187
    private void StartMonitors()
    {
        if (ShownIn is not { } window) return;
        window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true); // :171-179
        window.AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true); // :182-186

        if (popover.Child is { } content) // :175 popover.contentViewController?.view.window
            content.AddHandler(InputElement.KeyDownEvent, OnPopoverKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // :189-194
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!isShown || ShownIn is not { } window) return;
        var kind = e.GetCurrentPoint(window).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return; // :182 .leftMouseDown, .rightMouseDown
        if (shownRect.Contains(e.GetPosition(window))) return; // :191 a click inside the badge's recorded rect passes through
        Close(); // :192 anything else in that window closes the popover and still passes the event on
    }

    // :169-179. Escape belongs to the badge's chat window, not another window.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!isShown || e.Key != Key.Escape) return; // :174 key code 53
        Close();
        e.Handled = true; // :178 return nil: the key is swallowed
    }

    // :169-179. Escape in the popover's own window closes and is consumed.
    private void OnPopoverKeyDown(object? sender, KeyEventArgs e)
    {
        if (!isShown || e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    // :196-199
    private void StopMonitors()
    {
        if (ShownIn is { } window)
        {
            window.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
            window.RemoveHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed);
        }

        if (popover.Child is { } content)
            content.RemoveHandler(InputElement.KeyDownEvent, OnPopoverKeyDown);
    }
}

/// <summary>Badge content and the optional attention overlay (:49-65).</summary>
internal sealed class BadgeLayers : Panel
{
    // :49-65
    public BadgeLayers(Control pill)
    {
        Children.Add(pill); // :49-50 the pill
        ClipToBounds = true; // :65 .clipShape(...)
        CornerRadius = new CornerRadius(TimelineMetrics.PillRadius); // :65 RoundedRectangle(cornerRadius:, style: .continuous)
    }

    // :65
    public CornerRadius CornerRadius { get; set; }

    // :54-65
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    // :54-65
    protected override Size ArrangeOverride(Size finalSize)
    {
        var pillSize = Children.Count > 0 ? Children[0].DesiredSize : default;
        Clip = new RectangleGeometry(new Rect(pillSize), CornerRadius.TopLeft, CornerRadius.TopLeft); // :65 clipped to the pill shape
        if (Children.Count > 0)
            Children[0].Arrange(new Rect(pillSize)); // :49-50 the pill keeps its intrinsic size at the leading edge
        for (var i = 1; i < Children.Count; i++)
            Children[i].Arrange(new Rect(pillSize)); // :54-64 the overlay is measured against the badge
        return finalSize;
    }
}

/// <summary>The flipped attention dot field over the badge's top quarter (:54-65).</summary>
internal sealed class BadgeAttentionQuarter : Decorator
{
    // :56-60
    public BadgeAttentionQuarter(Control content)
    {
        Child = content;
        IsHitTestVisible = false; // :61 .allowsHitTesting(false)
    }

    // :56-60
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        return Child?.DesiredSize ?? default;
    }

    // :56-60 proxy.size.height * 0.25, aligned to the top
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height * 0.25)); // :59 .frame(height: proxy.size.height * 0.25)
        return finalSize;
    }
}
