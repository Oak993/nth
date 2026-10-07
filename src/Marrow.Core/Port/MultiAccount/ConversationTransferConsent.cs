// PORT-ONLY: MA-3b-E1 / OD-0120. Explicit, default-off consent for sending a conversation to different services.
using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount;

public sealed record ConversationTransferQuestion(Guid Thread, ProviderKind Source, IReadOnlyList<ProviderKind> Destinations)
{
    public bool DontAskAgain => false;
    public string CheckboxLabel => "Don't ask again";
}

public sealed record ConversationTransferAnswer(bool Accepted, bool DontAskAgain = false);

public interface IConversationTransferPrompt
{
    Task<ConversationTransferAnswer> ConfirmAsync(ConversationTransferQuestion question, CancellationToken ct);
}

public sealed class ConversationTransferConsent
{
    public const string SettingsKey = "multiAccountConversationTransferConsent";
    private readonly ISettingsStore settings;
    private readonly IConversationTransferPrompt prompt;
    private readonly object gate = new();
    private HashSet<string> approved = new(StringComparer.Ordinal);

    public ConversationTransferConsent(ISettingsStore settings, IConversationTransferPrompt prompt)
    {
        this.settings = settings; this.prompt = prompt;
        if (settings.Object(SettingsKey) is not { } saved) return;
        try
        {
            if (saved is not byte[] bytes) throw new InvalidDataException("Unsupported conversation transfer preferences.");
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version)
                || !version.TryGetInt32(out var value) || value != 1 || !root.TryGetProperty("pairs", out var pairs)
                || pairs.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Unsupported conversation transfer preferences.");
            var valid = SwiftEnum<ProviderKind>.AllCases.SelectMany(source => SwiftEnum<ProviderKind>.AllCases
                .Where(destination => destination != source).Select(destination => Pair(source, destination))).ToHashSet(StringComparer.Ordinal);
            foreach (var pair in pairs.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.String || pair.GetString() is not { } key || !valid.Contains(key) || !approved.Add(key))
                    throw new InvalidDataException("Invalid conversation transfer preferences.");
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new InvalidDataException("Conversation transfer preferences could not be read; the original value was preserved.", error); }
    }

    public async Task<bool> AuthorizeAsync(Guid thread, ProviderKind source, IReadOnlyList<ProviderKind> destinations, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(source) || destinations.Any(destination => !Enum.IsDefined(destination)))
            throw new ArgumentException("A known service is required.", nameof(destinations));
        ProviderKind[] pending;
        lock (gate) pending = destinations.Where(destination => destination != source && !approved.Contains(Pair(source, destination))).Distinct().ToArray();
        if (pending.Length == 0) return true;
        var answer = await prompt.ConfirmAsync(new(thread, source, Array.AsReadOnly(pending)), ct).WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (!answer.Accepted) return false;
        if (answer.DontAskAgain)
        {
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                var next = new HashSet<string>(approved, StringComparer.Ordinal);
                foreach (var destination in pending) next.Add(Pair(source, destination));
                Save(next);
            }
        }
        return true;
    }

    public void AskAgain(ProviderKind source, ProviderKind destination)
    {
        lock (gate)
        {
            var next = new HashSet<string>(approved, StringComparer.Ordinal);
            if (!next.Remove(Pair(source, destination))) return;
            Save(next);
        }
    }

    private static string Pair(ProviderKind source, ProviderKind destination) => source.Id + ">" + destination.Id;

    private void Save(HashSet<string> next)
    {
        // This setting contains service ids only: no accounts, credentials or conversation text.
        settings.Set(SettingsKey, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, pairs = next.Order(StringComparer.Ordinal).ToArray() }));
        approved = next;
    }
}
