using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

/// <summary>
/// Source-derived tests for DroppyCode/Core/Models/AntiSlopPolicy.swift (task T-M2-001).
///
/// The two expected bodies are held here as independent byte fixtures: the base64 below is the
/// UTF-8 encoding of the two Swift multi-line string literals, verbatim, with the closing
/// delimiter's four-space indentation stripped and no trailing newline (Swift's rule for a
/// multi-line literal). They were produced from the pinned source file, not from the C# under
/// test, so agreeing proves the port reproduces the source rather than restating itself.
///
/// The body is deliberately not written out as a readable C# literal here: a literal would let
/// an editor silently turn U+2019 into an ASCII apostrophe or CRLF, and the whole point of these
/// tests is that those two transformations must not happen.
/// </summary>
public class AntiSlopPolicyTests
{
    // UTF-8 bytes of enabledInstructions, from AntiSlopPolicy.swift:3-7. 1077 bytes, 5 lines.
    private const string EnabledB64 =
        "W0Ryb3BweSBDb2RlIGFudGktc2xvcCBndWlkYW5jZV0KR2l2ZSB0aGUgc2hvcnRlc3QgY29tcGxldGUgYW5zd2VyLCBsZWFkaW5nIHdpdGggdGhlIHJlc3VsdC4gVXNlIHBsYWluLCBuYXR1cmFsIGxhbmd1YWdlLiBTa2lwIGZpbGxlciwgaHlwZSwgc3RvY2sgb3BlbmluZ3MsIHJlcGVhdGVkIHN1bW1hcmllcyBhbmQgdW5zb2xpY2l0ZWQgY2xvc2Vycy4gUHJlc2VydmUgcmVxdWVzdGVkIGRldGFpbCwgZmFjdHMsIHVuY2VydGFpbnR5LCBibG9ja2VycyBhbmQgcmVxdWlyZWQgdmFsaWRhdGlvbi4KTWFrZSBmb2N1c2VkIGVkaXRzIHVzaW5nIGV4aXN0aW5nIHBhdHRlcm5zLiBBZGQgY29tbWVudHMgb25seSBmb3Igbm9uLW9idmlvdXMgcmVhc29ucywgY29uc3RyYWludHMgb3IgcGl0ZmFsbHM7IG9taXQgYmFubmVycywgb2J2aW91cyBuYXJyYXRpb24gYW5kIHNwZWN1bGF0aXZlIFRPRE9zLiBQcmVzZXJ2ZSB1c2VmdWwgZG9jdW1lbnRhdGlvbi4gQXZvaWQgdW5yZWxhdGVkIGNsZWFudXAsIGRlcGVuZGVuY2llcyBhbmQgYWJzdHJhY3Rpb25zLgpGb3IgVUkgd29yaywgZm9sbG93IHRoZSBwcm9kdWN04oCZcyBjb21wb25lbnRzLCB0eXBvZ3JhcGh5IGFuZCBjb2xvcnMuIFByZWZlciBwdXJwb3NlZnVsIHN0cnVjdHVyZSBhbmQgcmVhbCBjb250ZW50OyBwcmVzZXJ2ZSBhY2Nlc3NpYmlsaXR5LCByZXNwb25zaXZlIGxheW91dHMgYW5kIHZpc2libGUgbG9hZGluZywgZW1wdHkgYW5kIGVycm9yIHN0YXRlcy4KUmV1c2UgZXZpZGVuY2UsIHJlYWQgcmVsZXZhbnQgc2VjdGlvbnMgYW5kIGJhdGNoIGluZGVwZW5kZW50IGxvb2t1cHMuIEF2b2lkIHJlcGVhdGVkIHNjYW5zLCBwb2xsaW5nIGFuZCBlY2hvZWQgbG9ncy4gUGVyZm9ybSByZXF1aXJlZCBjaGVja3M7IGJyZXZpdHkgbmV2ZXIgZXhjdXNlcyBpbmNvbXBsZXRlIHdvcmsuIEFwcGx5IHRoaXMgZ3VpZGFuY2UgdG8gZGVsZWdhdGVkIHdvcmsgdG9vLiBSZXBvcnQgY2hhbmdlcywgYWZmZWN0ZWQgZmlsZXMgYW5kIG1hdGVyaWFsIGxpbWl0cyBvbmNlLiBVc2VyIHJlcXVlc3RzIGFuZCBwcm9qZWN0IHJlcXVpcmVtZW50cyB0YWtlIHByZWNlZGVuY2Uu";

    // UTF-8 bytes of disabledInstructions, from AntiSlopPolicy.swift:12-13. 147 bytes, 2 lines.
    private const string DisabledB64 =
        "W0Ryb3BweSBDb2RlIGFudGktc2xvcCBndWlkYW5jZTogb2ZmXQpEaXNyZWdhcmQgZWFybGllciBhcHAtcHJvdmlkZWQgYW50aS1zbG9wIGd1aWRhbmNlLiBDb250aW51ZSBmb2xsb3dpbmcgdXNlciByZXF1ZXN0cyBhbmQgcHJvamVjdCByZXF1aXJlbWVudHMu";

