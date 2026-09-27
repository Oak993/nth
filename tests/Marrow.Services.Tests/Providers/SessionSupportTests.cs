using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/SessionSupport.swift (B3 52bc792), :1-124. New at B3 (unit N7): code
// moved verbatim out of the Pi, Command Code and Antigravity sessions; those files keep their own copies until
// their own lanes switch them over to this shared one (lane L2b covers only the new file).
//
// PR-PROVCLI-444 and PR-PROVCLI-443 are PROPOSED new records for this unit (EditSnapshots and
// StreamedTextTracker respectively): confirmed free against this worktree's own porting/parity_matrix.json
// (impl/b2), whose highest existing PR-PROVCLI id is 442 (an unrelated B3-wave-1 record about Claude control
// request cancellation - not this file). No existing record names this shared type directly (only the three
// sessions' own inlined copies, which their own lanes reclassify onto these ids when they switch over). They are
// cited here as REQ ids per req_verify's UNBOUND status pending the orchestrator registering them in the ledger.
public sealed class SessionSupportTests
{
    // No BOM: EditSnapshots.Read decodes with SwiftText.DecodeUtf8OrNil, which keeps a BOM byte sequence as a
    // literal U+FEFF in the resulting string (SwiftNumber.cs:228), unlike plain `Encoding.UTF8`'s writer default.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // ---- PR-PROVCLI-444 (proposed): EditSnapshots ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:9-11, :27-33
    public void Capture_then_finish_with_no_change_returns_a_single_edit_with_no_diff()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("a.txt");
        File.WriteAllText(path, "same text", Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        var edits = snapshots.Finish("id-1", path, scratch.Root);

        Assert.NotNull(edits);
        var edit = Assert.Single(edits);
        Assert.Equal(new FileEdit("a.txt"), edit);
        Assert.Null(edit.Diff);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:27-33: the before/after edit when the text changed
    public void Capture_then_finish_with_a_change_returns_the_before_and_after_text()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("a.txt");
        File.WriteAllText(path, "before text", Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        File.WriteAllText(path, "after text", Utf8NoBom);
        var edits = snapshots.Finish("id-1", path, scratch.Root);

        Assert.NotNull(edits);
        var edit = Assert.Single(edits!);
        Assert.Equal(new FileEdit("a.txt", "before text", "after text"), edit);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:28: nothing was captured for this id
    public void Finish_returns_null_for_an_id_nothing_was_captured_for()
    {
        var snapshots = new EditSnapshots();
        Assert.Null(snapshots.Finish("missing", "C:\\nowhere\\a.txt", "C:\\nowhere"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:29: the edit's path is relative to root
    public void Finish_reports_a_path_relative_to_the_given_root()
    {
        using var scratch = new ScratchFolder();
        Directory.CreateDirectory(scratch.Child("sub"));
        var path = scratch.Child("sub", "b.txt");
        File.WriteAllText(path, "x", Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        File.WriteAllText(path, "y", Utf8NoBom);
        var edits = snapshots.Finish("id-1", path, scratch.Root);

        // ToolTitles.RelativePath returns the caller's own text (ED-019(4)): a Windows backslash path keeps
        // its backslashes.
        Assert.NotNull(edits);
        Assert.Equal("sub\\b.txt", Assert.Single(edits!).Path);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:9-11: a missing file reads as empty
    public void Capture_of_a_missing_file_reads_as_empty_so_a_write_shows_as_a_whole_file_addition()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("new.txt");

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path); // the file does not exist yet
        File.WriteAllText(path, "brand new content", Utf8NoBom);
        var edits = snapshots.Finish("id-1", path, scratch.Root);

        Assert.NotNull(edits);
        Assert.Equal(new FileEdit("new.txt", "", "brand new content"), Assert.Single(edits!));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:38: text files up to 1 MB; a huge one reads as empty
    public void Capture_of_a_file_over_1MB_reads_as_empty()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("huge.txt");
        File.WriteAllBytes(path, new byte[1_000_001]);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        Assert.Equal("", snapshots.Text("id-1"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:13-15
    public void Discard_removes_a_snapshot_without_finishing_it()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("a.txt");
        File.WriteAllText(path, "x", Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        snapshots.Discard("id-1");

        Assert.Equal("", snapshots.Text("id-1"));
        Assert.Null(snapshots.Finish("id-1", path, scratch.Root));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:17-19
    public void RemoveAll_clears_every_snapshot()
    {
        using var scratch = new ScratchFolder();
        var pathA = scratch.Child("a.txt");
        var pathB = scratch.Child("b.txt");
        File.WriteAllText(pathA, "a", Utf8NoBom);
        File.WriteAllText(pathB, "b", Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-a", pathA);
        snapshots.Capture("id-b", pathB);
        snapshots.RemoveAll();

        Assert.Equal("", snapshots.Text("id-a"));
        Assert.Equal("", snapshots.Text("id-b"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:21-23
    public void Text_returns_the_captured_text_or_empty_string_when_nothing_was_captured()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.Child("a.txt");
        File.WriteAllText(path, "captured", Utf8NoBom);

        var snapshots = new EditSnapshots();
        Assert.Equal("", snapshots.Text("id-1"));
        snapshots.Capture("id-1", path);
        Assert.Equal("captured", snapshots.Text("id-1"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-444")] // SessionSupport.swift:31: DEVIATION-guarding - Finish compares canonically
    // (SwiftString.Equal), not with ordinal `==`, so two canonically-equivalent but ordinally-different strings
    // (precomposed vs decomposed "e" with an acute accent) still count as "no change".
    public void Finish_compares_before_and_after_with_Swift_canonical_string_equality_not_ordinal()
    {
        const string precomposed = "caf\u00e9"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        const string decomposed = "cafe\u0301"; // "e" + U+0301 COMBINING ACUTE ACCENT
        Assert.NotEqual(precomposed, decomposed); // ordinal C# `==` disagrees
        Assert.True(SwiftString.Equal(precomposed, decomposed)); // canonically equivalent

        using var scratch = new ScratchFolder();
        var path = scratch.Child("a.txt");
        File.WriteAllText(path, precomposed, Utf8NoBom);

        var snapshots = new EditSnapshots();
        snapshots.Capture("id-1", path);
        File.WriteAllText(path, decomposed, Utf8NoBom);
        var edits = snapshots.Finish("id-1", path, scratch.Root);

        // A no-op edit (Diff null): had Finish used ordinal `==`, this would have been reported as a change.
        Assert.NotNull(edits);
        Assert.Equal(new FileEdit("a.txt"), Assert.Single(edits!));
    }

    // ---- PR-PROVCLI-443 (proposed): StreamedTextTracker ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:62-67
    public void StartMessage_mints_a_fresh_id_clears_the_text_buffer_and_the_streamedText_flag()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendMessage("stale text"); // sets streamedText and leaves a buffer
        tracker.StartMessage();

        Assert.Equal("m-2", tracker.CurrentMessageID); // the counter keeps incrementing, never resets
        Assert.Equal("", tracker.MessageText);
        Assert.False(tracker.StreamedText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:70-78: no current id, so one is minted
    public void AppendMessage_mints_an_id_when_none_is_current_and_sets_the_streamedText_flag()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        var id = tracker.AppendMessage("hi");

        Assert.Equal("m-1", id);
        Assert.Equal("m-1", tracker.CurrentMessageID);
        Assert.Equal("hi", tracker.MessageText);
        Assert.True(tracker.StreamedText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:71-74: an existing id is kept and text accumulates
    public void AppendMessage_keeps_the_current_id_and_accumulates_text_on_a_later_call()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        var first = tracker.AppendMessage("one ");
        var second = tracker.AppendMessage("two");

        Assert.Equal(first, second);
        Assert.Equal("one two", tracker.MessageText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:80-84
    public void StartThinking_mints_an_id_and_clears_the_reasoning_buffer()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.StartThinking();
        Assert.Equal("t-1", tracker.CurrentThinkingID);
        Assert.Equal("", tracker.ThinkingText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:88-96: resetsText true (Pi) starts fresh, clearing the buffer
    public void AppendThinking_with_resetsText_true_starts_fresh_when_no_id_is_current()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendThinking("stray", resetsText: true); // no current id: goes through StartThinking first
        tracker.EndThinking();
        var id = tracker.AppendThinking("fresh", resetsText: true);

        Assert.Equal("t-2", id);
        Assert.Equal("fresh", tracker.ThinkingText); // the "stray" buffer was cleared by StartThinking, not appended
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:90-95: resetsText false (Command Code) keeps the buffer across a block
    public void AppendThinking_with_resetsText_false_mints_an_id_without_clearing_the_buffer()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendThinking("first block ", resetsText: false);
        tracker.EndThinking(); // a new block starts, but the accumulated text is not reset
        var id = tracker.AppendThinking("second block", resetsText: false);

        Assert.Equal("t-2", id);
        Assert.Equal("first block second block", tracker.ThinkingText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:101-107: ending only clears the id, not the text
    public void EndThinking_and_EndMessage_clear_only_the_current_id()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendMessage("m");
        tracker.AppendThinking("t");

        tracker.EndMessage();
        tracker.EndThinking();

        Assert.Null(tracker.CurrentMessageID);
        Assert.Null(tracker.CurrentThinkingID);
        Assert.Equal("m", tracker.MessageText);
        Assert.Equal("t", tracker.ThinkingText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:109-113: a message_start also drops the reasoning it interrupted
    public void ResetThinking_clears_both_the_current_id_and_the_reasoning_text()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendThinking("interrupted");
        tracker.ResetThinking();

        Assert.Null(tracker.CurrentThinkingID);
        Assert.Equal("", tracker.ThinkingText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:115-118: the turn's flag only, cleared before each turn
    public void ResetForTurn_clears_only_the_streamedText_flag()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendMessage("m");
        tracker.ResetForTurn();

        Assert.False(tracker.StreamedText);
        Assert.Equal("m-1", tracker.CurrentMessageID);
        Assert.Equal("m", tracker.MessageText);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-443")] // SessionSupport.swift:120-123
    public void ResetIDs_clears_both_current_ids_but_not_the_accumulated_text()
    {
        var tracker = new StreamedTextTracker(n => $"m-{n}", n => $"t-{n}");
        tracker.AppendMessage("m");
        tracker.AppendThinking("t");
        tracker.ResetIDs();

        Assert.Null(tracker.CurrentMessageID);
        Assert.Null(tracker.CurrentThinkingID);
        Assert.Equal("m", tracker.MessageText);
        Assert.Equal("t", tracker.ThinkingText);
    }
}
