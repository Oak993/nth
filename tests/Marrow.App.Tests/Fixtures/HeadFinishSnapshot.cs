namespace Marrow.App.Tests.Fixtures;

/// <summary>What ThreadRuntimeHeadFinishedOrderTests reads right after a head finishes.</summary>
public sealed record HeadFinishSnapshot(bool FlushScheduled, bool FollowUpFlushScheduled, long Token, int PendingReports, int Batches);
