using System.Text.RegularExpressions;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "UI shows only the last 4 digits", "never reaches settings, logs, transcripts, crash dumps,
// exports, sync or the clipboard", "the fill buffer is zeroed"; lean-tests 10 "Logs"; ED-NEW-dextra-as20-8): with a sentinel test
// card, every path a purchase takes (paid, cancelled, handed over, refused, expired, unknown, locked out) is run and every text
// it made is scanned: the cards and their notes, the activity log's lines, what each model was told, purchases.json, the
// faults and the desk's state as a crash dump would print it. None holds a digit of the card beyond its last 4, its expiry, its
// security code or a brand. The buffers a fill used are zero after it, and no purchase source file touches the clipboard.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-8")]
public sealed class PurchaseLeakScanTests
{
    // GUIDs are hex and could hold any run of digits by chance; they are taken out before the scan.
    private static readonly Regex Guids = new("[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}", RegexOptions.CultureInvariant);

    /// <summary>What must never appear: every 6 digits in a row of the card's first 12, the expiry in each form, the code, a brand.</summary>
    private static IEnumerable<string> Forbidden()
    {
        var head = PurchaseRig.Number[..12];
        for (var i = 0; i + 6 <= head.Length; i++) yield return head.Substring(i, 6);
        yield return PurchaseRig.Number;
        yield return "12/30";
        yield return "1230";
        yield return "12/2030";
        yield return PurchaseRig.Code;
        yield return "Visa";
        yield return "VISA";
    }

    private static void Scan(IEnumerable<string> texts)
    {
        foreach (var raw in texts)
        {
            var text = Guids.Replace(raw, "");
            foreach (var bad in Forbidden()) Assert.False(text.Contains(bad, StringComparison.Ordinal), $"“{bad}” leaked into: {raw}");
        }
    }

    [Fact]
    public async Task Every_text_the_purchase_path_makes_holds_no_card_digit_beyond_the_last_4_and_no_expiry_code_or_brand()
    {
        using var rig = await new PurchaseRig().On();
        rig.SameOrigin();
        var tool = new List<string>();
        string Ask(string arguments)
        {
            var result = rig.Desk.Request(rig.Call(arguments));
            tool.Add(result.Text);
            return result.DecisionId!;
        }

        rig.Hello.Then(HelloResult.Cancelled);
        var card = Guid.Parse(Ask(PurchaseRig.Arguments()));
        tool.Add(await rig.Desk.Pay(card) ?? "");                                      // cancelled
        tool.Add(await rig.Desk.Pay(card) ?? "");                                      // paid
        rig.SameOrigin(12m).SubmitThrows = new IOException("the page closed");
        tool.Add(await rig.Desk.Pay(Guid.Parse(Ask(PurchaseRig.Arguments(12m)))) ?? "");   // unknown
        Ask(PurchaseRig.Arguments(70m));                                                // over the limit: handed over
        Ask(PurchaseRig.Arguments(kind: "subscription"));                               // refused
        Ask(PurchaseRig.Arguments(5m));
        rig.Clock.Advance(PurchaseDesk.Expiry);                                         // expired
        rig.ProviderFrame(6m);
        tool.Add(await rig.Desk.Pay(Guid.Parse(Ask(PurchaseRig.Arguments(6m)))) ?? ""); // handed over at the boundary
        rig.Hello.Then(HelloResult.Failed, HelloResult.Failed, HelloResult.Failed);
        for (var i = 0; i < 3; i++) tool.Add(await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 90m) ?? "");   // locked out

        Assert.Contains(rig.Desk.State.History, r => r.State == PurchaseRowState.Paid);
        Assert.Contains(rig.Desk.State.History, r => r.State == PurchaseRowState.Unknown);
        Assert.Equal(PurchaseWords.LockedOut, rig.Desk.State.Status);
        var everything = rig.EveryText().Concat(tool).ToList();
        Assert.Contains(everything, t => t.Contains("•••• 5556", StringComparison.Ordinal));   // the last 4 are what is shown
        Scan(everything);
        // A model is never told even the last 4: what it reads names the card {card}.
        Assert.All(rig.Delivered.Select(d => d.Text).Concat(tool), text => Assert.DoesNotContain("5556", Guids.Replace(text, ""), StringComparison.Ordinal));
        Assert.Contains(rig.Delivered, d => d.Text.Contains(PurchaseWords.Placeholder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_buffers_a_fill_used_are_zero_after_it_and_the_typed_entry_is_zero_once_saved()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var chars = new List<char[]>();
        var bytes = new List<byte[]>();
        rig.Desk.Vault.Zeroed = (c, b) =>
        {
            chars.AddRange(c);
            bytes.AddRange(b);
        };
        Assert.Null(await rig.Desk.Pay(Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!)));
        Assert.NotEmpty(chars);
        Assert.NotEmpty(bytes);
        Assert.All(chars, buffer => Assert.All(buffer, c => Assert.Equal('\0', c)));
        Assert.All(bytes, buffer => Assert.All(buffer, b => Assert.Equal(0, b)));
        Assert.All(page.Handed, m => Assert.All(m.Span.ToArray(), c => Assert.Equal('\0', c)));

        using var entry = new CardEntry();
        foreach (var c in PurchaseRig.Number) entry.Type(CardPart.Number, c);
        foreach (var c in PurchaseRig.Expiry) entry.Type(CardPart.Expiry, c);
        foreach (var c in PurchaseRig.Code) entry.Type(CardPart.Code, c);
        var buffers = entry.Buffers;
        Assert.Equal(CardVaultOutcome.Done, (await rig.Desk.SaveCard(entry)).Outcome);
        Assert.All(buffers, buffer => Assert.All(buffer, c => Assert.Equal('\0', c)));
    }

    [Fact]
    public void No_purchase_source_file_touches_the_clipboard_or_offers_a_reveal()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var files = new[]
        {
            "src/Marrow.App/Assistant/PurchaseDesk.cs", "src/Marrow.App/Assistant/AssistantController.Purchases.cs", "src/Marrow.App/Assistant/WindowsHelloGate.cs",
            "src/Marrow.App/Assistant/Views/PurchaseSettingsSection.cs", "src/Marrow.App/Assistant/Views/CardEntrySheet.cs", "src/Marrow.App/Assistant/Views/SecretDigitsBox.cs",
            "src/Marrow.Services/Assistant/CardVault.cs", "src/Marrow.Services/Assistant/CardSealer.cs", "src/Marrow.Services/Assistant/PurchaseBook.cs",
            "src/Marrow.Core/Port/Assistant/PurchaseWords.cs",
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("Clipboard", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetText", source, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(source, @"\b(Reveal|ShowNumber|Unmask)\w*\s*\(", RegexOptions.CultureInvariant), file + " has a reveal");
        }
        // The vault gives out its last 4 and nothing else in public: no member returns the number, the expiry or the code.
        var vault = typeof(CardVault).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(vault, m => m.Name is "Number" or "Expiry" or "Code" or "Open" or "Digits");
    }
}
