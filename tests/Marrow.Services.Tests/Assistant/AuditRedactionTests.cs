using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: what the audit log keeps of an action (DESIGN.md 12.5 and 12.7, unit AS-3): typed text as its length only,
// secrets masked wherever they appear (target, inputs, result, every text field), card-like numbers masked, ordinary
// text kept as it is, and long text cut after the mask. Every secret here is made up; the card numbers are built from
// an unissued 9999 prefix with their Luhn digit computed here.
[Trait("Decision", "ED-NEW-dextra-as3-4")]
[Trait("Feature", "AS-3")]
public sealed class AuditRedactionTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => temp.Dispose();

    /// <summary>A made-up number from <paramref name="body"/> with its Luhn check digit appended.</summary>
    private static string WithLuhnDigit(string body)
    {
        for (var digit = 0; digit <= 9; digit++)
            if (AuditRedactor.PassesLuhn(body + digit)) return body + digit;
        throw new InvalidOperationException("no check digit");
    }

    private string LoggedText(AuditEntry entry)
    {
        using (var log = AuditLog.Open(temp.Roots, temp.Keys, clock)) log.Append(entry);
        return File.ReadAllText(Path.Combine(temp.AuditDirectory, "2026-10-12.jsonl"));
    }

    public static TheoryData<string, string> Secrets() => new()
    {
        { "anthropic key", "sk-ant-api03-FAKEfakeFAKEfake0123456789" },
        { "openai key", "sk-proj-FAKEfakeFAKEfake0123456789ab" },
        { "github token", "ghp_FAKEfakeFAKEfakeFAKEfakeFAKEfake0123" },
        { "github fine-grained token", "github_pat_11FAKEFAKE0123456789_fakefakefake" },
        { "gitlab token", "glpat-FAKEfakeFAKEfake0123" },
        { "slack token", "xoxb-0000000000-fakefakefake" },
        { "aws key id", "AKIAFAKEFAKEFAKEFAKE" },
        { "google key", "AIzaFAKEfakeFAKEfakeFAKEfakeFAKEfake012" },
        { "stripe key", "sk_test_FAKEfakeFAKEfake0123" },
        { "web token", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmYWtlLXVzZXIifQ.ZmFrZS1zaWduYXR1cmUtMDEy" },
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void A_recognised_key_is_masked_in_every_field_it_appears_in(string kind, string secret)
    {
        Assert.NotEmpty(kind);
        var text = LoggedText(new AuditEntry
        {
            Assistant = "dextra",
            Tool = "shell",
            ActionClass = "Run",
            Target = "https://api.example.test/v1?x=" + secret,
            Inputs = [AuditInput.Plain("command", "deploy --with " + secret + " now")],
            Result = "echoed " + secret,
            AllowedBy = "decision:d-1",
        });
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(secret[^8..], text, StringComparison.Ordinal);
        var line = JsonNode.Parse(text)!;
        Assert.Equal("deploy --with [secret] now", line["inputs"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("echoed [secret]", line["result"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("password=fake-hunter2", "password=[secret]")]
    [InlineData("DB_PASSWORD: fake-hunter2", "DB_PASSWORD: [secret]")]
    [InlineData("{\"api_key\": \"fake-key-123456\"}", "{\"api_key\": \"[secret]\"}")]
    [InlineData("client_secret=abc123fake&next=1", "client_secret=[secret]&next=1")]
    [InlineData("tool --token abcdef123456 --verbose", "tool --token [secret] --verbose")]
    [InlineData("Set-Thing -Password fakePass99", "Set-Thing -Password [secret]")]
    [InlineData("curl -H 'Authorization: Bearer abcdefgh12345678'", "curl -H 'Authorization: Bearer [secret]'")]
    [InlineData("git clone https://me:fakepass99@git.example.test/r.git", "git clone https://me:[secret]@git.example.test/r.git")]
    [InlineData("https://cdn.example.test/f?sig=abc123fake&size=2", "https://cdn.example.test/f?sig=[secret]&size=2")]
    public void The_value_of_a_secret_name_is_masked_and_the_name_kept(string input, string expected) =>
        Assert.Equal(expected, AuditRedactor.Redact(input));

    [Fact]
    public void A_private_key_block_is_masked_whole()
    {
        var block = "-----BEGIN OPENSSH PRIVATE KEY-----\nZmFrZSBrZXkgYm9keQ==\n-----END OPENSSH PRIVATE KEY-----";
        Assert.Equal("key: [secret] end", AuditRedactor.Redact("key: " + block + " end"));
    }

    [Fact]
    public void A_card_like_number_that_passes_luhn_is_masked_in_any_grouping()
    {
        var number = WithLuhnDigit("999988887777666");
        var grouped = $"{number[..4]} {number[4..8]} {number[8..12]} {number[12..]}";
        var dashed = grouped.Replace(' ', '-');
        foreach (var form in new[] { number, grouped, dashed })
            Assert.Equal("pay with [secret] today", AuditRedactor.Redact("pay with " + form + " today"));
        var text = LoggedText(new AuditEntry { Assistant = "dextra", Tool = "browser", ActionClass = "Pay", Inputs = [AuditInput.Plain("field", grouped)] });
        foreach (var group in new[] { number[..4] + " " + number[4..8], number[8..12] + " " + number[12..], number })
            Assert.DoesNotContain(group, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_text_is_kept_as_it_is()
    {
        var notCard = "9999888877776660";
        if (AuditRedactor.PassesLuhn(notCard)) notCard = "9999888877776661";
        Assert.False(AuditRedactor.PassesLuhn(notCard));
        foreach (var text in new[]
        {
            "git status",
            "git checkout 9cb24e0f3a1b2c3d4e5f60718293a4b5c6d7e8f9",
            "dotnet test --filter FullyQualifiedName~Store",
            "the session token expired at noon",
            "tokens used: 1840",
            "order 123456789012 shipped",
            "ticket " + notCard,
            "C:\\Users\\owner\\Documents\\notes.md",
            "Remove-Item -Recurse build",
        })
            Assert.Equal(text, AuditRedactor.Redact(text));
    }

    [Fact]
    public void Typed_text_is_logged_as_its_length_only()
    {
        var typed = "Dear Sam, the keys are under the mat 👋";
        var input = AuditInput.Typed("text", typed);
        Assert.Null(input.Text);
        Assert.Equal(38, input.TypedLength);
        var text = LoggedText(new AuditEntry { Assistant = "dextra", Tool = "keyboard", ActionClass = "Type", Inputs = [input] });
        Assert.DoesNotContain("Dear Sam", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mat", text, StringComparison.Ordinal);
        Assert.Equal(38, JsonNode.Parse(text)!["inputs"]![0]!["typedLength"]!.GetValue<int>());
    }

    [Fact]
    public void A_secret_input_keeps_nothing_but_the_mask_and_numbers_and_flags_stay_numbers()
    {
        var text = LoggedText(new AuditEntry
        {
            Assistant = "dextra",
            Tool = "settings",
            ActionClass = "Write",
            Inputs = [AuditInput.Secret("password"), AuditInput.Numeric("retries", 3), AuditInput.Numeric("ratio", double.NaN), AuditInput.Boolean("force", true)],
        });
        var inputs = JsonNode.Parse(text)!["inputs"]!.AsArray();
        Assert.Equal("[secret]", inputs[0]!["secret"]!.GetValue<string>());
        Assert.Equal(3, inputs[1]!["number"]!.GetValue<double>());
        Assert.Equal("NaN", inputs[2]!["number"]!.GetValue<string>());
        Assert.True(inputs[3]!["flag"]!.GetValue<bool>());
        Assert.Equal(AuditVerificationStatus.Verified, AuditVerifier.Verify(temp.Roots, temp.Keys).Status);
    }

    [Fact]
    public void A_line_keeps_at_most_64_inputs_and_counts_the_rest()
    {
        var inputs = Enumerable.Range(1, 100).Select(i => AuditInput.Plain("arg" + i, "value " + i)).ToList();
        var text = LoggedText(new AuditEntry { Assistant = "dextra", Tool = "shell", ActionClass = "Run", Inputs = inputs });
        var line = JsonNode.Parse(text)!;
        Assert.Equal(64, line["inputs"]!.AsArray().Count);
        Assert.Equal("arg64", line["inputs"]![63]!["name"]!.GetValue<string>());
        Assert.Equal(36, line["inputsNotLogged"]!.GetValue<int>());
        Assert.Equal(AuditVerificationStatus.Verified, AuditVerifier.Verify(temp.Roots, temp.Keys).Status);
    }

    [Fact]
    public void A_long_value_scans_in_one_pass_and_is_never_masked_whole_by_a_timeout()
    {
        foreach (var text in new[]
        {
            new string('b', AuditRedactor.MaxScanChars),
            string.Concat(Enumerable.Repeat("a.", AuditRedactor.MaxScanChars / 2)),
            string.Concat(Enumerable.Repeat("a-", AuditRedactor.MaxScanChars / 2)),
            string.Concat(Enumerable.Repeat("password", AuditRedactor.MaxScanChars / 8)),
            string.Concat(Enumerable.Repeat("-p ", AuditRedactor.MaxScanChars / 3)),
            new string('1', AuditRedactor.MaxScanChars),
        })
            Assert.NotEqual(AuditRedactor.Mask, AuditRedactor.Redact(text));
    }

    [Fact]
    public void Long_text_is_masked_first_then_cut_and_a_secret_past_the_scan_limit_is_never_kept()
    {
        var secret = "ghp_FAKEfakeFAKEfakeFAKEfakeFAKEfake0123";
        var near = new string('a', 100) + " " + secret + " " + new string('b', 20000);
        var redacted = AuditRedactor.Redact(near);
        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.StartsWith(new string('a', 100) + " [secret] ", redacted, StringComparison.Ordinal);
        Assert.EndsWith("…[+" + (near.Length - AuditRedactor.MaxKeptChars) + " chars]", redacted, StringComparison.Ordinal);
        var far = new string('c', AuditRedactor.MaxScanChars - 10) + secret;
        var cut = AuditRedactor.Redact(far);
        Assert.DoesNotContain("ghp_", cut, StringComparison.Ordinal);
        Assert.True(cut.Length < AuditRedactor.MaxKeptChars + 40);
    }
}
