using Marrow.Core.Accounts;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6; ED-NEW-dextra-as14-3): "the owner's pill order as the tiebreak". The owner's order of accounts
// is the pill he uses for new threads first, then his other pills in the order he keeps them, each in its step order; an account
// takes the place where it first appears.
[Trait("Decision", "ED-NEW-dextra-as14-3")]
[Trait("Feature", "AS-14")]
public sealed class PillOrderTests
{
    private static readonly InMemoryAccountStore Store = new([
        new AccountInfo("a", "claude", "Claude 20x"),
        new AccountInfo("b", "claude", "Claude B"),
        new AccountInfo("c", "codex", "Codex A"),
        new AccountInfo("d", "codex", "Codex B"),
    ]);

    private static Pill MakePill(string name, params string[] accounts) => new(name, [.. accounts.Select(a => new PillStep(a))], Id: Guid.NewGuid());

    [Fact]
    public void The_pill_used_for_new_threads_comes_first_then_the_others_in_the_order_he_keeps_them()
    {
        var work = MakePill("Work", "a", "c");
        var night = MakePill("Night", "d", "a", "b");
        var order = PillOrder.Sequence([work, night], night.Id, Store);
        Assert.Equal(["d", "a", "b", "c"], order);
    }

    [Fact]
    public void With_no_pill_used_for_new_threads_the_pills_run_in_their_own_order()
    {
        var work = MakePill("Work", "b", "a");
        var night = MakePill("Night", "c", "a", "d");
        Assert.Equal(["b", "a", "c", "d"], PillOrder.Sequence([work, night], null, Store));
        Assert.Equal(["b", "a", "c", "d"], PillOrder.Sequence([work, night], Guid.NewGuid(), Store));
    }

    [Fact]
    public void A_step_whose_account_is_gone_is_skipped_and_no_pills_give_no_order()
    {
        var pill = MakePill("Work", "gone", "c", "also-gone", "c", "a");
        Assert.Equal(["c", "a"], PillOrder.Sequence([pill], pill.Id, Store));
        Assert.Empty(PillOrder.Sequence([], null, Store));
        Assert.Empty(PillOrder.Sequence([MakePill("Empty")], null, Store));
    }

    [Fact]
    public void A_step_that_names_an_api_key_of_a_service_stands_for_that_key()
    {
        var keys = new InMemoryAccountStore([new AccountInfo("or", "openrouter", "OpenRouter"), new AccountInfo("or-2", "openrouter", "OpenRouter 2")]);
        var pill = new Pill("Keys", [new PillStep("or", KeyId: "or-2"), new PillStep("or")], Id: Guid.NewGuid());
        Assert.Equal(["or-2", "or"], PillOrder.Sequence([pill], null, keys));
    }
}
