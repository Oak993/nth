using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourView.swift (B2 1fbf81e), :1-277.
//
// The welcome tour is a port of TourKit by Ram Patra (https://github.com/rampatra/TourKit, MIT License): the
// slideshow card, its page indicator and its controls, carried over to the app's glass and its asset catalog.
// See THIRD_PARTY_NOTICES.md. :4-6
//
// This file ports TourView.swift only. TourWindowController.swift (window chrome, key/main capability, drag by
// background, the sizing twin that measures the tallest page) is a separate unit (App.Views.Tour.TourWindowController)
// and is out of scope here; this control assumes its width/height are supplied by that caller as the Swift init does.

/// <summary>TourPage (:9-21). One page of the tour: its artwork from the asset catalog, a title and a line under it.</summary>
// R-VAL-1: struct -> immutable sealed record. Id is not an init parameter: Swift mints a fresh UUID in init (:16), never
// from a caller-supplied value, so a plain constructor (not a `with`-friendly required property) matches that exactly.
// PR-UPDATE-105: two pages with identical text are still distinct, because the record's default equality compares Id too.
public sealed record TourPage
{
    public Guid Id { get; } = Guid.NewGuid(); // :10 let id: UUID, :16 self.id = UUID()
    public string ImageName { get; } // :11
    public string Title { get; } // :12
    public string Description { get; } // :13

    public TourPage(string imageName, string title, string description) // :15-20
    {
        ImageName = imageName;
        Title = title;
        Description = description;
    }
}

/// <summary>
/// TourView (:23-277). The tour's card: an artwork band that crossfades between pages over a page indicator and two
/// icon controls, and a text panel with a title, a description and the primary action button, all on one glass card.
/// </summary>
public sealed class TourView : Panel
{
    public const double ImageAspectRatio = 16.0 / 10.0; // :113 static let imageAspectRatio: CGFloat = 16.0 / 10.0
    public const double EmptyStateHeight = 220; // :114 static let emptyStateHeight: CGFloat = 220

    private const double CardCornerRadius = 20; // :63 RoundedRectangle(cornerRadius: 20, style: .continuous)

    private readonly IReadOnlyList<TourPage> pages; // :24 let pages: [TourPage]
    private readonly double width; // :25 let width: CGFloat
    private readonly double? height; // :29 let height: CGFloat?
    private readonly string continueButtonTitle; // :30
    private readonly string finishButtonTitle; // :31
    private readonly Action? onFinish; // :32
    private readonly Action? onClose; // :33

    // :36 @State var currentIndex: Int. R-VIEW-2 ordinarily routes a body-read @State through a ViewState :
    // MainActorModel tracked by the view's own controls; here nothing outside this control ever reads or binds
    // currentIndex (there is no @Binding and no external observer), so each write below drives its dependants
    // directly instead of through a generic Tracked subscription. The observable result is identical.
    private int currentIndex;

    private readonly Panel artworkHost = new(); // hosts the crossfading artwork(for:) instances (:118-122)
    private readonly Panel bottomHost = new(); // hosts the crossfading bottomPanel instances (:179-180)
    private Control? currentArtwork;
    private Control? currentBottom;
    private TourPageIndicator? indicator;
    private Control? backButton;
    private AnimatedOpacity? backOpacity;

    public TourView(
        IReadOnlyList<TourPage> pages,
        double width = 660, // :40
        double? height = null, // :41
        int initialPageIndex = 0, // :42
        string continueButtonTitle = "Continue", // :43
        string finishButtonTitle = "Done", // :44
        Action? onFinish = null, // :45
        Action? onClose = null) // :46
    {
        this.pages = pages;
        this.width = width;
        this.height = height;
        this.continueButtonTitle = continueButtonTitle;
        this.finishButtonTitle = finishButtonTitle;
        this.onFinish = onFinish;
        this.onClose = onClose;
        currentIndex = Clamped(initialPageIndex, pages.Count); // :55 PR-UPDATE-104

        Width = width; // :76 .frame(width: width, height: height) on the whole Group, always constrains width
        if (height is { } fixedHeight) Height = fixedHeight; // height nil sizes to content (previews, empty state)

        Control content;
        if (pages.Count == 0)
        {
            content = BuildEmptyState(); // :66-67 PR-UPDATE-103
        }
        else
        {
            SetPage(currentIndex, animate: false); // fills artworkHost/bottomHost with the initial page, no crossfade
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, Width = width }; // :69 VStack(spacing: 0)
            if (height is { } stackHeight) stack.Height = stackHeight; // :73 .frame(width: width, height: height)
            stack.Children.Add(BuildImageSection());
            stack.Children.Add(bottomHost);
            content = stack;
        }

