using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageFloatingPanel.swift (B2 1fbf81e), :1-112.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; CGSize is Avalonia Size;
// `@Environment(WindowLiveResize.self)` is read from the environment, not passed in.

/// <summary>:9-112 <c>struct UsageFloatingPanel: View</c>: the chat's usage as a floating glass panel.</summary>
public sealed class UsageFloatingPanel : Decorator
{
    // :12-22 let runtime, provider, headsProvider, size, onContentHeight, onDrag, onDragEnd, dismiss; :24-74 body
    public UsageFloatingPanel(
        ThreadRuntime runtime,
        ProviderKind provider,
        ProviderKind? headsProvider,
        Size size,
        Action<double> onContentHeight,
        Action<Size> onDrag,
        Action onDragEnd,
        Action dismiss)
    {
        // :10 @Environment(WindowLiveResize.self) private var liveResize. Read once the control is parented, the
        // only point an inherited attached property reflects its ancestor's value; this class never re-applies
        // Width/Height after construction (R-VIEW-1, matching HydraPanel's own `size` handling), so nothing here
        // is ever wrapped in an ambient Transaction that liveResize.IsActive would need to suppress (:52
        // `.animation(nil, value: liveResize.isActive)` has no observable effect beyond that suppression). The read
        // is kept so the dependency is visible and so a future caller that re-applies size through Track picks up
        // the same environment value SwiftUI would have. ENGINEERING DECISION, not a deviation: the port's frame
        // is never animated in the first place (R-VIEW-1/PlacedPanel precedent), so "not animated" already holds.
        AttachedToVisualTree += (_, _) => _ = AppEnvironment.GetWindowLiveResize(this);

        // :25 let shape = RoundedRectangle(cornerRadius: HydraPanel.cornerRadius, style: .continuous)
        var cornerRadius = new CornerRadius(HydraPanel.CornerRadius);

        // :26-46 body reads runtime.usage, so it runs again whenever the usage changes: the placeholder gives way to the
        // usage panel when the first usage arrives (and back), and the pane is told the height of whichever shows.
        this.provider = provider;
        this.headsProvider = headsProvider;
        this.onContentHeight = onContentHeight;
        shownUsage = runtime.Usage;
        ShowContent(shownUsage);
        this.Track(() => runtime.Usage, next =>
        {
            if (Equals(next, shownUsage)) return; // the first read at attach is the usage already shown
            shownUsage = next;
            ShowContent(next);
        });

        // :47-49 .overlay(alignment: .top) { strip }
        var contentWithStrip = new Grid();
        contentWithStrip.Children.Add(contentSlot);
        var strip = BuildStrip(provider, headsProvider, onDrag, onDragEnd, dismiss);
        strip.VerticalAlignment = VerticalAlignment.Top;
        contentWithStrip.Children.Add(strip);

        // :53-63 .background { glass regular + Chrome.glassTint overlay }, sampling the app's own backdrop.
        var glassLayer = new Border();
        SwiftGlass.SetEffect(glassLayer, GlassEffect.Regular(GlassShape.RoundedRectangle(HydraPanel.CornerRadius, RoundedCornerStyle.Continuous))); // :25,59

        var panelContents = new Grid();
        panelContents.Children.Add(glassLayer);
        panelContents.Children.Add(contentWithStrip);

        // :66 .clipShape(shape)
        var panel = new Border
        {
            CornerRadius = cornerRadius,
            ClipToBounds = true,
            Child = panelContents,
        };

        // :68-73 the scrim, carrying the panel's single shadow.
        var scrim = new Border
        {
            CornerRadius = cornerRadius,
        };

        var floatingPanel = new Grid();
        floatingPanel.Children.Add(scrim);
        floatingPanel.Children.Add(panel);

        // :56 `let isDark = colorScheme == .dark` inside `body`, and :69's own re-read of the same environment
        // value for the scrim/shadow layer: SwiftUI recomputes `body` (and both `isDark` locals) on every
        // colorScheme change while the panel is on screen, so the glass tint and the scrim/shadow must be
        // re-derived on every theme change too, not fixed at construction (matching the established
        // ActualThemeVariantChanged -> redraw convention for this identical dependency, e.g.
        // Chrome.cs's WindowBackdrop, Views/Chat/SubagentPanel.cs and Views/Tour/TourView.cs).
        void UpdateTheme()
        {
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            glassLayer.Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(this);
            scrim.Background = new SolidColorBrush(ScrimColor(isDark));
            scrim.BoxShadow = new BoxShadows(ScrimShadow(isDark));
        }

        UpdateTheme();
        ActualThemeVariantChanged += (_, _) => UpdateTheme();

        // :50 .frame(width: size.width, height: size.height)
        Child = floatingPanel;
        ClipToBounds = false;
        Width = size.Width;
        Height = size.Height;
    }

