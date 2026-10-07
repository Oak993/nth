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
using Marrow.Core.Port.Skills.Terms;
using Marrow.Services.Port.Skills.Offers;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills;

/// <summary>
/// The offer card, a panel in the reply's own layout (never an overlay, so it cannot cover the composer's text). It reads <see cref="IOfferCardSource"/> by observation and draws
/// <see cref="OfferCardProjector"/>'s view: the item titles and their source host, then one action line per state in the words and controls the locked design draws for the same states on the Settings page
/// (Download with its size, the bar with its percent and a cancel, Checking…, Didn't download with its reason and Retry). ENGINEERING DECISIONS (ED-384): (1) the layout, spacing and tint are the
/// Settings page's (the Offer A card's own geometry is not in the repository's design sources); (2) every change of state crossfades on the frame clock and lands in a short fade under Reduce Motion (OD-0076, the
/// pages' own helpers); (3) done, continuing and declined collapse the card (after their fade); their one caption line is <see cref="OfferCaptionControl"/>'s, drawn right after the card; (4) the asked sentence, the why-needed line and the
/// decline control are LEAD-PROPOSED WORDING (OfferWords, ED-384 item 16); (5) Return acts only when the card itself has the focus and its primary action is on, so Return in the composer is the
/// composer's. Mounted inline in the reply: at the end of the timeline's rows (Offer A, OD-0099).
/// </summary>
public sealed class OfferCardControl : Border
{
    private readonly IOfferCardSource source;
    private readonly SkillsCatalogue catalogue;
    private readonly Func<AddonsState> state;
    private CrossfadeSlot slot = new();
    private readonly CrossfadeSlot whole = new() { ClipToBounds = true };
    private TextBlock title = LibraryUi.Text("", 13, weight: SwiftFont.Weight.Semibold, trim: true);
    private TextBlock host = LibraryUi.Text("", 11.5, secondary: true, trim: true);
    private string? kind;
    private string? contentKey;
    private OfferCardView? view;
    private LibraryBar? bar;
    private TextBlock? percent;
    private TextBlock? failure;
    private Button? primary;
    private Button? cancel;
    private Button? decline;
    private TextBlock? sentence;
    private TextBlock? why;
    private CrossfadeSlot? transfer;
    private Marrow.UI.Port.Settings.CzCrossfadeText? bytes;
    private bool knownTotal;

