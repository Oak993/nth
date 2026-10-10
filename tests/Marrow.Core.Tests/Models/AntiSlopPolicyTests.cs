using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/AntiSlopPolicy.swift (B2 1fbf81e), :1-19.
//
// Tests for the anti-slop guidance texts and their selector (PR-STORE-129, PR-STORE-130, PR-HYDRA-055).
//
// Where the expectations come from, never from the C# under test:
// - SwiftEnabledB64 and SwiftDisabledB64 are the UTF-8 bytes of the two Swift multi-line string literals,
//   :2-8 and :11-14 at 1fbf81e, as the literals evaluate: the file is stored with CRLF line ends, and Swift
//   normalizes every line break inside a multi-line literal to LF; the closing delimiter's four-space indentation is
//   stripped from each line and there is no trailing newline (Swift's rules for a multi-line literal). Enabled: 1077
//   bytes, SHA-256 2ad357d1...5a30. Disabled: 147 bytes, SHA-256 7dfa24d8...7a1c.
// - The port's expected text is that source text with only its first line replaced, as the records' deviation
//   (OD-0012 F1; OD-0135 names the product Nth) says: "[Droppy Code anti-slop guidance]" (:3) becomes "[Nth anti-slop guidance]" and
//   "[Droppy Code anti-slop guidance: off]" (:12) becomes "[Nth anti-slop guidance: off]"; every other line,
//   the U+2019 RIGHT SINGLE QUOTATION MARK after "product" (bytes E2 80 99) and the US spelling "colors" are unchanged. Result: enabled
//   1069 bytes (1067 UTF-16 units), disabled 139 bytes. The digests below were computed from those bytes.
// - The readable per-line literals repeat the same text a second, independent way (transcribed from :4-7 and :13),
//   so a wrong fixture and a wrong literal would both have to agree to hide an error.
//
// Scope: these tests prove the texts and the selector, the part of each record this file owns. The delivery points
// the records also name (Hydra.swift claudeAgents/copilotAgents, ThreadRuntime.swift:1323-1334 and :1365-1367, the
// Claude, Codex, Copilot and OpenAI-compatible session launches) belong to those files' units and are tested there.
// Verification of the earlier pass (waveA.json) noted that nothing in the port joins these texts into a prompt yet:
// the byte comparison of the assembled prompt at each delivery point, including the off branch (a resumed session
// gets the cancellation block), is owed by the Hydra (Hydra.swift:808-816 and :843-872), ThreadRuntime and session units.
public sealed class AntiSlopPolicyTests
{
    // UTF-8 bytes of the Swift literal enabledInstructions, AntiSlopPolicy.swift:2-8. 1077 bytes, 5 lines.
    private const string SwiftEnabledB64 =
        "W0Ryb3BweSBDb2RlIGFudGktc2xvcCBndWlkYW5jZV0KR2l2ZSB0aGUgc2hvcnRlc3QgY29tcGxldGUgYW5zd2VyLCBsZWFkaW5nIHdpdGggdGhlIHJlc3VsdC4gVXNlIHBsYWluLCBuYXR1cmFsIGxhbmd1YWdlLiBTa2lwIGZpbGxlciwgaHlwZSwgc3RvY2sgb3BlbmluZ3MsIHJlcGVhdGVkIHN1bW1hcmllcyBhbmQgdW5zb2xpY2l0ZWQgY2xvc2Vycy4gUHJlc2VydmUgcmVxdWVzdGVkIGRldGFpbCwgZmFjdHMsIHVuY2VydGFpbnR5LCBibG9ja2VycyBhbmQgcmVxdWlyZWQgdmFsaWRhdGlvbi4KTWFrZSBmb2N1c2VkIGVkaXRzIHVzaW5nIGV4aXN0aW5nIHBhdHRlcm5zLiBBZGQgY29tbWVudHMgb25seSBmb3Igbm9uLW9idmlvdXMgcmVhc29ucywgY29uc3RyYWludHMgb3IgcGl0ZmFsbHM7IG9taXQgYmFubmVycywgb2J2aW91cyBuYXJyYXRpb24gYW5kIHNwZWN1bGF0aXZlIFRPRE9zLiBQcmVzZXJ2ZSB1c2VmdWwgZG9jdW1lbnRhdGlvbi4gQXZvaWQgdW5yZWxhdGVkIGNsZWFudXAsIGRlcGVuZGVuY2llcyBhbmQgYWJzdHJhY3Rpb25zLgpGb3IgVUkgd29yaywgZm9sbG93IHRoZSBwcm9kdWN04oCZcyBjb21wb25lbnRzLCB0eXBvZ3JhcGh5IGFuZCBjb2xvcnMuIFByZWZlciBwdXJwb3NlZnVsIHN0cnVjdHVyZSBhbmQgcmVhbCBjb250ZW50OyBwcmVzZXJ2ZSBhY2Nlc3NpYmlsaXR5LCByZXNwb25zaXZlIGxheW91dHMgYW5kIHZpc2libGUgbG9hZGluZywgZW1wdHkgYW5kIGVycm9yIHN0YXRlcy4KUmV1c2UgZXZpZGVuY2UsIHJlYWQgcmVsZXZhbnQgc2VjdGlvbnMgYW5kIGJhdGNoIGluZGVwZW5kZW50IGxvb2t1cHMuIEF2b2lkIHJlcGVhdGVkIHNjYW5zLCBwb2xsaW5nIGFuZCBlY2hvZWQgbG9ncy4gUGVyZm9ybSByZXF1aXJlZCBjaGVja3M7IGJyZXZpdHkgbmV2ZXIgZXhjdXNlcyBpbmNvbXBsZXRlIHdvcmsuIEFwcGx5IHRoaXMgZ3VpZGFuY2UgdG8gZGVsZWdhdGVkIHdvcmsgdG9vLiBSZXBvcnQgY2hhbmdlcywgYWZmZWN0ZWQgZmlsZXMgYW5kIG1hdGVyaWFsIGxpbWl0cyBvbmNlLiBVc2VyIHJlcXVlc3RzIGFuZCBwcm9qZWN0IHJlcXVpcmVtZW50cyB0YWtlIHByZWNlZGVuY2Uu";

