using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "What it never answers", 12.1; ED-NEW-dextra-as22-2): what a question is about, read from its words.
// Money, credentials, anything sent out, deleting, installing and a promise are always the owner's; a taste pick is his unless a recorded
// decision settles it. English, Hindi and Urdu (both scripts and romanised), through hidden characters and look-alike letters.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-2")]
public sealed class DelegateTopicsTests
{
    [Theory]
    [InlineData("Should I pay for the Pro plan?", DelegateReason.Money)]
    [InlineData("Buy the licence for $49 now?", DelegateReason.Money)]
    [InlineData("Is 500 rupees fine for the domain?", DelegateReason.Money)]
    [InlineData("Subscribe to the newsletter service?", DelegateReason.Money)]
    [InlineData("Place the order for the filter papers?", DelegateReason.Money)]
    [InlineData("Add the item to the cart and go on?", DelegateReason.Money)]
    [InlineData("Put two of them into the basket?", DelegateReason.Money)]
    [InlineData("Move it to the bank account listed?", DelegateReason.Money)]
    [InlineData("Is this the right passport number?", DelegateReason.Credentials)]
    [InlineData("Should I use the account number from the invoice email?", DelegateReason.Credentials)]
    [InlineData("Do you want me to store the seed phrase in the notes?", DelegateReason.Credentials)]
    [InlineData("Comment on the pull request to say it is fixed?", DelegateReason.Outward)]
    [InlineData("Open an issue for this bug?", DelegateReason.Outward)]
    [InlineData("Tag the release as v1.2 now?", DelegateReason.Outward)]
    [InlineData("क्या मैं भुगतान कर दूँ?", DelegateReason.Money)]
    [InlineData("کیا میں ادائیگی کر دوں؟", DelegateReason.Money)]
    [InlineData("Kya main kharidari kar doon?", DelegateReason.Money)]
    [InlineData("What is the API key for the staging server?", DelegateReason.Credentials)]
    [InlineData("Enter your password to continue", DelegateReason.Credentials)]
    [InlineData("Should I sign in as the admin user?", DelegateReason.Credentials)]
    [InlineData("पासवर्ड क्या है?", DelegateReason.Credentials)]
    [InlineData("پاس ورڈ کیا ہے؟", DelegateReason.Credentials)]
    [InlineData("Do you want me to read the .env file?", DelegateReason.Credentials)]
    [InlineData("Should I email the report to the whole team?", DelegateReason.Outward)]
    [InlineData("Post this summary on Slack?", DelegateReason.Outward)]
    [InlineData("Run git push to origin now?", DelegateReason.Outward)]
    [InlineData("Open a pull request for this branch?", DelegateReason.Outward)]
    [InlineData("Deploy the build to production?", DelegateReason.Outward)]
    [InlineData("रिपोर्ट टीम को भेज दूँ?", DelegateReason.Outward)]
    [InlineData("Delete the old build folder first?", DelegateReason.Destructive)]
    [InlineData("Overwrite the existing config with the new one?", DelegateReason.Destructive)]
    [InlineData("Drop the table and recreate it?", DelegateReason.Destructive)]
    [InlineData("Run rm -rf on the cache directory?", DelegateReason.Destructive)]
    [InlineData("फ़ाइल डिलीट कर दूँ?", DelegateReason.Destructive)]
    [InlineData("Install the missing package?", DelegateReason.Install)]
    [InlineData("Run npm install before building?", DelegateReason.Install)]
    [InlineData("winget install Git.Git, ok?", DelegateReason.Install)]
    [InlineData("Add a new dependency for date parsing?", DelegateReason.Install)]
    [InlineData("Book the 3 pm slot with them?", DelegateReason.Commitment)]
    [InlineData("Accept the terms and continue?", DelegateReason.Commitment)]
    [InlineData("Do you agree to the licence?", DelegateReason.Commitment)]
    [InlineData("Promise them a fix by Friday?", DelegateReason.Commitment)]
    public void A_topic_the_owner_always_confirms_is_found(string text, DelegateReason expected) =>
        Assert.Equal(expected, DelegateTopics.Find(text));

    [Theory]
    [InlineData("Which of the two test frameworks does this project use?")]
    [InlineData("Should the helper be async?")]
    [InlineData("Is the config file called appsettings.json?")]
    [InlineData("Keep SQLite or move to a file-based store?")]
    [InlineData("Run the existing unit tests first?")]
    [InlineData("Which branch should I base this on?")]
    [InlineData("Should the order of the columns follow the schema?")]
    [InlineData("Is the cart component in the components folder?")]
    [InlineData("Which cryptography library does the repo use?")]
    [InlineData("Does the issue tracker config live in appsettings?")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_ordinary_technical_question_touches_none(string text) => Assert.Null(DelegateTopics.Find(text));

    [Fact]
    public void Hidden_characters_and_look_alike_letters_do_not_hide_a_topic()
    {
        Assert.Equal(DelegateReason.Credentials, DelegateTopics.Find("Enter the pa​ssw‍ord"));
        Assert.Equal(DelegateReason.Destructive, DelegateTopics.Find("ｄｅｌｅｔｅ the folder?"));
        // a Cyrillic "е" in "send" and a soft hyphen in "install"
        Assert.Equal(DelegateReason.Outward, DelegateTopics.Find("sеnd it to the client?"));
        Assert.Equal(DelegateReason.Install, DelegateTopics.Find("ins­tall the tool?"));
    }

    [Fact]
    public void Credentials_come_before_money_and_money_before_sending()
    {
        Assert.Equal(DelegateReason.Credentials, DelegateTopics.Find("Pay with the password you saved?"));
        Assert.Equal(DelegateReason.Money, DelegateTopics.Find("Send the payment now?"));
    }

    [Theory]
    [InlineData("Which chart style do you prefer?")]
    [InlineData("What colour should the button be?")]
    [InlineData("What should the new file be named?")]
    [InlineData("Pick a font for the header")]
    [InlineData("It is up to you")]
    public void A_design_or_taste_pick_reads_like_one(string text) => Assert.True(DelegateTopics.LooksLikeTaste(text));

    [Theory]
    [InlineData("Does the build use MSBuild?")]
    [InlineData("Which test project covers the router?")]
    [InlineData("")]
    public void A_technical_question_does_not_read_like_taste(string text) => Assert.False(DelegateTopics.LooksLikeTaste(text));

    [Fact]
    public void A_long_text_is_read_in_bounded_time_and_only_up_to_its_limit()
    {
        var filler = new string('a', 200_000);
        Assert.Null(DelegateTopics.Find(filler));
        Assert.False(DelegateTopics.LooksLikeTaste(filler));
        // the limit is the front of the text: a word past it is not read (the delegate refuses longer questions before this matters)
        Assert.Null(DelegateTopics.Find(new string('x', DelegateTopics.ScanChars + 10) + " delete"));
        Assert.Equal(DelegateReason.Destructive, DelegateTopics.Find("delete " + new string('x', 3_000)));
    }
}
