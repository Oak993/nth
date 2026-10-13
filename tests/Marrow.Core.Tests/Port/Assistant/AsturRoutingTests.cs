using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.AsturTeamSample;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.3 "Talking to one directly"; ED-NEW-dextra-as16-6): name routing. "@Channel A, ..." in text and
// "Channel A, ..." by voice go to that Astur's home thread; a name that fits two is asked about ("Which one?"), never guessed; a message
// that names two Asturs stays with Dextra; a name that matches nothing routes nowhere.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-6")]
public sealed class AsturRoutingTests
{
    private static AsturRoute Typed(string text, IReadOnlyList<AssistantProfile>? team = null) => AsturRouting.Route(text, team ?? Channels());

    private static AsturRoute Said(string text, IReadOnlyList<AssistantProfile>? team = null) => AsturRouting.Route(text, team ?? Channels(), spoken: true);

    [Theory]
    [InlineData("@Channel A, start the next video", "start the next video")]
    [InlineData("@channel a start the next video", "start the next video")]
    [InlineData("  @CHANNEL   A: start the next video  ", "start the next video")]
    [InlineData("@Channel A", "")]
    [InlineData("@Channel A - what is new?", "what is new?")]
    public void A_typed_mention_at_the_start_goes_to_that_Astur_with_the_name_taken_off(string text, string message)
    {
        var route = Typed(text);
        Assert.Equal(AsturRouteKind.Astur, route.Kind);
        Assert.Equal("Channel A", route.Target!.Name);
        Assert.Equal(message, route.Message);
    }

    [Theory]
    [InlineData("Channel A, start the next video", "start the next video")]
    [InlineData("channel a: start the next video", "start the next video")]
    [InlineData("Hey Channel A start the next video", "start the next video")]
    [InlineData("OK Channel A, start the next video", "start the next video")]
    [InlineData("Ask Channel A to start the next video", "start the next video")]
    [InlineData("tell channel a that the thumbnail is ready", "the thumbnail is ready")]
    [InlineData("Channel A", "")]
    [InlineData("Channel A.", "")]
    public void A_spoken_name_first_goes_to_that_Astur(string text, string message)
    {
        var route = Said(text);
        Assert.Equal(AsturRouteKind.Astur, route.Kind);
        Assert.Equal("Channel A", route.Target!.Name);
        Assert.Equal(message, route.Message);
    }

    [Theory]
    [InlineData("Channel A start the next video")]       // no comma, no opener: could be any sentence
    [InlineData("Please tell me about Channel A")]
    [InlineData("Start the next video on Channel A")]
    [InlineData("Channel A and Channel B stats please")]
    public void A_name_that_is_not_called_out_does_not_route_by_voice(string text) =>
        Assert.Equal(AsturRouteKind.None, Said(text).Kind);

    [Fact]
    public void A_common_word_that_is_an_Astur_name_is_not_a_call_unless_it_stands_apart()
    {
        var team = Team(Astur("Store", AsturShape.Diamond));
        var sentence = Said("Store the files in the shared folder", team);
        Assert.Equal(AsturRouteKind.None, sentence.Kind);
        Assert.Equal("Store the files in the shared folder", sentence.Message);
        Assert.Equal(AsturRouteKind.Astur, Said("Store, check the prices", team).Kind);
        Assert.Equal(AsturRouteKind.None, Typed("Store the files", team).Kind);                    // typed text needs the @
        Assert.Equal(AsturRouteKind.Astur, Typed("@Store check the prices", team).Kind);
    }

    [Fact]
    public void A_name_that_fits_two_Asturs_asks_which_one_and_carries_the_rest_of_the_message()
    {
        var route = Typed("@Channel start the next video");
        Assert.Equal(AsturRouteKind.Which, route.Kind);
        Assert.Equal(["Channel A", "Channel B"], route.Candidates.Select(c => c.Name));
        Assert.Equal("start the next video", route.Message);
        Assert.Null(route.Target);
        Assert.Equal("Which one: Channel A or Channel B?", AsturRouting.Question(route.Candidates));
        Assert.Equal(AsturRouteKind.Which, Said("Channel, start the next video").Kind);
        Assert.Equal(AsturRouteKind.Which, Said("Hey channel start the next video").Kind);
    }

