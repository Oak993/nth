// PORT-ONLY: first-release feature unit SK-14 (DL-18, skills-ONDEMAND-PLAN.md 5.4 and 5.6; OD-0099 Offer A, words S1). There is no Swift twin.
namespace Marrow.Services.Port.Skills.Offers;

/// <summary>
/// What the in-chat offer card shows now, with no Avalonia in it. <see cref="Status"/> and <see cref="PrimaryLabel"/> are words the locked skills-downloads design draws for the same state (LibraryWords:
/// "Download · size", "Checking…", "Didn't download", "Retry"); <see cref="Failure"/> is the reason as the downloader gave it (word set S1, never paraphrased). The asked <see cref="Sentence"/>, the <see cref="Why"/> line, the <see cref="DeclineLabel"/> and the done, continuing and declined <see cref="Caption"/> are LEAD-PROPOSED WORDING (OfferWords, ED-NEW-cloud-sk-14 item 16).
/// </summary>
public sealed record OfferCardView(OfferStage Phase, IReadOnlyList<string> Titles, double? SizeMb, IReadOnlyList<string> Hosts, int? Percent, long? BytesDone, long? BytesTotal,
    string? PrimaryLabel, bool PrimaryEnabled, bool CanCancel, bool CanDecline, string? Status, string? Failure, bool TermsOwed, string? Note = null, IReadOnlyList<string>? OwedItems = null,
    string? Sentence = null, string? Why = null, string? DeclineLabel = null, string? Caption = null);
