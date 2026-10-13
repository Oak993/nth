using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L7, gap after the first fix (DESIGN.md 7.6 "Allow broad access in the card's
// details, which names the risk", 8.1). The broad-access card named the first 24 entries in declaration order and counted the
// rest, so an app could put 24 narrow (or invalid) wildcards first and hide "*", 'unsafe-eval' or a local address in "and 1
// more"; each entry was the declared text, raw and of any length. Now, when not every entry fits, the widest of each kind is
// named first ("*", then 'unsafe-eval', then local addresses, then wildcards from the broadest); each entry is shown as broad
// access would grant it, plain and short; and an entry broad access would still refuse is not named as broad.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-5")]
public sealed class McpAppBroadCardTests
{
    private static McpAppDomainRequest? Request(params string[] connect) =>
        McpAppDomains.Request("shipyard", "Release board", new McpUiCsp(connect, [], [], []));

    private static string BroadText(params string[] connect) => Request(connect)!.BroadText!;

    private static string[] Fillers(Func<int, string> entry) => [.. Enumerable.Range(1, McpAppDomains.MaxNamed).Select(entry)];

    [Fact]
    public void Star_behind_narrow_wildcards_is_named_first()
    {
        var text = BroadText([.. Fillers(i => "*.a" + i + ".shipyard.dev"), "*"]);
        Assert.StartsWith("Allow broad access also allows: *, *.a1.shipyard.dev, ", text, StringComparison.Ordinal);
        Assert.EndsWith(", and 1 more", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Junk_wildcards_cannot_hide_star_unsafe_eval_or_a_local_address()
    {
        var text = BroadText([.. Fillers(i => "*x" + i), "'unsafe-eval'", "localhost:5173", "*"]);
        Assert.StartsWith("Allow broad access also allows: *, 'unsafe-eval', localhost:5173, *.x1, *.x2, ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("*x1", text, StringComparison.Ordinal); // named as broad access grants it
        Assert.EndsWith(", and 3 more", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_kind_keeps_its_widest_entry_named()
    {
        Assert.Contains("*.com", BroadText([.. Fillers(i => "localhost:" + (5000 + i)), "*.com"]), StringComparison.Ordinal);
        Assert.Contains("192.168.1.10", BroadText([.. Fillers(i => "*.b" + i + ".shipyard.dev"), "https://192.168.1.10"]), StringComparison.Ordinal);
        Assert.StartsWith("Allow broad access also allows: *.dev, *.c1.deep.shipyard.dev, ",
            BroadText([.. Fillers(i => "*.c" + i + ".deep.shipyard.dev"), "*.dev"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_entry_is_shown_plain_and_short()
    {
        var text = BroadText("https://*.shipyard.dev/‮gnp.exe", "https://*.shipyard.dev/" + new string('p', 500));
        Assert.DoesNotContain('‮', text);
        Assert.Contains("\\u202E", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.True(text.Length < 200, text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_entry_broad_access_would_still_refuse_is_not_named_as_broad()
    {
        var request = Request("*..", "*.shipyard-assets.dev")!;
        Assert.Equal("Allow broad access also allows: *.shipyard-assets.dev", request.BroadText);
        Assert.Equal(["*.shipyard-assets.dev"], request.Broad.Select(b => b.Source));
        Assert.Contains(request.Refused, d => d.Source == "*..");
        Assert.Null(Request("*.."));
    }
}
