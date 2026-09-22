using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// R-ERR rules (IMPLEMENTING.md 3.14). Source: drafts A and C (SwiftTry, traps, localizedDescription).
public sealed class SwiftErrorTests
{
    [Fact]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-078")]
    public void Try_question_turns_thrown_errors_and_cancellation_into_nil()
    {
        Assert.Null(SwiftTry.Optional<string>(() => throw new IOException("disk")));
        Assert.Null(SwiftTry.OptionalValue<int>(() => throw new CancellationError()));
        Assert.Equal(3, SwiftTry.OptionalValue(() => 3));
        SwiftTry.Ignore(() => throw new InvalidDataException("ignored"));
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")]
    public void Try_question_never_swallows_a_trap()
    {
        Assert.Throws<SwiftTrapException>(() => SwiftTry.Optional<string>(() => throw new SwiftTrapException("trap")));
        var big = long.MaxValue;
        Assert.Throws<OverflowException>(() => SwiftTry.OptionalValue(() => big + 1));
        int[] empty = [];
        Assert.Throws<IndexOutOfRangeException>(() => SwiftTry.OptionalValue(() => empty[1]));
        Assert.Throws<KeyNotFoundException>(() => SwiftTry.OptionalValue(() => new Dictionary<string, int>()["missing"]));
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")]
    public async Task Try_question_await_behaves_the_same()
    {
        Assert.Null(await SwiftTry.OptionalAsync<string>(() => Task.FromException<string?>(new IOException("disk"))));
        await Assert.ThrowsAsync<SwiftTrapException>(() => SwiftTry.IgnoreAsync(() => Task.FromException(new SwiftTrapException("trap"))));
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")]
    public void A_force_unwrap_of_nil_traps()
    {
        string? missing = null;
        int? none = null;
        Assert.Equal("x", SwiftTraps.Unwrap((string?)"x"));
        var error = Assert.Throws<SwiftTrapException>(() => SwiftTraps.Unwrap(missing));
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
        Assert.Throws<SwiftTrapException>(() => SwiftTraps.Unwrap(none));
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    [Trait("Oracle", "O-14")]
    public void LocalizedDescription_gives_the_description_or_Foundations_generic_text()
    {
        Assert.Equal("The agent process is not running.", SwiftErrors.LocalizedDescription(new RpcErrorSample(-32000, "The agent process is not running.")));
        Assert.Equal("The operation couldn\U00002019t be completed. (Swift.CancellationError error 1.)", SwiftErrors.LocalizedDescription(new CancellationError()));
        Assert.Equal("The operation couldn\U00002019t be completed. (DroppyCode.SampleError error 2.)", SwiftErrors.LocalizedDescription(new SampleError()));
        Assert.Contains("\U00002019", SwiftErrors.CouldNotComplete, StringComparison.Ordinal); // U+2019, not an apostrophe
    }

    [Fact]
    [Trait("Rule", "R-ERR-2")]
    public void LocalizedDescription_refuses_to_invent_text_for_a_foreign_error()
    {
        // An IOException stands in for an NSError whose Apple text Windows cannot reproduce: a per-site decision.
        Assert.Throws<NotSupportedException>(() => SwiftErrors.LocalizedDescription(new IOException("denied")));
    }
}
