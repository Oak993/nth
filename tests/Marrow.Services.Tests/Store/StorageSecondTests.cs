using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Tests for src/Marrow.Services/Store/Storage.cs, part 2 of the port of
// DroppyCode/Services/Store/Storage.swift (B2 1fbf81e). See StorageTests.cs for part 1's tests, R-TEST-3.
public sealed class StorageSecondTests
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    // ---- PR-STORE-031 / PR-STORE-032 / PR-STORE-033 (deleteDocuments / deleteDocument) ----

    [Fact]
    [Trait("Req", "PR-STORE-031")] // Services/Store/Storage.swift:92, :332-334
    public void DeleteDocuments_ForgetsThePrefetchedEntryAsWellAsRemovingTheFile()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(new ThreadDocument(id)));

        // Seed the shared ready cache the way the app does (warm, then wait for the decode to land).
        DocumentPrefetch.Shared.Warm([id]);
        Poll(() => DocumentPrefetch.Shared.IsReady(id));
        Assert.True(DocumentPrefetch.Shared.IsReady(id));

        Storage.DeleteDocuments([id]);

        Assert.False(File.Exists(Storage.ThreadURL(id)));
        Assert.False(DocumentPrefetch.Shared.IsReady(id)); // :92 forget(id) ran
    }

    [Fact]
    [Trait("Req", "PR-STORE-032")] // Services/Store/Storage.swift:87-94, :367-374: one hop removes every file
    public void DeleteDocuments_RemovesEveryFileInOneCall()
    {
        using var scratch = new StorageScratchFolder();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        File.WriteAllBytes(Storage.ThreadURL(idA), Coding.Encode(new ThreadDocument(idA)));
        File.WriteAllBytes(Storage.ThreadURL(idB), Coding.Encode(new ThreadDocument(idB)));

        Storage.DeleteDocuments([idA, idB]);

        Assert.False(File.Exists(Storage.ThreadURL(idA)));
        Assert.False(File.Exists(Storage.ThreadURL(idB)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-032")] // Services/Store/Storage.swift:93, :367-374: a queued save of a deleted thread never lands
    public void DeleteDocuments_CancelsAQueuedSaveOfAThreadBeingDeleted()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(id), url); // a save on its way to the writer

        Storage.DeleteDocuments([id]); // blocks for one drain; the queued save's pending entry is cleared first
        DiskWriter.Shared.WaitForQueuedWrites(); // drain anything the race let the hop's own Async schedule

        Assert.False(File.Exists(url)); // the thread never comes back
    }

    [Fact]
    [Trait("Req", "PR-STORE-033")] // Services/Store/Storage.swift:83-85
    public void DeleteDocument_IsTheSingleIdFormOfDeleteDocuments()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(new ThreadDocument(id)));

        Storage.DeleteDocument(id);

        Assert.False(File.Exists(Storage.ThreadURL(id)));
    }

    // ---- PR-STORE-035 to PR-STORE-041 (DiskWriter) ----

    [Fact]
    [Trait("Req", "PR-STORE-035")] // Services/Store/Storage.swift:338-339
    public void DiskWriter_UsesTheExactQueueLabel()
    {
        Assert.Equal("droppycode.storage.write", DiskWriter.Shared.QueueLabel);
    }

    [Fact]
    [Trait("Req", "PR-STORE-036")] // Services/Store/Storage.swift:340-356
    public void EncodeAndWrite_CoalescesRapidSavesOfTheSameFileToTheNewestSnapshot()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var stale = new ThreadDocument(id) with { Turns = ValueList<TurnRecord>.Empty };
        var newest = new ThreadDocument(id) with
        {
            FollowUps = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "newest" }]),
        };

        DiskWriter.Shared.EncodeAndWrite(stale, url);
        DiskWriter.Shared.EncodeAndWrite(newest, url); // queued behind (or replacing) the first, same file
        DiskWriter.Shared.WaitForQueuedWrites();

        var landed = Coding.Decode<ThreadDocument>(File.ReadAllBytes(url));
        Assert.NotNull(landed);
        Assert.Single(landed!.FollowUps);
        Assert.Equal("newest", landed.FollowUps[0].Text);
    }

    [Fact]
    [Trait("Req", "PR-STORE-037")] // Services/Store/Storage.swift:381-382
    public void EncodeAndWrite_SilentlySkipsAValueThatCannotBeEncoded_LeavingThePreviousFileIntact()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var good = new ThreadDocument(id);
        var goodBytes = Coding.Encode(good);
        File.WriteAllBytes(url, goodBytes); // the "previous good file"

        var badTurnSummary = new TurnSummary
        {
            TurnID = Guid.NewGuid(),
            Status = TurnStatus.Completed,
            Duration = double.NaN, // SwiftJson.WriteDouble throws EncodingError for a non-finite Double (R-CODE-5)
            FilesChanged = 0,
            Additions = 0,
            Deletions = 0,
        };
        var bad = new ThreadDocument(id) with
        {
            Items = new ValueList<TimelineItem>([new TimelineItem(null, new TimelineItem.Content.TurnEnd(badTurnSummary))]),
        };

        var exception = Record.Exception(() => DiskWriter.Shared.EncodeAndWrite(bad, url)); // never throws at the call site
        Assert.Null(exception);
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(goodBytes, File.ReadAllBytes(url)); // untouched
    }

    [Fact]
    [Trait("Req", "PR-STORE-038")] // Services/Store/Storage.swift:383-385: a write lands atomically and is fully readable
    public void WriteSynchronously_LandsTheBytesAtomicallyAndBlocksUntilTheyAreThere()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var document = new ThreadDocument(id);

        DiskWriter.Shared.WriteSynchronously(document, url); // returns only once the bytes have landed

        var landed = Coding.Decode<ThreadDocument>(File.ReadAllBytes(url));
        Assert.Equal(id, landed!.ThreadID);
    }

    [Fact]
    [Trait("Req", "PR-STORE-038")] // Services/Store/Storage.swift:386: a write failure is reported, never surfaced to the app
    public void Write_OnUnauthorizedAccess_LogsTheOSDescriptionAndReturnsSoTheWriterCanContinue()
    {
        // A directory at the destination lets File.Move exercise Windows' access-denied path without depending
        // on machine ACLs. FileOps retries that transient failure and then throws the final OS exception.
        using var scratch = new StorageScratchFolder();
        var failedURL = Path.Combine(scratch.Root, "denied.json");
        Directory.CreateDirectory(failedURL);
        var probeURL = Path.Combine(scratch.Root, "probe.tmp");
        File.WriteAllBytes(probeURL, [1]);
        var osError = Assert.Throws<UnauthorizedAccessException>(() => File.Move(probeURL, failedURL, overwrite: true));
        var failedDocument = new ThreadDocument(Guid.NewGuid());

        var logged = new List<(string Message, string Channel)>();
        using (Logger.Sink.Override((message, channel) => logged.Add((message, channel))))
        {
            DiskWriter.Shared.WriteSynchronously(failedDocument, failedURL); // failure must not escape the call site

            var continuedID = Guid.NewGuid();
            var continuedURL = Storage.ThreadURL(continuedID);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(continuedID), continuedURL);
            Assert.Equal(continuedID, Coding.Decode<ThreadDocument>(File.ReadAllBytes(continuedURL))!.ThreadID);
        }

        var line = Assert.Single(logged);
        Assert.Equal("iordv.marrow/storage", line.Channel);
        Assert.Equal($"Nth could not save denied.json: {osError.Message}", line.Message);
    }

    [Fact]
    [Trait("Req", "PR-STORE-038")] // Services/Store/Storage.swift:386: a final IOException is logged without escaping
    public void Write_OnFinalIOException_LogsTheOSDescriptionAndReturnsSoTheWriterCanContinue()
    {
        using var scratch = new StorageScratchFolder();
        var failedID = Guid.NewGuid();
        var failedURL = Path.Combine(scratch.Root, new string('x', 251) + ".json");
        var failedDocument = new ThreadDocument(failedID) with
        {
            FollowUps = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "replacement" }]),
        };
        var expected = Coding.Encode(failedDocument);

        // The parent directory exists; a 256-character Windows filename makes the final move report an IOException.
        var osError = Record.Exception(() => FileOps.WriteAtomic(failedURL, expected));
        var finalError = Assert.IsAssignableFrom<IOException>(osError);

        var logged = new List<(string Message, string Channel)>();
        using (Logger.Sink.Override((message, channel) => logged.Add((message, channel))))
        {
            var exception = Record.Exception(() => DiskWriter.Shared.WriteSynchronously(failedDocument, failedURL));
            Assert.Null(exception);

            var continuedID = Guid.NewGuid();
            var continuedURL = Storage.ThreadURL(continuedID);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(continuedID), continuedURL);
            Assert.Equal(continuedID, Coding.Decode<ThreadDocument>(File.ReadAllBytes(continuedURL))!.ThreadID);
        }

        var line = Assert.Single(logged);
        Assert.Equal("iordv.marrow/storage", line.Channel);
        var prefix = $"Nth could not save {Path.GetFileName(failedURL)}: ";
        Assert.StartsWith(prefix, line.Message, StringComparison.Ordinal);
        // FileOps picks a fresh staging filename per attempt; the rest of the OS message must match exactly.
        Assert.Equal(NormalizeTemporaryName(finalError.Message), NormalizeTemporaryName(line.Message[prefix.Length..]));

        static string NormalizeTemporaryName(string message)
        {
            const string pattern = @"\.[0-9a-f]{16}\.tmp";
            var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            var matches = System.Text.RegularExpressions.Regex.Matches(message, pattern, options);
            Assert.Single(matches);
            return System.Text.RegularExpressions.Regex.Replace(message, pattern, ".<staging-file>.tmp", options);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-039")] // Services/Store/Storage.swift:358-361
    public void WriteSynchronously_CancelsThePendingSnapshotAndTheFinalFileIsTheSynchronousValue()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var queued = new ThreadDocument(id) with { Turns = ValueList<TurnRecord>.Empty };
        var synchronous = new ThreadDocument(id) with
        {
            FollowUps = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = "sync" }]),
        };

        DiskWriter.Shared.EncodeAndWrite(queued, url); // pending[url] = queued, hop scheduled
        DiskWriter.Shared.WriteSynchronously(synchronous, url); // :359 clears pending[url] first, then writes now

        var landed = Coding.Decode<ThreadDocument>(File.ReadAllBytes(url));
        Assert.Single(landed!.FollowUps);
        Assert.Equal("sync", landed.FollowUps[0].Text); // never overwritten by the cancelled queued snapshot
    }

    [Fact]
    [Trait("Req", "PR-STORE-040")] // Services/Store/Storage.swift:363-374
    public void RemoveSynchronously_CancelsPendingWritesAndRemovesEveryPath()
    {
        using var scratch = new StorageScratchFolder();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var urlA = Storage.ThreadURL(idA);
        var urlB = Storage.ThreadURL(idB);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(idA), urlA);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(idB), urlB);

        DiskWriter.Shared.RemoveSynchronously([urlA, urlB]);
        DiskWriter.Shared.WaitForQueuedWrites(); // drain anything the race let land, then re-check

        Assert.False(File.Exists(urlA));
        Assert.False(File.Exists(urlB));
    }

    [Fact]
    [Trait("Req", "PR-STORE-041")] // Services/Store/Storage.swift:376-379
    public void WaitForQueuedWrites_IsABarrierAfterWhichEveryEarlierWriteHasLanded()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(id), url);

        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.True(File.Exists(url)); // already landed, not merely "eventually"
    }

    // ---- PR-STORE-174 (DiskWriter.LastWrittenSize) ----

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:336-338, :347-350
    public void LastWrittenSize_IsNullForAUrlNeverWrittenThroughEncodeAndWrite()
    {
        using var scratch = new StorageScratchFolder();
        var url = Storage.ThreadURL(Guid.NewGuid());
        Assert.Null(DiskWriter.Shared.LastWrittenSize(url));
    }

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:347-350: only encodeAndWrite's queued hop records a size
    public void EncodeAndWrite_RecordsTheWrittenByteSizeForThatUrl()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var document = new ThreadDocument(id);

        DiskWriter.Shared.EncodeAndWrite(document, url);
        DiskWriter.Shared.WaitForQueuedWrites(); // the queued hop has now run

        var expected = (int)new FileInfo(url).Length;
        Assert.Equal(expected, DiskWriter.Shared.LastWrittenSize(url));
    }

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:378: an encode failure leaves the recorded size unchanged
    public void EncodeAndWrite_WithAnUnencodableValue_LeavesThePreviousRecordedSizeUnchanged()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var good = new ThreadDocument(id);
        DiskWriter.Shared.EncodeAndWrite(good, url);
        DiskWriter.Shared.WaitForQueuedWrites();
        var recordedAfterGoodWrite = DiskWriter.Shared.LastWrittenSize(url);
        Assert.NotNull(recordedAfterGoodWrite);

        var badTurnSummary = new TurnSummary
        {
            TurnID = Guid.NewGuid(),
            Status = TurnStatus.Completed,
            Duration = double.NaN, // SwiftJson.WriteDouble throws EncodingError for a non-finite Double (R-CODE-5)
            FilesChanged = 0,
            Additions = 0,
            Deletions = 0,
        };
        var bad = new ThreadDocument(id) with
        {
            Items = new ValueList<TimelineItem>([new TimelineItem(null, new TimelineItem.Content.TurnEnd(badTurnSummary))]),
        };
        DiskWriter.Shared.EncodeAndWrite(bad, url);
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(recordedAfterGoodWrite, DiskWriter.Shared.LastWrittenSize(url));
    }

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:358-361: writeSynchronously never records or clears a size
    public void WriteSynchronously_NeverChangesTheRecordedSizeForThatUrl()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(id), url);
        DiskWriter.Shared.WaitForQueuedWrites();
        var recorded = DiskWriter.Shared.LastWrittenSize(url);
        Assert.NotNull(recorded);

        // A synchronous write of a much larger document touches the same file, but only encodeAndWrite's
        // queued hop ever records a size (:349-350): the earlier recorded size must survive untouched.
        var larger = new ThreadDocument(id) with
        {
            FollowUps = new ValueList<FollowUpPrompt>([new FollowUpPrompt { Text = new string('x', 4096) }]),
        };
        DiskWriter.Shared.WriteSynchronously(larger, url);

        Assert.Equal(recorded, DiskWriter.Shared.LastWrittenSize(url));
    }

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:363-374: removeSynchronously never clears a recorded size
    public void RemoveSynchronously_NeverChangesTheRecordedSizeForThatUrl()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(id), url);
        DiskWriter.Shared.WaitForQueuedWrites();
        var recorded = DiskWriter.Shared.LastWrittenSize(url);
        Assert.NotNull(recorded);

        DiskWriter.Shared.RemoveSynchronously(url);

        Assert.Equal(recorded, DiskWriter.Shared.LastWrittenSize(url));
    }

    // ---- PR-STORE-042 to PR-STORE-048 (DocumentPrefetch.Warm and its worker) ----

    [Fact]
    [Trait("Req", "PR-STORE-042")] // Services/Store/Storage.swift:264-273
    public void DocumentPrefetch_Shared_IsOneInstanceReusedByEveryCaller()
    {
        // Shared (:246) is a static let; the exact literals it hands the init (limit 12, costLimit 16*1024*1024,
        // pendingLimit 24) are exercised directly, via the same internal constructor with the same values, by
        // the queue-limit test below (Warming_QueuesUpToThePendingLimit...), since probing Shared's own queue
        // races against its always-on production loader (Storage.ReadDocument).
        Assert.Same(DocumentPrefetch.Shared, DocumentPrefetch.Shared);
    }

    [Fact]
    [Trait("Req", "PR-STORE-043")] // Services/Store/Storage.swift:285-291
    [Trait("Req", "PR-STORE-044")] // Services/Store/Storage.swift:287
    [Trait("Req", "PR-STORE-046")] // Services/Store/Storage.swift:292-307, :310-322
    public void Warming_SkipsPendingIds_StopsAtThePendingLimit_AndOneWorkerDrainsSerially()
    {
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12,
            costLimit: 16 * 1024 * 1024, // :266-267 the Swift defaults, threaded through the test-only ctor (PR-STORE-042)
            pendingLimit: 3,
            load: id =>
            {
                started.Release();
                release.Wait();
                return (new ThreadDocument(id), 1);
            });
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid(); // dropped: the 4th id against a pendingLimit of 3

        prefetch.Warm([a, b, c, d]);
        Assert.True(started.Wait(PollTimeout)); // the worker has dequeued a and is decoding it
        Assert.True(prefetch.IsPending(a));
        Assert.False(prefetch.IsPending(d)); // :287 break, not continue: d was never queued or marked pending
        Assert.Equal([b, c], prefetch.QueuedIds); // a already dequeued into flight

        prefetch.Warm([a]); // :286 already pending -> skipped, no duplicate queued
        Assert.Equal([b, c], prefetch.QueuedIds);

        release.Release(); // let a's decode finish
        Poll(() => prefetch.IsReady(a));
        Assert.True(prefetch.IsReady(a));
        Assert.False(prefetch.IsPending(a));

        Assert.True(started.Wait(PollTimeout)); // worker moved on to b
        Assert.Equal([c], prefetch.QueuedIds);
        release.Release();
        Poll(() => prefetch.IsReady(b));

        Assert.True(started.Wait(PollTimeout)); // worker moved on to c
        Assert.Empty(prefetch.QueuedIds);
        release.Release();
        Poll(() => prefetch.IsReady(c));

        Poll(() => !prefetch.HasWorker); // :316-318 the worker clears itself once the queue is empty
        Assert.False(prefetch.HasWorker);

        // d was never remembered as pending or ready, so it can still be warmed later (:286 lets it through).
        prefetch.Warm([d]);
        Assert.True(started.Wait(PollTimeout));
        release.Release();
        Poll(() => prefetch.IsReady(d));
        Assert.True(prefetch.IsReady(d));
    }

    [Fact]
    [Trait("Req", "PR-STORE-045")] // Services/Store/Storage.swift:281-284, :289
    public void Warming_Urgent_MovesAnAlreadyQueuedIdToTheFrontAndInsertsNewIdsAtTheFront()
    {
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12, costLimit: long.MaxValue, pendingLimit: 24,
            load: id => { release.Wait(); return (new ThreadDocument(id), 1); }); // blocks forever until released
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var e = Guid.NewGuid();
        try
        {
            prefetch.Warm([a, b, c]); // a is dequeued into flight (blocked in load); queue holds [b, c]
            Poll(() => prefetch.QueuedIds.Count == 2);
            Assert.Equal([b, c], prefetch.QueuedIds);

            prefetch.Warm([d, e], urgent: true); // :281 reversed() finds neither d nor e already queued (no-op);
                                                  // :289 inserts d then e at index 0, one at a time
            Assert.Equal([e, d, b, c], prefetch.QueuedIds);

            prefetch.Warm([c], urgent: true); // :281-283 c IS already queued: moved to the front
            Assert.Equal([c, e, d, b], prefetch.QueuedIds);
        }
        finally
        {
            release.Release(1000); // unblock every in-flight and future load call so the worker can exit
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-047")] // Services/Store/Storage.swift:290, :296-302
    public void ATokenClearedWhileADecodeIsInFlight_DiscardsTheResultWhenItLands()
    {
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12, costLimit: long.MaxValue, pendingLimit: 24,
            load: id => { started.Release(); release.Wait(); return (new ThreadDocument(id), 1); });
        var a = Guid.NewGuid();

        prefetch.Warm([a]);
        Assert.True(started.Wait(PollTimeout)); // a's decode is running; state.pending[a] still holds its token

        var taken = prefetch.Take(a); // :326 clears state.pending[a] while the decode is still in flight
        Assert.Null(taken); // nothing was ready yet
        Assert.False(prefetch.IsPending(a));

        release.Release(); // the decode finishes...
        Poll(() => !prefetch.HasWorker); // ...the worker consumes the (now token-mismatched) result and exits
        Assert.False(prefetch.IsReady(a)); // :297 guard fails, the result is thrown away, never inserted
    }

    [Fact]
    [Trait("Req", "PR-STORE-048")] // Services/Store/Storage.swift:312-315
    public void CancellingTheWorker_FlushesTheWholeQueueAndPendingMap_LeavingTheReadyCacheAlone()
    {
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12, costLimit: long.MaxValue, pendingLimit: 24,
            load: id => { started.Release(); release.Wait(); return (new ThreadDocument(id), 1); });
        var already = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        // First, an ordinary (uncancelled) decode lands in the ready cache.
        prefetch.Warm([already]);
        Assert.True(started.Wait(PollTimeout));
        release.Release();
        Poll(() => prefetch.IsReady(already));
        Assert.True(prefetch.IsReady(already));

        var worker = prefetch.Warm([a, b, c]); // a in flight, queue holds [b, c]
        Assert.True(started.Wait(PollTimeout));
        Assert.NotNull(worker);
        worker!.Cancel();
        release.Release(); // let a's in-flight decode return; :299 !Task.isCancelled is now false, so it is dropped

        Poll(() => !prefetch.HasWorker); // :313-314, :317 next() flushed the queue and pending, then cleared itself
        Assert.False(prefetch.HasWorker);
        Assert.Empty(prefetch.QueuedIds);
        Assert.False(prefetch.IsPending(a));
        Assert.False(prefetch.IsPending(b));
        Assert.False(prefetch.IsPending(c));
        Assert.False(prefetch.IsReady(a));
        Assert.False(prefetch.IsReady(b));
        Assert.False(prefetch.IsReady(c));
        Assert.True(prefetch.IsReady(already)); // untouched by the cancellation
    }

    // ---- PR-STORE-049 / PR-STORE-050 (Take / Forget) ----

    [Fact]
    [Trait("Req", "PR-STORE-049")] // Services/Store/Storage.swift:324-330
    public void Take_ConsumesAReadyDocumentOnceAndAlsoCancelsAQueuedRequestForTheSameId()
    {
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12, costLimit: long.MaxValue, pendingLimit: 24,
            load: id => { started.Release(); release.Wait(); return (new ThreadDocument(id), 1); });
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        prefetch.Warm([a, b]); // a in flight, b queued
        Assert.True(started.Wait(PollTimeout));
        Assert.Contains(b, prefetch.QueuedIds);

        var takenB = prefetch.Take(b); // :327 removed from the queue, not merely marked
        Assert.Null(takenB);
        Assert.DoesNotContain(b, prefetch.QueuedIds);
        Assert.False(prefetch.IsPending(b));

        release.Release();
        Poll(() => prefetch.IsReady(a));
        var takenA = prefetch.Take(a); // :328 consumes the ready entry
        Assert.NotNull(takenA);
        Assert.Equal(a, takenA!.ThreadID);
        Assert.False(prefetch.IsReady(a));

        Assert.Null(prefetch.Take(a)); // a second take finds nothing: served exactly once
    }

    [Fact]
    [Trait("Req", "PR-STORE-050")] // Services/Store/Storage.swift:332-334
    public void Forget_IsTakeWithTheResultDiscarded()
    {
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var prefetch = new DocumentPrefetch(
            limit: 12, costLimit: long.MaxValue, pendingLimit: 24,
            load: id => { started.Release(); release.Wait(); return (new ThreadDocument(id), 1); });
        var a = Guid.NewGuid();

        prefetch.Warm([a]);
        Assert.True(started.Wait(PollTimeout));
        release.Release();
        Poll(() => prefetch.IsReady(a));

        prefetch.Forget(a);

        Assert.False(prefetch.IsReady(a));
        Assert.Null(prefetch.Take(a));
    }

    // ---- PR-STORE-059 to PR-STORE-063 (attachmentLimit and attach) ----

    [Fact]
    [Trait("Req", "PR-STORE-059")] // Services/Store/Storage.swift:152-153
    public void AttachmentLimit_IsEight()
    {
        Assert.Equal(8, Storage.AttachmentLimit);
    }

    [Fact]
    [Trait("Req", "PR-STORE-060")] // Services/Store/Storage.swift:163
    public void Attach_CountsTheFreeRoomBeforeCopyingAndDoesNotBlockTheCaller()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var source = WriteTempFile("room.md", "content");
        try
        {
            var current = new ValueList<Attachment>(Enumerable.Range(0, 6)
                .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" })); // 2 of 8 free

            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach([new AttachmentSource.File(source)], () => current, v => current = v);
                return Task.CompletedTask;
            });

            Assert.NotNull(task); // :163-164 room (2) was counted synchronously; the batch was not empty
            Assert.Equal(6, current.Count); // :163's count returns before any byte is copied (nothing appended yet)
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-061")] // Services/Store/Storage.swift:164
    public void Attach_WithNoFreeSlots_ReturnsNullAndTouchesNothing()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var source = WriteTempFile("full.md", "content");
        try
        {
            var full = new ValueList<Attachment>(Enumerable.Range(0, Storage.AttachmentLimit)
                .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" }));

            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach([new AttachmentSource.File(source)], () => full, _ => throw new InvalidOperationException("must not be called"));
                return Task.CompletedTask;
            });

            Assert.Null(task); // :164 guard !batch.isEmpty else { return nil }
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-061")] // Services/Store/Storage.swift:164: an empty sources list also returns nil
    public void Attach_WithNoSources_ReturnsNull()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var empty = new ValueList<Attachment>([]);

        TaskHandle? task = null;
        runtime.Loop.Run(() =>
        {
            task = Storage.Attach([], () => empty, _ => throw new InvalidOperationException("must not be called"));
            return Task.CompletedTask;
        });

        Assert.Null(task);
    }

    [Fact]
    [Trait("Req", "PR-STORE-062")] // Services/Store/Storage.swift:166, :169
    public void Attach_ImportsOffTheCallingThreadAndAppendsInSourceOrder()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var goodOne = WriteTempFile("one.md", "one");
        var goodTwo = WriteTempFile("two.md", "two");
        var badHeic = WriteTempFileBytes("bad.heic", [0x00, 0x01]); // ImportSource drops this one (:186-188)
        try
        {
            var current = new ValueList<Attachment>([]);
            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach(
                    [new AttachmentSource.File(goodOne), new AttachmentSource.File(badHeic), new AttachmentSource.File(goodTwo)],
                    () => current, v => current = v);
                return Task.CompletedTask;
            });
            Assert.NotNull(task);
            runtime.Loop.RunUntil(() => task!.Completion.IsCompleted);
            Assert.Equal(TaskStatus.RanToCompletion, task!.Completion.Status);

            Assert.Equal(2, current.Count); // the dropped HEIC never lands
            Assert.Equal(Path.GetFileName(goodOne), current[0].Name); // :166 compactMap keeps the surviving order
            Assert.Equal(Path.GetFileName(goodTwo), current[1].Name);
        }
        finally
        {
            File.Delete(goodOne);
            File.Delete(goodTwo);
            File.Delete(badHeic);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-063")] // Services/Store/Storage.swift:172
    public void Attach_DeletesCopiesThatArriveAfterTheRecountedRoomIsExceeded()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var sources = new[] { WriteTempFile("s1.md", "1"), WriteTempFile("s2.md", "2"), WriteTempFile("s3.md", "3") };
        try
        {
            var current = new ValueList<Attachment>([]);
            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach(sources.Select(s => (AttachmentSource)new AttachmentSource.File(s)).ToArray(),
                    () => current, v => current = v);
                return Task.CompletedTask;
            });
            Assert.NotNull(task);

            // More attachments arrive before the batch's copies land: only 1 slot is free by then (:167-168).
            current = new ValueList<Attachment>(Enumerable.Range(0, Storage.AttachmentLimit - 1)
                .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" }));

            runtime.Loop.RunUntil(() => task!.Completion.IsCompleted);
            Assert.Equal(TaskStatus.RanToCompletion, task!.Completion.Status);

            Assert.Equal(Storage.AttachmentLimit, current.Count); // 7 pre-existing + exactly 1 kept
            var keptPath = current[Storage.AttachmentLimit - 1].Path;
            Assert.True(File.Exists(keptPath));
            // The other 2 imported copies were written under AttachmentsDirectory and then deleted again (:172):
            // nothing beyond the one kept file remains there.
            Assert.Single(Directory.GetFiles(Storage.AttachmentsDirectory));
        }
        finally
        {
            foreach (var s in sources) SwiftTry.Ignore(() => File.Delete(s));
        }
    }

    private static string WriteTempFile(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string WriteTempFileBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Poll(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) return; // the caller's own Assert reports the timeout as a failure
            Thread.Sleep(5);
        }
    }
}