        // :79-95. The floating-panel recipe: one shared rounded shape, one glass/tint surface, one clip, one hairline
        // border, and a scrim sitting under the glass, in this back-to-front order (:80-95 modifier order, applied
        // inside-out: background(glass) is behind content, clipShape crops that pair, overlay draws on top of it,
        // and the outer background(scrim) sits behind everything drawn so far).
        var glassFill = new Border { CornerRadius = new CornerRadius(CardCornerRadius) }; // :80-81 shape.fill(.clear)
        SwiftGlass.SetEffect(glassFill, GlassEffect.Regular(GlassShape.RoundedRectangle(CardCornerRadius, RoundedCornerStyle.Continuous))); // :82 .glassEffect(.regular, in: shape)
        var tintPaint = new AnimatedColor(glassFill);
        glassFill.Background = tintPaint.Brush;
        void DrawTint() => tintPaint.Set(Chrome.GlassTint.Opacity(PlainControls.IsDark(glassFill.ActualThemeVariant) ? 0.22 : 0.16), null); // :84
        glassFill.ActualThemeVariantChanged += (_, _) => DrawTint();
        DrawTint();

        var clipped = new Border // :87 .clipShape(shape)
        {
            CornerRadius = new CornerRadius(CardCornerRadius),
            ClipToBounds = true,
            Child = new Panel { Children = { glassFill, content } },
        };

        var stroke = new Border // :88-90 .overlay { shape.strokeBorder(Chrome.overlay(0.14), lineWidth: 1) }
        {
            CornerRadius = new CornerRadius(CardCornerRadius),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        stroke.BorderBrush = Chrome.Overlay(0.14).Brush(stroke); // :89

        var scrim = new Border { CornerRadius = new CornerRadius(CardCornerRadius) }; // :92-95
        var scrimPaint = new AnimatedColor(scrim);
        scrim.Background = scrimPaint.Brush;
        void DrawScrim() => scrimPaint.Set(
            PlainControls.IsDark(scrim.ActualThemeVariant) ? SwiftColor.Black.Opacity(0.3) : SwiftColor.White.Opacity(0.34), null); // :94
        scrim.ActualThemeVariantChanged += (_, _) => DrawScrim();
        DrawScrim();

        Children.Add(scrim); // back
        Children.Add(clipped); // middle
        Children.Add(stroke); // front

        this.PresentedChrome(); // :97
    }

    /// <summary>:229-232 clamped(_:pageCount:). PR-UPDATE-104: only the initial index is guarded this way.</summary>
    public static int Clamped(int index, int pageCount)
    {
        if (pageCount <= 0) return 0;
        return Math.Max(0, Math.Min(index, pageCount - 1));
    }

    /// <summary>:225-227 isLastPage.</summary>
    public bool IsLastPage => pages.Count > 0 && currentIndex == pages.Count - 1;

    /// <summary>The current page index, for tests (PR-UPDATE-104, PR-UPDATE-099).</summary>
    public int CurrentIndex => currentIndex;

    /// <summary>Test seam: the panel the crossfading artwork instances (:118-122) are inserted into and removed from.</summary>
    public Panel ArtworkHost => artworkHost;

    /// <summary>Test seam: the panel the crossfading bottom-panel instances (:179-180) are inserted into and removed from.</summary>
    public Panel BottomHost => bottomHost;

    private static double ImageHeight(double width) => SwiftNumber.Rounded(width / ImageAspectRatio); // :58-60

    private Control BuildEmptyState() // :100-111 PR-UPDATE-103
    {
        var glyph = SwiftImage.SystemName("rectangle.stack"); // :102
        glyph.Font(SwiftFont.Title); // :103
        glyph.ForegroundStyle(Chrome.SecondaryText); // :104
        var text = new TextBlock { Text = "No tour pages" }; // :105 Text("No tour pages")
        // ED-059: .headline resolves semibold, not SwiftFont's own Headline default (Bold, SwiftFont.cs:244); the
        // explicit weight below keeps this site correct without touching that foundation file.
        SwiftFont.Headline.FontWeight(SwiftFont.Weight.Semibold).Apply(text); // :106
        text.Foreground = Chrome.PrimaryText.Brush(text); // :107
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12, // :101 VStack(spacing: 12)
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(glyph);
        stack.Children.Add(text);
        return new Border
        {
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch, // :109 .frame(maxWidth: .infinity)
            Height = EmptyStateHeight, // :110 .frame(height: Self.emptyStateHeight)
        };
    }

