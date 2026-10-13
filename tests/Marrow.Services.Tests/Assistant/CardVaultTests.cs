using System.Reflection;
using System.Text;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "Where it is kept", "What the owner sees", "Windows Hello guards every change", "The
// fill is bound to the merchant"; 16 row AS-20; ED-NEW-dextra-as20-7, -8): the card goes to Credential Manager only sealed,
// beside its last 4 digits; saving, replacing and removing it, and every fill, ask Windows Hello first and a check that does
// not pass changes and fills nothing; nothing returns the number; a fill types each part into the shop's own field on the
// approved origin only, and every buffer that held a part is zero when it returns (the memory scan). The card is the
// published test number 4242 4242 4242 4242; no real card is ever used.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-7")]
public sealed class CardVaultTests
{
    private const string Number = "4242424242424242";
    private const string Shop = "http://shop.localhost:8080";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static (CardVault Vault, InMemoryCredentialBackend Backend, FakeHelloGate Hello, FakeCardSealer Sealer) Make()
    {
        var backend = new InMemoryCredentialBackend();
        var hello = new FakeHelloGate();
        var sealer = new FakeCardSealer();
        return (new CardVault(new SecretStore(backend), sealer, new HelloGuard(hello), "nth.dextra.card.test"), backend, hello, sealer);
    }

    private static CardEntry Typed(string number = Number, string expiry = "1230", string code = "123")
    {
        var entry = new CardEntry();
        foreach (var c in number) entry.Type(CardPart.Number, c);
        foreach (var c in expiry) entry.Type(CardPart.Expiry, c);
        foreach (var c in code) entry.Type(CardPart.Code, c);
        return entry;
    }

    private static FakeCheckoutPage SameOrigin() => new(FakeCheckoutPage.Page1(Shop, 46.80m,
        ("number", "cc-number", null, Shop, true), ("exp", "cc-exp", null, Shop, true), ("year", "cc-exp-year", null, Shop, true), ("csc", "cc-csc", null, Shop, true)));

    private static async Task<(CardVault Vault, InMemoryCredentialBackend Backend, FakeHelloGate Hello, FakeCardSealer Sealer)> Saved()
    {
        var rig = Make();
        using var entry = Typed();
        Assert.Equal(CardVaultOutcome.Done, (await rig.Vault.Save(entry, Now)).Outcome);
        rig.Hello.Reasons.Clear();
        return rig;
    }

    private static string Blobs(InMemoryCredentialBackend backend) =>
        string.Join("|", backend.TargetNames.Select(t => Encoding.Latin1.GetString(backend.Peek(t)!.Blob)));

    [Fact]
    public async Task Saving_asks_windows_hello_and_keeps_only_a_sealed_card_and_its_last_four()
    {
        var rig = Make();
        Assert.Equal(CardVaultState.NoCard, rig.Vault.State);
        using var entry = Typed();
        var saved = await rig.Vault.Save(entry, Now);
        Assert.Equal(CardVaultOutcome.Done, saved.Outcome);
        Assert.Equal(new[] { "save the card" }, rig.Hello.Reasons);
        Assert.Equal((CardVaultState.Held, "4242"), (rig.Vault.State, rig.Vault.LastFour));
        Assert.Equal(2, rig.Backend.TargetNames.Count);
        var kept = Blobs(rig.Backend);
        Assert.DoesNotContain(Number, kept, StringComparison.Ordinal);
        Assert.DoesNotContain("1230", kept, StringComparison.Ordinal);
        Assert.Contains("SEALED1:", kept, StringComparison.Ordinal);
        Assert.Equal(0, rig.Sealer.Opened); // saving and showing the last 4 never open the card
        Assert.All(entry.Buffers, b => Assert.All(b, c => Assert.Equal('\0', c)));
        Assert.Equal(0, entry.Length(CardPart.Number));
    }

    [Theory]
    [InlineData(HelloResult.Cancelled)]
    [InlineData(HelloResult.Failed)]
    [InlineData(HelloResult.Unavailable)]
    public async Task A_check_that_does_not_pass_saves_nothing_and_keeps_what_was_typed(HelloResult answer)
    {
        var rig = Make();
        rig.Hello.Then(answer);
        using var entry = Typed();
        var saved = await rig.Vault.Save(entry, Now);
        Assert.Equal((CardVaultOutcome.NotVerified, answer), (saved.Outcome, saved.Hello));
        Assert.Empty(rig.Backend.TargetNames);
        Assert.Equal(CardVaultState.NoCard, rig.Vault.State);
        Assert.Equal("4242", entry.LastFour); // the owner may try again
    }

