using System.Text.Json.Nodes;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.3, 15.3; ED-NEW-dextra-as16-12): who Dextra handed work to and which chat waits for it. A chat's pieces form one group; when none is
// still at work the group is due, once; a piece the owner stopped (or one that blocked) may go on, and its later report is due on its own. Kept in delegations.json.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-12")]
public sealed class DelegationBookTests
{
    private static readonly Guid Chat = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid MissionA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MissionB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static DelegationBook TwoPieces()
    {
        var book = new DelegationBook();
        book.Add(Chat, "dextra-1", MissionA, "channel-a");
        book.Add(Chat, "dextra-1", MissionB, "channel-b");
        return book;
    }

    [Fact]
    public void A_chats_pieces_join_one_group_that_is_due_when_the_last_one_ends_and_only_once()
    {
        var book = TwoPieces();
        var group = Assert.Single(book.Groups);
        Assert.Equal(2, group.Pieces.Count);
        Assert.False(group.Delivered);

        Assert.Null(book.Observe(MissionA, PieceState.Finished));                       // B is still at work
        var due = book.Observe(MissionB, PieceState.Finished);
        Assert.NotNull(due);
        Assert.False(due!.Late);
        Assert.Equal((Chat, "dextra-1"), (due.Chat, due.FromAssistant));
        Assert.Equal([PieceState.Finished, PieceState.Finished], due.Pieces.Select(p => p.State));
        Assert.Equal(["channel-a", "channel-b"], due.Pieces.Select(p => p.AsturId));

        Assert.Null(book.Observe(MissionB, PieceState.Finished));                       // the same move again: nothing
        Assert.Empty(book.Groups);                                                       // a group with nothing left to go on is forgotten
    }

    [Fact]
    public void A_group_is_marked_delivered_before_it_is_returned_so_a_crash_between_the_mark_and_the_message_never_repeats_it()
    {
        var book = TwoPieces();
        book.Observe(MissionA, PieceState.Stopped);
        Assert.NotNull(book.Observe(MissionB, PieceState.Finished));
        var document = book.ToJson()!;
        var restarted = new DelegationBook();
        restarted.Load(document);
        var group = Assert.Single(restarted.Groups);
        Assert.True(group.Delivered);
        Assert.Null(restarted.Observe(MissionB, PieceState.Finished));                  // not delivered a second time
    }

    [Fact]
    public void A_stopped_piece_does_not_hold_the_others_and_its_later_report_is_due_alone_as_late()
    {
        var book = TwoPieces();
        Assert.Null(book.Observe(MissionA, PieceState.Stopped));
        var due = book.Observe(MissionB, PieceState.Finished);
        Assert.NotNull(due);                                                             // the group goes, with A as stopped
        Assert.Equal([PieceState.Stopped, PieceState.Finished], due!.Pieces.Select(p => p.State));
        var group = Assert.Single(book.Groups);                                          // kept: A may go on
        Assert.True(group.Delivered);

        Assert.Null(book.Observe(MissionA, PieceState.Waiting));                         // the owner resumed it: nothing is due yet
        var late = book.Observe(MissionA, PieceState.Finished);
        Assert.NotNull(late);
        Assert.True(late!.Late);
        Assert.Equal([MissionA], late.Pieces.Select(p => p.Mission));
        Assert.Empty(book.Groups);
        Assert.Null(book.Observe(MissionA, PieceState.Finished));
    }

    [Fact]
    public void A_blocked_piece_counts_as_ended_for_the_group_and_may_still_report_late()
    {
        var book = new DelegationBook();
        book.Add(Chat, "dextra-1", MissionA, "channel-a");
        var due = book.Observe(MissionA, PieceState.Blocked);
        Assert.NotNull(due);
        Assert.Equal(PieceState.Blocked, due!.Pieces.Single().State);
        Assert.True(book.Observe(MissionA, PieceState.Failed)!.Late);
    }

    [Fact]
    public void A_piece_that_goes_back_to_work_before_the_group_is_due_holds_it_again()
    {
        var book = TwoPieces();
        book.Observe(MissionA, PieceState.Stopped);
        book.Observe(MissionA, PieceState.Waiting);                                      // resumed before B finished
        Assert.Null(book.Observe(MissionB, PieceState.Finished));
        Assert.NotNull(book.Observe(MissionA, PieceState.Finished));
    }

    [Fact]
    public void Another_chat_has_its_own_group_and_a_delivered_chat_starts_a_new_one()
    {
        var book = TwoPieces();
        var other = Guid.NewGuid();
        var mission = Guid.NewGuid();
        book.Add(other, "dextra-1", mission, "store");
        Assert.Equal(2, book.Groups.Count);
        book.Observe(MissionA, PieceState.Finished);
        Assert.NotNull(book.Observe(MissionB, PieceState.Finished));
        Assert.Single(book.Groups);                                                       // the other chat's is still waiting
        var again = book.Add(Chat, "dextra-1", Guid.NewGuid(), "channel-a");
        Assert.NotNull(again);
        Assert.Equal(2, book.Groups.Count);
    }

