// PORT-ONLY, owner-ruled (OD-0091, OD-0086 and its amendment; first-release voice typing, VT-10, voice-SPEC V-14.1): the attachment button, moved out of ComposerControls
// (ComposerView.swift:626-646) to the composer's left. Everything it does is the source's: the paperclip (Chrome.iconFont) on the chip style, the tooltip and name
// `Attach files`, the recent-downloads popover below it or the file chooser (:628-631, :637-644). Only its place and its plate are the port's.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

/// <summary>Where the attachment button is (<c>voiceOrb.attach</c>, V-14.1).</summary>
internal enum ComposerAttachPlace { Inside, Outside }

/// <summary>
/// The attachment button in one of its two places. <see cref="ComposerAttachPlace.Inside"/>: 34 by 28 (radius 14), the chip. <see cref="ComposerAttachPlace.Outside"/>:
/// a 36 by 36 circle on a faint plate (the design's <c>turn</c> fill) with a 1 px ring (<c>winedge</c>), the chip's hover and active plate over it. Both places share the
/// recents binding; the popover belongs to the one that is <see cref="Active"/> (the visible one), so switching the place moves an open popover to the button now showing.
/// </summary>
internal sealed class ComposerAttachButton : Decorator
{
    private sealed class ActiveState : MainActorModel
    {
        private bool active;
        public bool Active { get => Read(active); set => Set(ref active, value); }
    }

    private readonly ActiveState state = new();
    private readonly ScaleTransform scale = new(1, 1);

    /// <summary>The button.</summary>
    public Button Button { get; }

    public ComposerAttachPlace Place { get; }

    /// <summary>:626-629 whether a click shows the recent downloads (the setting) rather than the file chooser; the composer hands the current value in.</summary>
    public bool RecentDownloadsPicker { get; set; }

    public ComposerAttachButton(
        ComposerAttachPlace place,
        bool recentDownloadsPicker,
        Binding<bool> showingRecents,
        Action<IReadOnlyList<string>> onAttach,
        Action onChooseFiles)
    {
        Place = place;
        RecentDownloadsPicker = recentDownloadsPicker;
        var inside = place == ComposerAttachPlace.Inside;
        var icon = SwiftImage.SystemName("paperclip").Font(Chrome.IconFont); // :634-635
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.UseLayoutRounding = false; // T46: keep the symbol's fractional natural size centred inside the unchanged button
        Button = PlainControls.Button(icon, () =>
        {
            if (RecentDownloadsPicker) showingRecents.Value = !showingRecents.Value; // :628-629
            else onChooseFiles(); // :631
        });
        Button.Chip(showingRecents.Value); // :637 .buttonStyle(.chip(active: showingRecents)).
        Button.Focusable = false; // :638
        Button.Width = inside ? 34 : 36; // V-14.1
        Button.Height = inside ? 28 : 36;
        ToolTip.SetTip(Button, "Attach files"); // :639
        AutomationProperties.SetName(Button, "Attach files"); // :640
        var popup = new Popup { PlacementTarget = Button, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true }; // :641
        var presented = false;
        Button.Track(() => (Shown: showingRecents.Value, state.Active), now =>
        {
            Button.Chip(now.Shown); // :637 read the same binding as the popover, including external dismissal.
            var shows = now.Shown && now.Active; // the popover belongs to the button that is showing
            if (shows == presented) return;
            presented = shows;
            if (shows && popup.Child is null) popup.Child = new DownloadsPopover(
                pick: path => { showingRecents.Value = false; onAttach([path]); }, // :643
                chooseOther: () => { showingRecents.Value = false; onChooseFiles(); }); // :644
            Presentation.Presenter.Current.SetPresented(popup, shows);
        });
        popup.Closed += (_, _) => { popup.Child = null; if (presented && showingRecents.Value) showingRecents.Value = false; };
        DetachedFromVisualTree += (_, _) => { if (presented) Presentation.Presenter.Current.SetPresented(popup, false); };
        state.Active = true;
        if (inside)
        {
            Child = Button;
        }
        else
        {
            // the plate behind the chip: the faint fill and the 1 px ring of the design's `.attach.out`
            var plate = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false }.Capsule(RoundedCornerStyle.Circular);
            plate.Track(() => (Plate, Ring), colors =>
            {
                plate.Background = colors.Plate.Brush(plate);
                plate.BorderBrush = colors.Ring.Brush(plate);
            });
            Child = new Grid { Children = { plate, Button } };
        }
        RenderTransform = scale;
        RenderTransformOrigin = RelativePoint.Center;
    }

    // `turn` and `winedge` of voice-composer.html, light then dark
    private static SwiftColor Plate => SwiftColor.Dynamic(new SwiftColor(0, 0.035), new SwiftColor(1, 0.05));

    private static SwiftColor Ring => SwiftColor.Dynamic(new SwiftColor(0, 0.12), new SwiftColor(1, 0.10));

    /// <summary>
    /// How much of the button is showing, 0 to 1: its opacity, and its scale, <paramref name="scaleAmount"/> 0 to 1 for the design's 0.6 to 1 (the hidden states of
    /// <c>.attach.in</c> and <c>.attach.out</c>). A button that is nearly hidden takes no pointer and gives the popover up.
    /// </summary>
    public void Show(double amount, double scaleAmount)
    {
        Opacity = amount;
        scale.ScaleX = scale.ScaleY = 0.6 + 0.4 * scaleAmount;
        IsHitTestVisible = amount > 0.5;
        state.Active = amount > 0.5;
    }
}
