using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc7926), :1-845.

namespace Marrow.App.Views.Settings;

// :280, :755 ScrollView needs a real presenter even without an application-wide control theme.
internal static class AboutUpdateScroll
{
    // Round 3: :749-762 carries no .scrollIndicators modifier, unlike the sites this presenter/gesture recipe is
    // otherwise shared with (SidebarView :139 and ThreadTimeline :363 .scrollIndicators(.never); MCPSettingsPage
    // :781-787's TextEditor is likewise hidden; this template's own popover use at VerticalScrollBarVisibility.Hidden),
    // so SwiftUI keeps the platform default (visible-while-scrolling) indicator there. Avalonia's own ScrollBar
    // self-binds Value/Maximum/ViewportSize/Visibility from the ScrollViewer that owns a "PART_VerticalScrollBar"
    // named part (Avalonia.Controls.Primitives.ScrollBar.AttachToScrollViewer, confirmed by decompiling
    // Avalonia.Controls 12.1.2 -- no XAML/source ships with the package). The app has no application-wide control
    // theme to give that ScrollBar a stock visual (see MCPSettingsPage.EditorScrollTemplate; OD-0001 also makes any
    // native scroll-indicator appearance UNVERIFIED regardless of which theme supplied it), so its own template
    // here is the smallest real indicator: a track-width bar sized and positioned from the ScrollBar's own bound
    // range. ENGINEERING DECISION: the bar's thickness, colour and 24px minimum thumb length are not sourced from
    // the Swift; they exist only so the indicator that :752's default asks for is not literally invisible.
    private static readonly FuncControlTemplate<ScrollBar> IndicatorTemplate = new((bar, scope) =>
    {
        var thumb = new Border
        {
            Name = "PART_Thumb",
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(140, 128, 128, 128)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        void Update()
        {
            var viewport = bar.ViewportSize;
            var maximum = bar.Maximum;
            var extent = maximum + viewport;
            var trackHeight = bar.Bounds.Height;
            if (double.IsNaN(viewport) || extent <= 0 || trackHeight <= 0) { thumb.IsVisible = false; return; }
            var barHeight = Math.Clamp(trackHeight * (viewport / extent), Math.Min(24, trackHeight), trackHeight);
            var barOffset = maximum > 0 ? (trackHeight - barHeight) * (bar.Value / maximum) : 0;
            thumb.IsVisible = true;
            thumb.Height = barHeight;
            thumb.Margin = new Thickness(0, barOffset, 0, 0);
        }
        bar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty || e.Property == RangeBase.MaximumProperty ||
                e.Property == ScrollBar.ViewportSizeProperty || e.Property == Visual.BoundsProperty)
                Update();
        };
        return thumb.RegisterInNameScope(scope);
    });

    public static readonly FuncControlTemplate<ScrollViewer> Template = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent, // The ScrollView takes wheel input across its viewport, including text gaps.
            [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
        };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });

        var verticalBar = new ScrollBar
        {
            Name = "PART_VerticalScrollBar",
            Orientation = Orientation.Vertical,
            Template = IndicatorTemplate,
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var grid = new Grid();
        grid.Children.Add(presenter.RegisterInNameScope(scope));
        grid.Children.Add(verticalBar.RegisterInNameScope(scope));
        return grid;
    });
}

/// <summary>The three sections a release's notes are written in.</summary>
public enum ReleaseNoteCategory
{
    NewFeatures,
    BugFixes,
    Refinements,
}

public static class ReleaseNoteCategoryExtensions
{
    public static IReadOnlyList<ReleaseNoteCategory> AllCases { get; } =
    [
        ReleaseNoteCategory.NewFeatures,
        ReleaseNoteCategory.BugFixes,
        ReleaseNoteCategory.Refinements,
    ];

    extension(ReleaseNoteCategory category)
    {
        public ReleaseNoteCategory Id => category;