    private readonly ProviderKind provider;
    private readonly ProviderKind? headsProvider;
    private readonly Action<double> onContentHeight;
    private readonly Grid contentSlot = new(); // :30 the Group the body fills
    private ContextUsage? shownUsage;
    private Border? scrollPadded; // the padded content of the scroll view while it shows (:36-37)
    private double? lastHeight; // :34 onGeometryChange fires only when the derived height changes

    // :26-29 let hasSomething = usage?.fraction != nil || the lead's or the heads' provider exposes limits or credits
    private bool HasSomething(ContextUsage? usage) => usage?.Fraction != null
        || PlanLimitsReader.ExposesLimits(provider) || CreditsReader.ExposesCredits(provider)
        || (headsProvider is { } heads && (PlanLimitsReader.ExposesLimits(heads) || CreditsReader.ExposesCredits(heads)));

    // :30-46 Group { if hasSomething { ScrollView { UsagePanel } } else { placeholder } }. The scroll view stays while only the
    // figures change, as SwiftUI keeps the ScrollView's identity (and its offset) across a body run; the branch swap replaces it.
    private void ShowContent(ContextUsage? usage)
    {
        if (HasSomething(usage))
        {
            if (scrollPadded is not null) { scrollPadded.Child = BuildUsagePanel(usage); return; }
            contentSlot.Children.Clear();
            contentSlot.Children.Add(BuildScrollingContent(usage));
            return;
        }
        scrollPadded = null;
        lastHeight = null;
        contentSlot.Children.Clear();
        contentSlot.Children.Add(BuildPlaceholder(provider, onContentHeight));
    }

    private UsagePanel BuildUsagePanel(ContextUsage? usage)
    {
        var usagePanel = new UsagePanel(usage, provider, headsProvider, fillsWidth: true);

        // :34 .onGeometryChange(for: CGFloat.self, of: { $0.size.height }) { onContentHeight($0 + stripHeight + 8) }
        // onGeometryChange fires only when the derived value changes (SOURCE FACT: SwiftUI's documented contract
        // for onGeometryChange), so this dedupes the same way LayoutUpdated, which fires on every layout pass
        // regardless of whether the measured height moved, does not on its own.
        usagePanel.LayoutUpdated += (_, _) =>
        {
            var measured = usagePanel.Bounds.Height;
            if (lastHeight is { } previous && Math.Abs(measured - previous) < 0.001) return;
            lastHeight = measured;
            onContentHeight(measured + HydraPanel.StripHeight + 8);
        };
        return usagePanel;
    }

