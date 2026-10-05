// PORT-ONLY: first-release feature unit SK-14 (DL-18, skills-ONDEMAND-PLAN.md 5.4 to 5.6; OD-0099 Offer A / words S1; OD-0076). There is no Swift twin. The in-chat offer card, drawn from the states of an offer.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills;

/// <summary>
/// The offer card, a panel in the reply's own layout (never an overlay, so it cannot cover the composer's text). It reads <see cref="IOfferCardSource"/> by observation and draws
/// <see cref="OfferCardProjector"/>'s view: the item titles and their source host, then one action line per state in the words and controls the locked design draws for the same states on the Settings page
/// (Download with its size, the bar with its percent and a cancel, Checking…, Didn't download with its reason and Retry). ENGINEERING DECISIONS (ED-NEW-cloud-sk-14): (1) the layout, spacing and tint are the
/// Settings page's (the Offer A card's own geometry is not in the repository's design sources); (2) every change of state crossfades on the frame clock and lands at once under Reduce Motion (OD-0076, the
/// pages' own helpers); (3) done, continuing and declined draw nothing, because the design has no word for them: the card is collapsed; (4) the decline control is not drawn, because its label is not in the
/// design sources (the offer's Decline is still reachable by whoever mounts the card); (5) Return acts only when the card itself has the focus and its primary action is on, so Return in the composer is the
/// composer's. NOT MOUNTED in the chat's timeline: where the card sits in a reply is part of the missing design.
/// </summary>
public sealed class OfferCardControl : Border
{
    private readonly IOfferCardSource source;
    private readonly SkillsCatalogue catalogue;
    private readonly Func<AddonsState> state;
    private readonly CrossfadeSlot slot = new();
    private readonly TextBlock title = LibraryUi.Text("", 13, weight: SwiftFont.Weight.Semibold, trim: true);
    private readonly TextBlock host = LibraryUi.Text("", 11.5, secondary: true, trim: true);
    private string? kind;
    private string? contentKey;
    private OfferCardView? view;
    private LibraryBar? bar;
    private TextBlock? percent;
    private TextBlock? failure;
    private Button? primary;
    private Button? cancel;

