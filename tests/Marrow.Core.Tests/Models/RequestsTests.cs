using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Choice = Marrow.Core.Models.QuestionRequest.Choice;
using Kind = Marrow.Core.Models.ApprovalRequest.Kind;
using Option = Marrow.Core.Models.ApprovalRequest.Option;
using Question = Marrow.Core.Models.QuestionRequest.Question;
using Role = Marrow.Core.Models.ApprovalRequest.Option.Role;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/Requests.swift (B2 1fbf81e), :1-74.
// Every expectation below is transcribed from that file, from its callers cited per test, or from the
// published OD-0010 table; none is taken from the C# implementation.
// Shape the tests assume, from the conventions: Swift structs are immutable sealed records (R-VAL-1, ED-077)
// with PascalCase positional members (the MCPField convention), arrays inside them are ValueList (R-VAL-2),
// a member named like its nested type is prefixed (R-NAME-3: kind is RequestKind, role is OptionRole),
// toolItemID is ToolItemID (R-NAME-2), and the memberwise optionals default to null with every call site
// naming its arguments (ED-095 parameter order).
// Not Codable in Swift, so there is no decoding path to test; not cancellable, and no error path exists.
public sealed class RequestsTests
{
    [Theory]
    [Trait("Req", "PR-RUNTIME-017")] // Core/Models/Requests.swift:34-42
    [Trait("Req", "UI-RUNTIME-005")] // App/Views/Composer/RequestCards.swift:12 Text(request.headline)
    [InlineData(Kind.Command, "Run this command?")]
    [InlineData(Kind.FileChange, "Apply these changes?")]
    [InlineData(Kind.Tool, "Allow this action?")]
    [InlineData(Kind.Permissions, "Grant extra access?")]
    [InlineData(Kind.Plan, "Ready to build this plan?")]
    public void Headline_is_the_verbatim_question_for_its_kind(Kind kind, string expected)
    {
        var request = Sample(kind);

        Assert.Equal(expected, request.Headline);
        var bytes = Encoding.UTF8.GetBytes(request.Headline);
        Assert.Equal(expected.Length, bytes.Length);
        Assert.All(bytes, b => Assert.True(b < 0x80, "the Swift literal is plain ASCII"));
        Assert.Equal((byte)0x3F, bytes[^1]); // U+003F QUESTION MARK, not a lookalike
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-018")] // Core/Models/Requests.swift:44-52
    [Trait("Req", "UI-RUNTIME-005")] // App/Views/Composer/RequestCards.swift:10 Image(systemName: request.symbol)
    [InlineData(Kind.Command, "terminal")]
    [InlineData(Kind.FileChange, "doc.badge.plus")]
    [InlineData(Kind.Tool, "wrench.and.screwdriver")]
    [InlineData(Kind.Permissions, "lock.shield")]
    [InlineData(Kind.Plan, "checklist")]
    public void Symbol_is_the_SF_Symbol_name_for_its_kind(Kind kind, string expected)
    {
        var request = Sample(kind);

        Assert.Equal(expected, request.Symbol);
        Assert.Equal(Encoding.ASCII.GetBytes(expected), Encoding.UTF8.GetBytes(request.Symbol));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-017")] // Core/Models/Requests.swift:34-42 switch kind only
    [Trait("Req", "PR-RUNTIME-018")] // Core/Models/Requests.swift:44-52 switch kind only
    public void Headline_and_symbol_depend_on_the_kind_and_nothing_else()
    {
        var kinds = new[] { Kind.Command, Kind.FileChange, Kind.Tool, Kind.Permissions, Kind.Plan };
        foreach (var kind in kinds)
        {
            var bare = new ApprovalRequest(Id: "", RequestKind: kind, Title: "", Options: ValueList<Option>.Empty);
            var full = new ApprovalRequest(Id: "Other-ID", RequestKind: kind, Title: "Ready to build this plan?",
                Options: new ValueList<Option>([new Option(Id: "allow", Title: "Approve", OptionRole: Role.Approve)]),
                Detail: "Run this command?", Reason: "Grant extra access?", ToolItemID: "call_9");
            Assert.Equal(bare.Headline, full.Headline);
            Assert.Equal(bare.Symbol, full.Symbol);
        }

        Assert.Equal(5, kinds.Select(k => Sample(k).Headline).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, kinds.Select(k => Sample(k).Symbol).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-018")] // OD-0010, porting/icons/sf-symbols-to-lucide.json
    [Trait("Req", "UI-RUNTIME-005")] // notes: the five icons follow OD-0010
    [InlineData(Kind.Command, "square-terminal")]
    [InlineData(Kind.FileChange, "file-plus")]
    [InlineData(Kind.Tool, "wrench")]
    [InlineData(Kind.Permissions, "shield-lock")]
    [InlineData(Kind.Plan, "list-checks")]
    public void Each_symbol_maps_one_for_one_through_the_OD_0010_table_to_a_shipped_Lucide_icon(Kind kind, string lucide)
    {
        var symbol = Sample(kind).Symbol;
        var porting = Path.Combine(RepositoryRoot(), "porting", "icons");
        using var table = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(porting, "sf-symbols-to-lucide.json")));

        var entries = table.RootElement.GetProperty("symbols").EnumerateArray()
            .Where(e => e.GetProperty("sf").GetString() == symbol).ToList();
        var entry = Assert.Single(entries);
        Assert.Equal(lucide, entry.GetProperty("lucide").GetString());
        Assert.Contains(entry.GetProperty("sites").EnumerateArray(),
            s => (s.GetString() ?? "").StartsWith("DroppyCode/Core/Models/Requests.swift:", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(porting, "lucide", lucide + ".svg")), lucide + ".svg is shipped");
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-016")] // Core/Models/Requests.swift:5-11
    public void Approval_kinds_are_the_five_source_cases()
    {
        Assert.Equal(new[] { "Command", "FileChange", "Tool", "Permissions", "Plan" }, Enum.GetNames<Kind>());
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-016")] // Core/Models/Requests.swift:26 var id: String
    [InlineData("toolu_01ABCdefGHI")]
    [InlineData("Call_ID-42/\U000000C4 ")] // U+00C4 LATIN CAPITAL LETTER A WITH DIAERESIS, then a trailing space
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    [InlineData("  padded\tid  ")]
    [InlineData("")]
    public void Approval_id_is_a_free_form_provider_string_kept_byte_for_byte(string id)
    {
        var request = new ApprovalRequest(Id: id, RequestKind: Kind.Tool, Title: "t", Options: ValueList<Option>.Empty);

        Assert.Equal(typeof(string), typeof(ApprovalRequest).GetProperty("Id")!.PropertyType);
        Assert.Equal(id, request.Id);
        Assert.Equal(Encoding.UTF8.GetBytes(id), Encoding.UTF8.GetBytes(request.Id));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-016")] // Core/Models/Requests.swift:26-32
    public void Approval_request_keeps_its_seven_fields_and_the_option_order()
    {
        // Options as Services/Providers/CopilotSession.swift:791-795 builds them.
        var allow = new Option(Id: "allow", Title: "Approve", OptionRole: Role.Approve);
        var always = new Option(Id: "always", Title: "Approve for session", OptionRole: Role.ApproveAlways);
        var deny = new Option(Id: "deny", Title: "Decline", OptionRole: Role.Decline);

        var request = new ApprovalRequest(Id: "Req-7", RequestKind: Kind.Plan, Title: "Plan title",
            Options: new ValueList<Option>([allow, always, deny]), Detail: "Detail text", Reason: "Why",
            ToolItemID: "Row-3");

        Assert.Equal("Req-7", request.Id);
        Assert.Equal(Kind.Plan, request.RequestKind);
        Assert.Equal("Plan title", request.Title);
        Assert.Equal("Detail text", request.Detail);
        Assert.Equal("Why", request.Reason);
        Assert.Equal("Row-3", request.ToolItemID);
        Assert.Equal(3, request.Options.Count);
        Assert.Equal(allow, request.Options[0]);
        Assert.Equal(always, request.Options[1]);
        Assert.Equal(deny, request.Options[2]);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-016")] // Core/Models/Requests.swift:29-32 optionals; memberwise init defaults them to nil
    public void Approval_request_omitted_optionals_are_nil_as_the_Swift_memberwise_init_gives()
    {
        // Services/Providers/CopilotSession.swift:866 omits detail, reason and toolItemID.
        var request = new ApprovalRequest(Id: "", RequestKind: Kind.Tool, Title: "Manage an extension",
            Options: ValueList<Option>.Empty);

        Assert.Equal("", request.Id);
        Assert.Null(request.Detail);
        Assert.Null(request.Reason);
        Assert.Null(request.ToolItemID);
        Assert.Empty(request.Options);

        // CopilotSession.swift:841 passes reason but no detail.
        var withReason = new ApprovalRequest(Id: "", RequestKind: Kind.FileChange, Title: "a.txt", Reason: "intention",
            Options: ValueList<Option>.Empty);
        Assert.Null(withReason.Detail);
        Assert.Equal("intention", withReason.Reason);
        Assert.Null(withReason.ToolItemID);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-016")] // Services/Providers/CopilotSession.swift:796-801 var approval = ...; approval.id = ...
    [Trait("Rule", "R-VAL-1")]
    public void Approval_request_is_a_value_whose_changed_copy_leaves_the_original_alone()
    {
        var described = new ApprovalRequest(Id: "", RequestKind: Kind.Command, Title: "ls -la",
            Options: ValueList<Option>.Empty, Detail: "in /tmp");
        var options = new ValueList<Option>([new Option(Id: "allow", Title: "Approve", OptionRole: Role.Approve)]);

        var approval = described with { Id = "perm-12", Options = options, ToolItemID = "call-4", Reason = "judged" };

        Assert.Equal("perm-12", approval.Id);
        Assert.Equal("call-4", approval.ToolItemID);
        Assert.Equal("judged", approval.Reason);
        Assert.Equal("in /tmp", approval.Detail);
        Assert.Equal("ls -la", approval.Title);
        Assert.Equal(Kind.Command, approval.RequestKind);
        Assert.Single(approval.Options);
        Assert.Equal("", described.Id);
        Assert.Empty(described.Options);
        Assert.Null(described.ToolItemID);
        Assert.Null(described.Reason);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-016")] // Core/Models/Requests.swift:31 var options: [Option] (value semantics)
    [Trait("Rule", "R-VAL-2")]
    public void Approval_options_are_a_value_list_the_callers_later_changes_never_reach()
    {
        var source = new List<Option> { new(Id: "allow", Title: "Approve", OptionRole: Role.Approve) };
        var request = new ApprovalRequest(Id: "r", RequestKind: Kind.Tool, Title: "t", Options: new ValueList<Option>(source));

        source.Add(new Option(Id: "deny", Title: "Decline", OptionRole: Role.Decline));

        Assert.IsType<ValueList<Option>>(request.Options);
        Assert.Single(request.Options);
        Assert.Equal("allow", request.Options[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-019")] // Core/Models/Requests.swift:14-19
    public void Option_roles_are_the_four_source_cases()
    {
        Assert.Equal(new[] { "Approve", "ApproveAlways", "Decline", "Cancel" }, Enum.GetNames<Role>());
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-019")] // Core/Models/Requests.swift:21-23
    [InlineData(Role.Approve, "allow", "Approve")] // Services/Providers/CopilotSession.swift:791
    [InlineData(Role.ApproveAlways, "session", "Allow for session")] // Services/Providers/CodexSession.swift:960
    [InlineData(Role.Decline, "deny", "Decline")] // Services/Providers/CopilotSession.swift:795
    [InlineData(Role.Cancel, "cancel", "Stop turn")] // Services/Providers/CodexSession.swift:1001
    public void Option_keeps_its_id_the_providers_title_and_its_role(Role role, string id, string title)
    {
        var option = new Option(Id: id, Title: title, OptionRole: role);

        Assert.Equal(id, option.Id);
        Assert.Equal(title, option.Title);
        Assert.Equal(Encoding.UTF8.GetBytes(title), Encoding.UTF8.GetBytes(option.Title));
        Assert.Equal(role, option.OptionRole);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-019")] // Core/Models/Requests.swift:13 struct Option: Identifiable, Hashable
    public void Option_equality_is_synthesized_over_id_title_and_role()
    {
        var option = new Option(Id: "a", Title: "Approve", OptionRole: Role.Approve);
        var same = new Option(Id: "a", Title: "Approve", OptionRole: Role.Approve);

        Assert.Equal(option, same);
        Assert.Equal(option.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(option, option with { Id = "b" });
        Assert.NotEqual(option, option with { Title = "approve" });
        Assert.NotEqual(option, option with { OptionRole = Role.ApproveAlways });
        Assert.NotEqual(option, option with { OptionRole = Role.Decline });
        Assert.NotEqual(option, option with { OptionRole = Role.Cancel });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:72-73
    public void Question_request_carries_several_questions_in_their_order()
    {
        var first = new Question(Id: "q1", Header: "Scope", Prompt: "Which files?", Choices: ValueList<Choice>.Empty,
            AllowsMultiple: true, AllowsOther: true, IsSecret: false);
        var second = new Question(Id: "q2", Header: "Token", Prompt: "Paste the token", Choices: ValueList<Choice>.Empty,
            AllowsMultiple: false, AllowsOther: false, IsSecret: true);
        var third = new Question(Id: "q3", Header: "", Prompt: "Anything else?", Choices: ValueList<Choice>.Empty,
            AllowsMultiple: false, AllowsOther: true, IsSecret: false);

        var request = new QuestionRequest(Id: "Request-ID/9", Questions: new ValueList<Question>([first, second, third]));

        Assert.Equal("Request-ID/9", request.Id);
        Assert.Equal(3, request.Questions.Count);
        Assert.Same(first, request.Questions[0]);
        Assert.Same(second, request.Questions[1]);
        Assert.Same(third, request.Questions[2]);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:57-70
    public void Question_keeps_its_seven_fields_and_the_choice_order()
    {
        // Choice detail present and absent, as Services/Providers/ClaudeSession.swift:634 builds them.
        var yes = new Choice(Label: "Yes", Detail: "Keep going");
        var no = new Choice(Label: "No", Detail: null);

        var question = new Question(Id: "Which way?", Header: "Direction", Prompt: "Which way?",
            Choices: new ValueList<Choice>([yes, no]), AllowsMultiple: true, AllowsOther: false, IsSecret: true);

        Assert.Equal("Which way?", question.Id);
        Assert.Equal("Direction", question.Header);
        Assert.Equal("Which way?", question.Prompt);
        Assert.Equal(2, question.Choices.Count);
        Assert.Equal("Yes", question.Choices[0].Label);
        Assert.Equal("Keep going", question.Choices[0].Detail);
        Assert.Equal("No", question.Choices[1].Label);
        Assert.Null(question.Choices[1].Detail);
        Assert.True(question.AllowsMultiple);
        Assert.False(question.AllowsOther);
        Assert.True(question.IsSecret);

        var flipped = new Question(Id: "x", Header: "h", Prompt: "p", Choices: ValueList<Choice>.Empty,
            AllowsMultiple: false, AllowsOther: true, IsSecret: false);
        Assert.False(flipped.AllowsMultiple);
        Assert.True(flipped.AllowsOther);
        Assert.False(flipped.IsSecret);
        Assert.Empty(flipped.Choices);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:57 struct Question: Identifiable, Hashable
    public void Question_equality_is_synthesized_over_all_seven_fields()
    {
        var question = new Question(Id: "q", Header: "H", Prompt: "P",
            Choices: new ValueList<Choice>([new Choice(Label: "A", Detail: null), new Choice(Label: "B", Detail: "b")]),
            AllowsMultiple: false, AllowsOther: false, IsSecret: false);
        var same = new Question(Id: "q", Header: "H", Prompt: "P",
            Choices: new ValueList<Choice>([new Choice(Label: "A", Detail: null), new Choice(Label: "B", Detail: "b")]),
            AllowsMultiple: false, AllowsOther: false, IsSecret: false);

        Assert.Equal(question, same);
        Assert.Equal(question.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(question, question with { Id = "r" });
        Assert.NotEqual(question, question with { Header = "h" });
        Assert.NotEqual(question, question with { Prompt = "p" });
        Assert.NotEqual(question, question with { AllowsMultiple = true });
        Assert.NotEqual(question, question with { AllowsOther = true });
        Assert.NotEqual(question, question with { IsSecret = true });
        Assert.NotEqual(question, question with { Choices = new ValueList<Choice>([new Choice(Label: "A", Detail: null)]) });
        Assert.NotEqual(question, question with
        {
            Choices = new ValueList<Choice>([new Choice(Label: "B", Detail: "b"), new Choice(Label: "A", Detail: null)]),
        });
        Assert.NotEqual(question, question with
        {
            Choices = new ValueList<Choice>([new Choice(Label: "A", Detail: "a"), new Choice(Label: "B", Detail: "b")]),
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:67-70 struct Choice: Hashable
    public void Choice_equality_is_synthesized_over_label_and_detail_and_detail_defaults_to_nil()
    {
        var choice = new Choice(Label: "A", Detail: "x");

        Assert.Equal(choice, new Choice(Label: "A", Detail: "x"));
        Assert.Equal(choice.GetHashCode(), new Choice(Label: "A", Detail: "x").GetHashCode());
        Assert.NotEqual(choice, choice with { Label = "a" });
        Assert.NotEqual(choice, choice with { Detail = null });
        Assert.NotEqual(choice, choice with { Detail = "" });

        // Core/Models/Requests.swift:69 var detail: String? takes nil when the memberwise call omits it.
        var bare = new Choice(Label: "A");
        Assert.Null(bare.Detail);
        Assert.Equal(new Choice(Label: "A", Detail: null), bare);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:61, :73 arrays are values
    [Trait("Rule", "R-VAL-2")]
    public void Choices_and_questions_are_value_lists_the_callers_later_changes_never_reach()
    {
        var choices = new List<Choice> { new(Label: "A", Detail: null) };
        var question = new Question(Id: "q", Header: "h", Prompt: "p", Choices: new ValueList<Choice>(choices),
            AllowsMultiple: false, AllowsOther: true, IsSecret: false);
        var questions = new List<Question> { question };
        var request = new QuestionRequest(Id: "r", Questions: new ValueList<Question>(questions));

        choices.Add(new Choice(Label: "B", Detail: null));
        questions.Add(question with { Id = "q2" });

        Assert.IsType<ValueList<Choice>>(question.Choices);
        Assert.IsType<ValueList<Question>>(request.Questions);
        Assert.Single(question.Choices);
        Assert.Single(request.Questions);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-020")] // Core/Models/Requests.swift:57-65 var fields on a struct
    [Trait("Rule", "R-VAL-1")]
    public void Question_is_a_value_whose_changed_copy_leaves_the_original_alone()
    {
        var question = new Question(Id: "q", Header: "h", Prompt: "p", Choices: ValueList<Choice>.Empty,
            AllowsMultiple: false, AllowsOther: true, IsSecret: false);
        var request = new QuestionRequest(Id: "r", Questions: new ValueList<Question>([question]));

        var secret = question with { IsSecret = true };
        var moved = request with { Id = "r2" };

        Assert.False(question.IsSecret);
        Assert.True(secret.IsSecret);
        Assert.Equal("r", request.Id);
        Assert.Equal("r2", moved.Id);
        Assert.Same(question, moved.Questions[0]);
    }

    [Fact]
    [Trait("Rule", "R-ENUM-1")] // Core/Models/Requests.swift:35-41, :45-51 exhaustive switches with no default
    public void Headline_and_symbol_switches_have_no_invented_fallback_arm()
    {
        var outOfRange = Sample((Kind)99);

        Assert.Throws<SwitchExpressionException>(() => outOfRange.Headline);
        Assert.Throws<SwitchExpressionException>(() => outOfRange.Symbol);
    }

    private static ApprovalRequest Sample(Kind kind) =>
        new(Id: "id", RequestKind: kind, Title: "title", Options: ValueList<Option>.Empty);

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
