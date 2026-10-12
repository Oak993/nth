// PORT-ONLY. The default control themes this app registers, for controls built without their own template.
//
// ENGINEERING DECISION: the app carries no theme package (Marrow.App/Port/MarrowApplication.cs), so every control is
// expected to carry its own template. A ScrollViewer built without one fell back to a bare ContentPresenter: its extent
// was zero, the wheel and Offset did nothing, and tall content was centred and clipped at both ends (probe, 2026-10-01;
// 22 sites, among them the composer text view, the diff inspector, popover lists, Markdown code blocks). SwiftUI's
// ScrollView scrolls by default, so the default theme here gives a ScrollViewer the port's own scroll template, the same
// recipe as ThreadTimeline's and SettingsView's (a ScrollContentPresenter with the touch gesture recognizer, no visible
// scroll bars), and a transparent background so the wheel works over its whole frame, as NSScrollView's does
// (ThreadTimeline's fix of the same date). A control with an explicit Template keeps it: a local value wins over the
// theme's setter. Registered at the one shared builder point, next to WithSwiftFonts (ED-107 (5)).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. Default templates for controls built without one (see the file note).</summary>
public static class PortControlThemes
{
    private static readonly FuncControlTemplate<ScrollViewer> ScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
            [!ContentPresenter.PaddingProperty] = viewer[!TemplatedControl.PaddingProperty],
        };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = true });
        return presenter.RegisterInNameScope(scope);
    });

    /// <summary>The ScrollViewer default theme: the port's scroll template.</summary>
    public static ControlTheme ScrollViewerTheme { get; } = new(typeof(ScrollViewer))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, ScrollTemplate) },
    };

    // ProgressView(): the nth busy indicator (OD-0136 amendment 2). Unknown length is the tile-trace spinner (step 1 B) at the
    // bar's own size (16 when it gives none), or, when the bar is a thin strip (4 to 6 high, or at least 2.5 times wider than
    // tall), the liquid-silver bar growing and catching up (step 3 B) at 4 or 3 high. Known length is the liquid-silver bar
    // whose value follows Value/Minimum/Maximum: shown at once the first time, springing after.
    // ProgressBar's own code requires a PART_Indicator Border, which it sizes; it stays hidden, the drawing is ours.
    // The drawing follows IsIndeterminate (and the bar's size) as it changes: a site may switch a bar between the two.
    private static readonly FuncControlTemplate<ProgressBar> ProgressTemplate = new((bar, scope) =>
    {
        var panel = new Panel
        {
            Children =
            {
                NthBusyDrawing.For(bar),
                new Border { Name = "PART_Indicator", IsVisible = false }.RegisterInNameScope(scope),
            },
        };
        bar.PropertyChanged += (_, e) =>
        {
            if (e.Property == ProgressBar.IsIndeterminateProperty || e.Property == Layoutable.WidthProperty || e.Property == Layoutable.HeightProperty)
                panel.Children[0] = NthBusyDrawing.For(bar);
            else if (e.Property == RangeBase.ValueProperty || e.Property == RangeBase.MinimumProperty || e.Property == RangeBase.MaximumProperty)
            {
                if (panel.Children[0] is NthProgressBar known && !bar.IsIndeterminate) known.Value = NthBusyDrawing.Fraction(bar);
            }
        };
        return panel;
    });

    /// <summary>The ProgressBar default theme: NthSpinner or NthProgressBar (step 1 B and step 3 B).</summary>
    public static ControlTheme ProgressBarTheme { get; } = new(typeof(ProgressBar))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, ProgressTemplate) },
    };

    // `.help(...)` (NSView.toolTip): the system tooltip. Without a template a ToolTip drew its text with no card, floating
    // over whatever was under it. OD-0042 ("Mac look") / OD-0045: the macOS help tag, every value a SOURCE FACT (Apple
    // design resource) from the "Apple macOS 27 UI Kit" (Tooltips / Dark and / Light, Inspect panel, read 2026-10-02;
    // E:\Droppy\work\apple-kit\macos27-uikit-controls.md): the card fits its text with padding 6 horizontal and 3
    // vertical, square corners (the Inspect panel lists none), fill Dark #262626@0.8 / Light #E7E7E7@0.85, shadows
    // 0 0 2 #000000@0.4 and 0 2 8 #000000@0.2 and, in Dark, an inner 0.6 hairline #FFFFFF@0.15; the text SF Pro Medium 11 /
    // 13 (the port's own family), Dark #FFFFFF@0.96 (Labels Dark Vibrant 1 Primary, plus lighter), Light #4C4C4C (plus
    // darker; both drawn in normal blending). ToolTipBackdrop supplies the kit's 45-point background blur behind the card (a tooltip
    // is its own popup; OD-0005 governs materials). The card keeps 8 / 8 / 8 / 10 points round it inside the tip so the
    // popup does not clip its shadows (ENGINEERING DECISION).
    private static readonly SwiftColor TipFill = SwiftColor.Dynamic(SwiftColor.Rgb(0xE7E7E7).Opacity(0.85), SwiftColor.Rgb(0x262626).Opacity(0.8));

    private static readonly SwiftColor TipText = SwiftColor.Dynamic(SwiftColor.Rgb(0x4C4C4C), new SwiftColor(white: 1, opacity: 0.96));

    private static readonly FuncControlTemplate<ToolTip> TipTemplate = new((tip, scope) =>
    {
        var presenter = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            LineHeight = 13,
            [!ContentPresenter.ContentProperty] = tip[!ContentControl.ContentProperty],
        }.RegisterInNameScope(scope);
        presenter.SetValue(SwiftFont.LineBoxProperty, double.NaN); // its fixed 13-point line, never a text style's box from the owner (ED-290 (1))
        var card = new Border { Name = "PART_Tag", Padding = new Thickness(6, 3), Margin = new Thickness(8, 8, 8, 10), Child = presenter };
        card.Background = TipFill.Brush(card);
        presenter.Foreground = TipText.Brush(presenter);
        void Shadows()
        {
            var theme = card.ActualThemeVariant;
            var outer = new[]
            {
                new BoxShadow { Blur = 2, Color = new SwiftColor(white: 0, opacity: 0.4).Resolve(theme) },
                new BoxShadow { OffsetY = 2, Blur = 8, Color = new SwiftColor(white: 0, opacity: 0.2).Resolve(theme) },
            };
            card.BoxShadow = theme == ThemeVariant.Dark
                ? new BoxShadows(outer[0], [outer[1], new BoxShadow { IsInset = true, Spread = 0.6, Color = new SwiftColor(white: 1, opacity: 0.15).Resolve(theme) }])
                : new BoxShadows(outer[0], [outer[1]]);
        }
        card.ActualThemeVariantChanged += (_, _) => Shadows();
        Shadows();
        return new Panel { Children = { new ToolTipBackdrop(card), card } };
    });

    // Owner finding F4 (2026-10-08, installed build): hovering a title-bar button showed the dark tip card inside a white
    // square. A ToolTip is shown in a Popup of its own, and on Windows that Popup is a PopupRoot window whose default
    // Background is opaque white (PopupRoot's static constructor; PopoverHost.cs note 2 says the same of popovers). The
    // card above assumes a clear surround, so the tip's top-level gets no fill at all and a transparent surface, as a
    // tipped popover's does (PopoverHost.Register), the moment the tip is put in it, before the window is shown.
    private static readonly AttachedProperty<bool> ClearsTipSurroundProperty =
        AvaloniaProperty.RegisterAttached<ToolTip, bool>("ClearsTipSurround", typeof(PortControlThemes));

    static PortControlThemes()
    {
        ClearsTipSurroundProperty.Changed.AddClassHandler<ToolTip>((tip, change) =>
        {
            if (change.NewValue is not true) return;
            void Clear()
            {
                if (TopLevel.GetTopLevel(tip) is not PopupRoot root) return;
                root.Background = null;
                root.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            }
            tip.AttachedToVisualTree += (_, _) => Clear();
            Clear();
        });
    }

    /// <summary>The ToolTip default theme: the system tooltip card, in a clear top-level.</summary>
    public static ControlTheme ToolTipTheme { get; } = new(typeof(ToolTip))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, TipTemplate), new Setter(ClearsTipSurroundProperty, true) },
    };

    // Slider(...).controlSize(.small) (SettingsView.swift:532-534 transparency, WallpaperCard.swift:133-139 softness): drew
    // nothing without a template (probe; SettingsRestNativeDependencyTests recorded the blocker). ENGINEERING DECISION
    // (proposal ED-229 (2)), measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 96.5 s,
    // Settings > Appearance > Transparency, about 1 px = 1 pt): a 4 pt capsule track across the slider's width, the knob a
    // white 18 x 14 pt capsule kept inside the track's ends; the track's wash is primary text at 0.12 (measured about
    // white 10 % on the dark card); the filled side is the accent (1-settings-tour.mov 128-131 s: the theme's pink). A click on the track moves the knob there, as NSSlider's does (Avalonia's Slider does that on its own).
    // A vertical slider (no site builds one) turns the same track on its side.
    private static Border SliderBar(Slider slider, Func<Control, IBrush> fill)
    {
        var bar = new Border { CornerRadius = new CornerRadius(2) };
        bar.Background = fill(bar);
        void Orient()
        {
            var vertical = slider.Orientation == Avalonia.Layout.Orientation.Vertical;
            bar.Height = vertical ? double.NaN : 4;
            bar.Width = vertical ? 4 : double.NaN;
            bar.VerticalAlignment = vertical ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Center;
            bar.HorizontalAlignment = vertical ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.OrientationProperty) Orient(); };
        Orient();
        return bar;
    }

    private static RepeatButton SliderSide(Slider slider, string name, Func<Control, IBrush> fill) => new()
    {
        Name = name,
        Focusable = false,
        Background = Brushes.Transparent,
        Template = new FuncControlTemplate<RepeatButton>((side, _) => new Panel { Background = Brushes.Transparent, Children = { SliderBar(slider, fill) } }),
    };

    private static readonly FuncControlTemplate<Slider> SliderTemplate = new((slider, scope) =>
    {
        // At rest a white 18 x 14 capsule; while dragged (1-settings-tour.mov 128-131 s) it grows to about 23 x 20 and
        // turns into translucent glass with the fill showing through (over 0.08 s, back over 0.07 s: the held fraction below). The shadow and the glass alphas/border below are UNVERIFIED (estimated from the frames).
        // Keyboard focus (:focus-visible, so a click does not show it) rings the knob as the Windows focus visual does
        // (2 pt, primary text; UNVERIFIED, recalled, as SystemButtons' ring). Disabled, the whole slider dims to 40 % as the
        // other port controls do (UNVERIFIED for NSSlider).
        var face = new Border { CornerRadius = new CornerRadius(7), Background = Brushes.White, BoxShadow = BoxShadows.Parse("0 0.5 2 0 #40000000") };
        var ring = new Border { Margin = new Thickness(-3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), IsVisible = false, IsHitTestVisible = false };
        ring.BorderBrush = Marrow.UI.Chrome.Chrome.PrimaryText.Brush(ring);
        var knob = new Thumb
        {
            Name = "PART_Thumb",
            Template = new FuncControlTemplate<Thumb>((_, _) => new Panel { Children = { face, ring } }),
        };
        // The held knob (1-settings-tour.mov 128-131 s): the white 18 x 14 capsule turns into the translucent 23 x 20 glass one. ENGINEERING DECISION
        // (OD-0076, ED-280): it does so over 0.08 s on the press and back over 0.07 s on the release (the reference runtime: INDEX.md, 70 to
        // 90 ms and 60 to 80 ms), one fraction driving the size, the corner, the fill and the rim; reduce motion jumps.
        var held = new AnimatedScalar(knob, 0, fraction =>
        {
            var vertical = slider.Orientation == Avalonia.Layout.Orientation.Vertical;
            var along = 18 + 5 * fraction; var across = 14 + 6 * fraction;
            knob.Width = vertical ? across : along;
            knob.Height = vertical ? along : across;
            face.CornerRadius = new CornerRadius(7 + 3 * fraction);
            face.Background = fraction == 0 ? Brushes.White : new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 - (255 - 0x30) * fraction), 0xFF, 0xFF, 0xFF));
            face.BorderBrush = fraction == 0 ? null : new SolidColorBrush(Color.FromArgb((byte)Math.Round(0x55 * fraction), 0xFF, 0xFF, 0xFF));
            face.BorderThickness = fraction == 0 ? default : new Thickness(1);
        });
        Animation? Ramp(double seconds) => Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? null : Animation.EaseOut(seconds);
        knob.DragStarted += (_, _) => { held.Set(1, Ramp(0.08)); };
        knob.DragCompleted += (_, _) => { held.Set(0, Ramp(0.07)); };
        var decrease = SliderSide(slider, "PART_DecreaseButton", bar => Marrow.UI.Chrome.Chrome.Accent.Brush(bar)).RegisterInNameScope(scope);
        var increase = SliderSide(slider, "PART_IncreaseButton", bar => Marrow.UI.Chrome.Chrome.Overlay(0.12).Brush(bar)).RegisterInNameScope(scope);
        var track = new Track
        {
            Name = "PART_Track",
            [!Track.OrientationProperty] = slider[!Slider.OrientationProperty],
            [!Track.IsDirectionReversedProperty] = slider[!Slider.IsDirectionReversedProperty],
            Thumb = knob,
            DecreaseButton = decrease,
            IncreaseButton = increase,
            [!Track.MinimumProperty] = slider[!RangeBase.MinimumProperty],
            [!Track.MaximumProperty] = slider[!RangeBase.MaximumProperty],
            [!!Track.ValueProperty] = slider[!!RangeBase.ValueProperty],
        }.RegisterInNameScope(scope);
        var root = new Panel { Background = Brushes.Transparent, MinHeight = 14, Children = { track } };
        void Paint()
        {
            ring.IsVisible = slider.Classes.Contains(":focus-visible");
            root.Opacity = slider.IsEffectivelyEnabled ? 1 : 0.4;
        }
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty || e.Property == Avalonia.Input.InputElement.IsFocusedProperty) Paint();
            if (e.Property == Slider.OrientationProperty) held.Set(held.Value, null);
        };
        slider.Classes.CollectionChanged += (_, _) => Paint();
        Paint();
        return root;
    });

    /// <summary>The Slider default theme: the measured small slider, click-to-position.</summary>
    public static ControlTheme SliderTheme { get; } = new(typeof(Slider))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, SliderTemplate) }, // Avalonia's Slider moves to a clicked point itself
    };

    /// <summary>Registers the default themes on the application, after setup (app and test builders alike).</summary>
    public static AppBuilder WithPortControlThemes(this AppBuilder builder) =>
        builder.AfterSetup(setup =>
        {
            Register(setup.Instance!);
            NativeContextMenus.Install(); // context menus as the system menu (NativeContextMenus.cs)
        });

    /// <summary>Puts the default themes into <paramref name="application"/>'s resources, keyed by control type.</summary>
    public static void Register(Application application)
    {
        application.Resources[typeof(ScrollViewer)] = ScrollViewerTheme;
        SoftScroll.Install(); // OD-0071 (ED-246, ED-NEW-softscroll-1): every ScrollViewer scrolls softly unless it opted out
        application.Resources[typeof(ProgressBar)] = ProgressBarTheme;
        application.Resources[typeof(ToolTip)] = ToolTipTheme;
        // ED-NEW-T45: every .help site uses a long dwell, including after a previous tag.
        application.Styles.Add(new Style(selector => selector.Is<Control>())
        {
            Setters = { new Setter(ToolTip.ShowDelayProperty, 1500), new Setter(ToolTip.BetweenShowDelayProperty, 0) },
        });
        application.Resources[typeof(Slider)] = SliderTheme;
        RegisterPrimaryTextDefault(application);
        GreyscaleText.Register(application); // OD-0099 Q-TEXT A: text is greyscale everywhere (GreyscaleText.cs)
        PopoverHost.Register(application); // the panel a presented popover's top-level draws (PopoverHost.cs, ED-238)
        Marrow.UI.Common.PopupZoom.Install(); // ED-NEW-textsize-1: every popup at the app zoom of what it hangs from
    }

    // SwiftUI draws a Text, and any glyph, with no foreground style of its own in `.primary`: labelColor, which follows
    // the appearance (SwiftColor.LabelColor, ED-051: black 0.85 light, white 0.85 dark). Avalonia's inherited
    // TextElement.Foreground defaults to opaque black whatever the theme, and with no theme package nothing replaced it,
    // so every such text was black on the dark window (found 2026-10-01 rendering the user bubble beside
    // 2-chat-use.mov at 32 s: the bubble's words black, the video's light). Every TopLevel now starts the inheritance at
    // labelColor for its own theme; a site that sets a colour keeps it, and popups inherit it from their owner.
    internal const string PrimaryTextKey = "MarrowPort.PrimaryText";

    private static void RegisterPrimaryTextDefault(Application application)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!application.Resources.ThemeDictionaries.TryGetValue(variant, out var provider))
                application.Resources.ThemeDictionaries[variant] = provider = new ResourceDictionary();
            ((IResourceDictionary)provider)[PrimaryTextKey] = new SolidColorBrush(SwiftColor.LabelColor.Resolve(variant));
        }
        application.Styles.Add(new Style(selector => selector.Is<TopLevel>())
        {
            Setters = { new Setter(TextElement.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(PrimaryTextKey)) },
        });
    }
}

