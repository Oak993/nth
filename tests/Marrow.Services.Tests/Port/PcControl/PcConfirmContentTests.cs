using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 4.3, 10.4). A guarded action's confirm content: the
// category from the guarded word, the first line and its unread note, the send rows (Cc and Bcc share a row when both
// are empty, the body's first 200 characters), the last four digits of a payment method, secret fields as dots, and the
// rows that changed while the confirm waited, in the category's words.
[Trait("Decision", "ED-NEW-pcctl-16")]
public sealed class PcConfirmContentTests
{
    [Theory]
    [InlineData("send", PcConfirmCategory.Send)]
    [InlineData("press", PcConfirmCategory.Send)]
    [InlineData("publish", PcConfirmCategory.Post)]
    [InlineData("post", PcConfirmCategory.Post)]
    [InlineData("buy", PcConfirmCategory.Pay)]
    [InlineData("transfer", PcConfirmCategory.Pay)]
    [InlineData("sign out", PcConfirmCategory.Delete)]
    [InlineData("uninstall", PcConfirmCategory.Delete)]
    [InlineData("install", PcConfirmCategory.Install)]
    [InlineData("submit", PcConfirmCategory.Submit)]
    public void The_guarded_word_picks_the_category(string word, PcConfirmCategory category)
    {
        Assert.Equal(category, PcConfirmContent.CategoryOf(word));
    }

    [Fact]
    public void The_first_line_names_the_action_and_says_when_a_field_could_not_be_read()
    {
        var read = new PcConfirmContent(PcConfirmCategory.Send, "send", "Send", "Outlook", [new PcConfirmRow("To", "ana@example.com")]);
        var unread = read with { Rows = [new PcConfirmRow("To", null)] };

        Assert.Equal("nth wants to click Send in Outlook.", read.FirstLine);
        Assert.Equal("nth wants to click Send in Outlook. Some of it could not be read.", unread.FirstLine);
        Assert.Equal("Could not read", unread.Rows[0].Shown);
        Assert.Equal("nth wants to click a button with no name (it may be Send) in Teams.",
            new PcConfirmContent(PcConfirmCategory.Send, "press", "a button with no name (it may be Send)", "Teams", []).FirstLine);
        Assert.Equal("Send", new PcConfirmContent(PcConfirmCategory.Send, "send", "", "Mail", []).Verb);
    }

    [Fact]
    public void Send_rows_share_Cc_and_Bcc_when_both_are_empty_and_cut_the_body_at_200_characters()
    {
        var body = new string('a', 199) + "😀" + "rest";
        var rows = PcConfirmContent.SendRows("me@example.com", "ana@example.com", "", "", "Q3", ["plan.pdf (2 MB)"], body);

        Assert.Equal(new[] { "From", "To", "Cc, Bcc", "Subject", "Attachments", "Message" }, rows.Select(r => r.Label));
        Assert.Equal("none", rows[2].Value);
        Assert.Equal(new string('a', 199) + "…", rows[5].Value);

        var split = PcConfirmContent.SendRows(null, "ana@example.com", "bo@example.com", "", null, null, "short");
        Assert.Equal(new[] { "From", "To", "Cc", "Bcc", "Subject", "Attachments", "Message" }, split.Select(r => r.Label));
        Assert.Equal(new string?[] { null, "ana@example.com", "bo@example.com", "none", null, null, "short" }, split.Select(r => r.Value));
    }

    [Theory]
    [InlineData("Visa 4242 4242 4242 4242", "Visa •••• 4242")]
    [InlineData("4000-0000-0000-0002", "•••• 0002")]
    [InlineData("PayPal", "PayPal")]
    [InlineData("Card 12", null)]
    public void A_payment_method_shows_only_its_last_four_digits(string method, string? shown)
    {
        Assert.Equal(shown, PcConfirmContent.LastFour(method));
    }

    [Fact]
    public void A_secret_field_shows_dots_and_never_its_length()
    {
        Assert.Equal(PcConfirmContent.Dots, PcConfirmContent.Field("Password", "hunter2", secret: true).Value);
        Assert.Equal(PcConfirmContent.Dots, PcConfirmContent.Field("Password", "a much longer secret", secret: true).Value);
        Assert.Null(PcConfirmContent.Field("Password", null, secret: true).Value);
        Assert.Equal("Ana", PcConfirmContent.Field("Name", "Ana", secret: false).Value);
    }

    [Fact]
    public void Rows_that_changed_while_it_waited_are_marked_in_the_categorys_words()
    {
        var shown = new PcConfirmContent(PcConfirmCategory.Send, "send", "Send", "Outlook",
            PcConfirmContent.SendRows("me@example.com", "ana@example.com", "", "", "Q3", [], "Hi"));
        var recipients = shown with { Rows = PcConfirmContent.SendRows("me@example.com", "ana@example.com, bo@example.com", "", "", "Q3", [], "Hi") };
        var subject = shown with { Rows = PcConfirmContent.SendRows("me@example.com", "ana@example.com", "", "", "Q4", [], "Hi") };

        Assert.Empty(shown.Changes(shown with { }));
        Assert.Equal(new[] { "To" }, shown.Changes(recipients));
        Assert.Equal("The recipients changed while this waited. Check, then confirm.", shown.ChangedLine(shown.Changes(recipients)));
        Assert.Equal(new[] { "Subject" }, shown.Changes(subject));
        Assert.Equal("The message changed while this waited. Check, then confirm.", shown.ChangedLine(shown.Changes(subject)));

        var pay = new PcConfirmContent(PcConfirmCategory.Pay, "pay", "Pay", "Shop", [new PcConfirmRow("Amount", "10 EUR")]);
        var more = pay with { Rows = [new PcConfirmRow("Amount", "12 EUR"), new PcConfirmRow("Payee", "Shop")] };
        Assert.Equal(new[] { "Amount", "Payee" }, pay.Changes(more));
        Assert.Equal(new[] { "Amount" }, more.Changes(pay with { Rows = [new PcConfirmRow("Payee", "Shop")] }));
        Assert.Equal("The payment changed while this waited. Check, then confirm.", pay.ChangedLine(["Amount"]));
    }
}
