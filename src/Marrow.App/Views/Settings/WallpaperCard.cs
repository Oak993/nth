using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Automation;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/WallpaperCard.swift (B3 52bc7926), :1-190.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`. `let store = WallpaperStore.shared`
// (:8) has its value in the declaration, so it is not an input; WallpaperStore is Marrow.UI.Theme.WallpaperStore
// (ED-195, lane A10: B3 fixes the layering that used to keep it in Marrow.App, HANDOFF section 9).

/// <summary>:7-190 <c>struct WallpaperCard: View</c>: a picture of the user's own behind the glass.</summary>
public sealed class WallpaperCard : Decorator
{
    private const double PreviewHeight = 150; // :33, :63, :83, :87

    // :8 `let store = WallpaperStore.shared`.
    private readonly WallpaperStore store = WallpaperStore.Shared.Current;

    // :9 `@State private var isTargeted = false`. R-VIEW-2: @State read by body -> a private ViewState.
    private sealed class ViewState : MainActorModel
    {
        private bool isTargeted;

        public bool IsTargeted { get => Read(isTargeted); set => Set(ref isTargeted, value); }
    }

    private readonly ViewState state = new();

    // :11 `private var isOffStock: Bool { store.softness > 0.005 }`.
    private bool IsOffStock => store.Softness > 0.005;

    // :13-23 body: ChromeCard { preview; ChromeRowDivider(); wallpaperRow; if store.hasWallpaper { divider; softnessRow } }
    public WallpaperCard() => Child = new ChromeCard(Body);

    private Control Body()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        stack.Children.Add(Preview()); // :15
        stack.Children.Add(new ChromeRowDivider()); // :16
        stack.Children.Add(WallpaperRow()); // :17

        // :18-21 `if store.hasWallpaper { ChromeRowDivider(); softnessRow }`: R-VIEW-1, an if branch is a separate
        // control, added and removed entirely rather than merely hidden (PR-THEME-075 windows_path: "absent
        // entirely, not disabled").
        ChromeRowDivider? softDivider = null;
        Control? softRow = null;
        void SyncSoftnessRow()
        {
            if (store.HasWallpaper)
            {
                if (softRow is not null) return;
                softDivider = new ChromeRowDivider();
                softRow = SoftnessRow();
                stack.Children.Add(softDivider);
                stack.Children.Add(softRow);
            }
            else if (softRow is not null)
            {
                stack.Children.Remove(softDivider!);
                stack.Children.Remove(softRow);
                softDivider = null;
                softRow = null;
            }
        }
        SyncSoftnessRow();
        stack.Track(() => store.HasWallpaper, _ => SyncSoftnessRow());
        return stack;
    }

    // :25-102 preview: a plain Button opening the picture chooser, holding the picture-or-invitation ZStack, an
    // install spinner and a drag highlight, accepting a dropped picture.
    private Control Preview()
    {
        var content = new Panel
        {
            Height = PreviewHeight, // :87
            HorizontalAlignment = HorizontalAlignment.Stretch, // :88
            Background = Brushes.Transparent, // :94 .contentShape(.rect): the whole area, including empty space, hit-tests
        };

        var swap = new Panel(); // :27 ZStack's picture/empty branch
        content.Children.Add(swap);

        Control? pictureBranch = null;
        Control? emptyBranch = null;
        var initialized = false;
        void InsertBranch(Control branch)
        {
            branch.Transition(SwiftTransition.Scale(0.96).Combined(SwiftTransition.Opacity)
                .Animation(Animation.Spring(0.45, 0.2))); // :91 overrides the store's transaction in both directions.
            if (initialized) SwiftTransition.Insert(swap, 0, branch);
            else swap.Children.Insert(0, branch); // .animation(value:) does not animate initial presentation.
            initialized = true;
        }
        void SyncBranch()
        {
            if (store.Image is not null)
            {
                if (pictureBranch is not null) return;
                if (emptyBranch is { } gone)
                {
                    SwiftTransition.Remove(swap, gone);
                    emptyBranch = null;
                }
                pictureBranch = BuildPictureBranch(content); // :28-59
                InsertBranch(pictureBranch); // :59
            }
            else
            {
                if (emptyBranch is not null) return;
                if (pictureBranch is { } gone)
                {
                    SwiftTransition.Remove(swap, gone);
                    pictureBranch = null;
                }
                emptyBranch = BuildEmptyBranch(); // :60-75
                InsertBranch(emptyBranch); // :74
            }
        }
        SyncBranch();
        content.Track(() => store.Image is not null, _ => SyncBranch());

        // :76-79 `if store.isInstalling { ProgressView().controlSize(.small) }`
        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = store.IsInstalling,
        };
        content.Children.Add(progress);
        content.Track(() => store.IsInstalling, v => progress.IsVisible = v);

        // :80-85 `if isTargeted { RoundedRectangle(...).fill(Chrome.accent.opacity(0.18)) }.transition(.opacity)`
        Control? highlight = null;
        void SyncHighlight()
        {
            if (state.IsTargeted)
            {
                if (highlight is not null) return;
                var rect = new Border { Height = PreviewHeight, HorizontalAlignment = HorizontalAlignment.Stretch };
                rect.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :81
                rect.Background = Chrome.Accent.Opacity(0.18).Brush(rect); // :82
                highlight = rect;
                SwiftTransition.Insert(content, content.Children.Count,
                    highlight.Transition(SwiftTransition.Opacity.Animation(Animation.Snappy(0.2)))); // :84,90
            }
            else if (highlight is { } gone)
            {
                SwiftTransition.Remove(content, gone);
                highlight = null;
            }
        }
        SyncHighlight();
        content.Track(() => state.IsTargeted, _ => SyncHighlight());

        // :89-91 scaleEffect(isTargeted ? 1.02 : 1), snappy(0.2)
        var scale = new ScaleTransform(1, 1);
        content.RenderTransform = scale;
        content.RenderTransformOrigin = RelativePoint.Center;
        // Driven through the port's own FrameClock seam (ED-117), not a native Avalonia Transition, so a test
        // settles it the same way every other animation in this tree settles (WallpaperCardScope.Settle).
        var scaleValue = 1.0;
        IFrameSource? scaleSource = null;
        Action<TimeSpan>? scaleTick = null;
        void AnimateScale(double to)
        {
            var animation = Animation.Snappy(0.2); // :90
            var from = scaleValue;
            var start = SwiftClock.Now;
            var span = TimeSpan.FromSeconds(animation.Duration);
            if (scaleSource is not null && scaleTick is not null) scaleSource.CancelFrame(scaleTick);
            var frames = FrameClock.Source.Current;
            void Tick(TimeSpan _)
            {
                var now = SwiftClock.Now;
                // ED-117 (2): a span since `start` is complete at a frame when (now - start) + 1ms >= span.
                var complete = (now - start).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks;
                scaleValue = complete ? to : from + (to - from) * animation.Progress((now - start).TotalSeconds);
                scale.ScaleX = scale.ScaleY = scaleValue;
                if (!complete) frames.RequestFrame(Tick);
            }
            scaleSource = frames;
            scaleTick = Tick;
            frames.RequestFrame(Tick);
        }
        content.Track(() => state.IsTargeted, targeted => AnimateScale(targeted ? 1.02 : 1)); // :89-90

        var button = PlainControls.Button(content, Choose); // :26, :93 buttonStyle(.plain)

        // :95-99 dropDestination(for: URL.self, ...)
        DragDrop.SetAllowDrop(button, true);
        button.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            state.IsTargeted = true; // :99 isTargeted: { isTargeted = $0 }
            e.DragEffects = DragDropEffects.Copy;
        });
        button.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        });
        button.AddHandler(DragDrop.DragLeaveEvent, (_, _) => state.IsTargeted = false);
        button.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            state.IsTargeted = false;
            string? accepted = null;
            foreach (var item in e.DataTransfer.Items)
            {
                if (item.TryGetFile() is not { } file || file.TryGetLocalPath() is not { } path) continue;
                if (!WallpaperStore.Accepts(path)) continue;
                accepted = path; // :96 urls.first(where: WallpaperStore.accepts)
                break;
            }
            if (accepted is null)
            {
                e.Handled = false; // :96 `else { return false }`
                return;
            }
            store.Install(accepted); // :97
            e.Handled = true; // :98
        });

        var wrapper = new Border { Child = button, Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Stretch }; // :100-101
        return wrapper;
    }

    // :28-34, :59 the picture branch, and :35-58 the mock-window overlay.
    private Control BuildPictureBranch(Panel previewFrame)
    {
        var image = new Avalonia.Controls.Image
        {
            Stretch = Stretch.UniformToFill, // :31 aspectRatio(contentMode: .fill)
            Height = PreviewHeight, // :33
            HorizontalAlignment = HorizontalAlignment.Stretch, // :32
        };
        // :28-29 the outgoing picture keeps the image it had resolved while it scales and fades out (:59 transition).
        image.Track(() => store.Image, bitmap => { if (bitmap is not null) image.Source = bitmap; });
        var clipped = new Border { Child = image, ClipToBounds = true, Height = PreviewHeight };
        clipped.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :34

        var mock = MockWindow(previewFrame); // :35-58
        return new Panel { Children = { clipped, mock } };
    }

    // :35-58 `GeometryReader { geo in ... }`: a stand-in for the app window, 60% of the preview's size, centred.
    private static Control MockWindow(Panel previewFrame)
    {
        // :39-42 `mockShape.fill(.clear).glassEffect(in: mockShape)`, drawn under the source black overlay (OD-0005).
        var glass = new Border { Background = Brushes.Transparent, IsHitTestVisible = false };
        glass.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(8, RoundedCornerStyle.Continuous)));

        var wash = new Border { IsHitTestVisible = false }; // :43 .overlay { mockShape.fill(Color.black.opacity(0.2)) }
        wash.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        wash.Background = SwiftColor.Black.Opacity(0.2).Brush(wash);

        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4, // :45
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 8, 0, 0), // :52-53 padding(.top, 8), padding(.leading, 8)
            IsHitTestVisible = false,
        };
        for (var i = 0; i < 3; i++) // :46 ForEach(0..<3, id: \.self)
        {
            var dot = new Ellipse { Width = 5, Height = 5 }; // :49
            dot.Fill = Chrome.Overlay(0.35).Brush(dot); // :48
            dots.Children.Add(dot);
        }

        var mock = new Panel
        {
            Children = { glass, wash, dots },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, // :26-93 clicks pass to the well (UI-THEME-014)
        };

        void Resize()
        {
            mock.Width = previewFrame.Bounds.Width * 0.6; // :55
            mock.Height = previewFrame.Bounds.Height * 0.6;
        }
        Resize();
        previewFrame.SizeChanged += (_, _) => Resize();
        return mock;
    }

    // :60-74 the empty state.
    private Control BuildEmptyBranch()
    {
        var plate = new Border { Height = PreviewHeight, HorizontalAlignment = HorizontalAlignment.Stretch }; // :63
        plate.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :61
        plate.Background = Chrome.Overlay(0.04).Brush(plate); // :62

        var symbol = SwiftImage.SystemName("photo.on.rectangle.angled") // :66
            .Font(SwiftFont.System(SwiftFont.TextStyle.Title)) // :67
            .ForegroundStyle(Chrome.SecondaryText); // :68
        var text = new TextBlock { Text = "Drop a picture here, or choose one" }; // :69
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(text); // :70
        text.Foreground = Chrome.SecondaryText.Brush(text); // :71

        var invite = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6, // :65
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { symbol, text },
        };
        plate.Child = invite; // :64-72 .overlay { VStack { ... } }
        return plate;
    }

    // :104-124 wallpaperRow: title "Wallpaper", detail the error or one of two sentences, trailing Remove/Choose.
    private Control WallpaperRow()
    {
        string Detail() => store.LastError ?? (store.HasWallpaper // :107
            ? "Fills the window edge to edge under the glass; the transparency slider lets the desktop show through it" // :108
            : "A picture of your own behind the glass, mixed with the desktop by the transparency slider"); // :109

        var hstack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; // :111
        var changeLabel = new TextBlock();
        var changeButton = GlassTextButton(changeLabel, Choose); // :118
        Button? removeButton = null;

        void SyncButtons()
        {
            var has = store.HasWallpaper;
            if (has && removeButton is null)
            {
                removeButton = GlassTextButton(new TextBlock { Text = "Remove" }, store.Remove); // :113
                hstack.Children.Insert(0, removeButton);
            }
            else if (!has && removeButton is not null)
            {
                hstack.Children.Remove(removeButton);
                removeButton = null;
            }
            changeLabel.Text = has ? "Change…" : "Choose…"; // :118
            var installing = store.IsInstalling;
            changeButton.IsEnabled = !installing; // :121
            if (removeButton is not null) removeButton.IsEnabled = !installing; // :116
        }
        hstack.Children.Add(changeButton);
        SyncButtons();
        hstack.Track(() => (store.HasWallpaper, store.IsInstalling), _ => SyncButtons());

        var row = new ChromeRow("Wallpaper", () => hstack, Detail()); // :105-106
        UpdateDetail(row, Detail());
        row.Track(Detail, text => UpdateDetail(row, text));
        return row;
    }

    /// <summary>
    /// ChromeRow builds its title and (when non-empty) detail TextBlocks once, in that order, and exposes neither
    /// afterward; the wallpaper row's detail changes with the store, so this finds the second TextBlock the row
    /// built (the first is the fixed title "Wallpaper") and updates it in place. ENGINEERING DECISION: reusing
    /// Chrome.ChromeRow (shared by every other settings row) rather than duplicating its layout here.
    /// </summary>
    private static void UpdateDetail(Control row, string text)
    {
        var blocks = row.GetLogicalDescendants().OfType<TextBlock>().ToList();
        if (blocks.Count >= 2) blocks[1].Text = text;
    }

    // :126-166 softnessRow: title "Softness", a fixed detail, a 140pt slider between two drop glyphs and a reset.
    private Control SoftnessRow()
    {
        Control BuildControl()
        {
            var sharp = SwiftImage.SystemName("drop") // :129
                .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline)) // :130
                .ForegroundStyle(Chrome.SecondaryText); // :131
            ToolTip.SetTip(sharp, "Sharp"); // :132

            var slider = new Slider { Minimum = 0, Maximum = 1, Width = 140, Value = store.Softness }; // :133-139
            slider.AddHandler(Thumb.DragStartedEvent,
                (object? _, VectorEventArgs _) => store.IsAdjustingSoftness = true, RoutingStrategies.Bubble); // :136
            slider.AddHandler(Thumb.DragCompletedEvent,
                (object? _, VectorEventArgs _) => store.IsAdjustingSoftness = false, RoutingStrategies.Bubble); // :136
            var settingFromStore = false;
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty || settingFromStore) return;
                store.Softness = slider.Value; // :134 set { store.softness = $0 }
            };
            slider.Track(() => store.Softness, v =>
            {
                if (slider.Value == v) return; // :133 the getter returns the store's value exactly
                settingFromStore = true;
                slider.Value = v;
                settingFromStore = false;
            });

            var blurred = SwiftImage.SystemName("drop.fill") // :140
                .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline)) // :141
                .ForegroundStyle(Chrome.SecondaryText); // :142
            ToolTip.SetTip(blurred, "Blurred"); // :143

            var glyphs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8, // :128
                Children = { sharp, slider, blurred },
            };

            var group = new Panel { Children = { glyphs } }; // :145 .overlay(alignment: .leading)
            AutomationProperties.SetName(group, "Wallpaper softness"); // :164

            Control? reset = null;
            void SyncReset()
            {
                if (IsOffStock) // :146
                {
                    if (reset is not null) return;
                    reset = BuildResetButton();
                    reset.Transition(SwiftTransition.Opacity.Combined(SwiftTransition.Offset(6))
                        .Animation(Animation.Snappy(0.2))); // :159, :162
                    // .animation(value: isOffStock) does not animate a branch already true when the row first appears.
                    if (resetInitialized) SwiftTransition.Insert(group, group.Children.Count, reset);
                    else group.Children.Add(reset);
                }
                else if (reset is { } gone)
                {
                    SwiftTransition.Remove(group, gone);
                    reset = null;
                }
                resetInitialized = true;
            }
            SyncReset();
            group.Track(() => IsOffStock, _ => SyncReset());
            return group;
        }

        return new ChromeRow("Softness", BuildControl, "Blurs the picture so text stays easy to read"); // :127
    }

    // :147-158 the reset control: sets softness to 0 with no animation wrapper, unlike the transparency reset.
    private Control BuildResetButton()
    {
        var icon = SwiftImage.SystemName("arrow.counterclockwise") // :150
            .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium)) // :151
            .ForegroundStyle(Chrome.SecondaryText); // :152
        var frame = new Border
        {
            Child = icon,
            Width = 22, // :153
            Height = 22,
            Background = Brushes.Transparent, // :154 .contentShape(.rect)
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-28, 0, 0, 0), // :158 .offset(x: -28)
        };
        var button = PlainControls.Button(frame, () => store.Softness = 0); // :147-149 buttonStyle(.plain), no withAnimation
        ToolTip.SetTip(button, "Back to sharp"); // :157
        return button;
    }

    /// <summary>A small glass text button (<c>.buttonStyle(.glass).controlSize(.small)</c>): Remove, Choose or Change.</summary>
    private static Button GlassTextButton(TextBlock label, Action action)
    {
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(label);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var padded = new Border
        {
            Child = label,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var glass = padded.ChromeGlassCapsule();
        return PlainControls.Button(glass, action);
    }

    // :168-189 choose(): the panel runs async against the key window so the sheet never blocks (:169).
    private void Choose()
    {
        var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window; // :176 NSApp.keyWindow
        MainActor.Spawn(async () =>
        {
            var path = await WallpaperFileDialog.Current.Current.Choose(owner);
            if (path is { } chosen) store.Install(chosen); // :178-179, :184-185
        });
    }
}