/// <summary>PORT-ONLY. Which nth drawing a plain ProgressBar gets (see PortControlThemes' ProgressView note).</summary>
internal static class NthBusyDrawing
{
    /// <summary>A bar this thin, or this much wider than tall, is a strip, not a round busy mark.</summary>
    public const double StripMaxHeight = 6, StripAspect = 2.5;

    /// <summary>The drawing for <paramref name="bar"/> as it is now.</summary>
    public static Control For(ProgressBar bar)
    {
        if (!bar.IsIndeterminate)
        {
            var known = Strip(bar);
            known.Jump(Fraction(bar));
            return known;
        }
        if (IsStrip(bar))
        {
            var strip = Strip(bar);
            strip.IsIndeterminate = true;
            return strip;
        }
        // the spinner keeps the bar's own size; a bar with none is the small 16 pt busy indicator
        return new NthSpinner { Width = bar.Width, Height = bar.Height, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Whether an unknown-length bar of this shape is drawn as a strip.</summary>
    public static bool IsStrip(ProgressBar bar)
    {
        var height = bar.Height;
        if (double.IsNaN(height)) return false;
        return height <= StripMaxHeight || (!double.IsNaN(bar.Width) && bar.Width >= height * StripAspect);
    }

    private static NthProgressBar Strip(ProgressBar bar) =>
        new() { Height = !double.IsNaN(bar.Height) && bar.Height <= 3 ? 3 : 4, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The bar's value as 0 to 1 over its Minimum and Maximum.</summary>
    public static double Fraction(ProgressBar bar)
    {
        var span = bar.Maximum - bar.Minimum;
        return span <= 0 ? 0 : Math.Clamp((bar.Value - bar.Minimum) / span, 0, 1);
    }
}