    [Fact]
    public void A_group_holds_at_most_sixteen_pieces_and_a_piece_is_not_added_twice()
    {
        var book = new DelegationBook();
        for (var n = 0; n < DelegationBook.MaxPieces; n++) Assert.NotNull(book.Add(Chat, "dextra-1", Guid.NewGuid(), "channel-a"));
        Assert.Null(book.Add(Chat, "dextra-1", Guid.NewGuid(), "channel-a"));
        Assert.Equal(DelegationBook.MaxPieces, book.Groups.Single().Pieces.Count);
        var mission = Guid.NewGuid();
        var small = new DelegationBook();
        small.Add(Chat, "dextra-1", mission, "channel-a");
        small.Add(Chat, "dextra-1", mission, "channel-a");
        Assert.Single(small.Groups.Single().Pieces);
    }

    [Fact]
    public void The_groups_kept_are_bounded_the_oldest_delivered_make_room_and_a_group_still_waiting_never_does()
    {
        var book = new DelegationBook();
        var first = Guid.NewGuid();
        book.Add(Guid.NewGuid(), "dextra-1", first, "channel-a");
        book.Observe(first, PieceState.Stopped);                                         // delivered, but its piece may go on: kept for now
        var accepted = 0;
        for (var n = 0; n < DelegationBook.MaxGroups + 5; n++)
            if (book.Add(Guid.NewGuid(), "dextra-1", Guid.NewGuid(), "channel-a") is not null) accepted++;
        Assert.Equal(DelegationBook.MaxGroups, book.Groups.Count);
        Assert.Equal(DelegationBook.MaxGroups, accepted);                                // the first one gave its place; the last five were refused
        Assert.DoesNotContain(book.Groups, g => g.Pieces.Any(p => p.Mission == first));
        Assert.All(book.Groups, g => Assert.False(g.Delivered));
    }

    [Fact]
    public void A_chat_that_is_gone_takes_its_groups_with_it()
    {
        var book = TwoPieces();
        book.Forget(Chat);
        Assert.Empty(book.Groups);
        book.Forget(Chat);
    }

    [Fact]
    public void The_stored_document_round_trips_keeps_unknown_fields_and_a_newer_file_is_left_as_it_is()
    {
        var book = TwoPieces();
        book.Observe(MissionA, PieceState.Finished);
        var document = book.ToJson()!;
        Assert.Equal(1, (int)document["version"]!);
        document["future"] = "kept";

        var loaded = new DelegationBook();
        loaded.Load(document);
        Assert.Equal(document["groups"]!.ToJsonString(), loaded.ToJson()!["groups"]!.ToJsonString());
        Assert.Equal(2, loaded.Groups.Single().Pieces.Count);
        Assert.Equal(PieceState.Finished, loaded.Groups.Single().Pieces[0].State);
        Assert.Equal("kept", (string)loaded.ToJson()!["future"]!);

        var newer = new DelegationBook();
        newer.Load(new JsonObject { ["version"] = DelegationBook.FormatVersion + 1, ["groups"] = new JsonArray() });
        Assert.True(newer.ReadOnly);
        Assert.Empty(newer.Groups);
        Assert.Null(newer.ToJson());                                                     // never written over
    }

    [Fact]
    public void A_line_that_is_not_a_group_is_skipped_and_never_repaired()
    {
        var good = TwoPieces().ToJson()!;
        var groups = good["groups"]!.AsArray();
        groups.Add(new JsonObject { ["id"] = "x", ["chat"] = "y", ["from"] = "dextra-1", ["pieces"] = new JsonArray() });                    // ids are not guids
        groups.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString("D"), ["chat"] = Chat.ToString("D"), ["from"] = "..\\x", ["pieces"] = new JsonArray() });   // not an assistant id
        groups.Add("text");
        groups.Add(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"), ["chat"] = Chat.ToString("D"), ["from"] = "dextra-1",
            ["pieces"] = new JsonArray(new JsonObject { ["mission"] = MissionA.ToString("D"), ["astur"] = "channel-a", ["state"] = "Sideways" }),
        });
        var book = new DelegationBook();
        book.Load(good);
        Assert.Single(book.Groups);
        book.Load(null);
        Assert.Empty(book.Groups);
    }

    [Fact]
    public void Changes_are_announced_to_the_writer()
    {
        var changes = 0;
        var book = new DelegationBook();
        book.Changed += () => changes++;
        book.Add(Chat, "dextra-1", MissionA, "channel-a");
        book.Observe(MissionA, PieceState.Finished);
        book.Observe(MissionA, PieceState.Finished);                                     // no change, no announcement
        Assert.Equal(2, changes);
    }
}
