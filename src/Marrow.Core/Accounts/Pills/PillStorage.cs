using System.Text.Json.Nodes;
using Marrow.Core.Port;

namespace Marrow.Core.Accounts.Pills;

/// <summary>What a load found. <see cref="Newer"/>: the stored text is from a newer app; <see cref="Problem"/>: it could not be read at all. Either blocks saving over it.</summary>
public sealed record PillLoad(IReadOnlyList<Pill> Pills, Guid? UseForNewThreads, int FoundVersion, int Skipped, bool Newer, string? Problem);

/// <summary>Pills in settings as one JSON text under one key, versioned so an older or newer app never loses them (MA-7).</summary>
public sealed class PillStorage(
    ISettingsStore settings,
    string key = PillStorage.DefaultKey,
    int currentVersion = 1,
    IReadOnlyDictionary<int, Func<JsonObject, JsonObject>>? migrations = null)
{
    public const string DefaultKey = "multiAccount.pills";

    public PillLoad Load() => throw new NotImplementedException();

    /// <summary>Writes the pills. Refused (false, stored text untouched) after a load that found newer or unreadable text, unless <paramref name="overwrite"/>.</summary>
    public bool Save(IReadOnlyList<Pill> pills, Guid? useForNewThreads, bool overwrite = false) => throw new NotImplementedException();

    internal object Unused() => (settings, key, currentVersion, migrations);
}
