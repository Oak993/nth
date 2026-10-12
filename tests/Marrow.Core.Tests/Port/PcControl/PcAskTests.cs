// PORT-ONLY: pill v2's pc_ask wire (OD-0145, ED-NEW-pcctl-12; DESIGN.md 10.4, 10.9): its arguments and their limits, plain
// text that cannot fake nth's own words, the secret check, the answer shapes, the schema, and the reasons an ask ends.
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-12")]
public sealed class PcAskTests
{
    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void A_question_with_choices_reads_with_a_typed_answer_allowed_by_default()
    {
        var ask = PcAskRequest.Parse(Args("""{"question":"Which sheet should I fill?","choices":["Q3","Q4"],"window":"w:1a2b"}"""));

        Assert.Equal("Which sheet should I fill?", ask.Question);
        Assert.Equal(new[] { "Q3", "Q4" }, ask.Choices);
        Assert.True(ask.AllowText);
        Assert.Equal("w:1a2b", ask.Window);
        Assert.False(ask.AsksForSecret);
    }

    [Theory]
    [InlineData("""{}""", "question is required")]
    [InlineData("""{"question":"   "}""", "question must not be empty")]
    [InlineData("""{"question":7}""", "question must be text")]
    [InlineData("""{"question":"Go?","choices":"yes"}""", "choices must be a list of text")]
    [InlineData("""{"question":"Go?","choices":["a","b","c","d","e"]}""", "choices takes at most 4")]
    [InlineData("""{"question":"Go?","choices":["Yes","yes"]}""", "choices must differ")]
    [InlineData("""{"question":"Go?","choices":[""]}""", "a choice must not be empty")]
    [InlineData("""{"question":"Go?","choices":[3]}""", "choices must be a list of text")]
    [InlineData("""{"question":"Go?","allow_text":false}""", "give choices, or allow a typed answer")]
    [InlineData("""{"question":"Go?","allow_text":"no"}""", "allow_text must be true or false")]
    [InlineData("""{"question":"Go?","link":"https://example.com"}""", "pc_ask takes no link")]
    public void Wrong_arguments_are_refused_never_guessed(string json, string message)
    {
        var refused = Assert.Throws<PcArgsException>(() => PcAskRequest.Parse(Args(json)));
        Assert.Equal(message, refused.Message);
    }

    [Fact]
    public void The_question_takes_600_characters_and_a_choice_40()
    {
        Assert.Equal(600, PcAskRequest.Parse(Args("{\"question\":\"" + new string('q', 600) + "\"}")).Question.Length);
        Assert.Throws<PcArgsException>(() => PcAskRequest.Parse(Args("{\"question\":\"" + new string('q', 601) + "\"}")));
        Assert.Equal(40, PcAskRequest.Parse(Args("{\"question\":\"Go?\",\"choices\":[\"" + new string('c', 40) + "\"]}")).Choices[0].Length);
        Assert.Throws<PcArgsException>(() => PcAskRequest.Parse(Args("{\"question\":\"Go?\",\"choices\":[\"" + new string('c', 41) + "\"]}")));
    }

    [Fact]
    public void Text_is_plain_with_no_controls_overrides_or_invisible_marks()
    {
        Assert.Equal("Send the draft now?", PcAskRequest.Plain("  Send\tthe ‮draft‬​ now?\u0007 ", multiline: true));
        Assert.Equal("Line one\nLine two\n\nLine three", PcAskRequest.Plain("Line one  \r\nLine two\n\n\n\nLine three\n", multiline: true));
        Assert.Equal("Yes please", PcAskRequest.Plain("Yes\nplease", multiline: false));
        Assert.Equal("ایکسل میں؟", PcAskRequest.Plain("ایکسل‏ میں؟", multiline: false));
    }

    [Theory]
    [InlineData("What is the Wi-Fi password?", true)]
    [InlineData("Which choice?", false)]
    public void A_question_or_choice_that_asks_for_a_secret_is_marked(string question, bool secret)
    {
        Assert.Equal(secret, PcAskRequest.Parse(Args("{\"question\":\"" + question + "\"}")).AsksForSecret);
        Assert.True(PcAskRequest.Parse(Args("{\"question\":\"How do I sign in?\",\"choices\":[\"Use the one-time code\",\"Skip\"]}")).AsksForSecret);
    }

    [Fact]
    public void The_answer_is_a_choice_or_typed_text_kept_readable_in_any_script()
    {
        Assert.Equal("answer: {\"choice\":\"Q4\"}", PcAnswer.Chose("Q4").Render());
        Assert.Equal("answer: {\"text\":\"Use \\\"Summary\\\" اور جاری رکھیں\"}", PcAnswer.Typed("Use \"Summary\" اور جاری رکھیں").Render());
    }

    [Fact]
    public void An_unanswered_question_ends_after_30_minutes_as_expired_or_moved_on()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), PcAskRequest.Lifetime);
        Assert.Equal("expired", PcDeclineReason.Expired);
        Assert.Equal("moved_on", PcDeclineReason.MovedOn);
        var result = PcResult.Fail(PcErrorCode.DeclinedByUser, PcDeclineReason.MovedOn);
        Assert.StartsWith("error: declined_by_user: moved_on.", result.Render(), StringComparison.Ordinal);
        Assert.StartsWith("waiting_for_user: question. token: t:9.", PcResult.WaitingFor(PcWaitReason.Question, "t:9").Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_matches_the_parser()
    {
        var spec = PcToolSchemas.AskSpec;
        var schema = spec.Schema();
        var properties = (JsonObject)schema["properties"]!;

        Assert.Equal("pc_ask", spec.Name);
        Assert.True(spec.ReadOnly);
        Assert.Equal(new[] { "question" }, ((JsonArray)schema["required"]!).Select(n => n!.GetValue<string>()));
        Assert.Equal(new[] { "question", "choices", "allow_text", "window" }, properties.Select(p => p.Key));
        Assert.Equal(PcAskRequest.MaxQuestion, properties["question"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(PcAskRequest.MaxChoices, properties["choices"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(PcAskRequest.MaxChoice, properties["choices"]!["items"]!["maxLength"]!.GetValue<int>());
        Assert.True(properties["allow_text"]!["default"]!.GetValue<bool>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }
}
