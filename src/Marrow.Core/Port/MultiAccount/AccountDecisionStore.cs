// PORT-ONLY: OD-0114, OD-0116; ED-403. Absent preferences remain absent until the user chooses.
using System.Text.Json;
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.MultiAccount;

public sealed class AccountDecisionStore : IDecisionStore
{
    public const string SettingsKey = "multiAccountDecisions";
    private readonly ISettingsStore settings;
    private AccountDecisionDocument document = new();
    private readonly object gate = new();
    public event Action? Changed;
    public bool SaveBlocked { get; private init; }
    public static AccountDecisionStore ReadOnly(ISettingsStore settings) => new(settings, true);

    private AccountDecisionStore(ISettingsStore settings, bool readOnly)
    {
        this.settings = settings;
        SaveBlocked = readOnly;
    }

    public AccountDecisionStore(ISettingsStore settings)
    {
        this.settings = settings;
        if (settings.Object(SettingsKey) is not { } saved) return;
        try
        {
            if (saved is not byte[] bytes) throw new InvalidDataException("Unsupported account preferences.");
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Version", out var version)
                || !version.TryGetInt32(out var number) || number != 1
                || !json.RootElement.TryGetProperty("Settings", out _) || !json.RootElement.TryGetProperty("Threads", out _))
                throw new InvalidDataException("Unsupported account preferences.");
            document = JsonSerializer.Deserialize<AccountDecisionDocument>(bytes) ?? throw new InvalidDataException("Empty account preferences.");
            if (document.Settings is null || document.Threads is null) throw new InvalidDataException("Invalid account preferences.");
            foreach (var (kind, value) in document.Settings) Validate(kind, value);
            foreach (var (thread, choices) in document.Threads)
            {
                if (thread == Guid.Empty || choices is null) throw new InvalidDataException("Invalid thread preferences.");
                foreach (var (kind, value) in choices) Validate(kind, value);
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            throw new InvalidDataException("Account preferences could not be read; the original value was preserved.", error);
        }
    }

    public string? GetSetting(DecisionKind kind) { lock (gate) return document.Settings.GetValueOrDefault(kind); }
    public string? GetThread(Guid threadId, DecisionKind kind)
    {
        lock (gate) return document.Threads.GetValueOrDefault(threadId)?.GetValueOrDefault(kind);
    }

    public void SetSetting(DecisionKind kind, string? value)
    {
        Validate(kind, value);
        lock (gate)
        {
            var choices = new Dictionary<DecisionKind, string>(document.Settings);
            if (value is null) choices.Remove(kind); else choices[kind] = value;
            Save(document with { Settings = choices });
        }
        Changed?.Invoke();
    }

    public void SetThread(Guid threadId, DecisionKind kind, string? value)
    {
        if (threadId == Guid.Empty) throw new ArgumentException("A thread identity is required.", nameof(threadId));
        Validate(kind, value);
        lock (gate)
        {
            var threads = new Dictionary<Guid, Dictionary<DecisionKind, string>>(document.Threads);
            var choices = document.Threads.TryGetValue(threadId, out var old) ? new Dictionary<DecisionKind, string>(old) : [];
            if (value is null) choices.Remove(kind); else choices[kind] = value;
            if (choices.Count == 0) threads.Remove(threadId); else threads[threadId] = choices;
            Save(document with { Threads = threads });
        }
        Changed?.Invoke();
    }

    public void ClearThread(Guid threadId)
    {
        lock (gate)
        {
            var threads = new Dictionary<Guid, Dictionary<DecisionKind, string>>(document.Threads);
            threads.Remove(threadId);
            Save(document with { Threads = threads });
        }
        Changed?.Invoke();
    }

    private void Save(AccountDecisionDocument next)
    {
        if (SaveBlocked) throw new InvalidOperationException("Account preferences could not be read; the original value was preserved.");
        settings.Set(SettingsKey, JsonSerializer.SerializeToUtf8Bytes(next));
        document = next;
    }

    private static void Validate(DecisionKind kind, string? value)
    {
        if (!Enum.IsDefined(kind) || (value is not null && !DecisionValues.Allowed(kind).Contains(value, StringComparer.Ordinal)))
            throw new ArgumentException("Invalid account preference.", nameof(value));
    }
}