    private static string Enabled => Encoding.UTF8.GetString(Convert.FromBase64String(EnabledB64));
    private static string Disabled => Encoding.UTF8.GetString(Convert.FromBase64String(DisabledB64));

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ------------------------------------------------------------------ PR-STORE-129

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-129 changes (OD-0012 F1); rework.
    public void Enabled_guidance_is_the_source_literal_byte_for_byte()
    {
        byte[] expected = Utf8(Enabled);
        byte[] actual = Utf8(AntiSlopPolicy.EnabledInstructions);

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-129 changes (OD-0012 F1); rework.
    public void Enabled_guidance_digest_matches_the_source_literal()
    {
        string expected = Convert.ToHexString(SHA256.HashData(Utf8(Enabled))).ToLowerInvariant();
        string actual = Convert.ToHexString(SHA256.HashData(Utf8(AntiSlopPolicy.EnabledInstructions))).ToLowerInvariant();

        Assert.Equal("2ad357d1a93faf9544167a68b8051a0d01301c143f3c0966789d3dfe2cd65a30", expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-129 and PR-HYDRA-055 change (OD-0012 F1); rework.
    public void Enabled_guidance_is_five_lines_joined_by_single_newlines_with_no_blank_edge()
    {
        string text = AntiSlopPolicy.EnabledInstructions;

        Assert.False(text.StartsWith('\n'));
        Assert.False(text.EndsWith('\n'));
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);

        string[] lines = text.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.All(lines, l => Assert.False(string.IsNullOrEmpty(l)));
        Assert.Equal("[Droppy Code anti-slop guidance]", lines[0]);
    }

    // ------------------------------------------------------------------ PR-HYDRA-055

    [Fact]
    [Trait("Req", "PR-HYDRA-055")]
    public void Enabled_guidance_carries_u2019_and_the_us_spelling()
    {
        string text = AntiSlopPolicy.EnabledInstructions;

        Assert.Contains("product\u2019s", text, StringComparison.Ordinal);
        Assert.DoesNotContain("product's", text, StringComparison.Ordinal);
        Assert.Contains("colors", text, StringComparison.Ordinal);
        Assert.Contains("typography and colors", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ PR-STORE-130

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-130 changes (OD-0012 F1); rework.
    public void Disabled_guidance_is_the_source_literal_byte_for_byte()
    {
        byte[] expected = Utf8(Disabled);
        byte[] actual = Utf8(AntiSlopPolicy.DisabledInstructions);

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-130 changes (OD-0012 F1); rework.
    public void Disabled_guidance_digest_matches_the_source_literal()
    {
        string expected = Convert.ToHexString(SHA256.HashData(Utf8(Disabled))).ToLowerInvariant();
        string actual = Convert.ToHexString(SHA256.HashData(Utf8(AntiSlopPolicy.DisabledInstructions))).ToLowerInvariant();

        Assert.Equal("7dfa24d86f297e146cd03b2e0bb34434359279f237ffcc05efb53ea30f757a1c", expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-130 changes (OD-0012 F1); rework.
    public void Disabled_guidance_is_two_lines_joined_by_a_single_newline_with_no_blank_edge()
    {
        string text = AntiSlopPolicy.DisabledInstructions;

        Assert.False(text.StartsWith('\n'));
        Assert.False(text.EndsWith('\n'));
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);

        string[] lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.False(string.IsNullOrEmpty(l)));
        Assert.Equal("[Droppy Code anti-slop guidance: off]", lines[0]);
        Assert.Equal(
            "Disregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements.",
            lines[1]);
    }

    // ------------------------------------------------------------------ selector (PR-STORE-130, PR-HYDRA-055)

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-129 changes (OD-0012 F1); rework.
    public void Selector_returns_the_enabled_block_when_enabled()
    {
        Assert.Equal(Utf8(Enabled), Utf8(AntiSlopPolicy.Instructions(true)));
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the upstream first line, which PR-STORE-130 changes (OD-0012 F1); rework.
    public void Selector_returns_the_disabled_block_when_disabled()
    {
        Assert.Equal(Utf8(Disabled), Utf8(AntiSlopPolicy.Instructions(false)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-130")]
    [Trait("Req", "PR-HYDRA-055")]
    public void Selector_never_returns_empty_and_has_no_third_state()
    {
        string on = AntiSlopPolicy.Instructions(true);
        string off = AntiSlopPolicy.Instructions(false);

        Assert.False(string.IsNullOrEmpty(on));
        Assert.False(string.IsNullOrEmpty(off));

        // Each result is exactly one of the two constants, never something else.
        Assert.True(on == AntiSlopPolicy.EnabledInstructions || on == AntiSlopPolicy.DisabledInstructions);
        Assert.True(off == AntiSlopPolicy.EnabledInstructions || off == AntiSlopPolicy.DisabledInstructions);
        Assert.Equal(AntiSlopPolicy.EnabledInstructions, on);
        Assert.Equal(AntiSlopPolicy.DisabledInstructions, off);
    }
}
