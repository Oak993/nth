// PORT-ONLY: first-release feature unit SK-14 (DL-18). A scripted offer for the card's tests: the test sets the stage, the card and the progress, and counts what the card asked of it. No flow, no runtime, no download.
using Marrow.App.Port.Skills;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Tests.Fixtures;

public sealed class ScriptedOfferSource : MainActorModel, IOfferCardSource
{
    private OfferStage stage = OfferStage.Asked;
    private OfferCard? card;
    private OfferProgress? progress;
    private string? failure;
    private bool canFetch = true;

    public OfferStage Stage { get => Read(stage); set => Set(ref stage, value); }

    public OfferCard? Card { get => Read(card); set => Set(ref card, value); }

    public OfferProgress? Progress { get => Read(progress); set => Set(ref progress, value); }

    public string? Failure { get => Read(failure); set => Set(ref failure, value); }

    private string? why;

    public string? Why { get => Read(why); set => Set(ref why, value); }

    public bool CanFetch { get => Read(canFetch); set => Set(ref canFetch, value); }

    private int termsRevision;

    public int TermsRevision { get => Read(termsRevision); private set => Set(ref termsRevision, value); }

    /// <summary>The terms an item owes (the test sets them), the licence text the step would give (or the failure), and what the card does when the user accepts.</summary>
    public Dictionary<string, Marrow.Services.Port.Skills.Terms.TermsOffer> Terms { get; } = [];

    public Func<string, Task<string>> Licence { get; set; } = id => Task.FromResult("Marrow-made licence text of " + id);

    public Action<string>? OnAcceptTerms { get; set; }

    public List<string> Accepted { get; } = [];

    public Marrow.Services.Port.Skills.Terms.TermsOffer? TermsFor(string itemId) => Terms.GetValueOrDefault(itemId);

    public Task<string> LicenceText(string itemId) => Licence(itemId);

    public void AcceptTerms(string itemId)
    {
        Accepted.Add(itemId);
        OnAcceptTerms?.Invoke(itemId);
        TermsRevision++;
    }

    public int Accepts { get; private set; }

    public int Cancels { get; private set; }

    public int Declines { get; private set; }

    public Task Accept() { Accepts++; return Task.CompletedTask; }

    public void Cancel() => Cancels++;

    public void Decline() => Declines++;
}