    public OfferCardControl(IOfferCardSource source, SkillsCatalogue catalogue, Func<AddonsState> state)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        var column = new StackPanel { Spacing = 8 };
        column.Children.Add(title);
        column.Children.Add(host);
        column.Children.Add(slot);
        Child = column;
        Padding = new Thickness(12, 9, 12, 10);
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, "Offer");
        Background = ChromeTokens.Overlay(0.06).Brush(this);
        this.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        KeyDown += OnKeyDown;
        this.Track(() => (source.Stage, source.Card, source.Progress, source.Failure, source.CanFetch), _ => Refresh());
        Refresh();
    }

    /// <summary>"asked", "downloading", "checking", "failed" or "none" (nothing on screen).</summary>
    internal string Kind => kind ?? "none";

    internal OfferCardView? View => view;

    internal Button? Primary => primary;

    internal Button? CancelButton => cancel;

    internal Control? Bar => bar;

    internal TextBlock? PercentText => percent;

    internal TextBlock TitleText => title;

    internal TextBlock HostText => host;

    internal TextBlock? FailureText => failure;

    /// <summary>
    /// The curve of a change of state: the pages' fade, and under Reduce Motion the same opacity fade shortened to the locked page's reduced-motion rule (0.01 s: <c>transition-duration:.01s</c>), because movement is
    /// stilled to fades and not removed (ONDEMAND 5.6). Null only when no frame source is bound (nothing could animate).
    /// </summary>
    private Animation? Fade() => !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(duration: 0.01) : LibraryUi.Fade;

    private void Refresh()
    {
        view = OfferCardProjector.Project(catalogue, state(), source.Card, source.Stage, source.Progress, source.Failure, source.CanFetch);
        var next = view?.Phase switch
        {
            OfferStage.Asked => "asked",
            OfferStage.Downloading => "downloading",
            OfferStage.Checking => "checking",
            OfferStage.Failed => "failed",
            _ => "none",
        };
        if (next != "none") IsVisible = true;
        if (view is not null)
        {
            title.Text = string.Join(", ", view.Titles);
            host.Text = string.Join(", ", view.Hosts);
            host.IsVisible = view.Hosts.Count > 0;
        }
        // What is on show changes with the state and, within asked and failed, with the button's words (a need that joined changes the combined size) and the note: each is a transitioned swap.
        var key = next + "|" + (view?.PrimaryLabel ?? "") + "|" + (view?.Note ?? "");
        if (key != contentKey)
        {
            var first = contentKey is null;
            contentKey = key;
            kind = next;
            bar = null; percent = null; failure = null; primary = null; cancel = null;
            slot.Show(Build(next), first ? null : Fade());
            if (next == "none") HideWhenSettled();
        }
        if (view is not null)
        {
            if (bar is not null) bar.Set(Fraction(view), animated: true);
            if (percent is not null) percent.Text = (view.Percent ?? 0) + "%";
            if (failure is not null) failure.Text = view.Failure ?? "";
            if (primary is not null) primary.IsEnabled = view.PrimaryEnabled;
        }
    }

    /// <summary>A card that has nothing left to say fades and glides to no height first, and only then leaves the layout (the composer below it moves with the glide, never in one frame).</summary>
    private void HideWhenSettled()
    {
        if (!FrameClock.IsBound || !slot.Settling) { IsVisible = false; return; }
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (kind != "none") return; // a new offer came back while it faded
            if (slot.Settling) { frames.RequestFrame(tick!); return; }
            IsVisible = false;
        };
        frames.RequestFrame(tick);
    }

    private static double Fraction(OfferCardView view) => view.BytesTotal is > 0 and { } total ? Math.Clamp((double)(view.BytesDone ?? 0) / total, 0, 1) : 0;

    private Control Build(string what)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        void Add(Control control) { control.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(control); }
        switch (what)
        {
            case "asked":
                primary = LibraryButtons.Make(view!.PrimaryLabel!, () => _ = source.Accept(), ButtonLook.Primary, "offer-primary");
                Add(primary);
                if (view.Note is { } askedNote) Add(LibraryUi.Text(askedNote, 11.5, secondary: true, trim: true));
                break;
            case "downloading":
                bar = new LibraryBar { Width = 200 };
                percent = LibraryUi.Text("0%", 12.5, secondary: true);
                percent.MinWidth = 34;
                cancel = LibraryButtons.Glyph("xmark", Marrow.Core.Port.Skills.Pages.LibraryWords.Cancel, "offer-cancel", source.Cancel);
                Add(bar);
                Add(percent);
                Add(cancel);
                break;
            case "checking":
                Add(new SystemSpinner { Width = 14, Height = 14 });
                Add(LibraryUi.Text(view!.Status!, 12.5, secondary: true));
                break;
            case "failed":
                Add(LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning));
                Add(LibraryUi.Text(view!.Status!, 12.5));
                primary = LibraryButtons.Make(view.PrimaryLabel!, () => _ = source.Accept(), ButtonLook.Accent, "offer-retry");
                Add(primary);
                if (view.Note is { } failedNote) Add(LibraryUi.Text(failedNote, 11.5, secondary: true, trim: true));
                failure = LibraryUi.Text(view.Failure ?? "", 11.5, secondary: true, wrap: true);
                return new StackPanel { Spacing = 4, Children = { line, failure } };
            default:
                return new Border { Height = 0 };
        }
        return line;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Return is the primary action's only while the card itself has the focus: a focused button already clicks on Return, and Return typed in the composer never reaches this card.
        if (e.Key != Key.Enter || e.Handled || !ReferenceEquals(e.Source, this) || !IsFocused) return;
        if (primary is not { IsEffectivelyEnabled: true } button) return;
        e.Handled = true;
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
}
