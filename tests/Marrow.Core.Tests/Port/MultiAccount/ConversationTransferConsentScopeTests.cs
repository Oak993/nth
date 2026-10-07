using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationTransferConsentScopeTests
{
    [Fact]
    public async Task Asking_again_removes_only_that_direction_and_an_existing_preference_does_not_cover_a_new_service()
    {
        var settings = new InMemorySettingsStore();
        var prompt = new TransferPromptFake { Answer = new(true, true) };
        var consent = new ConversationTransferConsent(settings, prompt);
        Assert.True(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local, ProviderKind.Codex, ProviderKind.Local]));
        Assert.Equal([ProviderKind.Local, ProviderKind.Codex], prompt.Questions.Single().Destinations);
        consent.AskAgain(ProviderKind.Claude, ProviderKind.Codex);
        var restartedPrompt = new TransferPromptFake();
        var restarted = new ConversationTransferConsent(settings, restartedPrompt);
        Assert.True(await restarted.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local]));
        Assert.False(await restarted.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex, ProviderKind.Deepseek]));
        Assert.Equal([ProviderKind.Codex, ProviderKind.Deepseek], restartedPrompt.Questions.Single().Destinations);
    }
}