    [Fact]
    public void The_question_names_three_or_more_with_commas_and_one_with_a_plain_check()
    {
        var team = Team(Astur("Channel A", AsturShape.Squircle), Astur("Channel B", AsturShape.Hexagon), Astur("Channel C", AsturShape.Diamond));
        Assert.Equal("Which one: Channel A, Channel B or Channel C?", AsturRouting.Question(Typed("@channel go", team).Candidates));
        Assert.Equal("Do you mean Channel A?", AsturRouting.Question([team[1]]));
        Assert.Equal("Which one?", AsturRouting.Question([]));
    }

    [Fact]
    public void A_whole_name_beats_a_name_it_begins_and_the_longest_whole_name_wins()
    {
        var team = Team(Astur("Channel", AsturShape.Squircle, "channel"), Astur("Channel A", AsturShape.Hexagon, "channel-a"));
        Assert.Equal("Channel A", Typed("@Channel A hello", team).Target!.Name);
        var plain = Typed("@Channel hello", team);
        Assert.Equal(AsturRouteKind.Astur, plain.Kind);
        Assert.Equal("Channel", plain.Target!.Name);
        Assert.Equal("hello", plain.Message);
    }

    [Fact]
    public void A_name_that_only_begins_one_name_is_asked_about_not_guessed_and_a_name_that_matches_nothing_is_no_route()
    {
        var one = Team(Astur("Channel A", AsturShape.Squircle));
        var partial = Typed("@Channel go on", one);
        Assert.Equal(AsturRouteKind.Which, partial.Kind);
        Assert.Equal(["Channel A"], partial.Candidates.Select(c => c.Name));
        Assert.Equal("go on", partial.Message);
        Assert.Equal("Do you mean Channel A?", AsturRouting.Question(partial.Candidates));
        // "Channel A" typed after it was retired must not land on "Channel B".
        var afterRetirement = Typed("@Channel A hello", Team(Astur("Channel A", AsturShape.Squircle, null, true), Astur("Channel B", AsturShape.Hexagon)));
        Assert.Equal(AsturRouteKind.Which, afterRetirement.Kind);
        Assert.Equal(["Channel B"], afterRetirement.Candidates.Select(c => c.Name));
        var nobody = Typed("@Nobody go on", one);
        Assert.Equal(AsturRouteKind.None, nobody.Kind);
        Assert.Equal("@Nobody go on", nobody.Message);
    }

    [Fact]
    public void An_at_sign_in_an_address_or_inside_a_sentence_changes_nothing()
    {
        Assert.Equal(AsturRouteKind.None, Typed("mail kiran@channel.example about it").Kind);
        var inside = Typed("Please ask @Channel A to start the next video");
        Assert.Equal(AsturRouteKind.None, inside.Kind);
        Assert.Equal("Please ask @Channel A to start the next video", inside.Message);
        Assert.Equal(AsturRouteKind.None, Typed("").Kind);
        Assert.Equal(AsturRouteKind.None, Typed("   ").Kind);
        Assert.Equal(AsturRouteKind.None, Typed("@Channel A hi", Team()).Kind);                  // a team with no Astur routes nowhere
    }

    [Fact]
    public void A_message_that_names_two_Asturs_spans_areas_and_stays_with_Dextra()
    {
        const string text = "@Channel A and @Channel B: bring me the stats of both";
        var route = Typed(text);
        Assert.Equal(AsturRouteKind.None, route.Kind);
        Assert.Equal(text, route.Message);
        Assert.Equal(AsturRouteKind.None, Typed("@Channel A, compare with what @Channel B did last week").Kind);
        Assert.Equal(AsturRouteKind.Astur, Typed("@Channel A, same as @Channel A did last week").Kind);     // the same Astur twice is still one
    }