    // UTF-8 bytes of the Swift literal disabledInstructions, AntiSlopPolicy.swift:11-14. 147 bytes, 2 lines.
    private const string SwiftDisabledB64 =
        "W0Ryb3BweSBDb2RlIGFudGktc2xvcCBndWlkYW5jZTogb2ZmXQpEaXNyZWdhcmQgZWFybGllciBhcHAtcHJvdmlkZWQgYW50aS1zbG9wIGd1aWRhbmNlLiBDb250aW51ZSBmb2xsb3dpbmcgdXNlciByZXF1ZXN0cyBhbmQgcHJvamVjdCByZXF1aXJlbWVudHMu";

    // The first lines the Swift writes (:3, :12) and the ones the port writes (deviation OD-0012 F1).
    private const string SwiftEnabledHeader = "[Droppy Code anti-slop guidance]";
    private const string PortEnabledHeader = "[Nth anti-slop guidance]";
    private const string SwiftDisabledHeader = "[Droppy Code anti-slop guidance: off]";
    private const string PortDisabledHeader = "[Nth anti-slop guidance: off]";

    // The unchanged lines, transcribed from the Swift literal a second way.
    private const string EnabledLine2 = "Give the shortest complete answer, leading with the result. Use plain, natural language. Skip filler, hype, stock openings, repeated summaries and unsolicited closers. Preserve requested detail, facts, uncertainty, blockers and required validation."; // :4
    private const string EnabledLine3 = "Make focused edits using existing patterns. Add comments only for non-obvious reasons, constraints or pitfalls; omit banners, obvious narration and speculative TODOs. Preserve useful documentation. Avoid unrelated cleanup, dependencies and abstractions."; // :5
    private const string EnabledLine4 = "For UI work, follow the product\U00002019s components, typography and colors. Prefer purposeful structure and real content; preserve accessibility, responsive layouts and visible loading, empty and error states."; // :6, U+2019 RIGHT SINGLE QUOTATION MARK
    private const string EnabledLine5 = "Reuse evidence, read relevant sections and batch independent lookups. Avoid repeated scans, polling and echoed logs. Perform required checks; brevity never excuses incomplete work. Apply this guidance to delegated work too. Report changes, affected files and material limits once. User requests and project requirements take precedence."; // :7
    private const string DisabledLine2 = "Disregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements."; // :13

    private static readonly byte[] RightSingleQuotationMarkUtf8 = [0xE2, 0x80, 0x99]; // U+2019

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // The Swift literal's bytes with only the first line replaced (OD-0012 F1). Checks the fixture's own first line
    // first, so a fixture that does not start with the Swift header fails here instead of passing by accident.
    private static byte[] PortBytes(string swiftB64, string swiftHeader, string portHeader)
    {
        byte[] swift = Convert.FromBase64String(swiftB64);
        byte[] swiftHead = Utf8(swiftHeader + "\n");
        Assert.Equal(swiftHead, swift[..swiftHead.Length]);
        return [.. Utf8(portHeader + "\n"), .. swift[swiftHead.Length..]];
    }