    // :31-37 ScrollView(.vertical) { UsagePanel(...).onGeometryChange(...).padding(.top, stripHeight).padding(.bottom, 8) }
    private Control BuildScrollingContent(ContextUsage? usage)
    {
        var padded = new Border
        {
            Child = BuildUsagePanel(usage),
            Padding = new Thickness(0, HydraPanel.StripHeight, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // A ScrollView's content keeps its own height; the viewport stretching it would make the height reported
            // above the panel's own and pin the panel at its full size (seen in the native gallery).
            VerticalAlignment = VerticalAlignment.Top,
        };

        scrollPadded = padded;
        return new ScrollViewer
        {
            Content = padded,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    // :38-45 Text(verbatim: "Nothing to show for \(provider.displayName) yet.")...onAppear { onContentHeight(stripHeight + 44) }
    private Control BuildPlaceholder(ProviderKind provider, Action<double> onContentHeight)
    {
        var text = new TextBlock
        {
            Text = $"Nothing to show for {provider.DisplayName} yet.",
            Foreground = Chrome.SecondaryText.Brush(this),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.Body.Apply(text);

        var placeholder = new Border
        {
            Child = text,
            Padding = new Thickness(0, HydraPanel.StripHeight, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // :44 .onAppear { onContentHeight(HydraPanel.stripHeight + 44) }
        placeholder.AttachedToVisualTree += (_, _) => onContentHeight(HydraPanel.StripHeight + 44);
        return placeholder;
    }

    // :78-111 The handle across the top: the title at the left, the close button at the right, and the room
    // between them drags the panel.
    // :70-71 (isDark ? Color.black : Color.white).opacity(isDark ? 0.3 : 0.34). Pulled out to a pure function so
    // it (and ScrimShadow below) can be verified directly, without depending on ActualThemeVariant's resolution
    // timing, which only reflects an ancestor's theme once this control is actually attached to one.
    internal static Color ScrimColor(bool isDark) => Color.FromArgb(
        isDark ? (byte)77 : (byte)87,
        isDark ? (byte)0 : (byte)255,
        isDark ? (byte)0 : (byte)255,
        isDark ? (byte)0 : (byte)255);

    // :71 .shadow(color: .black.opacity(isDark ? 1 : 0.65), radius: 28, y: 10)
    internal static BoxShadow ScrimShadow(bool isDark) => new()
    {
        Color = Color.FromArgb(isDark ? (byte)255 : (byte)166, 0, 0, 0),
        Blur = 28,
        OffsetY = 10,
    };

    private Control BuildStrip(ProviderKind provider, ProviderKind? headsProvider, Action<Size> onDrag, Action onDragEnd, Action dismiss)
    {
        // :80-88 HStack(spacing: 6) { icon(s), Text("Usage") }
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new ProviderIcon { Provider = provider, Size = 12 });
        if (headsProvider is { } heads)
            titleRow.Children.Add(new ProviderIcon { Provider = heads, Size = 12 });
        var titleText = new TextBlock
        {
            Text = "Usage",
            Foreground = Chrome.PrimaryText.Opacity(0.92).Brush(this),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(titleText);
        titleRow.Children.Add(titleText);

        // :89-93 .padding(.horizontal, capsuleHorizontalPadding).frame(height: capsuleContentHeight)
        //        .padding(.vertical, capsuleVerticalPadding).fixedSize().chromeGlassCapsule()
        var capsuleContent = new Border
        {
            Child = titleRow,
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = Chrome.CapsuleContentHeight,
        };
        var capsulePadded = new Border
        {
            Child = capsuleContent,
            Padding = new Thickness(0, Chrome.CapsuleVerticalPadding),
        };
        var capsule = capsulePadded.ChromeGlassCapsule();

        // :94-97 // The title is a label, not a control, so it drags the panel too. .overlay { PanelDragHandle(...) }
        var capsuleWithDrag = new Grid();
        capsuleWithDrag.Children.Add(capsule);
        capsuleWithDrag.Children.Add(new PanelDragHandle(onDrag, onDragEnd));

        // :98-99 .padding(.top, chromeTopPadding).padding(.leading, chromeHorizontalPadding)
        var titleHolder = new Border
        {
            Child = capsuleWithDrag,
            Padding = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // :100-104 PanelDragHandle(...).frame(maxWidth: .infinity).frame(height: stripHeight).help("Drag to move")
        var middleDrag = new PanelDragHandle(onDrag, onDragEnd)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = HydraPanel.StripHeight,
        };
        ToolTip.SetTip(middleDrag, "Drag to move");
        AutomationProperties.SetName(middleDrag, "Drag to move");

        // :105-109 ChromeCircleButton(symbol: "xmark", help: "...") { dismiss() }.padding(.top, ...).padding(.trailing, ...)
        var closeButton = new ChromeCircleButton("xmark", "Dismiss the usage panel; the usage popover opens it again", dismiss);
        var closeHolder = new Border
        {
            Child = closeButton,
            Padding = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // :79 HStack(alignment: .top, spacing: 8) { titleHolder, middleDrag, closeHolder }
        var row = new Grid
        {
            Height = HydraPanel.StripHeight,
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        Grid.SetColumn(titleHolder, 0);
        Grid.SetColumn(middleDrag, 1);
        Grid.SetColumn(closeHolder, 2);
        row.Children.Add(titleHolder);
        row.Children.Add(middleDrag);
        row.Children.Add(closeHolder);
        return row;
    }
}