        public string AssetName => category switch
        {
            ReleaseNoteCategory.NewFeatures => "UpdateNewFeaturesIcon",
            ReleaseNoteCategory.BugFixes => "UpdateBugFixesIcon",
            ReleaseNoteCategory.Refinements => "UpdateRefinementsIcon",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        public string CardTitle => category switch
        {
            ReleaseNoteCategory.NewFeatures => "New",
            ReleaseNoteCategory.BugFixes => "Bugs",
            ReleaseNoteCategory.Refinements => "Refinements",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        public string SectionTitle => category switch
        {
            ReleaseNoteCategory.NewFeatures => "New features",
            ReleaseNoteCategory.BugFixes => "Bug fixes",
            ReleaseNoteCategory.Refinements => "Refinements",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        public string CountSummary(long count) => category switch
        {
            ReleaseNoteCategory.NewFeatures => count == 1 ? "1 addition" : $"{count} additions",
            ReleaseNoteCategory.BugFixes => $"{count} fixed",
            ReleaseNoteCategory.Refinements => $"{count} improved",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        public SwiftColor CardTint => category switch
        {
            ReleaseNoteCategory.NewFeatures => new SwiftColor(red: 0.09, green: 0.44, blue: 0.85),
            ReleaseNoteCategory.BugFixes => new SwiftColor(red: 0.90, green: 0.30, blue: 0.24),
            ReleaseNoteCategory.Refinements => new SwiftColor(red: 0.43, green: 0.23, blue: 0.72),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }
}

/// <summary>A release's description parsed into its recognized, non-empty sections.</summary>
public sealed record UpdateReleaseNotesDigest
{
    public sealed record Section
    {
        public required ReleaseNoteCategory Category { get; init; }
        public required ValueList<string> Bullets { get; init; }
        public string? Rollup { get; init; }
        public required long RollupCount { get; init; }

        public ReleaseNoteCategory Id => Category;
        public long ChangeCount => Bullets.Count + RollupCount;
    }

    public required ValueList<Section> Sections { get; init; }

    public bool IsEmpty => Sections.Count == 0;

    public Section? UpdateReleaseNotesDigestSection(ReleaseNoteCategory @for) =>
        Sections.FirstOrDefault(section => section.Category == @for);

    private static string? lastRaw;
    private static UpdateReleaseNotesDigest? lastDigest;

    public static UpdateReleaseNotesDigest Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new UpdateReleaseNotesDigest { Sections = new ValueList<Section>(Array.Empty<Section>()) };
        if (string.Equals(lastRaw, raw, StringComparison.Ordinal) && lastDigest is not null)
            return lastDigest;

        var digest = ParseNotes(raw);
        lastRaw = raw;
        lastDigest = digest;
        return digest;
    }

    private static UpdateReleaseNotesDigest ParseNotes(string raw)
    {
        var collected = ReleaseNotesParser.Parse(raw);

        var sections = new List<Section>();
        foreach (var category in ReleaseNoteCategoryExtensions.AllCases)
        {
            if (!collected.TryGetValue((ReleaseNotesCategory)category, out var parsed) || parsed.Count == 0)
                continue;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = parsed.Where(item => seen.Add(item)).ToList();
            if (unique.Count == 0)
                continue;

            var rollupCount = RollupCountIn(unique[^1]);
            if (rollupCount is > 0)
            {
                sections.Add(new Section
                {
                    Category = category,
                    Bullets = new ValueList<string>(unique.Take(unique.Count - 1)),
                    Rollup = unique[^1],
                    RollupCount = rollupCount.Value,
                });
            }
            else
            {
                sections.Add(new Section
                {
                    Category = category,
                    Bullets = new ValueList<string>(unique),
                    Rollup = null,
                    RollupCount = 0,
                });
            }
        }

        return new UpdateReleaseNotesDigest { Sections = new ValueList<Section>(sections) };
    }

    private static long? RollupCountIn(string bullet)
    {
        var words = bullet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var extraIndex = Array.FindIndex(words, word =>
            string.Equals(TrimPunctuation(word), "extra", StringComparison.OrdinalIgnoreCase));
        if (extraIndex <= 0)
            return null;

        var digits = new string(words[extraIndex - 1].Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0
            ? count
            : null;
    }

    private static string TrimPunctuation(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && char.IsPunctuation(value[start]))
            start++;
        while (end > start && char.IsPunctuation(value[end - 1]))
            end--;
        return value[start..end];
    }

}

public sealed class UpdateReleaseSectionCards : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private ReleaseNoteCategory? presented;
        public ReleaseNoteCategory? Presented { get => Read(presented); set => Set(ref presented, value); }
    }

    private readonly ViewState state = new();

    public UpdateReleaseSectionCards(UpdateReleaseNotesDigest digest, string version)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        for (var index = 0; index < ReleaseNoteCategoryExtensions.AllCases.Count; index++)
        {
            var category = ReleaseNoteCategoryExtensions.AllCases[index];
            var section = digest.UpdateReleaseNotesDigestSection(category);
            var button = BuildCard(category, section);
            var popover = new Popup
            {
                Placement = PlacementMode.Top, // :244 arrowEdge: .top, the arrow on the card's top edge (popover above it)
                PlacementTarget = button,
                IsLightDismissEnabled = true,
            };
            // UI-UPDATE-015: the real open above the card, light dismissal by an outside press and a press on the open popover's own card
            // (which closes it and does not reopen it) are proven on a real popup: AboutUpdateViewsRealPopupTests (Avalonia's overlay host,
            // real input) and AboutUpdateViewsNativeTests (a PopupRoot window, system pointer). STOP opencode-proofs-aboutupdate #5 is closed.
            popover.Opened += (_, _) => popover.Child = section is null
                ? new Border()
                : new UpdateReleaseSectionPopover(section, version).PresentedChrome();
            popover.Closed += (_, _) =>
            {
                if (state.Presented == category)
                    state.Presented = null;
            };
            var shown = false;
            button.Track(() => state.Presented == category, presented =>
            {
                if (presented == shown)
                    return;
                shown = presented;
                Presentation.SetPresented(popover, presented, button, () => { if (state.Presented == category) state.Presented = null; }); // ED-253: Escape closes it
            });
            Grid.SetColumn(button, index * 2);
            row.Children.Add(button);
        }

        Child = row;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    // :202 fixedSize(horizontal: false, vertical: true).
    protected override Size MeasureOverride(Size availableSize) =>
        PlainControls.MeasureFixed(this, Child, availableSize, horizontal: false, vertical: true);

    private Button BuildCard(ReleaseNoteCategory category, UpdateReleaseNotesDigest.Section? section)
    {
        var icon = SwiftImage.Named(category.AssetName) // :214 original-color artwork bundled by ED-215.
            .Resizable()
            .Interpolation(BitmapInterpolationMode.HighQuality)
            .AspectRatio(ContentMode.Fit);
        icon.Width = 26;
        icon.Height = 26;

        var title = new TextBlock
        {
            Text = category.CardTitle,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, // :228 inherited multilineTextAlignment(.center).
        };
        SwiftFont.System(12.5, weight: SwiftFont.Weight.Medium).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        title.TextTrimming = TextTrimming.CharacterEllipsis; // :226 one-line Text uses tail truncation.

        var count = new TextBlock
        {
            Text = section is null ? "Nothing here" : category.CountSummary(section.ChangeCount),
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, // :228 inherited by both texts.
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(count);
        count.Foreground = Chrome.SecondaryText.Brush(count);
        count.TextTrimming = TextTrimming.CharacterEllipsis; // :226 one-line Text uses tail truncation.

        var labelStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { icon, title, count },
        };
        var scaledLabels = TextScaling.Apply(labelStack, 0.8); // :227 VStack minimumScaleFactor reaches both texts (ED-214).
        var surface = new Border
        {
            Padding = new Thickness(8, 12),
            Child = scaledLabels,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        }.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        surface.Background = category.CardTint.Opacity(0.09).Brush(surface);

        var button = PlainControls.Button(surface, () =>
        {
            if (section is not null)
                state.Presented = state.Presented == category ? null : category;
        }).KeyboardFocus(14);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button.IsEnabled = section is not null;
        button.Opacity = section is null ? 0.45 : 1;
        ToolTip.SetTip(button, section is null
            ? "Nothing here"
            : $"Every change in {category.SectionTitle.ToLowerInvariant()}");
        ToolTip.SetShowOnDisabled(button, true); // :237-239 the .disabled absent card still shows its .help("Nothing here")
        AutomationProperties.SetName(button, category.SectionTitle);
        return button;
    }
}

internal sealed class UpdateReleaseSectionPopover : Decorator
{
    // PR-SETTINGS-246 (CLOSED from the Swift, OD-0098): neither reference video shows a release or its popover (the recorded app was up to
    // date, S 199.5-214.5), so the capture comparison is replaced by the values :255-301 states, asserted with and without a rollup in
    // AboutUpdateViewsProofGeometryTests.Popover_with_and_without_rollup_has_source_header_colours_wrapping_duplicate_bullets_and_scroll,
    // and the popover above the card by AboutUpdateViewsProofInputTests.Each_card_presents_its_own_popover_above_itself.
    public UpdateReleaseSectionPopover(UpdateReleaseNotesDigest.Section section, string version)
    {
        var headerIcon = SwiftImage.Named(section.Category.AssetName) // :261 the same ED-215 release artwork.
            .Resizable()
            .Interpolation(BitmapInterpolationMode.HighQuality)
            .AspectRatio(ContentMode.Fit);
        headerIcon.Width = 24;
        headerIcon.Height = 24;

        var title = StyledText(section.Category.SectionTitle, SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText);
        var releaseVersion = StyledText(version, SwiftFont.System(SwiftFont.TextStyle.Subheadline), Chrome.SecondaryText);
        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            Children = { title, releaseVersion },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children = { headerIcon, titleStack, new Control { Width = 0, HorizontalAlignment = HorizontalAlignment.Stretch } },
        };

        var bullets = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 9,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var bullet in section.Bullets)
        {
            var text = StyledText(bullet, SwiftFont.System(SwiftFont.TextStyle.Body), Chrome.SecondaryText);
            text.TextWrapping = TextWrapping.Wrap;
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            bullets.Children.Add(text);
        }