    public OfferCardControl(IOfferCardSource source, SkillsCatalogue catalogue, Func<AddonsState> state)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        Child = whole;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, "Offer");
        KeyDown += OnKeyDown;
        this.Track(() => (source.Stage, source.Card, source.Progress, source.Failure, source.CanFetch, source.TermsRevision, source.Why), _ => Refresh());
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

    internal TextBlock? SentenceText => sentence;

    internal TextBlock? WhyText => why;

    internal Button? DeclineButton => decline;

    /// <summary>
    /// The curve of a change of state: the pages' fade, and under Reduce Motion the same opacity fade shortened to the locked page's reduced-motion rule (0.01 s: <c>transition-duration:.01s</c>), because movement is
    /// stilled to fades and not removed (ONDEMAND 5.6). Null only when no frame source is bound (nothing could animate).
    /// </summary>
    private Animation? Fade() => !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(duration: 0.01) : LibraryUi.Fade;

    private void Refresh()
    {
        view = OfferCardProjector.Project(catalogue, state(), source.Card, source.Stage, source.Progress, source.Failure, source.CanFetch, source.Why);
        var next = view?.Phase switch
        {
            OfferStage.Asked => "asked",
            OfferStage.Downloading => "downloading",
            OfferStage.Checking => "checking",
            OfferStage.Failed => "failed",
            _ => "none",
        };
        var newCard = next != "none" && kind is null or "none";
        if (next != "none") IsVisible = true;
        if (newCard)
        {
            // A returning offer gets its own tree. The previous card may still be fading; no control is reparented out of that animation.
            title = LibraryUi.Text("", 13, weight: SwiftFont.Weight.Semibold, trim: true);
            host = LibraryUi.Text("", 11.5, secondary: true, trim: true);
            slot = new CrossfadeSlot();
            var column = new StackPanel { Spacing = 8, Children = { title, host, slot } };
            var surface = new Border { Child = column, Padding = new Thickness(12, 9, 12, 10) }.RoundedRectangle(10, RoundedCornerStyle.Continuous);
            surface.Background = ChromeTokens.Overlay(0.06).Brush(surface);
            whole.Show(surface, contentKey is null ? null : Fade());
        }
        if (view is not null)
        {
            title.Text = string.Join(", ", view.Titles);
            host.Text = string.Join(", ", view.Hosts);
            host.IsVisible = view.Hosts.Count > 0;
        }
        // What is on show changes with the state and, within asked and failed, with the button's words (a need that joined changes the combined size) and the note: each is a transitioned swap.
        var key = next + "|" + (view?.PrimaryLabel ?? "") + "|" + (view?.Note ?? "") + "|" + (view?.Sentence ?? "") + "|" + (view?.Why ?? "") + "|" + string.Join(",", view?.OwedItems ?? []);
        if (key != contentKey)
        {
            var first = contentKey is null;
            contentKey = key;
            kind = next;
            bar = null; percent = null; failure = null; primary = null; cancel = null; decline = null; sentence = null; why = null; transfer = null; bytes = null;
            if (next == "none")
            {
                whole.Show(new Border { Height = 0 }, first ? null : Fade());
                HideWhenSettled();
            }
            else slot.Show(Build(next), first || newCard ? null : Fade());
        }
        if (view is not null)
        {
            if (transfer is not null)
            {
                var known = view.Percent is not null;
                if (known != knownTotal)
                {
                    knownTotal = known;
                    transfer.Show(TransferFace(known), Fade());
                }
            }
            if (bar is not null) bar.Set(Fraction(view), animated: true);
            if (percent is not null) percent.Text = view.Percent is { } number ? number + "%" : "";
            if (bytes is not null)
            {
                var done = OfferTransferWords.Bytes(view.BytesDone ?? 0);
                bytes.SetText(view.BytesTotal is > 0 and { } total ? done + " of " + OfferTransferWords.Bytes(total) : done);
            }
            if (failure is not null) failure.Text = view.Failure ?? "";
            if (primary is not null) primary.IsEnabled = view.PrimaryEnabled;
        }
    }

    /// <summary>A card that has nothing left to say fades and glides to no height first, and only then leaves the layout (the composer below it moves with the glide, never in one frame).</summary>
    private void HideWhenSettled()
    {
        if (!FrameClock.IsBound || !whole.Settling) { IsVisible = false; return; }
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (kind != "none") return; // a new offer came back while it faded
            if (whole.Settling) { frames.RequestFrame(tick!); return; }
            IsVisible = false;
        };
        frames.RequestFrame(tick);
    }

    private TermsBox TermsFor(string itemId)
    {
        var item = catalogue.Items.Single(i => i.Id == itemId);
        var offer = source.TermsFor(itemId);
        return new TermsBox(offer?.Label ?? SkillsNotices.UnderTerms(item.Vendor), offer?.LicenceName ?? item.LicenceName, true, () => source.LicenceText(itemId), () => source.AcceptTerms(itemId));
    }

    private static double Fraction(OfferCardView view) => view.BytesTotal is > 0 and { } total ? Math.Clamp((double)(view.BytesDone ?? 0) / total, 0, 1) : 0;

    private Control TransferFace(bool known)
    {
        if (known) return bar = new LibraryBar { Width = 150 };
        bar = null;
        return new SystemSpinner { Width = 14, Height = 14 };
    }

    private Control Build(string what)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        void Add(Control control) { control.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(control); }
        switch (what)
        {
            case "asked":
                primary = LibraryButtons.Make(view!.PrimaryLabel!, () => _ = source.Accept(), ButtonLook.Primary, "offer-primary");
                Add(primary);
                AddDecline(Add);
                if (view.Note is { } askedNote) Add(LibraryUi.Text(askedNote, 11.5, secondary: true, trim: true));
                var asked = new StackPanel { Spacing = 6 };
                AddWords(asked);
                asked.Children.Add(line);
                if (view.OwedItems is { Count: > 0 } owed)
                {
                    // ONDEMAND 5.4: the terms step stays inside asked. Each item that owes terms shows the Settings page's own box; the Download waits for the acceptances.
                    var boxes = new StackPanel { Spacing = 8, Children = { asked } };
                    foreach (var owedId in owed) boxes.Children.Add(TermsFor(owedId));
                    return boxes;
                }
                return asked;
            case "downloading":
                knownTotal = view!.Percent is not null;
                transfer = new CrossfadeSlot { Width = 150 }; // rounds/skills-downloads-v3-KBarrEq1.html:342 `.mw .ofs .bar{width:150px}`
                transfer.Show(TransferFace(knownTotal), null);
                percent = LibraryUi.Text("", 12.5, secondary: true);
                percent.MinWidth = 34;
                cancel = LibraryButtons.Glyph("xmark", Marrow.Core.Port.Skills.Pages.LibraryWords.Cancel, "offer-cancel", source.Cancel);
                Add(transfer);
                Add(percent);
                Add(cancel);
                bytes = new Marrow.UI.Port.Settings.CzCrossfadeText("") { FontSize = 11.5, Secondary = true };
                return new StackPanel { Spacing = 4, Children = { line, bytes } };
            case "checking":
                Add(new SystemSpinner { Width = 14, Height = 14 });
                Add(LibraryUi.Text(view!.Status!, 12.5, secondary: true));
                break;
            case "failed":
                Add(LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning));
                Add(LibraryUi.Text(view!.Status!, 12.5));
                primary = LibraryButtons.Make(view.PrimaryLabel!, () => _ = source.Accept(), ButtonLook.Accent, "offer-retry");
                Add(primary);
                AddDecline(Add);
                if (view.Note is { } failedNote) Add(LibraryUi.Text(failedNote, 11.5, secondary: true, trim: true));
                failure = LibraryUi.Text(view.Failure ?? "", 11.5, secondary: true, wrap: true);
                return new StackPanel { Spacing = 4, Children = { line, failure } };
            default:
                return new Border { Height = 0 };
        }
        return line;
    }

    /// <summary>LEAD-PROPOSED WORDING (OfferWords, ED-384 item 16): the asked sentence, then the why-needed line when there is one.</summary>
    private void AddWords(StackPanel into)
    {
        sentence = LibraryUi.Text(view!.Sentence ?? "", 12.5, weight: SwiftFont.Weight.Medium, wrap: true);
        into.Children.Add(sentence);
        if (view.Why is not { } reason) return;
        why = LibraryUi.Text(reason, 11.5, secondary: true, wrap: true);
        into.Children.Add(why);
    }

    private void AddDecline(Action<Control> add)
    {
        if (view!.DeclineLabel is not { } label) return;
        decline = LibraryButtons.Make(label, source.Decline, ButtonLook.Plain, "offer-decline");
        add(decline);
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
