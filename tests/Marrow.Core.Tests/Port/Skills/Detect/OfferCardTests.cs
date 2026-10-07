using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: "one card for two missing items". ONDEMAND-PLAN.md 5.3: "One offer at a time per chat; a second missing item joins the same card as 'also
// needs'"; 5.4: "An item that needs others (P1 needs T1) is offered as one download with its combined size"; 2.4: "a download of a skill set offers its missing needs
// in the same confirmation ('Also needs Python and LibreOffice: 369 MB more')". The card names what was found missing (Requested) and everything to download
// to satisfy them (Items, needs first, each once) with the plan's approximate sizes added up.
[Trait("Spec", "DL-10")]
public sealed class OfferCardTests
{
    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Two_missing_items_found_together_share_one_card_with_the_first_found_first_and_a_combined_size()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(), new CommandHeld("soffice a.docx && pdftoppm -png a.pdf out"));
        var card = detection.Card!;
        Assert.Equal(["T3", "T4"], card.Requested);
        Assert.Equal(["T3", "T4"], card.Items);
        Assert.Equal(356.0 + 43.7, card.ApproxSizeMb!.Value, 6);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Items_found_by_different_events_of_one_batch_still_make_one_card()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(),
            new CommandHeld("pandoc a.docx -t plain"),
            DetectFixtures.Failed("qpdf --check a.pdf", 127, "bash: qpdf: command not found", false),
            new ToolRequested("gh", "list pull requests"));
        Assert.Equal(["T6", "T5", "T8"], detection.Card!.Requested);
        Assert.Equal(["T6", "T5", "T8"], detection.Card.Items);
        Assert.Equal(new[] { DetectedSignal.HeldCommand, DetectedSignal.FailedCommand, DetectedSignal.ModelRequest }, detection.Findings.Select(f => f.Signal));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_that_needs_others_brings_the_missing_ones_first_in_dependency_order_and_all_sizes_are_added()
    {
        var card = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("python s.py", 1, "ModuleNotFoundError: No module named 'docx'", false)).Card!;
        Assert.Equal(["P1"], card.Requested);
        Assert.Equal(["T1", "T1p", "P1"], card.Items);
        Assert.Equal(12.6 + 1.8 + 5.3, card.ApproxSizeMb!.Value, 6);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Needs_that_are_installed_found_or_already_on_their_way_are_left_out_of_the_card()
    {
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1"))), new CommandHeld("python -m docx"));
        Assert.Equal(["T1p", "P1"], detection.Card!.Items);
        Assert.Equal(1.8 + 5.3, detection.Card.ApproxSizeMb!.Value, 6);
        var both = DetectFixtures.Run(DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T1"), DetectFixtures.Has("T1p"))), new CommandHeld("python -m docx"));
        Assert.Equal(["P1"], both.Card!.Items);
        var found = AddonsState.Empty.MergeFound(DetectFixtures.Catalogue, ["T1"], [new FoundOnPc("T1", @"C:\Python314\python.exe")]);
        Assert.Equal(["T1p", "P1"], DetectFixtures.Run(DetectFixtures.Ctx(found), new CommandHeld("python -m docx")).Card!.Items);
        var coming = DetectFixtures.State(new ItemRecord("T1", ItemState.Downloading, null, "3.14.8", null));
        Assert.Equal(["T1p", "P1"], DetectFixtures.Run(DetectFixtures.Ctx(coming), new CommandHeld("python -m docx")).Card!.Items);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_need_shared_by_two_requested_items_is_in_the_card_once()
    {
        var card = DetectFixtures.Run(DetectFixtures.Ctx(),
            DetectFixtures.Failed("python make.py", 1, "ModuleNotFoundError: No module named 'docx'", false),
            DetectFixtures.Failed("python s.py", 1, "ModuleNotFoundError: No module named 'pypdf'", false)).Card!;
        Assert.Equal(["P1", "P2"], card.Requested);
        Assert.Equal(["T1", "T1p", "P1", "P2"], card.Items);
        Assert.Equal(12.6 + 1.8 + 5.3 + 17.2, card.ApproxSizeMb!.Value, 6);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_set_requested_by_the_model_brings_its_missing_needs_and_a_size_the_plan_does_not_know_is_unknown_not_zero()
    {
        var card = DetectFixtures.Run(DetectFixtures.Ctx(), new ToolRequested("A2", "document skills")).Card!;
        Assert.Equal(["A2"], card.Requested);
        Assert.Equal("A2", card.Items[^1]);
        Assert.Equal(["P1", "P2", "P6", "T1", "T1p", "T2", "T3", "T4", "T5", "T6"], card.Items.Take(card.Items.Count - 1).OrderBy(i => i, StringComparer.Ordinal));
        var items = card.Items.ToList();
        Assert.True(items.IndexOf("T1") < items.IndexOf("T1p"));
        Assert.True(items.IndexOf("T1p") < items.IndexOf("P1"));
        Assert.True(items.IndexOf("T2") < items.IndexOf("P6"));
        Assert.Null(card.ApproxSizeMb);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_suppressed_or_attached_item_is_not_part_of_the_card_but_its_neighbour_still_is()
    {
        var state = DetectFixtures.State(DetectFixtures.Has("T3"));
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(state), new CommandHeld("soffice a.docx && pdftoppm -png a.pdf out"));
        Assert.Equal(["T4"], detection.Card!.Requested);
        Assert.Equal(["T4"], detection.Card.Items);
        Assert.Equal([FindingVerdict.Suppressed, FindingVerdict.Offer], detection.Findings.Select(f => f.Verdict));
        var attach = DetectFixtures.State(new ItemRecord("T3", ItemState.Downloading, null, "26.2.6", null));
        var mixed = DetectFixtures.Run(DetectFixtures.Ctx(attach), new CommandHeld("soffice a.docx && pdftoppm -png a.pdf out"));
        Assert.Equal(["T4"], mixed.Card!.Items);
        Assert.Equal(["T3"], mixed.AttachTo);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void No_missing_item_means_no_card_and_the_result_is_the_same_whatever_the_event_list_is_called_with()
    {
        Assert.Null(DetectFixtures.Run(DetectFixtures.Ctx()).Card);
        Assert.Empty(DetectFixtures.Run(DetectFixtures.Ctx()).Findings);
        var one = MissingToolDetector.Detect(DetectFixtures.Ctx(), new CommandHeld("soffice a"));
        var many = MissingToolDetector.Detect(DetectFixtures.Ctx(), [new CommandHeld("soffice a")]);
        Assert.Equal(one.Card!.Items, many.Card!.Items);
        Assert.Equal(one.Findings, many.Findings);
    }
}
