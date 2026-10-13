using System.Reflection;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-open-14")]
public sealed class DextraUnavailableReasonTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(ProviderKind.Codex, "Cannot turn off all built-in tools for answers.")]
    [InlineData(ProviderKind.Antigravity, "Cannot turn off all built-in tools for answers.")]
    [InlineData(ProviderKind.Copilot, "Cannot block every tool before it runs.")]
    [InlineData(ProviderKind.Cursor, "Cannot turn off its built-in tools for answers.")]
    [InlineData(ProviderKind.Opencode, "Cannot turn off its built-in tools for answers.")]
    [InlineData(ProviderKind.OpencodeGo, "Cannot turn off its built-in tools for answers.")]
    [InlineData(ProviderKind.Grok, "Cannot turn off its built-in tools for answers.")]
    [InlineData(ProviderKind.Devin, "Cannot turn off its built-in tools for answers.")]
    public void Each_unavailable_provider_shows_its_exact_plain_reason_in_the_real_picker(ProviderKind provider, string reason)
    {
        headless.OnUiThread(() =>
        {
            var settings = new DextraSettings(new InMemorySettingsStore());
            var rows = new AssistantRows(settings, new BrainChoices(() => [provider], _ => [], _ => []));
            var item = Assert.Single(rows.WithMenu.Items, i => i.Id == provider.ToString());
            Assert.Equal(provider.DisplayName + " — unavailable: " + reason, item.Label.Text);
            Assert.False(item.IsEnabled);
        });
    }
}