    private Control BuildImageSection() // :116-147
    {
        var grid = new Panel { Width = width, Height = ImageHeight(width) }; // :117 ZStack(alignment: .top), :146

        artworkHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        artworkHost.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(artworkHost); // :118-122

        // :124-137. A short seat for the page indicator only: the picture is the point of the page, so the scrim
        // never reaches into it.
        var seat = new Border // LinearGradient stops :127-131
        {
            Height = 96, // :135
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom, // :136 alignment: .bottom
            IsHitTestVisible = false, // :137 .allowsHitTesting(false)
        };
        seat.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), // :132 startPoint: .top
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), // :133 endPoint: .bottom
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0), // :128 .clear at 0
                new GradientStop(SeatColor(0.28), 0.55), // :129 white(0.08).opacity(0.28) at 0.55
                new GradientStop(SeatColor(0.55), 1.0), // :130 white(0.08).opacity(0.55) at 1.0
            },
        };
        grid.Children.Add(seat);

        indicator = new TourPageIndicator(pages.Count, currentIndex); // :139 TourPageIndicator(...)
        indicator.Margin = new Thickness(0, 0, 0, 14); // :140 .padding(.bottom, 14)
        indicator.HorizontalAlignment = HorizontalAlignment.Center;
        indicator.VerticalAlignment = VerticalAlignment.Bottom; // :141 alignment: .bottom
        indicator.IsHitTestVisible = false; // :142 .allowsHitTesting(false)
        grid.Children.Add(indicator);

        grid.Children.Add(BuildTopControls()); // :144
        return grid;
    }

    private static Color SeatColor(double opacity) =>
        Color.FromArgb((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero), 0x14, 0x14, 0x14); // white(0.08) -> rgb 0x14

    private Control BuildTopControls() // :183-202
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; // HStack { back; Spacer(); close }
        row.Margin = new Thickness(14, 12, 14, 0); // :200-201 .padding(.horizontal, 14).padding(.top, 12)
        row.VerticalAlignment = VerticalAlignment.Top;
        row.HorizontalAlignment = HorizontalAlignment.Stretch;

        backButton = BuildIconButton("chevron.left", GoBack); // :185-187
        backOpacity = new AnimatedOpacity(backButton, currentIndex > 0 ? 1 : 0); // :188 .opacity(currentIndex > 0 ? 1 : 0)
        Grid.SetColumn(backButton, 0);
        row.Children.Add(backButton);

        var close = BuildIconButton("checkmark", () => // :192-198
        {
            if (onClose is { } handler) handler();
            else Dismiss(); // :196 else { dismiss() }
        });
        Grid.SetColumn(close, 2);
        row.Children.Add(close);

        return row;
    }

    // :214-223 iconButton(systemName:action:). A 32x32 hit area behind a body/semibold glyph, chromeGlassCircle().
    private static Control BuildIconButton(string systemName, Action action)
    {
        var image = SwiftImage.SystemName(systemName); // :216
        image.Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold)); // :217
        var frame = new Border { Width = 32, Height = 32, Child = image }; // :218
        var button = PlainControls.Button(frame, action); // :215, :221 .buttonStyle(.plain)
        return button.ChromeGlassCircle(); // :222
    }

    // :204-212 primaryActionButton. UI-UPDATE-044: the tint is the app's accent (its "colors" field), and this record
    // carries OD-0005's deviation for the full 22-site glass material. ENGINEERING DECISION: SwiftUI's system
    // `.glassProminent` button style draws its own outer shape from the control's own geometry; this file's PA-0001
    // audit lists no explicit `in: shape` for it (unlike the 19 `.glassEffect(...)` call sites it does enumerate), so
    // there is no source-cited corner radius here. It is drawn as a capsule at the button's fixed 220x42 frame, the
    // same capsule family every other glass control in this codebase draws (Chrome.swift :164-165,
    // ChromeGlassCircle/ChromeGlassSurface); this is a shape assumption, not a source fact, and stays UNVERIFIED
    // under OD-0001 (no macOS reference to compare against).
    private Control BuildPrimaryActionButton()
    {
        var text = new TextBlock
        {
            Text = IsLastPage ? finishButtonTitle : continueButtonTitle, // :206 Text(verbatim:)
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.Title3.FontWeight(SwiftFont.Weight.Semibold).Apply(text); // :207
        var frame = new Border { Width = 220, Height = 42, Child = text }; // :208
        frame.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(frame, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive()); // :210 .buttonStyle(.glassProminent)
        frame.Background = Chrome.Accent.Opacity(0.3).Brush(frame);
        var button = PlainControls.Button(frame, Advance); // :205 Button(action: advance)
        button.IsDefault = true; // :211 .keyboardShortcut(.defaultAction)
        return button;
    }

    // :234-246 advance(). PR-UPDATE-099.
    public void Advance()
    {
        if (IsLastPage)
        {
            if (onFinish is { } finish) finish();
            else if (onClose is { } close) close();
            else Dismiss();
        }
        else
        {
            SetIndex(currentIndex + 1);
        }
    }

    // :248-252 goBack(). PR-UPDATE-100.
    public void GoBack()
    {
        var newIndex = Math.Max(0, currentIndex - 1);
        if (newIndex == currentIndex) return;
        SetIndex(newIndex);
    }

    // ENGINEERING DECISION: `@Environment(\.dismiss)` (:34) has no ambient presentation here in shipped code, because
    // TourWindowController.present always supplies both onFinish and onClose (TourPages.swift Tour.present), so this
    // path is unreachable at the pinned commit (as PR-UPDATE-099's own evidence notes). It reuses the popover-close
    // ambient (R-VIEW-9) as the nearest equivalent "ambient way to close the presented thing" this codebase already has.
    private void Dismiss() => Popovers.GetClosePopover(this)?.Invoke();

    // Applies a page change under the same 0.25s ease-in-out the whole subtree animates under at the source's root
    // (:96 .animation(.easeInOut(duration: 0.25), value: currentIndex)): the crossfade, the indicator's capsule width
    // and colour, and the back button's opacity all move together.
    private void SetIndex(int newIndex)
    {
        currentIndex = newIndex;
        Transaction.WithAnimation(Animation.EaseInOut(duration: 0.25), () =>
        {
            SetPage(newIndex, animate: true);
            indicator?.Update(pages.Count, newIndex); // :139-142 PR-UPDATE-106, UI-UPDATE-039
            backOpacity?.Set(newIndex > 0 ? 1 : 0, Transaction.Current); // :188 PR-UPDATE-100
        });
    }

    // Builds the framed artwork (:118-122) and the bottom panel (:179-180) for `index` and swaps them into their
    // hosts, each keyed by index and crossfaded via .transition(.opacity) (SwiftTransition.Insert/Remove).
    private void SetPage(int index, bool animate)
    {
        var page = pages[index];

        var artwork = BuildArtworkFramed(page).Transition(SwiftTransition.Opacity); // :120-122 .id(currentIndex).transition(.opacity)
        var bottom = BuildBottomPanel(page).Transition(SwiftTransition.Opacity); // :179-180 .id(currentIndex).transition(.opacity)

        void Swap()
        {
            if (currentArtwork is { } oldArt) SwiftTransition.Remove(artworkHost, oldArt);
            SwiftTransition.Insert(artworkHost, artworkHost.Children.Count, artwork);
            currentArtwork = artwork;

            if (currentBottom is { } oldBottom) SwiftTransition.Remove(bottomHost, oldBottom);
            SwiftTransition.Insert(bottomHost, bottomHost.Children.Count, bottom);
            currentBottom = bottom;
        }

        if (animate) Swap();
        else Transaction.WithAnimation(null, Swap); // first fill: no prior sibling to cross from
    }

    private Control BuildArtworkFramed(TourPage page) // :118-120 .frame(width:height:).clipped()
    {
        var art = BuildArtwork(page);
        return new Border
        {
            Width = width,
            Height = ImageHeight(width),
            ClipToBounds = true, // :120 .clipped()
            Child = art,
        };
    }

    // :254-276 artwork(for:). PR-UPDATE-102, UI-UPDATE-037, UI-UPDATE-054.
    private static Control BuildArtwork(TourPage page)
    {
        var image = SwiftImage.Named(page.ImageName); // :256 NSImage(named: page.imageName)
        if (!image.IsEmpty)
        {
            image.Resizable(); // :257
            image.AspectRatio(ContentMode.Fill); // :258 .scaledToFill()
            return image;
        }

        // :261-275. The asset-not-found safety net: a vertical purple-to-black gradient with a centred photo glyph.
        var gradient = new Border();
        gradient.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), // :266 startPoint: .top
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), // :267 endPoint: .bottom
            GradientStops =
            {
                new GradientStop(RgbColor(0.16, 0.12, 0.40), 0), // :263
                new GradientStop(RgbColor(0.05, 0.05, 0.10), 1), // :264
            },
        };
        var glyph = SwiftImage.SystemName("photo"); // :270
        glyph.Font(SwiftFont.System(40)); // :271
        glyph.ForegroundStyle(SwiftColor.White.Opacity(0.25)); // :272
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        gradient.Child = glyph;
        return gradient;
    }

    private static Color RgbColor(double r, double g, double b) => Color.FromRgb(Byte(r), Byte(g), Byte(b));

    private static byte Byte(double component) => (byte)Math.Round(Math.Clamp(component, 0, 1) * 255, MidpointRounding.AwayFromZero);

    // :149-181 bottomPanel. UI-UPDATE-042, UI-UPDATE-043, UI-UPDATE-044.
    private Control BuildBottomPanel(TourPage page)
    {
        var title = new TextBlock // :156-160
        {
            Text = page.Title, // Text(verbatim: currentPage.title)
            TextAlignment = Avalonia.Media.TextAlignment.Center, // :158 .multilineTextAlignment(.center)
            TextWrapping = TextWrapping.Wrap, // :160 .fixedSize(horizontal: false, vertical: true)
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        SwiftFont.System(28, SwiftFont.Weight.Bold).Apply(title); // :157
        title.Foreground = Chrome.PrimaryText.Brush(title); // :159

        var description = new TextBlock // :162-167
        {
            Text = page.Description, // Text(verbatim: currentPage.description)
            TextAlignment = Avalonia.Media.TextAlignment.Center, // :164
            TextWrapping = TextWrapping.Wrap, // :166
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0), // :167 .padding(.top, 6)
        };
        SwiftFont.Body.Apply(description); // :163
        description.Foreground = Chrome.SecondaryText.Brush(description); // :165

        var textStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        textStack.Children.Add(title);
        textStack.Children.Add(description);
        // ENGINEERING DECISION: Spacer(minLength: 12) above and below (:154, :169) inside a maxHeight-infinity
        // container is a flexible push with a 12pt floor; a fixed top/bottom margin with vertical centring
        // reproduces the same floor and the same centred result without a flexible-space primitive.
        var textArea = new Border
        {
            Child = textStack,
            Padding = new Thickness(0, 12, 0, 12),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var button = BuildPrimaryActionButton(); // :173
        button.HorizontalAlignment = HorizontalAlignment.Center;

        var panel = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; // VStack(spacing: 0) { textArea (fills); button }
        Grid.SetRow(textArea, 0);
        Grid.SetRow(button, 1);
        panel.Children.Add(textArea);
        panel.Children.Add(button);
        panel.Margin = new Thickness(32, 12, 32, 24); // :175-177 .padding(.horizontal, 32).padding(.top, 12).padding(.bottom, 24)
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.VerticalAlignment = VerticalAlignment.Stretch;
        return panel;
    }
}