        if (section.Rollup is { } rollup)
        {
            var text = StyledText(rollup, SwiftFont.System(SwiftFont.TextStyle.Callout), Chrome.SecondaryText);
            text.TextWrapping = TextWrapping.Wrap;
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            bullets.Children.Add(text);
        }

        var scroll = new ScrollViewer
        {
            Content = bullets,
            Template = AboutUpdateScroll.Template,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Children = { header, scroll },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Child = new Border
        {
            Width = 320,
            Padding = new Thickness(14),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private static TextBlock StyledText(string value, SwiftFont font, SwiftColor color)
    {
        var text = new TextBlock { Text = value };
        font.Apply(text);
        text.Foreground = color.Brush(text);
        return text;
    }
}

internal sealed class FractionChase
{
    private double displayed;
    private double? lastTime;

    public FractionChase(double initial) => displayed = initial;

    public double Advance(double to, double target, double response, double minimumSpeed)
    {
        var previousTime = lastTime;
        lastTime = to;
        if (previousTime is null)
        {
            displayed = target;
            return displayed;
        }

        if (target < displayed)
        {
            displayed = target;
            return displayed;
        }

        var dt = Math.Max(0, Math.Min(to - previousTime.Value, 0.25));
        var remaining = target - displayed;
        if (remaining <= 0 || dt <= 0)
            return displayed;

        var step = remaining * (1 - Math.Exp(-dt / Math.Max(response, 0.01)));
        step = Math.Max(step, minimumSpeed * dt);
        displayed = Math.Min(displayed + step, target);
        if (target - displayed < 0.0001)
            displayed = target;
        return displayed;
    }
}

public sealed class SmoothedUpdateProgressSlider : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private double target;
        private bool settled;

        public double Target { get => Read(target); set => Set(ref target, value); }
        public bool Settled { get => Read(settled); set => Set(ref settled, value); }
    }

    private readonly ViewState state = new();
    private readonly FractionChase chase;
    private readonly UpdateProgressSlider slider;
    private readonly double response;
    private readonly double minimumSpeed;

    public SmoothedUpdateProgressSlider(
        UpdateInstallProgress.FractionTarget target,
        double response = 0.3,
        double minimumSpeed = 0,
        double trackHeight = 14,
        double headDiameter = 20)
    {
        this.response = response;
        this.minimumSpeed = minimumSpeed;
        state.Target = target.Value;
        chase = new FractionChase(target.Value);
        slider = new UpdateProgressSlider(target.Value, trackHeight, headDiameter);
        Child = slider;

        this.Track(() => target.Value, value =>
        {
            state.Target = value;
            state.Settled = false;
        });
        this.TimelineView(
            () => new AnimationTimelineSchedule(
                1.0 / 60.0,
                state.Settled || ScrollActivity.Shared.Current.IsScrolling),
            date =>
            {
                var time = (date.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (double)TimeSpan.TicksPerSecond;
                var fraction = chase.Advance(time, state.Target, this.response, this.minimumSpeed);
                slider.SetFraction(fraction);
                state.Settled = fraction == state.Target;
            });
    }
}

public sealed class UpdateProgressSlider : Decorator
{
    private const ulong SparkleSeed = 0x5EED_D80B;
    private static readonly ValueList<SwiftColor> FillColors = new(
    [
        new SwiftColor(red: 0x67 / 255.0, green: 0xB9 / 255.0, blue: 0xEE / 255.0),
        new SwiftColor(red: 0x1A / 255.0, green: 0x77 / 255.0, blue: 0xB4 / 255.0),
        new SwiftColor(red: 0x12 / 255.0, green: 0x6C / 255.0, blue: 0xAF / 255.0),
    ]);
    private static readonly ValueList<SparkleParticle> Sparkles = SparkleParticle.Field(14, SparkleSeed);

    private readonly ProgressTrack track;
    private double fraction;

    public double Fraction => fraction;

    public UpdateProgressSlider(double fraction, double trackHeight = 14, double headDiameter = 20)
    {
        this.fraction = fraction;
        Height = headDiameter; // :435 .frame(height: headDiameter)
        track = new ProgressTrack(fraction, trackHeight, headDiameter, FillColors, Sparkles);
        Child = track;
        AutomationProperties.SetName(this, "Updating Nth");
        AttachedToVisualTree += (_, _) =>
            track.SetReduceMotion(EnvironmentValues.GetAccessibilityReduceMotion(this));
        // :387,408 @Environment(\.accessibilityReduceMotion) is read by body on every evaluation, so a change while the slider
        // shows (the system setting, WM_SETTINGCHANGE, ED-084) drops or brings back the sparkles at once.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
                track.SetReduceMotion(EnvironmentValues.GetAccessibilityReduceMotion(this));
        };
        ActualThemeVariantChanged += (_, _) => track.UpdateTheme(ActualThemeVariant);
    }

    internal void SetFraction(double value)
    {
        if (fraction.Equals(value))
            return;
        fraction = value;
        track.SetFraction(value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ProgressPeer(this);

    internal static double HeadX(double @for, double width, double trackHeight) =>
        trackHeight / 2 + (width - trackHeight) * Math.Clamp(@for, 0, 1);

    internal static double FillOpacity(double @for)
    {
        const double start = 0.012;
        const double end = 0.05;
        if (@for <= start)
            return 0;
        if (@for >= end)
            return 1;
        var t = (@for - start) / (end - start);
        return t * t * (3 - 2 * t);
    }

    private sealed class ProgressPeer(UpdateProgressSlider owner) : ControlAutomationPeer(owner), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

        public string Value => owner.fraction.ToString("P0", CultureInfo.CurrentCulture);
        public bool IsReadOnly => true;

        public void SetValue(string? value) =>
            throw new InvalidOperationException("The progress value is read-only.");
    }
}

internal sealed record SparkleParticle
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Size { get; init; }
    public required double Speed { get; init; }
    public required double Phase { get; init; }
    public required double Drift { get; init; }

    public static ValueList<SparkleParticle> Field(long count, ulong seed)
    {
        var state = seed;
        double Next()
        {
            unchecked
            {
                state += 0x9E37_79B9_7F4A_7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
                z ^= z >> 31;
                return (z >> 11) / 9007199254740992.0;
            }
        }

        var particles = new List<SparkleParticle>((int)Math.Max(0, count));
        for (var index = 0L; index < count; index++)
        {
            particles.Add(new SparkleParticle
            {
                X = Next(),
                Y = 0.12 + Next() * 0.76,
                Size = 1.2 + Next() * 1.6,
                Speed = 0.9 + Next() * 2.4,
                Phase = Next() * Math.PI * 2,
                Drift = 0.6 + Next() * 1.4,
            });
        }
        return new ValueList<SparkleParticle>(particles);
    }
}

internal sealed class ProgressTrack : Panel
{
    private readonly Border track;
    private readonly FillCanvas fill;
    private readonly Border head;
    private readonly double trackHeight;
    private readonly double headDiameter;
    private double fraction;
    private double width;

