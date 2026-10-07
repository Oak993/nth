// PORT-ONLY: OD-0114 / ED-404. Multi-account identity and defaults have no Swift twin.
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Accounts;

public sealed record HandshakePoint
{
    [JsonConstructor]
    public HandshakePoint(int? percentage = null)
    {
        if (percentage is < 80 or > 99) throw new ArgumentOutOfRangeException(nameof(percentage));
        Percentage = percentage;
    }

    public int? Percentage { get; }
    public static HandshakePoint Smart { get; } = new();
}

public sealed record AccountDefaults
{
    public string? Model { get; init; }
    public string? Effort { get; init; }
    public bool FastMode { get; init; }
    /// <summary>Null inherits the general point; an explicit Smart overrides it.</summary>
    public HandshakePoint? HandshakePoint { get; init; }
}

public sealed record Account
{
    public required Guid Id { get; init; }
    public required ProviderKind Service { get; init; }
    /// <summary>Reported plan label (e.g. Pro or Max); no credential or email is stored here.</summary>
    public string? Plan { get; init; }
    public string? CustomName { get; init; }
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; }
    public AccountDefaults Defaults { get; init; } = new();
    /// <summary>Migrated Account 1 retains the original CLI environment and secret target. Nothing is moved.</summary>
    public bool UsesLegacyCredentials { get; init; }
    public bool HasSignedIn { get; init; }
}

public static class AccountCapabilities
{
    // Copilot/Antigravity vaults lack an account selector. Command Code permits one personal identity (MA-0 §6).
    public static bool SupportsMultipleAccounts(ProviderKind service) => service is not
        (ProviderKind.Copilot or ProviderKind.Antigravity or ProviderKind.Commandcode);
}

/// <summary>Optional extension to the existing provider settings contract; no UI depends on it.</summary>
public interface AccountProviderSettings
{
    AccountStore Accounts { get; }
}

internal sealed class AccountServiceConverter : JsonConverter<ProviderKind>
{
    public override ProviderKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && SwiftEnum<ProviderKind>.FromRawValue(reader.GetString()) is { } service
            ? service : throw new JsonException("Unknown account service.");

    public override void Write(Utf8JsonWriter writer, ProviderKind value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);
}
