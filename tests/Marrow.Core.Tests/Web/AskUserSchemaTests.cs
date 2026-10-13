using System.Text.Json.Nodes;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user). How a model's ask_user arguments become a card: the four kinds,
// the lenient readings small models need, and the errors written for the model when a call cannot be read.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserSchemaTests
{
    private static AskParse Read(string json) => AskUserSchema.Parse(JsonNode.Parse(json), "card-1");

    private static AskQuestion Only(string json)
    {
        var parse = Read(json);
        Assert.True(parse.Ok, string.Join(" | ", parse.Errors));
        return Assert.Single(parse.Card!.Questions);
    }

    [Fact]
    public void A_single_choice_with_plain_strings_takes_the_persons_own_words_unless_told_not_to()
    {
        var question = Only("""{"questions":[{"question":"Which database?","type":"single","options":["PostgreSQL","SQLite"]}]}""");
        Assert.Equal(AskKind.Single, question.Kind);
        Assert.Equal("Which database?", question.Prompt);
        Assert.Equal(["PostgreSQL", "SQLite"], question.Options.Select(option => option.Label));
        Assert.True(question.AllowsOther);
        Assert.True(question.Required);
        Assert.Equal("q1", question.Id);

        var closed = Only("""{"questions":[{"question":"Ship it?","options":["Yes","No"],"allow_other":false}]}""");
        Assert.False(closed.AllowsOther);
    }

    [Fact]
    public void Options_may_be_objects_with_a_label_and_a_line_under_it()
    {
        var question = Only("""{"questions":[{"question":"Pick a plan","type":"single","options":[{"label":"Free","description":"1 project"},{"text":"Pro","detail":"Unlimited"},{"value":"Team"}]}]}""");
        Assert.Equal("Free", question.Options[0].Label);
        Assert.Equal("1 project", question.Options[0].Detail);
        Assert.Equal("Pro", question.Options[1].Label);
        Assert.Equal("Unlimited", question.Options[1].Detail);
        Assert.Equal("Team", question.Options[2].Label);
        Assert.Null(question.Options[2].Detail);
    }

    [Fact]
    public void Multiple_text_and_number_questions_each_keep_their_own_fields()
    {
        var parse = Read("""
            {"questions":[
             {"question":"Which features?","type":"multiple","options":["Search","Export","Sharing"],"required":false},
             {"question":"Anything else?","type":"text","placeholder":"Optional notes","multiline":true},
             {"question":"How many GB?","type":"number","min":1,"max":512,"step":8,"unit":"GB","default":64}]}
            """);
        Assert.True(parse.Ok, string.Join(" | ", parse.Errors));
        var (features, notes, size) = (parse.Card!.Questions[0], parse.Card.Questions[1], parse.Card.Questions[2]);
        Assert.Equal(AskKind.Multiple, features.Kind);
        Assert.False(features.Required);
        Assert.Equal(AskKind.Text, notes.Kind);
        Assert.True(notes.Multiline);
        Assert.Equal("Optional notes", notes.Placeholder);
        Assert.Empty(notes.Options);
        Assert.False(notes.AllowsOther);
        Assert.Equal(AskKind.Number, size.Kind);
        Assert.Equal((1d, 512d, 8d, "GB", "64"), (size.Min, size.Max, size.Step, size.Unit, size.Default));
        Assert.Equal(["q1", "q2", "q3"], parse.Card.Questions.Select(item => item.Id));
        Assert.Equal(AskState.Waiting, parse.Card.State);
        Assert.Equal("card-1", parse.Card.Id);
    }

    [Theory]
    [InlineData("radio", AskKind.Single)]
    [InlineData("select", AskKind.Single)]
    [InlineData("single-choice", AskKind.Single)]
    [InlineData("checkbox", AskKind.Multiple)]
    [InlineData("multi_select", AskKind.Multiple)]
    [InlineData("string", AskKind.Text)]
    [InlineData("free text", AskKind.Text)]
    [InlineData("integer", AskKind.Number)]
    [InlineData("numeric", AskKind.Number)]
    public void Other_names_for_a_kind_are_read_as_the_kind(string name, AskKind expected)
    {
        var json = $$"""{"questions":[{"question":"Q?","type":"{{name}}","options":["A","B"]}]}""";
        Assert.Equal(expected, Only(json).Kind);
    }

    [Fact]
    public void Options_without_a_type_make_a_choice_and_a_multiple_flag_makes_it_several()
    {
        Assert.Equal(AskKind.Single, Only("""{"questions":[{"question":"Q?","options":["A","B"]}]}""").Kind);
        Assert.Equal(AskKind.Multiple, Only("""{"questions":[{"question":"Q?","options":["A","B"],"multiple":true}]}""").Kind);
        Assert.Equal(AskKind.Text, Only("""{"questions":[{"question":"Q?"}]}""").Kind);
    }

    [Fact]
    public void An_integer_kind_or_flag_asks_for_a_whole_number()
    {
        Assert.True(Only("""{"questions":[{"question":"How many?","type":"integer"}]}""").WholeNumber);
        Assert.True(Only("""{"questions":[{"question":"How many?","type":"number","integer":true}]}""").WholeNumber);
        Assert.False(Only("""{"questions":[{"question":"Price?","type":"number"}]}""").WholeNumber);
    }

    [Fact]
    public void A_default_outside_the_numbers_range_is_dropped()
    {
        Assert.Null(Only("""{"questions":[{"question":"N?","type":"number","min":1,"max":5,"default":9}]}""").Default);
        Assert.Null(Only("""{"questions":[{"question":"N?","type":"number","integer":true,"default":2.5}]}""").Default);
        Assert.Equal("3", Only("""{"questions":[{"question":"N?","type":"number","min":1,"max":5,"default":"3"}]}""").Default);
    }

    [Fact]
    public void A_single_question_without_the_list_and_questions_sent_as_a_json_string_are_read()
    {
        var bare = Read("""{"question":"Proceed?","options":["Yes","No"]}""");
        Assert.True(bare.Ok);
        Assert.Equal("Proceed?", bare.Card!.Questions[0].Prompt);

        var wrapped = AskUserSchema.Parse(new JsonObject { ["questions"] = """[{"question":"Name?","type":"text"}]""" }, "c");
        Assert.True(wrapped.Ok);
        Assert.Equal(AskKind.Text, wrapped.Card!.Questions[0].Kind);

        var asString = AskUserSchema.Parse(JsonValue.Create("""{"questions":[{"question":"Name?"}]}"""), "c");
        Assert.True(asString.Ok);
    }

    [Fact]
    public void Ids_are_cleaned_and_made_unique()
    {
        var parse = Read("""{"questions":[{"id":"db name!","question":"A?"},{"id":"dbname","question":"B?"},{"question":"C?"}]}""");
        Assert.Equal(["dbname", "dbname-2", "q3"], parse.Card!.Questions.Select(question => question.Id));
    }

    [Fact]
    public void Long_text_is_cut_with_an_ellipsis_not_refused()
    {
        var prompt = new string('x', 900);
        var question = Only($$"""{"questions":[{"question":"{{prompt}}","header":"{{new string('h', 60)}}"}]}""");
        Assert.Equal(AskUserSchema.MaxPrompt, question.Prompt.Length);
        Assert.EndsWith("…", question.Prompt, StringComparison.Ordinal);
        Assert.True(question.Header!.Length <= AskUserSchema.MaxHeader);
    }

    [Theory]
    [InlineData("""{"questions":[]}""", "empty")]
    [InlineData("""{"nothing":1}""", "questions is required")]
    [InlineData("""{"questions":[{"type":"text"}]}""", "no question text")]
    [InlineData("""{"questions":[{"question":"Q?","type":"single","options":["Only"]}]}""", "needs 2 to 12 options (it has one)")]
    [InlineData("""{"questions":[{"question":"Q?","type":"multiple"}]}""", "needs 2 to 12 options")]
    [InlineData("""{"questions":[{"question":"Q?","options":["A","a"]}]}""", "appears twice")]
    [InlineData("""{"questions":[{"question":"Q?","type":"colour"}]}""", "is not one of single, multiple, text, number")]
    [InlineData("""{"questions":[{"question":"Q?","type":"number","min":9,"max":1}]}""", "min is more than max")]
    [InlineData("""{"questions":["just a string"]}""", "must be an object")]
    public void A_call_that_cannot_be_read_returns_errors_the_model_can_act_on(string json, string expected)
    {
        var parse = Read(json);
        Assert.False(parse.Ok);
        Assert.Contains(parse.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void More_than_four_questions_are_refused_and_the_error_says_to_ask_the_rest_later()
    {
        var questions = string.Join(",", Enumerable.Range(1, 5).Select(i => $$"""{"question":"Q{{i}}?"}"""));
        var parse = Read($$"""{"questions":[{{questions}}]}""");
        Assert.False(parse.Ok);
        Assert.Contains("At most 4 questions", Assert.Single(parse.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_that_are_not_an_object_get_an_example()
    {
        var parse = AskUserSchema.Parse(null, "c");
        Assert.False(parse.Ok);
        Assert.Contains("{\"questions\":", Assert.Single(parse.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void More_than_twelve_options_are_refused()
    {
        var options = string.Join(",", Enumerable.Range(1, 13).Select(i => $"\"Option {i}\""));
        var parse = Read($$"""{"questions":[{"question":"Pick","options":[{{options}}]}]}""");
        Assert.False(parse.Ok);
        Assert.Contains("at most 12 options", Assert.Single(parse.Errors), StringComparison.Ordinal);
    }
}
