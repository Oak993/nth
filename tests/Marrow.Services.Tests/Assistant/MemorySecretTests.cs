using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.2 "Never stored"): "Secrets: keys, passwords, card numbers and one-time codes are caught by the
// privacy-shield patterns before a note is written." A secret is refused whole, on every route, and the words of it are on no file.
[Trait("Decision", "ED-NEW-dextra-as12-5")]
[Trait("Feature", "AS-12")]
public sealed class MemorySecretTests : IDisposable
{
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    public static TheoryData<string, string> Secrets => new()
    {
        { "My password is hunter2horse for the staging box", "hunter2horse" },
        { "The API key is sk-proj-AbCdEfGhIjKlMnOpQrStUv1234", "AbCdEfGhIjKlMnOpQrStUv1234" },
        { "Push with ghp_abcdefghijklmnopqrstuvwxyz0123456789", "abcdefghijklmnopqrstuvwxyz0123456789" },
        { "AWS id AKIAIOSFODNN7EXAMPLE is the deploy user", "AKIAIOSFODNN7EXAMPLE" },
        { "Pay with the card 4111 1111 1111 1111 for travel", "4111 1111 1111 1111" },
        { "Card number 5555555555554444 is the work one", "5555555555554444" },
        { "The card's cvv 123 is on the back", "cvv 123" },
        { "Your verification code is 482913", "482913" },
        { "token=abc123def456ghi789 works for the CI", "abc123def456ghi789" },
        { "Database is postgres://user:pa55w0rd@db.test/app", "pa55w0rd" },
        { "Authorization: Bearer abc123def456ghi789xyz", "abc123def456ghi789xyz" },
        { "SSN 123-45-6789 is on the form", "123-45-6789" },
        { "The key is Zx9fQ2LmN8pR4tV7yB1cD5gH3jK6wA", "Zx9fQ2LmN8pR4tV7yB1cD5gH3jK6wA" },
        { "Hash 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 is the release", "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" },
        { "The pin is 4821 for the safe", "4821" },
        { "Card exp 12/27 and cvc 4821", "12/27" },
        { "my passphrase is correct horse battery staple", "correct horse battery staple" },
        { "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA1234567890\n-----END RSA PRIVATE KEY-----", "MIIEowIBAAKCAQEA1234567890" },
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void A_note_that_holds_a_secret_is_refused_whole_and_its_words_reach_no_file(string text, string secret)
    {
        var outcome = rig.Memory.Propose(MemoryRig.Clean(text));
        Assert.Equal(NoteResult.Refused, outcome.Result);
        Assert.Equal(NoteRefusal.Secret, outcome.Refusal);
        Assert.Contains("Secrets are never stored", outcome.Message, StringComparison.Ordinal);
        Assert.Null(outcome.Note);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.DoesNotContain(secret, rig.AllText(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(rig.Folder(MemoryRig.Main)) && rig.FileNames(MemoryRig.Main).Length > 0);
    }

    [Theory]
    [MemberData(nameof(Secrets))]
    public void A_secret_in_untrusted_content_is_refused_too_not_quarantined(string text, string secret)
    {
        var outcome = rig.Memory.Propose(MemoryRig.FromWeb(text));
        Assert.Equal(NoteRefusal.Secret, outcome.Refusal);
        Assert.Empty(rig.Memory.Notes(null));
        Assert.DoesNotContain(secret, rig.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_owner_typing_a_secret_is_refused_as_well_and_an_edit_cannot_add_one()
    {
        Assert.Equal(NoteRefusal.Secret, rig.Memory.Propose(MemoryRig.Typed("The wifi password is hunter2horse")).Refusal);
        var kept = rig.Memory.Propose(MemoryRig.Clean("The owner prefers short answers", NoteKind.Preference)).Note!;
        var edit = rig.Memory.Edit(kept.Id, "The owner prefers short answers; the key is sk-proj-AbCdEfGhIjKlMnOpQrStUv1234");
        Assert.Equal(NoteRefusal.Secret, edit.Refusal);
        Assert.Equal("The owner prefers short answers", rig.Memory.Find(kept.Id)!.Text);
        Assert.DoesNotContain("AbCdEfGhIjKlMnOpQrStUv1234", rig.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_batch_refuses_the_secret_and_keeps_the_notes_around_it()
    {
        var outcomes = rig.Memory.ProposeMany([
            MemoryRig.Clean("Releases go out on Fridays"),
            MemoryRig.Clean("The password is hunter2horse"),
            MemoryRig.Clean("Builds use dotnet 10"),
        ]);
        Assert.Equal([NoteResult.Kept, NoteResult.Refused, NoteResult.Kept], outcomes.Select(o => o.Result));
        Assert.Equal(2, rig.Memory.Notes(null).Count);
        Assert.DoesNotContain("hunter2horse", rig.AllText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The owner prefers a password manager over browser autofill")]
    [InlineData("Max tokens is 4096 for the summary model")]
    [InlineData("Exit code 10061 means the connection was refused")]
    [InlineData("The tokens budget resets on Monday")]
    [InlineData("Use the API key from the keychain, never from a file")]
    [InlineData("Phone support opens at nine")]
    [InlineData("Deploy to build 20481 after the review")]
    public void Ordinary_notes_that_mention_secret_words_are_kept(string text)
    {
        var outcome = rig.Memory.Propose(MemoryRig.Clean(text));
        Assert.Equal(NoteResult.Kept, outcome.Result);
    }

    [Fact]
    public void A_long_unbroken_run_is_read_in_bounded_time_and_taken_for_a_key()
    {
        // A 490-character run of mixed letters and digits: the guard answers in bounded time, and the run is a key.
        var text = new string('a', 400) + string.Concat(Enumerable.Repeat("Ab1", 30));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var held = MemoryGuard.HoldsSecret(text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(held);
    }
}