    public ProgressTrack(
        double fraction,
        double trackHeight,
        double headDiameter,
        IReadOnlyList<SwiftColor> colors,
        IReadOnlyList<SparkleParticle> sparkles)
    {
        this.fraction = fraction;
        this.trackHeight = trackHeight;
        this.headDiameter = headDiameter;
        track = new Border { HorizontalAlignment = HorizontalAlignment.Stretch }.Capsule(RoundedCornerStyle.Circular); // :402-405 Capsule()
        fill = new FillCanvas(fraction, trackHeight, colors, sparkles) { IsHitTestVisible = false }; // :409 allowsHitTesting(false)
        var inner = new Ellipse
        {
            Width = headDiameter * 0.64,
            Height = headDiameter * 0.64,
            Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        head = new Border { Width = headDiameter, Height = headDiameter, Child = inner };
        AutomationProperties.SetAccessibilityView(track, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(fill, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(head, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(inner, AccessibilityView.Raw);
        Children.Add(track);
        Children.Add(fill);
        Children.Add(head);
        UpdateTheme(ActualThemeVariant);
    }

    public void SetFraction(double value)
    {
        fraction = value;
        fill.SetFraction(value, width, trackHeight);
        InvalidateArrange();
    }

    public void SetReduceMotion(bool reduceMotion) => fill.SetReduceMotion(reduceMotion);

    public void UpdateTheme(ThemeVariant? theme)
    {
        var dark = PlainControls.IsDark(theme);
        track.Background = dark
            ? new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(18, 0, 0, 0));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measuredWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : 104;
        track.Measure(new Size(measuredWidth, trackHeight));
        fill.Measure(new Size(measuredWidth, trackHeight));
        head.Measure(new Size(headDiameter, headDiameter));
        return new Size(measuredWidth, headDiameter);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        width = finalSize.Width;
        var y = (finalSize.Height - trackHeight) / 2;
        track.Arrange(new Rect(0, y, finalSize.Width, trackHeight));
        fill.Arrange(new Rect(0, y, finalSize.Width, trackHeight));
        fill.SetFraction(fraction, finalSize.Width, trackHeight);
        var x = UpdateProgressSlider.HeadX(fraction, finalSize.Width, trackHeight) - headDiameter / 2;
        head.Arrange(new Rect(x, (finalSize.Height - headDiameter) / 2, headDiameter, headDiameter));
        return finalSize;
    }
}

internal sealed class FillCanvas : Decorator
{
    private readonly Grid content;
    private readonly Border gradient;
    private readonly Canvas sparkleCanvas;
    private readonly IReadOnlyList<SparkleParticle> allSparkles;
    private readonly double trackHeight;
    private IReadOnlyList<SparkleParticle> sparkles;
    private readonly List<Ellipse> sparkleEllipses = [];
    private double width;
    private double currentTime;
    private readonly ViewState animationState = new();

    private sealed class ViewState : MainActorModel
    {
        private bool paused;
        public bool Paused { get => Read(paused); set => Set(ref paused, value); }
    }

    public FillCanvas(double fraction, double trackHeight, IReadOnlyList<SwiftColor> colors, IReadOnlyList<SparkleParticle> sparkles)
    {
        this.trackHeight = trackHeight;
        allSparkles = sparkles;
        this.sparkles = sparkles;

        var stops = new GradientStops();
        for (var index = 0; index < colors.Count; index++)
        {
            stops.Add(new GradientStop(colors[index].Resolve(ThemeVariant.Light), index / (double)(colors.Count - 1)));
        }
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops,
        };
        gradient = new Border { Background = brush };
        sparkleCanvas = new Canvas();
        AutomationProperties.SetAccessibilityView(gradient, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(sparkleCanvas, AccessibilityView.Raw);
        content = new Grid { Children = { gradient, sparkleCanvas } };
        AutomationProperties.SetAccessibilityView(content, AccessibilityView.Raw); // :439 children ignored
        Child = content;
        BuildSparkles();
        SetFraction(fraction, 0, trackHeight);
        animationState.Paused = sparkles.Count == 0 || ScrollActivity.Shared.Current.IsScrolling;
        this.TimelineView(
            () => new AnimationTimelineSchedule(
                1.0 / 30.0,
                animationState.Paused || ScrollActivity.Shared.Current.IsScrolling),
            date =>
            {
                currentTime = (date.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (double)TimeSpan.TicksPerSecond;
                UpdateSparkles(currentTime);
            });
    }

    public void SetFraction(double value, double availableWidth, double height)
    {
        width = availableWidth;
        Opacity = UpdateProgressSlider.FillOpacity(value);
        var headX = UpdateProgressSlider.HeadX(value, availableWidth, trackHeight);
        var revealWidth = Math.Max(headX, trackHeight / 2);
        content.Clip = availableWidth > 0 && height > 0
            ? new RectangleGeometry(new Rect(0, 0, Math.Min(revealWidth, availableWidth), height), Math.Min(revealWidth, height) / 2, Math.Min(revealWidth, height) / 2) // :458-462 circular clip, exact authored rectangle
            : null;
        UpdateSparkles(currentTime);
    }

    public void SetReduceMotion(bool reduceMotion)
    {
        sparkles = reduceMotion ? Array.Empty<SparkleParticle>() : allSparkles;
        animationState.Paused = sparkles.Count == 0 || ScrollActivity.Shared.Current.IsScrolling;
        BuildSparkles();
        UpdateSparkles(currentTime);
    }

    private void BuildSparkles()
    {
        sparkleEllipses.Clear();
        sparkleCanvas.Children.Clear();
        foreach (var particle in sparkles)
        {
            var sparkle = new Ellipse
            {
                Width = particle.Size,
                Height = particle.Size,
                Fill = Brushes.White,
                IsHitTestVisible = false,
            };
            sparkle.Classes.Add("update-sparkle");
            AutomationProperties.SetAccessibilityView(sparkle, AccessibilityView.Raw);
            sparkleEllipses.Add(sparkle);
            sparkleCanvas.Children.Add(sparkle);
        }
    }

    private void UpdateSparkles(double time)
    {
        currentTime = time;
        if (width <= 0)
            return;

        var usableWidth = Math.Max(0, width - 10);
        var usableHeight = Math.Max(0, trackHeight - 6);
        for (var index = 0; index < sparkles.Count; index++)
        {
            var particle = sparkles[index];
            var twinkle = 0.5 + 0.5 * Math.Sin(time * particle.Speed + particle.Phase);
            var opacity = 0.08 + 0.72 * twinkle * twinkle;
            var drift = Math.Sin(time * particle.Speed * 0.31 + particle.Phase * 2.1) * particle.Drift;
            var x = 5 + particle.X * usableWidth + drift - particle.Size / 2;
            var y = 3 + particle.Y * usableHeight - particle.Size / 2;
            var sparkle = sparkleEllipses[index];
            Canvas.SetLeft(sparkle, x);
            Canvas.SetTop(sparkle, y);
            sparkle.Opacity = Math.Min(1, opacity);
        }
    }
}

public sealed class UpdateAvailableDot : Decorator
{
    public UpdateAvailableDot()
    {
        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Chrome.Accent.Brush(this),
            RenderTransform = new TranslateTransform(2.5, -2.5),
        };
        AutomationProperties.SetName(dot, "Update available");
        Child = dot;
    }
}

public sealed class AboutUpdateChromeAccessory : Decorator
{
    public AboutUpdateChromeAccessory()
    {
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new AboutIdentityPill(), new AboutVersionPill() },
        };
    }
}

internal sealed class AboutIdentityPill : Decorator
{
    // :554 NSApp.applicationIconImage. OD-0015 / OD-0033 use the approved packaged app icon here.
    // Share the immutable app-lifetime bitmap; each pill keeps its own 20 pt layout and accessibility state.
    // Icon (20) + the HStack's :553 spacing (6) + the capsule's own left/right padding (:560-561, 8+12): the fixed
    // width the text's row never gives up, so a compressed offer can be translated into the text's own constraint.
    private const double NonTextWidth = 20 + 6 + (Chrome.CapsuleHorizontalPadding - 4) + Chrome.CapsuleHorizontalPadding;

