// PORT-ONLY: first-release feature unit SK-14 (DL-18; OD-0099 Offer A, words S1). The one caption line an offer leaves in the reply once its card has collapsed: done, continuing or declined. There is no Swift twin.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Offers;
using Marrow.UI.Port;

namespace Marrow.App.Port.Skills;

/// <summary>
/// The caption (OfferWords.Done, Continuing, Declined: LEAD-PROPOSED WORDING, ED-384 item 16) of an offer that has ended. It reads the same <see cref="OfferCardProjector"/> view as the card and shows nothing for any
/// state that has no caption, so it is empty while an offer is open and goes when a new one is asked. It fades in on the frame clock like the card's states (a short fade under Reduce Motion) and fades out before it
/// leaves the layout. ENGINEERING DECISION: it shows while the flow still holds the ended offer; a later offer in the same chat replaces it (one offer at a time per chat, ONDEMAND 5.3).
/// </summary>
public sealed class OfferCaptionControl : Border
{
    private readonly IOfferCardSource source;
    private readonly SkillsCatalogue catalogue;
    private readonly CrossfadeSlot slot = new();
    private string? shown;
    private TextBlock? line;
    private readonly OfferCardControl? card;
    private string? wanted;

    public OfferCaptionControl(IOfferCardSource source, SkillsCatalogue catalogue, OfferCardControl? card = null)
    {
        this.card = card;
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        Child = slot;
        slot.Show(new Border { Height = 0 }, null); // something is on show, so the first caption crossfades in and glides open
        Padding = new Thickness(12, 2, 12, 2);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        IsVisible = false;
        AutomationProperties.SetName(this, "Offer result");
        this.Track(() => (source.Stage, source.Card), _ => Refresh());
        if (card is not null) card.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Refresh(); };
        Refresh();
    }

    /// <summary>The caption on show, or null.</summary>
    public string? Text => shown;

    private Animation? Fade() => !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(duration: 0.01) : LibraryUi.Fade;

    private void Refresh()
    {
        wanted = OfferCardProjector.Project(catalogue, AddonsState.Empty, source.Card, source.Stage, source.Progress, source.Failure)?.Caption;
        // The caption follows the card: while the card is still fading out and gliding shut the caption waits, so the composer below moves once and never in the frame the state changes.
        var next = wanted is not null && card is { IsVisible: true } ? shown : wanted;
        if (next == shown) return;
        shown = next;
        if (next is not null)
        {
            IsVisible = true;
            line = LibraryUi.Text(next, 11.5, secondary: true, wrap: true);
            slot.Show(line, Fade());
            return;
        }
        line = null;
        slot.Show(new Border { Height = 0 }, Fade());
        HideWhenSettled();
    }

    private void HideWhenSettled()
    {
        if (!FrameClock.IsBound || !slot.Settling) { IsVisible = false; return; }
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (shown is not null) return; // a caption came back while the old one faded
            if (slot.Settling) { frames.RequestFrame(tick!); return; }
            IsVisible = false;
        };
        frames.RequestFrame(tick);
    }
}
