using Marrow.App.Runtime;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class AutoContinueTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-184")] // App/Runtime/AutoContinue.swift:14-15
    public void Margin_is_sixty_seconds()
    {
        Assert.Equal(60d, AutoContinue.Margin);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-185")] // App/Runtime/AutoContinue.swift:11-12
    public void Continue_message_matches_the_source_literal()
    {
        Assert.Equal(
            "The usage limit has reset. Continue exactly where you left off and finish the task.",
            AutoContinue.ContinueMessage);
    }
}

// STOP App.Runtime.AutoContinue #1
// Kind: missing dependency
// Swift: DroppyCode/App/Runtime/AutoContinue.swift:43-158 (B2 1fbf81e)
// Requirements: PR-RUNTIME-180, PR-RUNTIME-181, PR-RUNTIME-182, PR-RUNTIME-183, PR-RUNTIME-184, PR-RUNTIME-185, PR-RUNTIME-186, PR-RUNTIME-245, PR-RUNTIME-246, PR-RUNTIME-247, PR-RUNTIME-248, PR-RUNTIME-249, PR-RUNTIME-250, UI-RUNTIME-016, UI-RUNTIME-033
// What is unclear or missing: The provided declarations do not include the AppSettings, ChatThread, ThreadRuntime, notification, and app-construction APIs needed to arrange the six turn-finish gates or observe wait effects. AppModel's constructor and relevant methods are stubs, and the supplied context does not provide usable constructors or test seams for these dependencies. Without those APIs, tests for note consumption, wait replacement and cancellation, reset-time lookup, notices, notifications, and follow-up sending cannot be written against settled repository types.
// Readings or options: (a) invent setup APIs or inspect private implementation details not established by the packet -> tests would guess dependency behavior; (b) wait for the dependency APIs and test seams -> tests can exercise the recorded paths without inventing them.
// Needed to proceed: usable AppModel, AppSettings, ChatThread, and ThreadRuntime setup APIs, plus a notification observation seam.
// Done so far: Tests assert the source-defined margin and continue-message literal. No tests were written for the blocked behaviors.
