using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class RequestsTests
{
    [Theory] // PR-RUNTIME-016..018
    [InlineData(ApprovalRequest.Kind.Command, "Run this command?", "terminal")]
    [InlineData(ApprovalRequest.Kind.FileChange, "Apply these changes?", "doc.badge.plus")]
    [InlineData(ApprovalRequest.Kind.Tool, "Allow this action?", "wrench.and.screwdriver")]
    [InlineData(ApprovalRequest.Kind.Permissions, "Grant extra access?", "lock.shield")]
    [InlineData(ApprovalRequest.Kind.Plan, "Ready to build this plan?", "checklist")]
    public void Approval_mapping_and_provider_fields_are_preserved(ApprovalRequest.Kind kind, string headline, string symbol)
    {
        var option = new ApprovalRequest.Option("provider/yes", "Provider supplied label", ApprovalRequest.Option.Role.Approve);
        var request = new ApprovalRequest("not-a-uuid/42", kind, "provider title", "detail", "reason", [option], "tool-row");
        Assert.Equal("not-a-uuid/42", request.Id); Assert.Equal(kind, request.RequestKind);
        Assert.Equal("provider title", request.Title); Assert.Equal("detail", request.Detail);
        Assert.Equal("reason", request.Reason); Assert.Equal("tool-row", request.ToolItemId);
        Assert.Equal(option, Assert.Single(request.Options));
        Assert.Equal(headline, request.Headline); Assert.Equal(symbol, request.Symbol);
    }

    [Fact] // PR-RUNTIME-016, PR-RUNTIME-019
    public void Kinds_and_roles_have_exact_source_cardinality_and_options_use_value_equality()
    {
        Assert.Equal(5, Enum.GetValues<ApprovalRequest.Kind>().Length);
        Assert.Equal(new[] { ApprovalRequest.Option.Role.Approve, ApprovalRequest.Option.Role.ApproveAlways, ApprovalRequest.Option.Role.Decline, ApprovalRequest.Option.Role.Cancel }, Enum.GetValues<ApprovalRequest.Option.Role>());
        foreach (var role in Enum.GetValues<ApprovalRequest.Option.Role>())
        {
            var first = new ApprovalRequest.Option("id", "title", role);
            var second = new ApprovalRequest.Option("id", "title", role);
            Assert.Equal(role, first.OptionRole); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }
        Assert.NotEqual(new ApprovalRequest.Option("id", "title", ApprovalRequest.Option.Role.Approve), new ApprovalRequest.Option("id", "title", ApprovalRequest.Option.Role.ApproveAlways));
    }

    [Fact] // PR-RUNTIME-020
    public void Multi_question_payload_preserves_all_fields_flags_and_optional_choice_detail()
    {
        var one = new QuestionRequest.Question("q/1", "Header", "Prompt", [new("A", "detail"), new("B", null)], true, false, true);
        var two = new QuestionRequest.Question("q/2", "Other", "Second", [], false, true, false);
        var request = new QuestionRequest("request/not-a-uuid", [one, two]);
        Assert.Equal("request/not-a-uuid", request.Id); Assert.Equal(2, request.Questions.Count);
        Assert.Equal("q/1", one.Id); Assert.Equal("Header", one.Header); Assert.Equal("Prompt", one.Prompt);
        Assert.Equal("A", one.Choices[0].Label); Assert.Equal("detail", one.Choices[0].Detail); Assert.Null(one.Choices[1].Detail);
        Assert.True(one.AllowsMultiple); Assert.False(one.AllowsOther); Assert.True(one.IsSecret);
        Assert.False(two.AllowsMultiple); Assert.True(two.AllowsOther); Assert.False(two.IsSecret); Assert.Empty(two.Choices);
    }

    [Fact] // Source Question/Choice Hashable, PR-RUNTIME-020
    public void Question_equality_compares_choice_values_and_copies_the_input_collection()
    {
        var choices = new List<QuestionRequest.Choice> { new("A", null) };
        var first = new QuestionRequest.Question("id", "h", "p", choices, false, true, false);
        var second = new QuestionRequest.Question("id", "h", "p", [new("A", null)], false, true, false);
        choices.Add(new("B", null));
        Assert.Single(first.Choices); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new QuestionRequest.Question("id", "h", "p", [new("A", "different")], false, true, false));
    }
}
