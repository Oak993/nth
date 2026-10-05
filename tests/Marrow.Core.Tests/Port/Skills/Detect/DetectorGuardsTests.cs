using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: the guards of ONDEMAND-PLAN.md 5.3: "no offer when the item is installed and healthy, found on this PC, already offered in this chat, declined in
// this chat ('Don't ask again in this chat' is the decline's meaning), or currently downloading (the offer then attaches to that download's progress)."
// Installed and found come from the AddonsState (SK-7), the per-chat facts from its declined list and from the set of items already offered in the chat that the
// caller passes. The order when several apply, a damaged or failed item, and a removal are ENGINEERING DECISIONS (ED-296).
[Trait("Spec", "DL-10")]
public sealed class DetectorGuardsTests
{
    private static readonly CommandHeld Soffice = new("soffice a.docx");

    private static Detection Held(AddonsState? state = null, string chat = "chat-1", params string[] offered) =>
        DetectFixtures.Run(DetectFixtures.Ctx(state, chat, offered), Soffice);

    private static Finding One(Detection detection) => Assert.Single(detection.Findings);

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void With_no_guard_in_the_way_the_missing_tool_is_offered()
    {
        var finding = One(Held());
        Assert.Equal(FindingVerdict.Offer, finding.Verdict);
        Assert.Null(finding.Reason);
        Assert.Equal(["T3"], Held().Card!.Requested);
        Assert.Empty(Held().AttachTo);
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(ItemState.Installed)]
    [InlineData(ItemState.UpdateAvailable)]
    public void An_installed_and_healthy_item_is_not_offered(ItemState state)
    {
        var record = state == ItemState.Installed ? DetectFixtures.Has("T3") : new ItemRecord("T3", ItemState.UpdateAvailable, "26.2.6", "26.8.1", null);
        var detection = Held(DetectFixtures.State(record));
        Assert.Equal(SuppressReason.InstalledAndHealthy, One(detection).Reason);
        Assert.Equal(FindingVerdict.Suppressed, One(detection).Verdict);
        Assert.Null(detection.Card);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_update_in_flight_or_a_failed_update_leaves_the_old_version_healthy_and_a_repair_does_not()
    {
        foreach (var state in new[] { ItemState.Queued, ItemState.Downloading, ItemState.Checking })
            Assert.Equal(SuppressReason.InstalledAndHealthy, One(Held(DetectFixtures.State(new ItemRecord("T3", state, "26.2.6", "26.8.1", null)))).Reason);
        Assert.Equal(SuppressReason.InstalledAndHealthy, One(Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Failed, "26.2.6", "26.8.1", "boom")))).Reason);
        // A repair downloads the installed pin itself: the install is damaged, so the running download is what the offer attaches to.
        Assert.Equal(FindingVerdict.AttachToDownload, One(Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Downloading, "26.2.6", "26.2.6", null)))).Verdict);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_tool_found_on_this_PC_is_not_offered_and_a_found_answer_for_another_tool_is_no_help()
    {
        var found = AddonsState.Empty.MergeFound(DetectFixtures.Catalogue, ["T3"], [new FoundOnPc("T3", @"C:\Program Files\LibreOffice\program\soffice.exe")]);
        var detection = Held(found);
        Assert.Equal(SuppressReason.FoundOnPc, One(detection).Reason);
        Assert.Null(detection.Card);
        var other = AddonsState.Empty.MergeFound(DetectFixtures.Catalogue, ["T4"], [new FoundOnPc("T4", @"C:\poppler\pdftoppm.exe")]);
        Assert.Equal(FindingVerdict.Offer, One(Held(other)).Verdict);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_already_offered_in_this_chat_is_not_offered_again_and_another_item_still_is()
    {
        Assert.Equal(SuppressReason.AlreadyOffered, One(Held(null, "chat-1", "T3")).Reason);
        Assert.Equal(FindingVerdict.Offer, One(Held(null, "chat-1", "T4")).Verdict);
        Assert.Equal(FindingVerdict.Offer, One(Held(null, "chat-1")).Verdict);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_decline_holds_for_the_chat_that_made_it_only()
    {
        var declined = AddonsState.Empty.Decline("chat-1", "T3");
        Assert.Equal(SuppressReason.Declined, One(Held(declined, "chat-1")).Reason);
        Assert.Equal(FindingVerdict.Offer, One(Held(declined, "chat-2")).Verdict);
        Assert.Equal(FindingVerdict.Offer, One(Held(AddonsState.Empty.Decline("chat-1", "T4"), "chat-1")).Verdict);
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(ItemState.Queued)]
    [InlineData(ItemState.Downloading)]
    [InlineData(ItemState.Checking)]
    public void An_item_whose_first_download_is_running_attaches_the_offer_to_that_download(ItemState state)
    {
        var detection = Held(DetectFixtures.State(new ItemRecord("T3", state, null, "26.2.6", null)));
        Assert.Equal(FindingVerdict.AttachToDownload, One(detection).Verdict);
        Assert.Equal(["T3"], detection.AttachTo);
        Assert.Null(detection.Card);
        Assert.Empty(DetectFixtures.Offered(detection));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_download_running_for_an_offer_made_in_this_chat_still_attaches_it_instead_of_staying_silent()
    {
        var running = DetectFixtures.State(new ItemRecord("T3", ItemState.Downloading, null, "26.2.6", null));
        Assert.Equal(FindingVerdict.AttachToDownload, One(Held(running, "chat-1", "T3")).Verdict);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_damaged_or_failed_item_is_not_there_so_it_is_offered_for_repair_or_retry_unless_already_offered()
    {
        Assert.Equal(FindingVerdict.Offer, One(Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Damaged, "26.2.6", null, null)))).Verdict);
        Assert.Equal(FindingVerdict.Offer, One(Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Failed, null, "26.2.6", "boom")))).Verdict);
        Assert.Equal(SuppressReason.AlreadyOffered, One(Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Failed, null, "26.2.6", "boom")), "chat-1", "T3")).Reason);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_being_removed_is_not_offered()
    {
        var detection = Held(DetectFixtures.State(new ItemRecord("T3", ItemState.Removing, "26.2.6", null, null)));
        Assert.Equal(SuppressReason.Removing, One(detection).Reason);
        Assert.Null(detection.Card);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void When_several_guards_apply_the_first_in_this_order_is_the_reason_installed_found_removing_attach_declined_offered()
    {
        var found = new FoundOnPc("T3", @"C:\LO\soffice.exe");
        AddonsState Build(ItemRecord? item, bool isFound, bool declined) =>
            AddonsState.Restore(item is null ? [] : [item], [], declined ? [new ChatDeclined("chat-1", ["T3"])] : [], isFound ? [found] : []);
        var installed = DetectFixtures.Has("T3");
        Assert.Equal(SuppressReason.InstalledAndHealthy, One(Held(Build(installed, true, true), "chat-1", "T3")).Reason);
        Assert.Equal(SuppressReason.FoundOnPc, One(Held(Build(null, true, true), "chat-1", "T3")).Reason);
        Assert.Equal(SuppressReason.FoundOnPc, One(Held(Build(new ItemRecord("T3", ItemState.Removing, "1", null, null), true, false))).Reason);
        Assert.Equal(SuppressReason.Removing, One(Held(Build(new ItemRecord("T3", ItemState.Removing, "1", null, null), false, true), "chat-1", "T3")).Reason);
        Assert.Equal(FindingVerdict.AttachToDownload, One(Held(Build(new ItemRecord("T3", ItemState.Downloading, null, "2", null), false, true), "chat-1", "T3")).Verdict);
        Assert.Equal(SuppressReason.Declined, One(Held(Build(null, false, true), "chat-1", "T3")).Reason);
        Assert.Equal(SuppressReason.AlreadyOffered, One(Held(Build(null, false, false), "chat-1", "T3")).Reason);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_guards_apply_to_every_signal_alike()
    {
        var state = DetectFixtures.State(DetectFixtures.Has("T3"));
        var context = DetectFixtures.Ctx(state);
        foreach (var ev in new ToolEvent[]
        {
            new CommandHeld("soffice a"),
            DetectFixtures.Failed("soffice a", 127, "bash: soffice: command not found", false),
            new ToolRequested("soffice", "convert"),
        })
        {
            var detection = DetectFixtures.Run(context, ev);
            Assert.Equal(SuppressReason.InstalledAndHealthy, One(detection).Reason);
            Assert.Null(detection.Card);
        }
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_context_that_cannot_be_trusted_is_refused_not_guessed_at()
    {
        var ok = DetectFixtures.Ctx();
        Assert.Throws<ArgumentException>(() => DetectFixtures.Run(ok with { ChatId = "" }, Soffice));
        Assert.Throws<ArgumentException>(() => DetectFixtures.Run(ok with { ChatId = "  " }, Soffice));
        Assert.Throws<ArgumentNullException>(() => MissingToolDetector.Detect(null!, [Soffice]));
        Assert.Throws<ArgumentNullException>(() => MissingToolDetector.Detect(ok, (IEnumerable<ToolEvent>)null!));
        Assert.Throws<ArgumentException>(() => MissingToolDetector.Detect(ok, new ToolEvent[] { null! }));
        Assert.Throws<ArgumentNullException>(() => DetectFixtures.Run(ok with { Catalogue = null! }, Soffice));
        Assert.Throws<ArgumentNullException>(() => DetectFixtures.Run(ok with { State = null! }, Soffice));
        Assert.Throws<ArgumentNullException>(() => DetectFixtures.Run(ok with { OfferedInChat = null! }, Soffice));
    }
}
