using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port;

// OD-0135: the product is named Nth, so the Credential Manager services move from "Marrow" / "Marrow Dev" (provider keys,
// MCP values) and "Marrow Code" (the voice key) to "Nth" / "Nth Dev" and "Nth". A value still kept under the old service
// is read once, written under the new one, and the old item is left as it was (an older copy of the app may still read
// it). Every test runs on the in-memory Credential Manager; the real one is never touched.
[Trait("Decision", "OD-0135")]
public sealed class CredentialCarryOverTests
{
    private const string DeepSeekAccount = "DeepSeek API Key";
    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp(string name, bool development) =>
        AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = name }, development));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private void Plant(string service, string account, string value) =>
        Assert.Equal(0, backend.Write(new GenericCredential(SecretStore.TargetName(service, account), account,
            Encoding.UTF8.GetBytes(value), 1, 2, [])));

    private string? Stored(string service, string account) =>
        backend.Peek(SecretStore.TargetName(service, account)) is { } credential ? Encoding.UTF8.GetString(credential.Blob) : null;

    private bool Marked(string service, string account) =>
        backend.Peek(SecretStore.TargetName(service, account) + SecretStore.CarriedMarkerSuffix) is not null;

    [Theory]
    [InlineData(false, "Nth", "Marrow")]
    [InlineData(true, "Nth Dev", "Marrow Dev")]
    public void A_provider_key_kept_under_the_old_name_is_carried_over_once_and_the_old_item_stays(bool development, string service, string legacy)
    {
        using var app = UseApp(service, development);
        using var store = UseStore();
        Plant(legacy, DeepSeekAccount, "sk-old");

        Assert.Equal("sk-old", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));

        Assert.Equal("sk-old", Stored(service, DeepSeekAccount));
        Assert.Equal("sk-old", Stored(legacy, DeepSeekAccount));
        Assert.True(Marked(service, DeepSeekAccount));
        // The second read finds the new item and goes no further.
        backend.Operations.Clear();
        Assert.Equal("sk-old", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));
        Assert.Equal(["read " + SecretStore.TargetName(service, DeepSeekAccount)], backend.Operations);
    }

    [Fact]
    public void The_new_item_wins_over_the_old_one_and_nothing_is_written()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();
        Plant("Marrow", DeepSeekAccount, "sk-old");
        Plant("Nth", DeepSeekAccount, "sk-new");
        backend.Operations.Clear();

        Assert.Equal("sk-new", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));

        Assert.DoesNotContain(backend.Operations, operation => operation.StartsWith("write ", StringComparison.Ordinal));
        Assert.Equal("sk-old", Stored("Marrow", DeepSeekAccount));
    }

    [Fact]
    public void A_key_removed_after_the_carry_over_does_not_come_back_from_the_old_name()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();
        Plant("Marrow", DeepSeekAccount, "sk-old");
        Assert.Equal("sk-old", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));

        APIKeychain.SetKey("", ProviderKind.Deepseek);

        Assert.Equal("none", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));
        Assert.Null(Stored("Nth", DeepSeekAccount));
        Assert.Equal("sk-old", Stored("Marrow", DeepSeekAccount));
    }

    [Fact]
    public void A_key_set_or_removed_before_any_read_is_never_replaced_by_the_old_one()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();
        Plant("Marrow", DeepSeekAccount, "sk-old");

        APIKeychain.SetKey("sk-typed", ProviderKind.Deepseek);
        Assert.Equal("sk-typed", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));
        APIKeychain.SetKey("", ProviderKind.Deepseek);

        Assert.Equal("none", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));
        Assert.Equal("sk-old", Stored("Marrow", DeepSeekAccount));
    }

    [Fact]
    public void Nothing_under_either_name_reads_as_nothing_and_writes_nothing()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();

        Assert.Equal("none", APIKeychain.Key(ProviderKind.Deepseek, fallback: "none"));

        Assert.DoesNotContain(backend.Operations, operation => operation.StartsWith("write ", StringComparison.Ordinal));
        Assert.Empty(backend.TargetNames);
    }

    [Fact]
    public void An_explicit_service_is_read_as_given_with_no_carry_over()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();
        Plant("Marrow", DeepSeekAccount, "sk-old");

        Assert.Null(APIKeychain.Read(DeepSeekAccount, service: "Nth"));
        Assert.Null(Stored("Nth", DeepSeekAccount));
    }

    [Fact]
    public void An_MCP_value_is_carried_over_and_a_removed_one_stays_removed()
    {
        using var app = UseApp("Nth", false);
        using var store = UseStore();
        Plant("Marrow", "MCP github token", "ghp-old");

        Assert.Equal("ghp-old", MCPKeychain.Value("github", "token"));
        Assert.Equal("ghp-old", Stored("Nth", "MCP github token"));

        Assert.True(MCPKeychain.Delete("github", "token"));
        Assert.Null(MCPKeychain.Value("github", "token"));
        Assert.Equal("ghp-old", Stored("Marrow", "MCP github token"));
    }

    [Fact]
    public void The_voice_key_is_carried_over_from_the_old_data_identity()
    {
        var keys = new VoiceKeyStore(new SecretStore(backend));
        Plant("Marrow Code", VoiceKeyStore.Account, "sk-voice");

        Assert.Equal("sk-voice", keys.Read());
        Assert.Equal("sk-voice", Stored("Nth", VoiceKeyStore.Account));
        Assert.Equal("sk-voice", Stored("Marrow Code", VoiceKeyStore.Account));

        Assert.True(keys.Remove());
        Assert.Null(keys.Read());
        Assert.Equal("sk-voice", Stored("Marrow Code", VoiceKeyStore.Account));
    }

    [Fact]
    public void A_chunked_value_is_carried_over_whole()
    {
        var store = new SecretStore(backend);
        var big = new string('k', SecretStore.ChunkBytes * 2 + 17);
        Assert.Equal(SecretStoreStatus.Success, store.Add("Marrow", "MCP big token", Encoding.UTF8.GetBytes(big)));

        Assert.Equal(big, store.ReadCarryingOver("Nth", "Marrow", "MCP big token"));
        Assert.Equal(big, store.Read("Nth", "MCP big token"));
        Assert.Equal(big, store.Read("Marrow", "MCP big token"));
    }

    [Fact]
    public void A_refused_write_still_returns_the_old_value_and_tries_again_later()
    {
        var refusing = new RefusingCredentialBackend(backend);
        var store = new SecretStore(refusing);
        Plant("Marrow", DeepSeekAccount, "sk-old");
        refusing.RefuseWriteTargets.Add(SecretStore.TargetName("Nth", DeepSeekAccount));

        Assert.Equal("sk-old", store.ReadCarryingOver("Nth", "Marrow", DeepSeekAccount));
        Assert.False(Marked("Nth", DeepSeekAccount));

        refusing.RefuseWriteTargets.Clear();
        Assert.Equal("sk-old", store.ReadCarryingOver("Nth", "Marrow", DeepSeekAccount));
        Assert.Equal("sk-old", Stored("Nth", DeepSeekAccount));
        Assert.True(Marked("Nth", DeepSeekAccount));
    }
}
