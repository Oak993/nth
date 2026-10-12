using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user). The person's input becomes answers: what each kind accepts, what it
// refuses and says why, and numbers kept in one invariant form whatever the person's culture.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserCheckTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static AskCard Card(params AskQuestion[] questions) => new() { Id = "c", Questions = new ValueList<AskQuestion>(questions) };

    private static AskQuestion Choice(string id, AskKind kind, bool other = false, bool required = true) => new()
    {
        Id = id, Prompt = "Pick", Kind = kind, AllowsOther = other, Required = required,
        Options = new ValueList<AskOption>([new AskOption("Red"), new AskOption("Green"), new AskOption("Blue")]),
    };

    private static AskQuestion Number(string id, double? min = null, double? max = null, bool whole = false, bool required = true) =>
        new() { Id = id, Prompt = "How many?", Kind = AskKind.Number, Min = min, Max = max, WholeNumber = whole, Required = required };

    private static AskCheck Check(AskCard card, string id, CultureInfo culture, params string[] values) =>
        AskUserSchema.Check(card, new Dictionary<string, IReadOnlyList<string>> { [id] = values }, culture);

    [Fact]
    public void A_single_choice_takes_one_option_and_corrects_its_case()
    {
        var check = Check(Card(Choice("a", AskKind.Single)), "a", English, "gReEn");
        Assert.True(check.IsValid);
        Assert.Equal(["Green"], check.Answers["a"]);
    }

    [Fact]
    public void A_choice_outside_the_options_needs_the_other_field_on()
    {
        Assert.Equal(AskUserWords.NotAnOption, Check(Card(Choice("a", AskKind.Single)), "a", English, "Purple").Problems["a"]);
        var open = Check(Card(Choice("a", AskKind.Single, other: true)), "a", English, "Purple");
        Assert.True(open.IsValid);
        Assert.Equal(["Purple"], open.Answers["a"]);
    }

    [Fact]
    public void Several_choices_are_deduplicated_and_keep_the_persons_own_words_last()
    {
        var check = Check(Card(Choice("a", AskKind.Multiple, other: true)), "a", English, "Red", "red", "Blue", "Teal");
        Assert.Equal(["Red", "Blue", "Teal"], check.Answers["a"]);
    }

    [Fact]
    public void A_required_question_without_an_answer_says_what_to_do_and_an_optional_one_is_left_out()
    {
        var card = Card(Choice("one", AskKind.Single), Choice("many", AskKind.Multiple), Number("n"), new AskQuestion { Id = "t", Prompt = "Why?", Kind = AskKind.Text },
            Choice("opt", AskKind.Single, required: false));
        var check = AskUserSchema.Check(card, new Dictionary<string, IReadOnlyList<string>>(), English);
        Assert.Equal(AskUserWords.PickOne, check.Problems["one"]);
        Assert.Equal(AskUserWords.PickAtLeastOne, check.Problems["many"]);
        Assert.Equal(AskUserWords.NotANumber, check.Problems["n"]);
        Assert.Equal(AskUserWords.WriteAnswer, check.Problems["t"]);
        Assert.False(check.Problems.ContainsKey("opt"));
        Assert.False(check.Answers.ContainsKey("opt"));
    }

    [Fact]
    public void Text_is_trimmed_and_capped()
    {
        var card = Card(new AskQuestion { Id = "t", Prompt = "Why?", Kind = AskKind.Text });
        Assert.Equal(["hello"], Check(card, "t", English, "  hello \n").Answers["t"]);
        Assert.Equal(AskUserWords.TooLong(AskUserSchema.MaxText), Check(card, "t", English, new string('x', AskUserSchema.MaxText + 1)).Problems["t"]);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.5", "3.5")]
    [InlineData(" -7 ", "-7")]
    [InlineData("1e3", "1000")]
    public void A_number_is_kept_in_its_invariant_form(string typed, string kept)
    {
        var check = Check(Card(Number("n")), "n", English, typed);
        Assert.True(check.IsValid);
        Assert.Equal([kept], check.Answers["n"]);
    }

    [Fact]
    public void A_number_is_read_in_the_persons_culture_and_the_invariant_form()
    {
        var card = Card(Number("n"));
        Assert.Equal(["3.5"], Check(card, "n", German, "3,5").Answers["n"]);
        Assert.Equal(["3.5"], Check(card, "n", German, "3.5").Answers["n"]);
    }

    [Theory]
    [InlineData("1.000")]
    [InlineData("12.345.678")]
    [InlineData("1 000")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void A_number_that_could_mean_two_things_or_none_is_refused_in_german(string typed)
    {
        // "1.000" is a thousand in German and one in English: nth never decides for the person.
        var check = Check(Card(Number("n")), "n", German, typed);
        Assert.False(check.IsValid);
        Assert.Equal(AskUserWords.NotANumber, check.Problems["n"]);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1,5")]
    [InlineData("12,345,678")]
    public void A_comma_is_not_guessed_at_in_english(string typed)
    {
        var check = Check(Card(Number("n")), "n", English, typed);
        Assert.False(check.IsValid);
        Assert.Equal(AskUserWords.NotANumber, check.Problems["n"]);
    }

    [Fact]
    public void A_number_must_sit_inside_its_range_and_be_whole_when_asked()
    {
        var card = Card(Number("n", min: 1, max: 10, whole: true));
        Assert.Equal(AskUserWords.AtLeast(1, English), Check(card, "n", English, "0").Problems["n"]);
        Assert.Equal(AskUserWords.AtMost(10, English), Check(card, "n", English, "11").Problems["n"]);
        Assert.Equal(AskUserWords.NotWhole, Check(card, "n", English, "2.5").Problems["n"]);
        Assert.True(Check(card, "n", English, "10").IsValid);
        Assert.True(Check(card, "n", English, "1").IsValid);
        Assert.Equal("At least 0.5.", AskUserWords.AtLeast(0.5, English));
        Assert.Equal("At least 0,5.", AskUserWords.AtLeast(0.5, German));
    }

    [Fact]
    public void Every_question_is_checked_and_the_good_ones_are_kept()
    {
        var card = Card(Choice("a", AskKind.Single), Number("n", min: 5));
        var check = AskUserSchema.Check(card, new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["Red"], ["n"] = ["1"] }, English);
        Assert.False(check.IsValid);
        Assert.Equal(["Red"], check.Answers["a"]);
        Assert.Equal("n", Assert.Single(check.Problems).Key);
    }

    [Fact]
    public void Numbers_print_without_exponents_or_stray_zeros()
    {
        Assert.Equal("5", AskUserSchema.Canonical(5.0));
        Assert.Equal("-0.25", AskUserSchema.Canonical(-0.25));
        Assert.Equal("100000", AskUserSchema.Canonical(1e5));
        Assert.Equal("0.1", AskUserSchema.Canonical(0.1));
    }
}