    [Theory]
    [InlineData("4242424242424241", "1230", "123", CardEntryProblem.BadNumber)]
    [InlineData(Number, "1330", "123", CardEntryProblem.BadMonth)]
    [InlineData(Number, "0926", "123", CardEntryProblem.PastExpiry)]
    [InlineData(Number, "1230", "12", CardEntryProblem.Incomplete)]
    [InlineData("42424242", "1230", "123", CardEntryProblem.Incomplete)]
    public async Task Details_that_do_not_check_out_never_reach_windows_hello(string number, string expiry, string code, CardEntryProblem problem)
    {
        var rig = Make();
        using var entry = Typed(number, expiry, code);
        var saved = await rig.Vault.Save(entry, Now);
        Assert.Equal((CardVaultOutcome.Invalid, problem), (saved.Outcome, saved.Problem));
        Assert.Equal(0, rig.Hello.Asked);
        Assert.Empty(rig.Backend.TargetNames);
    }

    [Fact]
    public void An_expiry_is_good_through_its_month_and_only_digits_are_typed()
    {
        using var entry = Typed(expiry: "1026");
        Assert.Null(entry.Problem(Now));
        Assert.False(entry.Type(CardPart.Code, 'a'));
        Assert.False(entry.Type(CardPart.Code, '4') && entry.Type(CardPart.Code, '5'));
        entry.Erase(CardPart.Number);
        entry.Type(CardPart.Number, '1');
        Assert.Null(entry.LastFour);
        Assert.Equal(CardEntryProblem.BadNumber, entry.Problem(Now));
    }

    [Fact]
    public async Task Replacing_and_removing_ask_windows_hello_and_removing_takes_both_items_out()
    {
        var rig = await Saved();
        using (var other = Typed("5555555555554444"))
            Assert.Equal(CardVaultOutcome.Done, (await rig.Vault.Save(other, Now)).Outcome);
        Assert.Equal("4444", rig.Vault.LastFour);
        rig.Hello.Then(HelloResult.Cancelled);
        Assert.Equal(CardVaultOutcome.NotVerified, (await rig.Vault.Remove()).Outcome);
        Assert.Equal(CardVaultState.Held, rig.Vault.State);
        Assert.Equal(CardVaultOutcome.Done, (await rig.Vault.Remove()).Outcome);
        Assert.Equal(new[] { "replace the card", "remove the card", "remove the card" }, rig.Hello.Reasons);
        Assert.Equal((CardVaultState.NoCard, (string?)null), (rig.Vault.State, rig.Vault.LastFour));
        Assert.Empty(rig.Backend.TargetNames);
        Assert.Equal(CardVaultOutcome.NoCard, (await rig.Vault.Remove()).Outcome);
    }

    [Fact]
    public async Task A_label_that_cannot_be_written_takes_the_half_saved_card_out_again()
    {
        var rig = Make();
        rig.Backend.BeforeWrite = credential => credential.TargetName.Contains("last4", StringComparison.Ordinal) ? InMemoryCredentialBackend.ErrorAccessDenied : 0;
        using var entry = Typed();
        Assert.Equal(CardVaultOutcome.Failed, (await rig.Vault.Save(entry, Now)).Outcome);
        Assert.Empty(rig.Backend.TargetNames);
        Assert.Equal(CardVaultState.NoCard, rig.Vault.State);
    }

    [Fact]
    public async Task A_fill_asks_windows_hello_holds_the_agents_types_each_part_and_zeroes_every_buffer()
    {
        var rig = await Saved();
        var page = SameOrigin();
        IReadOnlyList<char[]>? chars = null;
        IReadOnlyList<byte[]>? bytes = null;
        rig.Vault.Zeroed = (c, b) => (chars, bytes) = (c, b);
        var filled = await rig.Vault.Fill(page, Shop, 46.80m, "pay $46.80 to shop.localhost");
        Assert.Equal(CardFillOutcome.Filled, filled.Outcome);
        Assert.Equal(new[] { "pay $46.80 to shop.localhost" }, rig.Hello.Reasons);
        Assert.Equal(new[] { "hold", "snapshot", "fill number (held)", "fill exp (held)", "fill year (held)", "fill csc (held)", "release" }, page.Events);
        Assert.Equal(Number, page.Filled["number"]);
        Assert.Equal("12/30", page.Filled["exp"]);
        Assert.Equal("2030", page.Filled["year"]);
        Assert.Equal("123", page.Filled["csc"]);
        // the memory scan: the very memory each field was handed, and every buffer that held the card, is zero now
        Assert.All(page.Handed, m => Assert.All(m.Span.ToArray(), c => Assert.Equal('\0', c)));
        Assert.NotNull(chars);
        Assert.All(chars!, buffer => Assert.All(buffer, c => Assert.Equal('\0', c)));
        Assert.All(bytes!, buffer => Assert.All(buffer, b => Assert.Equal(0, b)));
        Assert.NotEmpty(bytes!);
    }