    private readonly TextBlock text;

    // :566-567 .frame(height:) and the vertical padding fix the height only; there is no .fixedSize here (unlike
    // AboutVersionPill's :635 fixedSize), so the width is whatever the parent offers, up to the natural width. Avalonia's
    // horizontal StackPanel measures its children unconstrained on that axis (unlike SwiftUI's HStack, which shrinks a
    // flexible Text), so the offered width has to be pushed onto the text directly for :562's lineLimit(1) to truncate.
    protected override Size MeasureOverride(Size availableSize)
    {
        text.MaxWidth = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - NonTextWidth);
        return PlainControls.MeasureFixed(this, Child, availableSize, horizontal: false, vertical: true);
    }

    public AboutIdentityPill()
    {
        var version = AppInfo.Current.Version;
        var icon = new Image { Source = Marrow.App.Views.AppIconImage.Bitmap, Width = 20, Height = 20, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);

        text = new TextBlock
        {
            Text = $"v{version}",
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, // :562 lineLimit(1) truncates the tail once the capsule is compressed
            VerticalAlignment = VerticalAlignment.Center, // :553 HStack(spacing: 6) centres the 15-point text on the 20-point icon
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(text);
        text.Foreground = Chrome.PrimaryText.Brush(text);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { icon, text },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var padded = new Border
        {
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding - 4, Chrome.CapsuleVerticalPadding,
                Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
            Height = Chrome.CapsuleContentHeight + Chrome.CapsuleVerticalPadding * 2,
            Child = content,
        };
        Child = padded.ChromeGlassCapsule();
        AutomationProperties.SetName(this, $"Nth {version}");
    }
}

internal sealed class AboutVersionPill : Decorator
{
    private enum Standing
    {
        UpToDate,
        Failed,
        Unchecked,
    }

    private readonly record struct Snapshot(
        AvailableUpdate? Update,
        bool UpdateAvailable,
        bool IsChecking,
        DateTimeOffset? LastCheckedAt,
        string? LastError,
        UpdateInstallProgress.Phase Phase,
        bool ReduceMotion);

    private readonly UpdateChecker checker;
    private readonly UpdateInstallProgress progress;
    private bool? previousReady;
    private SwiftColor? previousIconTint;
    private readonly CrossfadeSlot slot = new();
    private string lastLook;

    public AboutVersionPill()
    {
        checker = UpdateChecker.Shared;
        progress = UpdateInstallProgress.Shared;
        var first = ReadSnapshot();
        lastLook = Look(first);
        slot.Show(Build(first), null);
        Child = slot;
        this.Track(ReadSnapshot, Swap);
    }

    // ENGINEERING DECISION (OD-0076 cut T2 and T3, :671): what the pill shows as one of the slider, the ready button or one of the three standings. A change
    // from one to another is a swap of content (SwiftUI: the `if` branches of :643-660 and :638-682 are removed and inserted, and under :671
    // `.animation(.easeInOut(duration: 0.18), value: isReady)` that takes the default opacity transition), so the old face fades out where it stood while the
    // new one fades in, over the same 0.18 s on the ease of :671, on the frame clock; the slot eases to the new face's width. Under Reduce Motion (:671 `nil`) they cut.
    // A rebuild that looks the same replaces the face at once (the slider's phase changes rebuild it with a new chase), unless a swap is still running.
    private static string Look(Snapshot snapshot) =>
        snapshot.Phase is not (UpdateInstallProgress.Phase.Idle or UpdateInstallProgress.Phase.Failed) ? "slider"
        : snapshot.UpdateAvailable && snapshot.Update is not null ? "ready"
        : ResolveStanding(snapshot).ToString();

    private void Swap(Snapshot snapshot)
    {
        var look = Look(snapshot);
        var changed = look != lastLook || slot.Settling;
        lastLook = look;
        slot.Show(Build(snapshot), changed && !snapshot.ReduceMotion ? Animation.EaseInOut(0.18) : null);
    }

    private Snapshot ReadSnapshot() => new(
        checker.Update,
        checker.UpdateAvailable,
        checker.IsChecking,
        checker.LastCheckedAt,
        checker.LastError,
        progress.UpdateInstallProgressPhase,
        EnvironmentValues.GetAccessibilityReduceMotion(this));

    // :635 fixedSize; :660 fixed horizontal label, :662-663 exact capsule height including its padding.
    protected override Size MeasureOverride(Size availableSize) => PlainControls.MeasureFixed(this, Child, availableSize);