    private static byte[] ExpectedEnabled => PortBytes(SwiftEnabledB64, SwiftEnabledHeader, PortEnabledHeader);

    private static byte[] ExpectedDisabled => PortBytes(SwiftDisabledB64, SwiftDisabledHeader, PortDisabledHeader);

    // ------------------------------------------------------------------ fixture integrity (no port code involved)

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_fixtures_are_the_Swift_literal_bytes_and_agree_with_the_transcribed_lines()
    {
        byte[] enabled = Convert.FromBase64String(SwiftEnabledB64);
        byte[] disabled = Convert.FromBase64String(SwiftDisabledB64);

        Assert.Equal(1077, enabled.Length);
        Assert.Equal("2ad357d1a93faf9544167a68b8051a0d01301c143f3c0966789d3dfe2cd65a30", Sha256Hex(enabled));
        Assert.Equal(147, disabled.Length);
        Assert.Equal("7dfa24d86f297e146cd03b2e0bb34434359279f237ffcc05efb53ea30f757a1c", Sha256Hex(disabled));

        string enabledLiteral = string.Join('\n', SwiftEnabledHeader, EnabledLine2, EnabledLine3, EnabledLine4, EnabledLine5);
        string disabledLiteral = string.Join('\n', SwiftDisabledHeader, DisabledLine2);
        Assert.Equal(enabled, Utf8(enabledLiteral));
        Assert.Equal(disabled, Utf8(disabledLiteral));
    }

