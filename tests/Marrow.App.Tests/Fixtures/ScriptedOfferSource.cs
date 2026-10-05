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

    public bool CanFetch { get => Read(canFetch); set => Set(ref canFetch, value); }

    public int Accepts { get; private set; }

    public int Cancels { get; private set; }

    public int Declines { get; private set; }

    public Task Accept() { Accepts++; return Task.CompletedTask; }

    public void Cancel() => Cancels++;

    public void Decline() => Declines++;
}