    private Control Build(Snapshot snapshot)
    {
        if (snapshot.Phase is not (UpdateInstallProgress.Phase.Idle or UpdateInstallProgress.Phase.Failed))
        {
            // PR-UPDATE-074 (CLOSED from the Swift, OD-0098): the 104 pt slider in the capsule for every phase, and the chase's response and
            // minimum speed per phase, are asserted in AboutUpdateViewsProofGeometryTests and AboutUpdateViewsPillStateTests; neither
            // reference video shows an install or its celebration (the recorded app was up to date), so :625-636 is the proof.
            var slider = new SmoothedUpdateProgressSlider(
                progress.UpdateInstallProgressFractionTarget,
                response: EnvironmentValues.GetAccessibilityReduceMotion(this)
                    ? 0.15
                    : snapshot.Phase == UpdateInstallProgress.Phase.Celebrating ? 0.72 : 0.3,
                minimumSpeed: snapshot.Phase == UpdateInstallProgress.Phase.Celebrating ? 0.12 : 0,
                trackHeight: 14,
                headDiameter: 20)
            {
                Width = 104,
                Height = Chrome.CapsuleContentHeight,
            };
            var inner = new Border
            {
                Width = 104,
                Height = Chrome.CapsuleContentHeight,
                Child = slider,
            };
            var padded = new Border
            {
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
                Child = inner,
            };
            return padded.ChromeGlassCapsule();
        }

        // PR-UPDATE-072 (CLOSED, OD-0098): the Up to date chip is compared with the reference frame (AboutUpdateViewsChipCaptureTests); Check failed
        // and Not checked yet appear on no frame (the recorded feed answered), so their order, labels, symbols and tints are proven from :587-621 by
        // AboutUpdateViewsProofGeometryTests.Pill_faces_have_exact_metrics_tints_glass_help_and_only_ready_has_the_button_role.
        var isReady = snapshot.UpdateAvailable && snapshot.Update is not null;
        var wasReady = previousReady;
        previousReady = isReady;
        var animation = wasReady is not null && wasReady != isReady && !snapshot.ReduceMotion ? Animation.EaseInOut(0.18) : null; // :671
        var fromReady = animation is null ? isReady : wasReady == true;
        var standing = ResolveStanding(snapshot);
        var iconName = isReady ? "arrow.down.circle.fill" : Symbol(standing);
        var icon = SwiftImage.SystemName(iconName).Font(Chrome.InlineIconFont);
        var iconTint = isReady ? Chrome.Blue : Tint(standing);
        var fromIconTint = previousIconTint ?? iconTint;
        var iconColor = new AnimatedColor(icon);
        iconColor.Set(animation is null ? iconTint : fromIconTint, null);
        iconColor.Set(iconTint, animation);
        icon.Foreground = iconColor.Brush;
        previousIconTint = iconTint;
        var title = new TextBlock
        {
            Text = isReady ? "Update & restart" : Title(standing),
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold).Apply(title);
        var titleColor = new AnimatedColor(title);
        titleColor.Set(fromReady ? Chrome.Blue : Chrome.PrimaryText, null);
        titleColor.Set(isReady ? Chrome.Blue : Chrome.PrimaryText, animation);
        title.Foreground = titleColor.Brush;

        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { icon, title },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new Border
        {
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
            Height = Chrome.CapsuleContentHeight + Chrome.CapsuleVerticalPadding * 2,
            Child = label,
        };
        face.Capsule(RoundedCornerStyle.Continuous);
        var fill = new AnimatedColor(face);
        fill.Set(Chrome.Blue.Opacity(fromReady ? 0.2 : 0), null);
        fill.Set(Chrome.Blue.Opacity(isReady ? 0.2 : 0), animation);
        face.Background = fill.Brush; // :666-671 the source's animated ready wash, nil animation under Reduce Motion.
        if (animation is not null)
        {
            // Newly rebuilt children acquire their inherited theme on attachment. AnimatedColor resets
            // on that change, so start the :671 animation after the entire subtree inherits its theme.
            face.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(face) is null) return;
                iconColor.Set(fromIconTint, null);
                iconColor.Set(iconTint, animation);
                titleColor.Set(fromReady ? Chrome.Blue : Chrome.PrimaryText, null);
                titleColor.Set(isReady ? Chrome.Blue : Chrome.PrimaryText, animation);
                fill.Set(Chrome.Blue.Opacity(fromReady ? 0.2 : 0), null);
                fill.Set(Chrome.Blue.Opacity(isReady ? 0.2 : 0), animation);
            });
        }

        // :682 add the button trait only while ready; hit testing alone does not change the role.
        var button = new PillButton(isReady)
        {
            Content = face,
            Padding = default,
            Template = PlainControls.Button(face, () => { }).Template,
        };
        button.Click += (_, _) =>
        {
            // :639 `guard let update = checker.update, isReady` reads the live checker and progress when the click
            // lands (:581-583 isReady), not the snapshot this face was built from.
            if (checker.Update is { } update && checker.UpdateAvailable && !progress.ShowsProgressUI)
                AppUpdater.Shared.Install(update);
        };
        button.IsHitTestVisible = isReady;
        button.Focusable = isReady;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, isReady
            ? "Update & restart"
            : $"Nth {AppInfo.Current.Version}, {Title(standing).ToLowerInvariant()}");
        // STOP opencode-proofs-aboutupdate #3 UI-UPDATE-005 - RESOLVED 2026-10-04: F.GlassMaterial renders the interactive response
        // (Marrow.UI.Tests GlassMaterialTests.InteractiveGlassRespondsToHoverAndPress) and the press reaches this glass's host
        // (AboutUpdateViewsPillGlassTests).
        var glass = button.ChromeGlassCapsule();
        // :674-680 `.help` is applied after `.allowsHitTesting(isReady)` and `.chromeGlassCapsule()`, and :676-677 says
        // the seal still says what it means: the tooltip belongs to the whole capsule, which takes the hover even while
        // the button takes no hits. It does nothing with a press, so a standing stays inert.
        var tipHost = new Border { Background = Brushes.Transparent, Child = glass };
        ToolTip.SetTip(tipHost, isReady
            ? $"Installs Nth {snapshot.Update?.Version ?? string.Empty} and relaunches"
            : StandingHelp(snapshot, standing));
        return tipHost;
    }

    private sealed class PillButton(bool ready) : Button
    {
        protected override AutomationPeer OnCreateAutomationPeer() => ready
            ? new ButtonAutomationPeer(this)
            : new StandingPeer(this);

        private sealed class StandingPeer(Control owner) : ControlAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        }
    }

    private static Standing ResolveStanding(Snapshot snapshot)
    {
        if (snapshot.LastError is not null)
            return Standing.Failed;
        if (snapshot.LastCheckedAt is null)
            return Standing.Unchecked;
        return Standing.UpToDate;
    }

    private static string Symbol(Standing standing) => standing switch
    {
        Standing.UpToDate => "checkmark.seal.fill",
        Standing.Failed => "exclamationmark.triangle.fill",
        Standing.Unchecked => "clock",
        _ => throw new ArgumentOutOfRangeException(nameof(standing)),
    };

    private static string Title(Standing standing) => standing switch
    {
        Standing.UpToDate => "Up to date",
        Standing.Failed => "Check failed",
        Standing.Unchecked => "Not checked yet",
        _ => throw new ArgumentOutOfRangeException(nameof(standing)),
    };

    private static SwiftColor Tint(Standing standing) => standing switch
    {
        Standing.UpToDate => Chrome.Success,
        Standing.Failed => Chrome.Warning,
        Standing.Unchecked => Chrome.SecondaryText,
        _ => throw new ArgumentOutOfRangeException(nameof(standing)),
    };

    private static string StandingHelp(Snapshot snapshot, Standing standing) => standing switch
    {
        Standing.UpToDate => $"Nth {AppInfo.Current.Version} is the latest version",
        Standing.Failed => snapshot.LastError ?? "The last check did not reach GitHub",
        Standing.Unchecked => "Nth has not asked GitHub for a release yet",
        _ => string.Empty,
    };
}

public sealed class AboutSoftwareUpdateSection : Decorator
{
    private readonly UpdateChecker checker;
    private readonly UpdateInstallProgress progress;
    private UpdateReleaseNotesDigest digest = new()
    {
        Sections = new ValueList<UpdateReleaseNotesDigest.Section>(Array.Empty<UpdateReleaseNotesDigest.Section>()),
    };
    private string? digestVersion;
    private TaskHandle? celebration;