/// <summary>
/// TourPageIndicator (:279-299). An HStack of capsules, 7 apart: the current page's is 24x8 and bright, every other is
/// 8x8 and dim, and all of it collapses into one accessibility element labelled "Page N of M".
/// </summary>
public sealed class TourPageIndicator : Control
{
    private static readonly SwiftColor CurrentFill = SwiftColor.White.Opacity(0.95); // :292
    private static readonly SwiftColor OtherFill = SwiftColor.White.Opacity(0.32); // :292

    private readonly StackPanel stack;
    private readonly List<CapsuleDot> dots = [];
    private int totalPages;
    private int currentIndex;

    // :283-286 init(totalPages:currentIndex:)
    public TourPageIndicator(int totalPages, int currentIndex)
    {
        this.totalPages = totalPages;
        this.currentIndex = currentIndex;

        stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; // :289 HStack(spacing: 7)
        for (var index = 0; index < totalPages; index++)
        {
            var dot = new CapsuleDot(index == currentIndex); // :291-293
            dots.Add(dot);
            stack.Children.Add(dot);
        }

        // :296-297 .accessibilityElement(children: .ignore).accessibilityLabel("Page \(currentIndex + 1) of \(max(totalPages, 1))")
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Control);
        VisualChild = stack;
        UpdateLabel();
    }

    // Avalonia.Controls.Control has no single-child slot; host the stack as the control's one visual child.
    private Control VisualChild
    {
        set
        {
            LogicalChildren.Clear();
            VisualChildren.Clear();
            LogicalChildren.Add(value);
            VisualChildren.Add(value);
            ((ISetLogicalParent)value).SetParent(this);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        stack.Measure(availableSize);
        return stack.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        stack.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>Re-syncs every dot's current/other state and the accessibility label after a page change.</summary>
    public void Update(int newTotalPages, int newCurrentIndex)
    {
        totalPages = newTotalPages;
        currentIndex = newCurrentIndex;
        for (var index = 0; index < dots.Count; index++) dots[index].SetCurrent(index == currentIndex);
        UpdateLabel();
    }

    private void UpdateLabel() =>
        AutomationProperties.SetName(this, $"Page {currentIndex + 1} of {Math.Max(totalPages, 1)}"); // :297

    /// <summary>One capsule (:291-293), animating its width and fill under the enclosing transaction.</summary>
    private sealed class CapsuleDot : Border
    {
        private readonly AnimatedColor fill;
        private readonly AnimatedDouble widthSpan;

        public CapsuleDot(bool isCurrent)
        {
            Height = 8; // :293 height: 8
            fill = new AnimatedColor(this);
            Background = fill.Brush;
            widthSpan = new AnimatedDouble(w => Width = w);
            this.Capsule(RoundedCornerStyle.Continuous); // :291 Capsule(style: .continuous)
            SetCurrent(isCurrent, animate: false);
        }

        public void SetCurrent(bool isCurrent, bool animate = true)
        {
            var animation = animate ? Transaction.Current : null;
            fill.Set(isCurrent ? CurrentFill : OtherFill, animation); // :292
            widthSpan.Set(isCurrent ? 24 : 8, animation); // :293 width: 24 : 8
        }
    }
}

/// <summary>
/// PORT-ONLY, local to this file: a single double value that, set under a transaction's animation, moves to the new
/// value along that animation's curve on the frame clock (the same mechanic <see cref="AnimatedColor"/> gives a
/// colour). Used for the page indicator's capsule width and the back button's opacity, both bare numeric properties
/// with no colour-typed counterpart in SwiftColor to reuse.
/// </summary>
internal sealed class AnimatedDouble
{
    private readonly Action<double> apply;
    private double current;
    private bool primed;
    private IFrameSource? source;
    private Action<TimeSpan>? pending;

    public AnimatedDouble(Action<double> apply) => this.apply = apply;

    public void Set(double value, Animation? animation)
    {
        Stop();
        if (!primed || animation is null || current == value)
        {
            primed = true;
            current = value;
            apply(value);
            return;
        }
        var from = current;
        var start = SwiftClock.Now;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var now = SwiftClock.Now;
            var complete = IsComplete(start, now, span); // ED-117 (2), mirrored locally: FrameClock.IsComplete is internal to Marrow.UI
            var v = complete ? value : from + (value - from) * animation.Progress((now - start).TotalSeconds);
            current = v;
            apply(v);
            if (complete) pending = null;
            else frames.RequestFrame(tick!);
        };
        source = frames;
        pending = tick;
        frames.RequestFrame(tick);
    }

    private void Stop()
    {
        if (pending is { } frame) source?.CancelFrame(frame);
        pending = null;
    }

    // ED-117 (2): a span S from instant s is complete at a frame when (SwiftClock.Now - s) + 1 ms >= S, in ticks.
    private static bool IsComplete(ContinuousInstant since, ContinuousInstant now, TimeSpan span) =>
        (now - since).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks;
}

/// <summary>PORT-ONLY, local to this file: the same mechanic as <see cref="AnimatedDouble"/>, driving a control's Opacity.</summary>
internal sealed class AnimatedOpacity
{
    private readonly AnimatedDouble span;

    public AnimatedOpacity(Control owner, double initial)
    {
        span = new AnimatedDouble(v => owner.Opacity = v);
        span.Set(initial, null);
    }

    public void Set(double value, Animation? animation) => span.Set(value, animation);
}