    // ------------------------------------------------------------------ PR-STORE-129: the enabled text

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:2-8, deviation OD-0012 F1
    public void Enabled_instructions_are_the_Swift_literal_bytes_with_only_the_first_line_renamed()
    {
        byte[] actual = Utf8(AntiSlopPolicy.EnabledInstructions);

        Assert.Equal(1069, ExpectedEnabled.Length);
        Assert.Equal(ExpectedEnabled.Length, actual.Length);
        Assert.Equal(ExpectedEnabled, actual);
    }

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:2-8, deviation OD-0012 F1
    public void Enabled_instructions_digest_is_the_digest_of_the_renamed_Swift_literal()
    {
        const string expected = "94d3df4e3422678b3f7766826c74acb5a0f8782c357f1e2c3fb66be95d8a75ea";

        Assert.Equal(expected, Sha256Hex(ExpectedEnabled));
        Assert.Equal(expected, Sha256Hex(Utf8(AntiSlopPolicy.EnabledInstructions)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:3-7
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:3-7
    public void Enabled_instructions_are_five_lines_joined_by_single_newlines_with_no_blank_edge()
    {
        string text = AntiSlopPolicy.EnabledInstructions;

        Assert.Equal(1067, text.Length);
        Assert.False(text.StartsWith('\n'));
        Assert.False(text.EndsWith('\n'));
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);

        string[] lines = text.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.Equal(PortEnabledHeader, lines[0]);
        Assert.Equal(EnabledLine2, lines[1]);
        Assert.Equal(EnabledLine3, lines[2]);
        Assert.Equal(EnabledLine4, lines[3]);
        Assert.Equal(EnabledLine5, lines[4]);
        Assert.StartsWith("[Nth anti-slop guidance]\n", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:6
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:6
    public void Enabled_instructions_carry_U2019_as_E2_80_99_in_products_and_the_US_spelling_colors()
    {
        string text = AntiSlopPolicy.EnabledInstructions;
        byte[] bytes = Utf8(text);

        // The one non-ASCII character is U+2019, at UTF-16 offset 559 and byte offset 559 (all ASCII before it).
        Assert.Equal(559, text.IndexOf('\U00002019', StringComparison.Ordinal));
        Assert.Equal(1, text.Count(c => c > 0x7F));
        Assert.Equal(RightSingleQuotationMarkUtf8, bytes[559..562]);
        Assert.Contains("product\U00002019s components", text, StringComparison.Ordinal);

        // No ASCII apostrophe anywhere in the literal.
        Assert.DoesNotContain((byte)0x27, bytes);
        Assert.DoesNotContain("product's", text, StringComparison.Ordinal);

        Assert.Contains("typography and colors.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("colour", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:16-18
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:16-18
    public void Instructions_enabled_true_returns_the_enabled_text_byte_for_byte()
    {
        Assert.Equal(ExpectedEnabled, Utf8(AntiSlopPolicy.Instructions(enabled: true)));
    }

    // ------------------------------------------------------------------ PR-STORE-130: the disabled text

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:11-14, deviation OD-0012 F1
    public void Disabled_instructions_are_the_Swift_literal_bytes_with_only_the_first_line_renamed()
    {
        byte[] actual = Utf8(AntiSlopPolicy.DisabledInstructions);

        Assert.Equal(139, ExpectedDisabled.Length);
        Assert.Equal(ExpectedDisabled.Length, actual.Length);
        Assert.Equal(ExpectedDisabled, actual);
    }

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:11-14, deviation OD-0012 F1
    public void Disabled_instructions_digest_is_the_digest_of_the_renamed_Swift_literal()
    {
        const string expected = "855b05d5311f61fed787ef8531c3a48b08bf3440779320846c61f49c0ca716a4";

        Assert.Equal(expected, Sha256Hex(ExpectedDisabled));
        Assert.Equal(expected, Sha256Hex(Utf8(AntiSlopPolicy.DisabledInstructions)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:12-13
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:12-13
    public void Disabled_instructions_are_exactly_the_two_line_cancellation_text()
    {
        string text = AntiSlopPolicy.DisabledInstructions;

        Assert.Equal(
            "[Nth anti-slop guidance: off]\nDisregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements.",
            text);
        Assert.DoesNotContain('\r', text);

        string[] lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(PortDisabledHeader, lines[0]);
        Assert.Equal(DisabledLine2, lines[1]);
    }

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:10-18
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:10-18
    public void Instructions_enabled_false_returns_the_cancellation_block_instead_of_nothing()
    {
        string off = AntiSlopPolicy.Instructions(enabled: false);

        // The record's failure case: a port that omits the block when the setting is off.
        Assert.NotNull(off);
        Assert.NotEqual(string.Empty, off);
        Assert.Equal(ExpectedDisabled, Utf8(off));
    }

    // ------------------------------------------------------------------ the selector: two constants, no third state

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:16-18
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:16-18
    public void Instructions_never_returns_an_empty_string_and_returns_one_of_the_two_constants_for_each_flag()
    {
        string on = AntiSlopPolicy.Instructions(enabled: true);
        string off = AntiSlopPolicy.Instructions(enabled: false);

        // The runtime's !instructions.isEmpty guard (ThreadRuntime.swift:1323-1334) is therefore always true.
        Assert.False(string.IsNullOrEmpty(on));
        Assert.False(string.IsNullOrEmpty(off));
        Assert.Equal(AntiSlopPolicy.EnabledInstructions, on);
        Assert.Equal(AntiSlopPolicy.DisabledInstructions, off);
        Assert.NotEqual(on, off);

        // A static let: the same value on every call.
        Assert.Equal(on, AntiSlopPolicy.Instructions(enabled: true));
        Assert.Equal(off, AntiSlopPolicy.Instructions(enabled: false));
    }

    [Fact]
    [Trait("Req", "PR-STORE-129")] // Core/Models/AntiSlopPolicy.swift:3, deviation OD-0012 F1
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:12, deviation OD-0012 F1
    [Trait("Req", "PR-HYDRA-055")] // Core/Models/AntiSlopPolicy.swift:3 and :12, deviation OD-0012 F1
    public void Neither_text_names_the_upstream_product_and_only_the_first_lines_differ_from_the_Swift()
    {
        string on = AntiSlopPolicy.Instructions(enabled: true);
        string off = AntiSlopPolicy.Instructions(enabled: false);

        Assert.DoesNotContain("Droppy", on, StringComparison.Ordinal);
        Assert.DoesNotContain("Droppy", off, StringComparison.Ordinal);

        string swiftOn = Encoding.UTF8.GetString(Convert.FromBase64String(SwiftEnabledB64));
        string swiftOff = Encoding.UTF8.GetString(Convert.FromBase64String(SwiftDisabledB64));
        Assert.Equal(swiftOn[(SwiftEnabledHeader.Length + 1)..], on[(PortEnabledHeader.Length + 1)..]);
        Assert.Equal(swiftOff[(SwiftDisabledHeader.Length + 1)..], off[(PortDisabledHeader.Length + 1)..]);
    }

    [Fact]
    [Trait("Req", "PR-STORE-130")] // Core/Models/AntiSlopPolicy.swift:16 static func instructions(enabled: Bool) -> String
    public void The_selector_takes_a_plain_bool_and_returns_a_string_so_there_is_no_third_state()
    {
        // The record's windows_path: "The selector takes a Bool and returns one of the two constants; there is no
        // third state." A bool? parameter or a second overload would admit one.
        var methods = typeof(AntiSlopPolicy).GetMethods().Where(m => m.Name == "Instructions").ToArray();

        var method = Assert.Single(methods);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(string), method.ReturnType);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(bool), parameter.ParameterType);
        Assert.Equal("enabled", parameter.Name);
    }
}