    [Fact]
    public async Task A_cancelled_check_fills_nothing_and_never_opens_the_card()
    {
        var rig = await Saved();
        rig.Hello.Then(HelloResult.Cancelled);
        var page = SameOrigin();
        var filled = await rig.Vault.Fill(page, Shop, 46.80m, "pay");
        Assert.Equal((CardFillOutcome.NotVerified, HelloResult.Cancelled), (filled.Outcome, filled.Hello));
        Assert.Empty(page.Events);
        Assert.Equal(0, rig.Sealer.Opened);
    }

    [Fact]
    public async Task A_page_that_moved_after_windows_hello_fills_nothing_and_never_opens_the_card()
    {
        var rig = await Saved();
        var page = SameOrigin();
        page.Page = FakeCheckoutPage.Page1("http://collect.test:9000", 46.80m, ("number", "cc-number", null, null, true));
        var filled = await rig.Vault.Fill(page, Shop, 46.80m, "pay");
        Assert.Equal(CardFillOutcome.Refused, filled.Outcome);
        Assert.Equal(FillRefusalKind.TopFrameMoved, filled.Refusal!.Kind);
        Assert.Empty(page.Filled);
        Assert.Equal(0, rig.Sealer.Opened);
        Assert.Equal(new[] { "hold", "snapshot", "release" }, page.Events);
    }

    [Fact]
    public async Task A_field_that_will_not_take_its_part_stops_the_fill_and_still_zeroes()
    {
        var rig = await Saved();
        var page = SameOrigin();
        page.RefuseFields.Add("exp");
        Assert.Equal(CardFillOutcome.FieldRefused, (await rig.Vault.Fill(page, Shop, 46.80m, "pay")).Outcome);
        Assert.False(page.Filled.ContainsKey("csc"));
        Assert.All(page.Handed, m => Assert.All(m.Span.ToArray(), c => Assert.Equal('\0', c)));
    }

    [Fact]
    public async Task A_seal_from_elsewhere_does_not_open_and_fills_nothing()
    {
        var rig = await Saved();
        var target = rig.Backend.TargetNames.First(t => t.Contains("card", StringComparison.Ordinal) && !t.Contains("last4", StringComparison.Ordinal));
        var held = rig.Backend.Peek(target)!;
        rig.Backend.Write(held with { Blob = [1, 2, 3, 4] });
        var page = SameOrigin();
        Assert.Equal(CardFillOutcome.Damaged, (await rig.Vault.Fill(page, Shop, 46.80m, "pay")).Outcome);
        Assert.Empty(page.Filled);
    }

    [Fact]
    public void There_is_no_reveal_path_nothing_public_returns_the_card()
    {
        var secretShapes = new[] { typeof(char[]), typeof(byte[]), typeof(ReadOnlySpan<char>), typeof(Span<char>), typeof(ReadOnlyMemory<char>), typeof(Memory<char>) };
        foreach (var type in new[] { typeof(CardVault), typeof(CardEntry), typeof(CardDigits) })
        {
            var returns = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(m => m switch
            {
                MethodInfo method => (m.Name, Type: method.ReturnType),
                PropertyInfo property => (m.Name, Type: property.PropertyType),
                FieldInfo field => (m.Name, Type: field.FieldType),
                _ => (m.Name, Type: typeof(void)),
            }).ToList();
            Assert.All(returns.Where(r => r.Type == typeof(string)).Select(r => r.Name),
                name => Assert.Contains(name, new[] { "LastFour", "get_LastFour", "Service", "get_Service", "ToString", "DefaultService" }));
            Assert.DoesNotContain(returns, r => secretShapes.Contains(r.Type));
        }
        // the seal and its opening are internal: nothing outside the vault can open a card
        Assert.False(typeof(CardVault).Assembly.GetType("Marrow.Services.Assistant.DpapiCardSealer")!.IsPublic);
        Assert.False(typeof(CardVault).Assembly.GetType("Marrow.Services.Assistant.ICardSealer")!.IsPublic);
        Assert.DoesNotContain(typeof(CardVault).GetConstructors(), c => c.IsPublic);
    }
}