    private readonly record struct Snapshot(bool UpdateAvailable, AvailableUpdate? Update);

    public AboutSoftwareUpdateSection()
    {
        checker = UpdateChecker.Shared;
        progress = UpdateInstallProgress.Shared;
        ApplySnapshot(ReadSnapshot());
        this.Track(ReadSnapshot, ApplySnapshot);
        this.Task(
            () => 0,
            async _ =>
            {
                if (progress.ConsumePendingCelebration())
                    StartCelebrationSweep();
                await checker.Check();
            });
        DetachedFromVisualTree += (_, _) =>
        {
            celebration?.Cancel();
            celebration = null;
            progress.CompleteCelebration();
        };
    }

    private Snapshot ReadSnapshot()
    {
        var offered = checker.UpdateAvailable;
        return new Snapshot(offered, offered ? checker.Update : checker.InstalledRelease);
    }

    private void ApplySnapshot(Snapshot snapshot)
    {
        var version = snapshot.Update?.Version;
        if (!string.Equals(version, digestVersion, StringComparison.Ordinal))
        {
            digestVersion = version;
            digest = UpdateReleaseNotesDigest.Parse(snapshot.Update?.Notes);
        }

        Child = Build(snapshot);
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private Control Build(Snapshot snapshot)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Chrome.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (snapshot.Update is { } update)
            stack.Children.Add(ReleaseBody(update, snapshot.UpdateAvailable));
        return stack;
    }

    private Control ReleaseBody(AvailableUpdate update, bool offered)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (!offered)
            content.Children.Add(new TextBlock { Text = $"What's new in Nth {update.Version}", TextWrapping = TextWrapping.Wrap }.Font(SwiftFont.Callout));

        if (UpdateReleaseNotesContent.Create(digest, update.Version, update.Notes) is { } notes)
            content.Children.Add(notes);

        // Installed notes remain readable without offering to install the running version again.
        if (offered)
        {
            var releaseLink = BuildReleaseLink(update);
            var detail = ReleaseDetail(update);
            content.Children.Add(new ChromeCard(() =>
                new ChromeRow("Unsigned Windows installer", () => releaseLink, detail)));
        }
        return content;
    }

    private static Control BuildReleaseLink(AvailableUpdate update)
    {
        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock { Text = "Release notes" };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Apply(text);
        text.Foreground = Chrome.Accent.Brush(text);
        var arrow = SwiftImage.SystemName("arrow.up.right")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold))
            .ForegroundStyle(Chrome.Accent);
        label.Children.Add(text);
        label.Children.Add(arrow);

        var button = PlainControls.Button(label, () =>
            ShellWorkspace.OpenWithDefaultApp(update.PageURL.RelativeString));
        ToolTip.SetTip(button, "Open the release on GitHub");
        AutomationProperties.SetName(button, "Release notes");
        return button;
    }

    // :781-791 releaseDetail(_:); unchanged between B2 and B3 (ED-198: the Swift call was already
    // ByteCountFormatStyle(style: .file) at B2, only this port's own FileByteCount call site was wrong).
    private static string ReleaseDetail(AvailableUpdate update)
    {
        var parts = new List<string>();
        if (update.ReleasedAt is { } releasedAt)
        {
            var date = SwiftFormatters.Formatted(
                releasedAt,
                DateFormatStyle.Of(DateFormatStyle.DateStyle.Long, DateFormatStyle.TimeStyle.Omitted));
            parts.Add($"Released {date}");
        }
        if (update.Size is { } size)
            parts.Add(SwiftFormatters.ByteCountStyleFile(size, SwiftFormatters.CurrentLocale.Current)); // ED-198: ByteCountFormatStyle(style: .file), not the ByteCountFormatter-based FileByteCount
        parts.Add("Update & restart installs it in place and relaunches.");
        return string.Join(" · ", parts);
    }

    private void StartCelebrationSweep()
    {
        // STOP opencode-proofs-aboutupdate #2 PR-UPDATE-061 - RESOLVED 2026-10-04: SystemReduceMotion (ED-084) puts the Windows
        // animation-effects setting on every window the app opens, and AboutUpdateViewsHapticTests drives the sweep from it.
        // The record's every-step phase-abort clause is stronger than pinned Swift :795-818: only :801 checks the phase in this
        // task; finish/complete guard their own writes. No unstated guards are invented (the Swift wins).
        celebration?.Cancel();
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        celebration = MainActor.Spawn(async () =>
        {
            var currentProgress = UpdateInstallProgress.Shared;
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(550));
            if (SwiftTask.IsCancelled || currentProgress.UpdateInstallProgressPhase != UpdateInstallProgress.Phase.Celebrating)
                return;

            currentProgress.FinishCelebrationSweep();
            if (reduceMotion)
            {
                Haptics.Perform(HapticPattern.Alignment);
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(900));
            }
            else
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(900));
                if (SwiftTask.IsCancelled)
                    return;
                Haptics.Perform(HapticPattern.Alignment);
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(450));
                if (SwiftTask.IsCancelled)
                    return;
                Haptics.Perform(HapticPattern.LevelChange);
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(700));
            }

            if (!SwiftTask.IsCancelled)
                currentProgress.CompleteCelebration();
        });
    }
}

public sealed class AboutUpdateCheckRow : Decorator
{
    private readonly record struct Snapshot(
        bool IsChecking,
        DateTimeOffset? LastCheckedAt,
        string? LastError,
        bool UpdateAvailable,
        string? Version);

    private readonly UpdateChecker checker;
    private readonly CrossfadeSlot buttonSlot = new();
    private readonly TextBlock detailText;
    private readonly SentenceHeightGlide detailBox;
    private string detailShown;
    private bool buttonChecking;
    private IFrameSource? detailFrames;
    private Action<TimeSpan>? detailFrame;

    public AboutUpdateCheckRow()
    {
        checker = UpdateChecker.Shared;
        var first = ReadSnapshot();
        buttonChecking = first.IsChecking;
        buttonSlot.Show(MakeButton(first), null);
        detailShown = Detail(first);
        // The row is built once; its button and its detail sentence swap with a fade (ENGINEERING DECISION, OD-0076 cut T1): ChromeRow keeps its title
        // and metrics, the Mac's `Button(checker.isChecking ? ...)` and `Text(detail)` (:826-844) change in place.
        var chrome = new ChromeRow("Software update", () => buttonSlot, detailShown);
        var textStack = (StackPanel)((Grid)((Border)chrome.Child!).Child!).Children[0];
        detailText = (TextBlock)textStack.Children[1];
        // The sentence sits in a box whose height eases when its words swap to a different number of lines (OD-0076: a card that changes size eases to the new size).
        textStack.Children.Remove(detailText);
        textStack.Children.Add(detailBox = new SentenceHeightGlide(detailText));
        Child = chrome;
        this.Track(ReadSnapshot, Apply);
    }

    private Snapshot ReadSnapshot() => new(
        checker.IsChecking,
        checker.LastCheckedAt,
        checker.LastError,
        checker.UpdateAvailable,
        checker.Update?.Version);

