using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-7; DESIGN.md 2.12). Precise targeting: identity with a content
// fingerprint, re-resolution in a fresh tree, recycled containers, look-alike elements, counts in names, locators from the
// model, the element under a point, and element numbers that stay with their element across looks.
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class PcElementMatchTests
{
    private static PcUiNode Button(string id, string name, int x, string? automationId = null) =>
        new(id, "button", name) { Actions = PcActions.Invoke, Bounds = new PcRect(x, 10, 50, 20), AutomationId = automationId };

    private static PcUiNode Item(string id, string name, int y) =>
        new(id, "list item", name) { Actions = PcActions.Select, Bounds = new PcRect(0, y, 300, 20) };

    private static PcUiNode Group(string id, string name, params PcUiNode[] children) =>
        new(id, "group", name) { Bounds = new PcRect(0, 0, 400, 100), Children = children };

    private static PcTree Window(params PcUiNode[] children) =>
        TreeFormatter.Format(new PcUiNode("w", "window", "Mail") { Bounds = new PcRect(0, 0, 800, 600), Children = children });

    private static int At(PcTree tree, string runtimeId) => tree.IndexOf(runtimeId)!.Value;

    [Fact]
    public void An_element_is_found_again_by_its_runtime_id_even_after_others_move()
    {
        var before = Window(Button("1.1", "Send", 10), Button("1.2", "Cancel", 70));
        var identity = PcElementMatch.Identity(before, At(before, "1.2"));
        var after = Window(Button("1.0", "New", 0), Button("1.1", "Send", 60), Button("1.2", "Cancel", 120));
        var match = PcElementMatch.Resolve(after, identity);
        Assert.True(match.Found);
        Assert.Equal("Cancel", after.Elements[match.Index].Name);
    }

    [Fact]
    public void A_renamed_element_is_gone_and_says_what_is_there_now()
    {
        var before = Window(Button("1.1", "Play", 10));
        var match = PcElementMatch.Resolve(Window(Button("1.1", "Pause", 10)), PcElementMatch.Identity(before, At(before, "1.1")));
        Assert.Equal(PcMatchKind.Gone, match.Kind);
        Assert.Contains("button \"Play\" is now button \"Pause\"", match.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recycled_container_is_never_taken_for_the_item_it_used_to_show()
    {
        // A virtualised list scrolled: container 5.2 now shows "Invoice 9", and "Invoice 7" moved to container 5.4.
        var before = Window(Item("5.1", "Invoice 6", 0), Item("5.2", "Invoice 7", 20), Item("5.3", "Invoice 8", 40));
        var identity = PcElementMatch.Identity(before, At(before, "5.2"));
        var after = Window(Item("5.2", "Invoice 9", 0), Item("5.3", "Invoice 8", 20), Item("5.4", "Invoice 7", 40));
        var match = PcElementMatch.Resolve(after, identity);
        Assert.True(match.Found);
        Assert.Equal("5.4", after.Elements[match.Index].RuntimeId);
        // Gone from view entirely: refused, never the container's new item.
        var scrolledAway = Window(Item("5.2", "Invoice 9", 0), Item("5.3", "Invoice 10", 20));
        var gone = PcElementMatch.Resolve(scrolledAway, identity);
        Assert.Equal(PcMatchKind.Gone, gone.Kind);
    }

    [Fact]
    public void Look_alike_buttons_are_told_apart_by_their_ancestors()
    {
        var before = Window(Group("g1", "Reply", Button("1.1", "Send", 10)), Group("g2", "Forward", Button("1.2", "Send", 10)));
        var identity = PcElementMatch.Identity(before, At(before, "1.2"));
        Assert.Equal(new[] { "group:forward" }, identity.Path);
        // The app made new RuntimeIds (a re-render): the ancestor path still picks the right one.
        var after = Window(Group("g1", "Reply", Button("9.1", "Send", 10)), Group("g2", "Forward", Button("9.2", "Send", 10)));
        var match = PcElementMatch.Resolve(after, identity);
        Assert.True(match.Found);
        Assert.Equal("9.2", after.Elements[match.Index].RuntimeId);
    }

    [Fact]
    public void Two_identical_matches_are_ambiguous_never_guessed()
    {
        var before = Window(Group("g1", "Toolbar", Button("1.1", "Send", 10)));
        var identity = PcElementMatch.Identity(before, At(before, "1.1"));
        var after = Window(Group("g1", "Toolbar", Button("9.1", "Send", 10), Button("9.2", "Send", 70)));
        var match = PcElementMatch.Resolve(after, identity);
        Assert.Equal(PcMatchKind.Ambiguous, match.Kind);
        Assert.Equal(2, match.Candidates.Count);
    }

    [Fact]
    public void A_vanished_element_is_gone()
    {
        var before = Window(Button("1.1", "Send", 10));
        var match = PcElementMatch.Resolve(Window(Button("1.2", "Cancel", 10)), PcElementMatch.Identity(before, At(before, "1.1")));
        Assert.Equal(PcMatchKind.Gone, match.Kind);
    }

    [Fact]
    public void An_automation_id_finds_the_element_when_its_runtime_id_changed()
    {
        var before = Window(Button("1.1", "OK", 10, "okButton"), Button("1.2", "OK", 70, "okAll"));
        var identity = PcElementMatch.Identity(before, At(before, "1.2"));
        var after = Window(Button("7.1", "OK", 10, "okButton"), Button("7.2", "OK", 70, "okAll"));
        Assert.Equal("7.2", after.Elements[PcElementMatch.Resolve(after, identity).Index].RuntimeId);
    }

    [Fact]
    public void A_count_in_a_name_may_change_but_only_one_element_may_fit()
    {
        var before = Window(Item("1.1", "Inbox (12)", 0), Item("1.2", "Sent", 20));
        var identity = PcElementMatch.Identity(before, At(before, "1.1"));
        var after = Window(Item("8.1", "Inbox (13)", 0), Item("8.2", "Sent", 20));
        var match = PcElementMatch.Resolve(after, identity);
        Assert.True(match.Found);
        Assert.Equal("Inbox (13)", after.Elements[match.Index].Name);
        var two = Window(Item("8.1", "Inbox (13)", 0), Item("8.3", "Inbox (2)", 20));
        Assert.Equal(PcMatchKind.Ambiguous, PcElementMatch.Resolve(two, identity).Kind);
        Assert.Equal("inbox", PcElementMatch.Fuzzy(" INBOX  (1,204) "));
    }

    [Fact]
    public void A_locator_from_the_model_matches_exactly_once_or_says_why_not()
    {
        var tree = Window(Button("1.1", "Save", 10), Button("1.2", "Save as", 70), new PcUiNode("1.3", "text", "Save") { Bounds = new PcRect(0, 50, 40, 20) });
        // A label and a button share the name: the one that can be acted on wins.
        var save = PcElementMatch.Find(tree, new PcLocator("  save "));
        Assert.True(save.Found);
        Assert.Equal("1.1", tree.Elements[save.Index].RuntimeId);
        Assert.Equal(PcMatchKind.Gone, PcElementMatch.Find(tree, new PcLocator("Sav")).Kind);
        var twins = Window(Button("1.1", "Delete", 10), Button("1.2", "Delete", 70));
        var both = PcElementMatch.Find(twins, new PcLocator("Delete", "button"));
        Assert.Equal(PcMatchKind.Ambiguous, both.Kind);
        Assert.Equal("2 elements match button \"Delete\"", both.Detail);
        Assert.Equal("1.2", twins.Elements[PcElementMatch.Find(twins, new PcLocator("Delete", Nth: 2)).Index].RuntimeId);
        Assert.Equal(PcMatchKind.Gone, PcElementMatch.Find(twins, new PcLocator("Delete", Nth: 3)).Kind);
    }

    [Fact]
    public void Within_and_id_pick_one_of_look_alike_buttons()
    {
        var tree = Window(Group("g1", "Reply", Button("1.1", "Send", 10, "replySend")), Group("g2", "Forward", Button("1.2", "Send", 10, "fwdSend")));
        Assert.Equal("1.2", tree.Elements[PcElementMatch.Find(tree, new PcLocator("Send", Within: "forward")).Index].RuntimeId);
        Assert.Equal("1.1", tree.Elements[PcElementMatch.Find(tree, new PcLocator(null, Id: "replySend")).Index].RuntimeId);
        Assert.Equal(PcMatchKind.Ambiguous, PcElementMatch.Find(tree, new PcLocator("Send")).Kind);
    }

    [Fact]
    public void Urdu_and_hindi_names_match_in_any_unicode_form()
    {
        var urdu = "محفوظ کریں";
        var tree = Window(Button("1.1", urdu, 10), Button("1.2", "सहेजें", 70));
        Assert.Equal("1.1", tree.Elements[PcElementMatch.Find(tree, new PcLocator(urdu)).Index].RuntimeId);
        // The same Hindi word written with a decomposed form still matches.
        var decomposed = "सहेजें".Normalize(System.Text.NormalizationForm.FormD);
        Assert.Equal("1.2", tree.Elements[PcElementMatch.Find(tree, new PcLocator(decomposed)).Index].RuntimeId);
    }

    [Fact]
    public void The_element_under_a_point_is_the_smallest_one_that_has_an_action()
    {
        var tree = Window(Group("g", "Panel", Button("1.1", "Send", 10)));
        Assert.Equal("1.1", tree.Elements[PcElementMatch.AtPoint(tree, new PcPoint(20, 15))].RuntimeId);
        Assert.Equal(0, PcElementMatch.AtPoint(tree, new PcPoint(700, 500)));
    }

    [Fact]
    public void Element_numbers_stay_with_their_element_across_looks()
    {
        var numbering = new PcNumbering();
        var first = numbering.Apply(Window(Button("1.1", "Send", 10), Button("1.2", "Cancel", 70)));
        var cancel = At(first, "1.2");
        var second = numbering.Apply(Window(Button("1.0", "New", 0), Button("1.1", "Send", 60), Button("1.2", "Cancel", 120)));
        Assert.Equal(cancel, second.IndexOf("1.2"));
        Assert.Equal(first.IndexOf("1.1"), second.IndexOf("1.1"));
        Assert.Equal(1, second.IndexOf("w"));
        // The new element got a number nobody had, and a removed one's number is never reused.
        var third = numbering.Apply(Window(Button("1.3", "Retry", 0)));
        Assert.DoesNotContain(At(third, "1.3"), new[] { At(first, "1.1"), cancel, At(second, "1.0") });
        Assert.Contains("[" + cancel.ToString(System.Globalization.CultureInfo.InvariantCulture) + "] ", second.Render(), StringComparison.Ordinal);
    }
}
