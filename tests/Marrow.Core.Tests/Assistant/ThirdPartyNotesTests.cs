using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2 "Never stored"): "Notes about a third party are kept only when the owner states them, and only what he
// said." Find says a note is about someone else; StatedBy says the owner's own words hold everything the note says.
[Trait("Decision", "ED-NEW-dextra-as12-3")]
[Trait("Feature", "AS-12")]
public sealed class ThirdPartyNotesTests
{
    [Theory]
    [InlineData("My wife likes tulips", "a person close to the owner")]
    [InlineData("The owner's manager approves every release", "a person close to the owner")]
    [InlineData("his best friend lives in Pune", "a person close to the owner")]
    [InlineData("Our parents visit in June", "a person close to the owner")]
    [InlineData("The owner's new boss wants weekly reports", "a person close to the owner")]
    [InlineData("colleague Sam reviews the PRs", "a named person")]
    [InlineData("Her manager is Priya", "a person close to the owner")]
    [InlineData("client Acme pays on the 5th", "a named person")]
    [InlineData("friend named Asha moved", "a named person")]
    [InlineData("Dr. Rao is the dentist", "a named person")]
    [InlineData("Mrs Kapoor teaches piano", "a named person")]
    [InlineData("Priya's birthday is on June 3", "a named person's private matter")]
    [InlineData("Rahul’s salary was raised", "a named person's private matter")]
    [InlineData("Reach Asha at asha.k@example.test", "an email address")]
    [InlineData("Call +91 98765 43210 after five", "a phone number")]
    [InlineData("Office line (415) 555-0132", "a phone number")]
    [InlineData("Priya wants the weekly report on Monday", "a named person")]
    [InlineData("Rahul lives in Pune and works for Acme", "a named person")]
    [InlineData("He prefers tea over coffee", "someone other than the owner")]
    [InlineData("Remember that she is allergic to peanuts", "someone other than the owner")]
    public void A_note_about_someone_else_is_found(string text, string reason)
    {
        var finding = ThirdPartyNotes.Find(text);
        Assert.NotNull(finding);
        Assert.Equal(reason, finding!.Reason);
        Assert.Contains(finding.Quote, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("The owner prefers short answers")]
    [InlineData("Builds with dotnet 10.0.100 on Windows 11")]
    [InlineData("My client library is generated from the schema")]
    [InlineData("Droppy Code is the codebase in E:\\Droppy")]
    [InlineData("Always reply in English")]
    [InlineData("The friendly tone is preferred")]
    [InlineData("Version 10.0.19045.1234 is installed")]
    [InlineData("Ports 5000 and 5001 are used by the API")]
    [InlineData("Owner prefers short answers")]
    [InlineData("Windows needs a restart after the update")]
    [InlineData("Postgres works on port 5432 here")]
    [InlineData("Priya is nervous about the report")]
    [InlineData("")]
    [InlineData(null)]
    public void A_note_about_the_owner_and_his_work_is_not_flagged(string? text) => Assert.Null(ThirdPartyNotes.Find(text));

    [Fact]
    public void A_name_the_owner_gave_to_a_person_marks_a_later_note_that_uses_it_bare()
    {
        const string said = "My manager Priya wants the weekly report; colleague Sam reviews it; Dr. Rao is my dentist; Asha is my sister.";
        Assert.Equal(new[] { "Priya", "Sam", "Rao", "Asha" }, ThirdPartyNotes.NamesIn(said));
        var finding = ThirdPartyNotes.Find("Priya is nervous about the report", said);
        Assert.Equal(("Priya", "a person the owner named"), (finding!.Quote, finding.Reason));
        Assert.NotNull(ThirdPartyNotes.Find("Asha, again", said));
        Assert.Null(ThirdPartyNotes.Find("Priyanka is a name", said));                                        // a longer word is not the name
        Assert.Null(ThirdPartyNotes.Find("Priya is nervous about the report", "I prefer short answers"));     // nothing marked her as a person
        Assert.Empty(ThirdPartyNotes.NamesIn("I prefer short answers"));
        Assert.Empty(ThirdPartyNotes.NamesIn(null));
    }

    [Fact]
    public void Only_what_the_owner_said_is_stated_by_him()
    {
        const string said = "My manager Priya wants the weekly report on Monday morning.";
        Assert.True(ThirdPartyNotes.StatedBy(said, "Priya wants the weekly report on Monday morning"));
        Assert.True(ThirdPartyNotes.StatedBy(said, "The owner's manager Priya wants weekly reports"));
        Assert.False(ThirdPartyNotes.StatedBy(said, "Priya is nervous about the weekly report"));
        Assert.False(ThirdPartyNotes.StatedBy(said, "Priya wants the report every Friday"));
        Assert.False(ThirdPartyNotes.StatedBy(said, "Priya's number is 555 0100"));
    }

    [Fact]
    public void Nothing_is_stated_by_nothing_and_an_empty_note_says_nothing()
    {
        Assert.False(ThirdPartyNotes.StatedBy(null, "Priya wants the report"));
        Assert.False(ThirdPartyNotes.StatedBy("", "Priya wants the report"));
        Assert.False(ThirdPartyNotes.StatedBy("Priya wants the report", ""));
        Assert.False(ThirdPartyNotes.StatedBy("Priya wants the report", "the and of"));
    }

    [Fact]
    public void The_owner_saying_it_in_Hindi_states_a_note_in_the_same_words()
    {
        const string said = "मेरी मैनेजर प्रिया को सोमवार की रिपोर्ट चाहिए";
        Assert.True(ThirdPartyNotes.StatedBy(said, "प्रिया को सोमवार की रिपोर्ट चाहिए"));
        Assert.False(ThirdPartyNotes.StatedBy(said, "प्रिया को शुक्रवार की रिपोर्ट चाहिए"));
    }

    [Fact]
    public void A_long_hostile_line_is_read_in_bounded_time()
    {
        var text = string.Concat(Enumerable.Repeat("my my my my friend ", 400)) + "x";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _ = ThirdPartyNotes.Find(text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
