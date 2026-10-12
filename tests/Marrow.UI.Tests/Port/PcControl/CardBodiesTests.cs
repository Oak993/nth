using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the card bodies (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Approval body", "Confirm body",
// "Question body", provider approvals, the withdrawn draft; canvas `bodyOf`). Each body says DESIGN's words and turns a
// choice into the answer the broker takes: the See and Use chips (Use implies See), Reply instead's reason with Don't
// allow, Cancel with the note, chips and typed answers, Open chat with the draft.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class CardBodiesTests(HeadlessSession headless)
{
    private static readonly PillApprovalBody Approval = new("a1", 0, "Outlook", "C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE", Risky: true, UseAsked: true, AllowAlways: true, Helper: "GPT-6.1 Mini");

    private static IEnumerable<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    [Fact]
    public void The_approval_card_says_its_words_and_its_chips_choose_See_and_Use() => headless.OnUiThread(() =>
    {
        var body = new ApprovalCardBody(Approval);
        var texts = Texts(body).ToList();

        Assert.Contains(ApprovalCardBody.RiskyLine, texts);
        Assert.Contains("Also seen by GPT-6.1 Mini (fast helper)", texts);
        Assert.Equal("This chat will be able to see and use Outlook.", body.Summary);
        Assert.True(body.Use);

        Assert.Null(body.Act(ApprovalCardBody.UseId));
        Assert.Equal("This chat will only see Outlook. It can't click or type.", body.Summary);
        Assert.Equal(new PillCardAnswer("a1", 0, PillCardChoice.AllowChat, Use: false), body.Act(ApprovalCardBody.ChatId));

        // Use implies See: turning See off turns Use off; turning Use on turns See on
        Assert.Null(body.Act(ApprovalCardBody.SeeId));
        Assert.False(body.SeeChip.IsOn);
        Assert.Equal(ApprovalCardBody.NothingLine, body.Summary);
        Assert.Null(body.Act(ApprovalCardBody.ChatId));
        Assert.Null(body.Act(ApprovalCardBody.AlwaysId));
        Assert.Null(body.Act(ApprovalCardBody.UseId));
        Assert.True(body.SeeChip.IsOn);
        Assert.Equal(new PillCardAnswer("a1", 0, PillCardChoice.AllowAlways, Use: true), body.Act(ApprovalCardBody.AlwaysId));
        Assert.Equal(new PillCardAnswer("a1", 0, PillCardChoice.Deny, Use: true), body.Act(ApprovalCardBody.DenyId));
    });

    [Fact]
    public void Reply_instead_sends_the_reason_with_Dont_allow() => headless.OnUiThread(() =>
    {
        var body = new ApprovalCardBody(Approval with { Risky = false, AllowAlways = false });
        var relaid = 0;
        body.Relaid += () => relaid++;
        Assert.DoesNotContain(ApprovalCardBody.RiskyLine, Texts(body));
        Assert.Null(body.AlwaysButton);
        Assert.False(body.Composer!.IsVisible);

        Assert.Null(body.Act(ApprovalCardBody.ReplyId));
        Assert.True(body.Composer.IsVisible);
        Assert.Equal(1, relaid);
        Assert.Equal(ApprovalCardBody.ReplyPlaceholder, body.Composer.Placeholder);
        Assert.Null(body.Act(MiniComposer.SendId));
        body.Composer.Field.Text = "  Use the web version  ";

        Assert.Equal(new PillCardAnswer("a1", 0, PillCardChoice.Deny, Use: true, Text: "Use the web version"), body.Act(MiniComposer.SendId));
    });

    [Fact]
    public void A_long_path_is_cut_in_the_middle() => headless.OnUiThread(() =>
    {
        var cut = ApprovalCardBody.MiddleTruncate("C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE", 30);

        Assert.Equal(30, cut.Length);
        Assert.StartsWith("C:\\Program", cut, StringComparison.Ordinal);
        Assert.EndsWith("OUTLOOK.EXE", cut, StringComparison.Ordinal);
        Assert.Contains('…', cut);
        Assert.Equal("C:\\a.exe", ApprovalCardBody.MiddleTruncate("C:\\a.exe", 30));
    });

    [Fact]
    public void The_confirm_card_marks_what_changed_and_cancels_with_a_note() => headless.OnUiThread(() =>
    {
        var rows = new[] { new PillConfirmRow("To", "ana@example.com"), new PillConfirmRow("Subject", "Q3 numbers", Changed: true), new PillConfirmRow("Message", "Could not read", Unreadable: true, Long: true) };
        var body = new ConfirmCardBody(new PillConfirmBody("c1", 1, "Outlook", "Send", "Send", rows, Unreadable: true, ChangedNote: "Subject changed since the card opened."));
        var texts = Texts(body).ToList();

        Assert.Contains(texts, t => t.Contains("Some of it could not be read.", StringComparison.Ordinal));
        Assert.Contains("Subject changed since the card opened.", texts);
        Assert.Contains("Q3 numbers", texts);
        Assert.Contains("Confirm send", texts);
        Assert.Equal(new PillCardAnswer("c1", 1, PillCardChoice.Confirm), body.Act(ConfirmCardBody.ConfirmId));
        Assert.Equal(new PillCardAnswer("c1", 1, PillCardChoice.Cancel), body.Act(ConfirmCardBody.CancelId));

        Assert.Null(body.Act(ConfirmCardBody.ReplyId));
        Assert.True(body.Composer!.IsVisible);
        Assert.Equal(ConfirmCardBody.ReplyPlaceholder, body.Composer.Placeholder);
        body.Composer.Field.Text = "Change the subject first";
        Assert.Equal(new PillCardAnswer("c1", 1, PillCardChoice.Cancel, Text: "Change the subject first"), body.Act(MiniComposer.SendId));
    });

    [Fact]
    public void The_show_card_says_what_happens_and_answers_Show_or_Not_now() => headless.OnUiThread(() =>
    {
        var model = new PillShowBody("s1", 0, "Excel", "to read the chart");
        var body = new ShowWindowCardBody(model);

        Assert.Equal("Tactus wants to show Excel for one step: to read the chart.", ShowWindowCardBody.Lead(model));
        Assert.Contains(ShowWindowCardBody.Detail("Excel"), Texts(body));
        Assert.Equal(new PillCardAnswer("s1", 0, PillCardChoice.Show), body.Act(ShowWindowCardBody.ShowId));
        Assert.Equal(new PillCardAnswer("s1", 0, PillCardChoice.NotNow), body.Act(ShowWindowCardBody.NotNowId));
    });

    [Fact]
    public void A_question_offers_at_most_four_chips_and_a_reply() => headless.OnUiThread(() =>
    {
        var long41 = new string('x', 41);
        var model = new PillQuestionBody("q1", 2, "GPT-6.1 Sol", "Which sheet?", ["Q1", "Q2", "Q3", long41, "Q5"]);
        var body = new QuestionCardBody(model);

        Assert.Contains("GPT-6.1 Sol asks", Texts(body));
        Assert.Equal(4, body.Chips.Count);
        Assert.Equal(new string('x', 40), AutomationProperties.GetName(body.Chips[3]));
        Assert.Equal(new PillCardAnswer("q1", 2, PillCardChoice.Chip, Text: "Q2"), body.Act(QuestionCardBody.ChipPrefix + "Q2"));
        Assert.Equal(QuestionCardBody.Placeholder, body.Composer!.Placeholder);
        Assert.Null(body.Act(MiniComposer.SendId));
        body.Composer.Field.Text = "the second one";
        Assert.Equal(new PillCardAnswer("q1", 2, PillCardChoice.Send, Text: "the second one"), body.Act(MiniComposer.SendId));
        Assert.Equal(new PillCardAnswer("q1", 2, PillCardChoice.Dismiss), body.Act(QuestionCardBody.DismissId));
    });

    [Fact]
    public void A_multi_select_question_toggles_its_chips_and_sends_them() => headless.OnUiThread(() =>
    {
        var body = new QuestionCardBody(new PillQuestionBody("q2", 0, "GPT-6.1 Sol", "Which rows?", ["A", "B", "C"], Multi: true, AllowText: false, CanDismiss: false));

        Assert.Null(body.Composer);
        Assert.Null(body.Act(QuestionCardBody.SendChipsId));
        Assert.Null(body.Act(QuestionCardBody.ChipPrefix + "A"));
        Assert.Null(body.Act(QuestionCardBody.ChipPrefix + "C"));
        Assert.Null(body.Act(QuestionCardBody.DismissId));
        Assert.Equal(new[] { "A", "C" }, body.Chosen);

        var sent = body.Act(QuestionCardBody.SendChipsId);
        Assert.Equal(PillCardChoice.Send, sent!.Choice);
        Assert.Equal(new[] { "A", "C" }, sent.Chips);
    });

    [Fact]
    public void A_question_is_cut_at_600_characters_and_follows_its_scripts_direction() => headless.OnUiThread(() =>
    {
        Assert.Equal(601, QuestionCardBody.Clipped(new string('a', 700)).Length);
        Assert.Equal("short", QuestionCardBody.Clipped("short"));
        Assert.Equal(FlowDirection.RightToLeft, QuestionCardBody.Direction("کون سی شیٹ؟"));
        Assert.Equal(FlowDirection.RightToLeft, QuestionCardBody.Direction("12: איזה גיליון?"));
        Assert.Equal(FlowDirection.LeftToRight, QuestionCardBody.Direction("Which sheet?"));
    });

    [Fact]
    public void A_secret_question_and_a_provider_ask_only_point_at_the_chat() => headless.OnUiThread(() =>
    {
        var secret = new ProviderPointerBody(new PillQuestionBody("q3", 0, "GPT-6.1 Sol", "What is the PIN?", [], Secret: true));
        Assert.Contains(QuestionCardBody.SecretLine, Texts(secret));
        Assert.Null(secret.Composer);
        Assert.Equal(new PillCardAnswer("q3", 0, PillCardChoice.OpenChat), secret.Act(ProviderPointerBody.OpenChatId));

        var provider = new ProviderPointerBody(new PillProviderBody("p1", 0, "Claude wants to run a command.", "npm test"));
        var texts = Texts(provider).ToList();
        Assert.Contains("npm test", texts);
        Assert.Contains(ProviderPointerBody.ChatLine, texts);
        Assert.Equal(CardControlKind.Free, Assert.Single(provider.Controls).Kind);
    });

    [Fact]
    public void Type_in_chat_carries_the_draft_and_a_withdrawn_draft_stays() => headless.OnUiThread(() =>
    {
        var chat = new ProviderPointerBody(new PillTypeInChatBody("q1", 0, "the second one"));
        Assert.Contains(ProviderPointerBody.TypeInChatLine, Texts(chat));
        Assert.Equal(new PillCardAnswer("q1", 0, PillCardChoice.OpenChat, Text: "the second one"), chat.Act(ProviderPointerBody.OpenChatId));

        var stopped = new WithdrawnCardBody(new PillWithdrawnBody("q1", 1, AnsweredInChat: false, "the second one"));
        Assert.Contains(WithdrawnCardBody.StoppedLine, Texts(stopped));
        Assert.Contains("the second one", Texts(stopped));
        Assert.Equal(new PillCardAnswer("q1", 1, PillCardChoice.SendToChat, Text: "the second one"), stopped.Act(WithdrawnCardBody.SendToChatId));
        Assert.Equal(new PillCardAnswer("q1", 1, PillCardChoice.Discard), stopped.Act(WithdrawnCardBody.DiscardId));
        Assert.Contains(WithdrawnCardBody.AnsweredLine, Texts(new WithdrawnCardBody(new PillWithdrawnBody("q1", 1, AnsweredInChat: true, "x"))));
    });
}
