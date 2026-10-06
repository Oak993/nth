// PORT-ONLY: DL-13 progress, ONDEMAND 5.4.
namespace Marrow.Services.Port.Skills.Offers;
public sealed record OfferProgress(OfferStage Stage, long BytesDone, long? BytesTotal);