    private Control MakeButton(Snapshot snapshot)
    {
        // :826-833 Button(checker.isChecking ? "Checking…" : "Check now") { Task { await checker.check() } }.buttonStyle(.glass)
        // .controlSize(.small).disabled(checker.isChecking): the small accent glass capsule the recorded Mac runtime shows
        // (SmallAccentGlassButton, ED-261); the label and the disabled look share isChecking.
        return SmallAccentGlassButton.Create(snapshot.IsChecking ? "Checking…" : "Check now", () =>
        {
            if (!snapshot.IsChecking)
                MainActor.Spawn(async () => await checker.Check());
        }, enabled: !snapshot.IsChecking);
    }

    // OD-0076 cut T1: the button (label, accent capsule to grey and back) crossfades into its next state over 0.18 s on the ease of the pill's :671, and its slot
    // eases to the new width; the detail sentence fades out and the new one fades in over the same time. Reduce Motion cuts them.
    private void Apply(Snapshot snapshot)
    {
        var animation = EnvironmentValues.GetAccessibilityReduceMotion(this) ? null : Animation.EaseInOut(0.18);
        // The button looks only as isChecking says (:827,832); a change of anything else leaves the one on show.
        if (snapshot.IsChecking != buttonChecking) { buttonChecking = snapshot.IsChecking; buttonSlot.Show(MakeButton(snapshot), animation); }
        SwapDetail(Detail(snapshot), animation);
    }

    private void SwapDetail(string next, Animation? animation)
    {
        if (next == detailShown)
        {
            // The words on show, or on their way in, are these: a swap that is running goes on to them (cancelling it in its first half would leave the old words at full opacity).
            // Under Reduce Motion a swap still running is finished at once.
            if (detailFrame is null || animation is not null) { if (detailFrame is null) detailText.Opacity = 1; return; }
            if (detailFrames is { } finishing) finishing.CancelFrame(detailFrame);
            detailFrame = null; detailBox.Settle(); detailText.Text = next; detailText.Opacity = 1;
            return;
        }
        if (detailFrame is { } running) detailFrames?.CancelFrame(running);
        detailFrame = null;
        detailShown = next;
        if (animation is null || TopLevel.GetTopLevel(detailText) is null) { detailBox.Settle(); detailText.Text = next; detailText.Opacity = 1; return; }
        // The old words fade out where they stood over the first half, the new ones in over the second (HydraProgressBar's swap); the words on show when
        // a swap is turned back part way fade from the opacity they have. The box they sit in eases from the height it has to the new words' height over
        // the whole swap on the same curve, so a sentence that rewraps never steps the row (and the card) by a line in one frame.
        var half = animation.Duration / 2; var frames = detailFrames = FrameClock.Source.Current; var since = SwiftClock.Now; var from = detailText.Opacity; var shown = false;
        detailBox.Ease(next, animation);
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - since).TotalSeconds;
            if (elapsed < half) { detailText.Opacity = from * (1 - Animation.EaseIn(half).Progress(elapsed)); detailBox.Tick(); frames.RequestFrame(frame!); return; }
            if (!shown) { shown = true; detailText.Text = next; }
            var done = elapsed + 0.001 >= 2 * half;
            detailText.Opacity = done ? 1 : Animation.EaseOut(half).Progress(elapsed - half);
            if (done) { detailFrame = null; detailBox.Settle(); return; }
            detailBox.Tick();
            frames.RequestFrame(frame!);
        };
        detailFrame = frame; frames.RequestFrame(frame);
    }

    private static string Detail(Snapshot snapshot)
    {
        if (snapshot.IsChecking)
            return "Asking GitHub for the latest release…";

        var checkedText = snapshot.LastCheckedAt is { } checkedAt
            ? $"Checked {SwiftFormatters.RelativeNamed(checkedAt)}."
            : "Not checked yet.";
        if (snapshot.LastError is { } error)
            return $"{error} {checkedText}";
        if (snapshot.UpdateAvailable && snapshot.Version is { } version)
            return $"Nth {version} is ready to install. {checkedText}";
        return $"Nth checks GitHub for new versions a few times a day. {checkedText}";
    }
}

/// <summary>
/// PORT-ONLY (OD-0076, ED-261 T1): the box the check row's sentence sits in. While the sentence's words swap, the box's height eases from the
/// height it has to the height the new words take at the width it is given (measured by a probe that carries the new words, invisible, for the swap's length)
/// over the swap's animation, so the row and the card around it ease with it; with no swap it is the sentence's own height.
/// </summary>
internal sealed class SentenceHeightGlide : Panel
{
    private readonly TextBlock text;
    private TextBlock? probe;
    private double presented;
    private double from;
    private Animation? glide;
    private ContinuousInstant glideStart;

    public SentenceHeightGlide(TextBlock text) { this.text = text; Children.Add(text); }

    /// <summary>The invisible measuring text that carries the new words while the height eases (null at rest).</summary>
    public TextBlock? Probe => probe is not null && Children.Contains(probe) ? probe : null;

    /// <summary>Whether the height is still easing.</summary>
    public bool Gliding => glide is not null;

    /// <summary>Starts easing from the height shown to the height <paramref name="next"/> takes, over <paramref name="animation"/>.</summary>
    public void Ease(string next, Animation animation)
    {
        if (presented <= 0) { Settle(); return; } // never laid out: nothing to ease from
        if (probe is null)
        {
            probe = new TextBlock
            {
                FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontFeatures = text.FontFeatures,
                LineHeight = text.LineHeight, // the sentence's text-style line box (ED-290 (1)), so the new words are measured as they will be drawn
                TextWrapping = text.TextWrapping, Opacity = 0, IsHitTestVisible = false,
            };
            AutomationProperties.SetAccessibilityView(probe, AccessibilityView.Raw);
        }
        probe.Text = next;
        if (!Children.Contains(probe)) Children.Add(probe);
        from = presented; glide = animation; glideStart = SwiftClock.Now; ClipToBounds = true;
        InvalidateMeasure();
    }

    /// <summary>A frame of the swap: the height moves on.</summary>
    public void Tick() => InvalidateMeasure();

    /// <summary>The swap is over (or cut): the height is the sentence's own.</summary>
    public void Settle()
    {
        glide = null; ClipToBounds = false;
        if (probe is not null) Children.Remove(probe);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        text.Measure(availableSize);
        var size = text.DesiredSize;
        if (glide is { } animation && probe is not null)
        {
            probe.Measure(availableSize);
            var seconds = Math.Min((SwiftClock.Now - glideStart).TotalSeconds, animation.Duration);
            size = new Size(size.Width, from + (probe.DesiredSize.Height - from) * animation.Progress(seconds));
        }
        presented = size.Height;
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        text.Arrange(new Rect(0, 0, finalSize.Width, text.DesiredSize.Height));
        probe?.Arrange(new Rect(0, 0, finalSize.Width, probe.DesiredSize.Height));
        return finalSize;
    }
}