    [Fact]
    public void An_alias_routes_like_the_name_and_a_retired_Astur_is_never_routed_to()
    {
        var team = Team(Astur("Channel A", AsturShape.Squircle, null, false, "the news channel", "CA"), Astur("Old", AsturShape.Star, null, true));
        Assert.Equal("Channel A", Typed("@CA hello", team).Target!.Name);
        Assert.Equal("Channel A", Said("The news channel, any news?", team).Target!.Name);
        Assert.Equal(AsturRouteKind.None, Typed("@Old hello", team).Kind);
    }

    [Fact]
    public void Names_are_compared_without_case_accents_or_apostrophes_and_other_scripts_keep_their_marks()
    {
        var team = Team(Astur("Café Désk", AsturShape.Squircle), Astur("Kiran's", AsturShape.Hexagon), Astur("किरण", AsturShape.Diamond), Astur("करण", AsturShape.Star));
        Assert.Equal("Café Désk", Typed("@cafe desk go", team).Target!.Name);
        Assert.Equal("Kiran's", Typed("@KIRANS go", team).Target!.Name);
        Assert.Equal("किरण", Typed("@किरण शुरू करो", team).Target!.Name);
        Assert.Equal("करण", Typed("@करण शुरू करो", team).Target!.Name);               // two Hindi names that differ in a vowel sign stay two names
        Assert.Equal(AsturRouting.Key("Café  Désk"), AsturRouting.Key("cafe desk"));
        Assert.NotEqual(AsturRouting.Key("किरण"), AsturRouting.Key("करण"));
    }

    [Fact]
    public void Dextra_is_addressed_by_its_own_name_and_an_Astur_named_like_an_opener_still_works()
    {
        var dextra = Typed("@Dextra what is going on");
        Assert.Equal(AsturRouteKind.Dextra, dextra.Kind);
        Assert.Equal("what is going on", dextra.Message);
        Assert.Equal(AsturRouteKind.Dextra, Said("Dextra, stop everything").Kind);
        var team = Team(Astur("Ask", AsturShape.Clover));
        Assert.Equal("Ask", Said("Ask, check the prices", team).Target!.Name);
    }

    [Fact]
    public void Only_the_start_of_a_very_long_message_is_read_and_the_whole_rest_goes_with_it()
    {
        var log = string.Join(' ', Enumerable.Repeat("error at line 42 from kiran@channel.example and the rest of the log", 40_000));
        var routed = Typed("@Channel A, look at this: " + log);
        Assert.Equal(AsturRouteKind.Astur, routed.Kind);
        Assert.Equal("Channel A", routed.Target!.Name);
        Assert.Equal("look at this: " + log, routed.Message);                                              // nothing of the message is cut
        Assert.True(log.Length > 100 * AsturRouting.RoutingWindow);
        // A second name far down a pasted text is not read: the message still goes to the Astur named first.
        var late = Typed("@Channel A, report" + new string(' ', AsturRouting.RoutingWindow) + "and @Channel B too");
        Assert.Equal(AsturRouteKind.Astur, late.Kind);
        Assert.Equal(AsturRouteKind.None, Typed("@Channel A and @Channel B, report").Kind);
        Assert.Equal(AsturRouteKind.None, Said(new string('x', 3_000_000)).Kind);
        Assert.Equal(AsturRouteKind.None, Typed(new string(' ', 3_000) + "@Channel A hello").Kind);        // past the window it is not read
    }

    [Fact]
    public void Routing_never_changes_a_message_it_does_not_route()
    {
        foreach (var text in new[] { "hello there", "@", "@@", "@ Channel A", "what about channel a?" })
        {
            var route = Said(text);
            Assert.Equal(AsturRouteKind.None, route.Kind);
            Assert.Equal(text, route.Message);
        }
    }
}
