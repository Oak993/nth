using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationTransferConsentTests
{
    [Fact]
    public async Task Another_service_is_asked_first_with_an_unchecked_box_and_refusal_saves_nothing()
    {
        var settings = new InMemorySettingsStore();
        var prompt = new TransferPromptFake();
        var consent = new ConversationTransferConsent(settings, prompt);
        Assert.False(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local, ProviderKind.Codex]));
        var question = Assert.Single(prompt.Questions);
        Assert.False(question.DontAskAgain);
        Assert.Equal("Don't ask again", question.CheckboxLabel);
        Assert.Equal([ProviderKind.Local, ProviderKind.Codex], question.Destinations);
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public async Task Accepting_without_checking_the_box_asks_again_and_does_not_enable_the_Handshake()
    {
        var settings = new InMemorySettingsStore();
        var prompt = new TransferPromptFake { Answer = new(true, false) };
        var consent = new ConversationTransferConsent(settings, prompt);
        Assert.True(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex]));
        Assert.True(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex]));
        Assert.Equal(2, prompt.Questions.Count);
        Assert.Empty(settings.Writes);
        Assert.False(new Marrow.Core.Port.MultiAccount.Settings.HandshakeSwitch(settings).Enabled);
    }

    [Fact]
    public async Task Only_checked_acceptance_persists_and_it_covers_only_the_approved_service_pairs_after_restart()
    {
        var settings = new InMemorySettingsStore();
        var prompt = new TransferPromptFake { Answer = new(true, true) };
        var consent = new ConversationTransferConsent(settings, prompt);
        Assert.True(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex]));
        Assert.Single(settings.Writes);
        var restartedPrompt = new TransferPromptFake();
        var restarted = new ConversationTransferConsent(settings, restartedPrompt);
        Assert.True(await restarted.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Codex]));
        Assert.Empty(restartedPrompt.Questions);
        Assert.False(await restarted.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Codex, [ProviderKind.Claude]));
        Assert.False(await restarted.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local]));
        Assert.Equal(2, restartedPrompt.Questions.Count);
        Assert.False(new Marrow.Core.Port.MultiAccount.Settings.HandshakeSwitch(settings).Enabled);
    }

    [Fact]
    public async Task Same_service_needs_no_transfer_prompt_and_a_checked_refusal_is_never_remembered()
    {
        var settings = new InMemorySettingsStore();
        var prompt = new TransferPromptFake { Answer = new(false, true) };
        var consent = new ConversationTransferConsent(settings, prompt);
        Assert.True(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Claude]));
        Assert.Empty(prompt.Questions);
        Assert.False(await consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local]));
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public async Task Cancellation_cannot_save_a_late_acceptance_and_corrupt_preferences_are_preserved()
    {
        var settings = new InMemorySettingsStore();
        var pending = new TaskCompletionSource<ConversationTransferAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new TransferPromptFake { Pending = pending.Task };
        var consent = new ConversationTransferConsent(settings, prompt);
        using var cancel = new CancellationTokenSource();
        var wait = consent.AuthorizeAsync(Guid.NewGuid(), ProviderKind.Claude, [ProviderKind.Local], cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        pending.SetResult(new(true, true));
        Assert.Empty(settings.Writes);
        byte[] bad = [123, 33];
        settings.Set(ConversationTransferConsent.SettingsKey, bad);
        Assert.Throws<InvalidDataException>(() => new ConversationTransferConsent(settings, prompt));
        Assert.Same(bad, settings.Object(ConversationTransferConsent.SettingsKey));
    }
}
