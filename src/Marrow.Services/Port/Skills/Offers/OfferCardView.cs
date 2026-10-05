// PORT-ONLY: first-release feature unit SK-14 (DL-18, skills-ONDEMAND-PLAN.md 5.4 and 5.6; OD-0099 Offer A, words S1). There is no Swift twin.
namespace Marrow.Services.Port.Skills.Offers;

/// <summary>
/// What the in-chat offer card shows now, with no Avalonia in it. <see cref="Status"/> and <see cref="PrimaryLabel"/> are words the locked skills-downloads design draws for the same state (LibraryWords:
/// "Download · size", "Checking…", "Didn't download", "Retry"); <see cref="Failure"/> is the reason as the downloader gave it (word set S1, never paraphrased). Where the design draws no word (the asked sentence,
/// the line saying why the item is needed, the decline control, the done, continuing and declined captions) the view has none: it is not invented here (ED-NEW-cloud-sk-14).
/// </summary>
public sealed record OfferCardView(OfferStage Phase, IReadOnlyList<string> Titles, double? SizeMb, IReadOnlyList<string> Hosts, int? Percent, long? BytesDone, long? BytesTotal,
    string? PrimaryLabel, bool PrimaryEnabled, bool CanCancel, bool CanDecline, string? Status, string? Failure, bool TermsOwed);
