using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e).
//
// Tests for unit Services.Providers.ProviderRegistry part 5 of 5: OpenCodeModels (PR-PROVREG-247), the
// pure id-prefix split and name-prefix strip that both OpenCodeCatalog's memoised probe (PR-PROVREG-246)
// and LoadCatalog's sibling delivery (PR-PROVREG-248) build on. A sixth class in the file this unit's
// tests mirror (R-TEST-3): parts 1 to 5 live in ProviderRegistryTests.cs, ProviderRegistryRefreshTests.cs,
// ProviderRegistryCatalogTests.cs, ProviderRegistryLimitsAndCommandsTests.cs and
// ProviderRegistrySlashCommandsTests.cs.
//
// UNTESTABLE IN THIS PART (section 6: reported, not invented):
//  - PR-PROVREG-246 (the shared in-flight probe task: force discards it first, a concurrent unforced
//    caller for either OpenCode provider awaits the same stored task rather than spawning a second CLI,
//    and only a nil result clears the memo) and PR-PROVREG-248 (LoadCatalog filling the other OpenCode
//    provider's catalog from the same delivered raw list) both need OpenCodeCatalog to receive a REAL
//    delivered [ModelOption] from ACPSession.ProbeModels, i.e. a full ACP initialize/session handshake
//    over a scripted stand-in process. ProviderRegistryCatalogTests.cs (this unit, part 3) already
//    investigated exactly this: driving ACPSession.ProbeModels's real process I/O through
//    OpenCodeCatalog's MainActor.Spawn under TestRuntime's ManualMainLoop SynchronizationContext (the
//    only way to pump a MainActor.Spawn task at all, R-ISO-4) does not settle within any reasonable test
//    timeout, confirmed by running it, even though the identical probe fails fast off that context and
//    other MainActor.Invoke-driven CLI probes elsewhere in this unit pump cleanly through the same
//    harness (see that file's "OpenCode Zen/Go: shared memoised probe" comment, above its
//    load_claude_catalog_swallows_a_handshake_failure_to_nil test). That is a property of ACPSession's
//    own implementation (a different, already-committed unit), not of this file's code, and a missing- or
//    invalid-executable probe fails too fast to distinguish "one launch, two awaiters" from "two
//    launches" from ProviderRegistry's public surface without instrumenting ACPSession directly, which is
//    out of this part's scope. OpenCodeModels, the pure function both records are built from, is proven
//    exhaustively below, including the exact id-prefix and name-prefix rules PR-PROVREG-246's memo and
//    PR-PROVREG-248's sibling fill each apply their half of the delivered list through.
public sealed class ProviderRegistryOpenCodeSiblingTests
{
    private static ModelOption Option(string id, string name) => new(id, name);

    // ---- PR-PROVREG-247: id-prefix split keeps Zen's non-go rows, Go's go-prefixed rows -------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:544-549
    public void zen_keeps_every_row_whose_id_does_not_start_with_the_go_prefix()
    {
        var list = new ValueList<ModelOption>(
        [
            Option("zen-1", "Zen One"),
            Option("opencode-go/go-1", "OpenCode Go/Go One"),
            Option("zen-2", "Zen Two"),
        ]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.Opencode);
        Assert.Equal(["zen-1", "zen-2"], result.Select(o => o.Id).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:544-549
    public void go_keeps_only_rows_whose_id_starts_with_the_go_prefix()
    {
        var list = new ValueList<ModelOption>(
        [
            Option("zen-1", "Zen One"),
            Option("opencode-go/go-1", "OpenCode Go/Go One"),
            Option("opencode-go/go-2", "OpenCode Go/Go Two"),
        ]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal(["opencode-go/go-1", "opencode-go/go-2"], result.Select(o => o.Id).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:545-549 (default: list) - every other provider
    public void every_provider_other_than_zen_or_go_keeps_the_whole_list_unfiltered()
    {
        var list = new ValueList<ModelOption>([Option("zen-1", "Zen One"), Option("opencode-go/go-1", "OpenCode Go/Go One")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.Cursor);
        Assert.Equal(list, result);
    }

    // ---- PR-PROVREG-247: the redundant "<displayName>/" name prefix is stripped, case-sensitively --------

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:551-556 - the doc comment's own example
    public void a_go_row_named_opencode_go_slash_deepseek_v4_flash_shows_as_deepseek_v4_flash_under_go()
    {
        var list = new ValueList<ModelOption>([Option("opencode-go/deepseek-v4-flash", "OpenCode Go/DeepSeek V4 Flash")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal("DeepSeek V4 Flash", result[0].Name);
        Assert.Equal("opencode-go/deepseek-v4-flash", result[0].Id); // ids never change
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:552, 554-556 - "OpenCode Zen/" stripped under Zen
    public void a_zen_row_named_with_the_opencode_zen_prefix_loses_exactly_that_prefix()
    {
        var list = new ValueList<ModelOption>([Option("zen-1", "OpenCode Zen/GLM 5.3")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.Opencode);
        Assert.Equal("GLM 5.3", result[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:553-554 - HasPrefix is case-sensitive
    public void a_differently_cased_prefix_is_left_untouched()
    {
        var list = new ValueList<ModelOption>([Option("opencode-go/x", "opencode go/Lowercase Row")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal("opencode go/Lowercase Row", result[0].Name); // "OpenCode Go/" != "opencode go/"
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:554-556 - dropFirst(namePrefix.count) on a name
                                      // equal to the prefix alone leaves the empty string; the source does
                                      // not guard it (record's own failure_cases entry).
    public void a_name_equal_to_the_prefix_alone_becomes_the_empty_string()
    {
        var list = new ValueList<ModelOption>([Option("opencode-go/x", "OpenCode Go/")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal("", result[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:551-556 - a row NOT carrying the prefix is untouched
    public void a_row_not_carrying_the_display_name_prefix_keeps_its_name_as_is()
    {
        var list = new ValueList<ModelOption>([Option("zen-1", "GLM 5.3")]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.Opencode);
        Assert.Equal("GLM 5.3", result[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-247")] // ProviderRegistry.swift:544-559 - row order is preserved through the filter
    public void row_order_is_preserved_through_the_filter_and_the_name_strip()
    {
        var list = new ValueList<ModelOption>(
        [
            Option("opencode-go/b", "OpenCode Go/B"),
            Option("opencode-go/a", "OpenCode Go/A"),
        ]);
        var result = ProviderRegistry.OpenCodeModels(list, ProviderKind.OpencodeGo);
        Assert.Equal(["opencode-go/b", "opencode-go/a"], result.Select(o => o.Id).ToArray());
        Assert.Equal(["B", "A"], result.Select(o => o.Name).ToArray());
    }
}
