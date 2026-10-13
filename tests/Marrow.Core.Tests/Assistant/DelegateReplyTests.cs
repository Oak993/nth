using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3; ED-NEW-dextra-as22-3): what the delegate's model said, read defensively. Prose and code fences around
// the object are fine; anything that does not read cleanly gives nothing, and the delegate then leaves the question to the owner.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-3")]
public sealed class DelegateReplyTests
{
    private const string Plain =
        "{\"answers\": {\"q1\": [\"Postgres\"]}, \"confidence\": 0.92, \"basis\": \"technical\", \"flag\": \"none\", \"evidence\": \"\", \"reason\": \"The repo already uses it.\"}";

    [Fact]
    public void A_plain_object_is_read()
    {
        var reply = DelegateReply.Parse(Plain)!;
        Assert.Equal(["Postgres"], reply.Answers["q1"]);
        Assert.Equal(0.92, reply.Confidence, 3);
        Assert.Equal(DelegateBasis.Technical, reply.Basis);
        Assert.Null(reply.Flag);
        Assert.Equal("The repo already uses it.", reply.Reason);
    }

    [Fact]
    public void Prose_and_a_code_fence_around_the_object_are_ignored()
    {
        var reply = DelegateReply.Parse("Sure, here is my answer:\n```json\n" + Plain + "\n```\nHope that helps {really}.")!;
        Assert.Equal(["Postgres"], reply.Answers["q1"]);
    }

    [Fact]
    public void An_answer_may_be_one_string_or_a_list_and_braces_inside_strings_do_not_confuse_it()
    {
        var reply = DelegateReply.Parse("{\"answers\": {\"a\": \"Keep it {as is}\", \"b\": [\"x\", \"y\"]}, \"confidence\": 0.9, \"basis\": \"decided\", \"evidence\": \"we keep it { in } the repo\"}")!;
        Assert.Equal(["Keep it {as is}"], reply.Answers["a"]);
        Assert.Equal(["x", "y"], reply.Answers["b"]);
        Assert.Equal("we keep it { in } the repo", reply.Evidence);
        Assert.Equal(DelegateBasis.Decided, reply.Basis);
    }

    [Theory]
    [InlineData("85", 0.85)]
    [InlineData("0.5", 0.5)]
    [InlineData("1", 1.0)]
    [InlineData("100", 1.0)]
    [InlineData("250", 0.0)]
    [InlineData("-3", 0.0)]
    [InlineData("\"high\"", 0.0)]
    public void Confidence_is_a_fraction_a_percent_reads_as_one_and_anything_else_is_nothing(string raw, double expected)
    {
        var reply = DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}, \"confidence\": " + raw + "}")!;
        Assert.Equal(expected, reply.Confidence, 3);
    }

    [Fact]
    public void A_missing_confidence_and_basis_read_as_not_sure()
    {
        var reply = DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}}")!;
        Assert.Equal(0, reply.Confidence);
        Assert.Equal(DelegateBasis.Unsure, reply.Basis);
        Assert.Equal(DelegateBasis.Unsure, DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}, \"basis\": \"vibes\"}")!.Basis);
    }

    [Theory]
    [InlineData("money", DelegateReason.Money)]
    [InlineData("credentials", DelegateReason.Credentials)]
    [InlineData("outward", DelegateReason.Outward)]
    [InlineData("destructive", DelegateReason.Destructive)]
    [InlineData("install", DelegateReason.Install)]
    [InlineData("commitment", DelegateReason.Commitment)]
    [InlineData("taste", DelegateReason.Taste)]
    [InlineData("something else", DelegateReason.LowConfidence)]
    public void A_flag_names_the_topic_and_an_unknown_one_reads_as_not_sure(string word, DelegateReason expected)
    {
        var reply = DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}, \"flag\": \"" + word + "\"}")!;
        Assert.Equal(expected, reply.Flag);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("null")]
    public void No_flag_is_no_flag(string word) =>
        Assert.Null(DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}, \"flag\": \"" + word + "\"}")!.Flag);

    [Fact]
    public void The_first_object_that_holds_answers_is_the_reply()
    {
        var reply = DelegateReply.Parse("{\"note\": 1} and then " + Plain)!;
        Assert.Equal(["Postgres"], reply.Answers["q1"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I think Postgres.")]
    [InlineData("{\"answers\": ")]
    [InlineData("{\"confidence\": 0.9}")]
    [InlineData("{\"answers\": [\"Postgres\"], \"confidence\": 0.9}")]
    [InlineData("{\"answers\": {\"q1\": 5}}")]
    [InlineData("{\"answers\": {\"q1\": [\"A\", 5]}}")]
    [InlineData("{\"answers\": {\"q1\": {\"nested\": \"A\"}}}")]
    [InlineData("{\"answers\": {\"q1\": [\"A\"]}, \"answers\": {\"q1\": [\"B\"]}}")]
    public void Anything_that_does_not_read_cleanly_gives_nothing(string? text) => Assert.Null(DelegateReply.Parse(text));

    [Fact]
    public void A_reply_over_the_limit_is_not_read()
    {
        Assert.Null(DelegateReply.Parse(Plain + new string(' ', DelegateReply.MaxChars)));
    }

    [Fact]
    public void Evidence_and_reason_are_kept_to_a_length()
    {
        var long600 = new string('w', 5_000);
        var reply = DelegateReply.Parse("{\"answers\": {\"q1\": [\"A\"]}, \"evidence\": \"" + long600 + "\", \"reason\": \"" + long600 + "\"}")!;
        Assert.Equal(DelegateReply.NoteChars, reply.Evidence.Length);
        Assert.Equal(DelegateReply.NoteChars, reply.Reason.Length);
    }

    [Fact]
    public void Many_unclosed_braces_are_tried_a_bounded_number_of_times()
    {
        var text = string.Concat(Enumerable.Repeat("{ ", 5_000)) + Plain;
        // the readable object is far past the tries; the point is that this returns, not how
        var reply = DelegateReply.Parse(text);
        Assert.True(reply is null || reply.Answers["q1"][0] == "Postgres");
    }
}
