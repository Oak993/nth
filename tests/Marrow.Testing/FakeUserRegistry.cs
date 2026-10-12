using Marrow.Core.Support;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8, unit U5, ED-NEW-inst-u5-1). An in-memory <see cref="IUserRegistry"/>: keys hold
/// strings and DWORDs, like <c>RegistryUserHive</c> returns them. Nothing here reaches the real registry. It counts writes
/// and can stand in for Velopack writing its uninstall key (<see cref="WriteVelopackUninstallKey"/>). This file is also
/// compiled into tests/Marrow.Setup.Tests against the setup's linked copy of the contract.
/// </summary>
public sealed class FakeUserRegistry : IUserRegistry
{
    private readonly Dictionary<string, Dictionary<string, object>> keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Values set or deleted through <see cref="IUserRegistry"/> (seeding does not count).</summary>
    public int Writes { get; private set; }

    /// <summary>Makes a key as an outside writer would (not counted).</summary>
    public void SeedKey(string subKey)
    {
        if (!keys.ContainsKey(subKey)) keys[subKey] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary><see cref="IUserRegistry.CreateKey"/>: counts a write only when the key was missing.</summary>
    public void CreateKey(string subKey)
    {
        if (keys.ContainsKey(subKey)) return;
        SeedKey(subKey);
        Writes++;
    }

    /// <summary>Sets a value as an outside writer would (not counted); creates the key. A value is a string, an int or bytes (a <c>REG_BINARY</c>).</summary>
    public void Seed(string subKey, string name, object value)
    {
        if (value is not (string or int or byte[])) throw new ArgumentException("A value is a string, an int or bytes.", nameof(value));
        SeedKey(subKey);
        keys[subKey][name] = value;
    }

    /// <summary>How many values a key holds, for a check that nothing was left behind; null when the key does not exist.</summary>
    public int? ValueCount(string subKey) => keys.TryGetValue(subKey, out var values) ? values.Count : null;

    /// <summary>
    /// What Velopack 1.2.161 writes for NthApp (registry.rs:9-57): Update.exe's uninstall strings, the size of
    /// <c>current\</c> only, the root as InstallLocation, NoModify and NoRepair.
    /// </summary>
    public void WriteVelopackUninstallKey(string root, int currentSizeKb = 300_000)
    {
        var key = UninstallEntry.KeyPath;
        var update = Path.Combine(root, "Update.exe");
        Seed(key, "DisplayIcon", Path.Combine(root, "current", "nth.exe"));
        Seed(key, "DisplayName", "nth");
        Seed(key, "DisplayVersion", "1.0.0");
        Seed(key, "InstallDate", "20261011");
        Seed(key, "InstallLocation", root);
        Seed(key, "Publisher", "nth");
        Seed(key, "QuietUninstallString", "\"" + update + "\" --uninstall --silent");
        Seed(key, "UninstallString", "\"" + update + "\" --uninstall");
        Seed(key, "EstimatedSize", currentSizeKb);
        Seed(key, "NoModify", 1);
        Seed(key, "NoRepair", 1);
        Seed(key, "Language", 0x0409);
    }

    /// <summary>The values of a key, or null when it does not exist.</summary>
    public IReadOnlyDictionary<string, object>? Values(string subKey) => keys.TryGetValue(subKey, out var values) ? new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase) : null;

    /// <summary>Deletes a key as an outside writer would (Velopack's uninstaller removing its entry; unit U6), not counted.</summary>
    public void RemoveKey(string subKey) => keys.Remove(subKey);

    public bool KeyExists(string subKey) => keys.ContainsKey(subKey);

    public object? GetValue(string subKey, string name) => keys.TryGetValue(subKey, out var values) && values.TryGetValue(name, out var value) ? value : null;

    public void SetString(string subKey, string name, string value) => Set(subKey, name, value);

    public void SetDword(string subKey, string name, int value) => Set(subKey, name, value);

    public void DeleteValue(string subKey, string name)
    {
        if (keys.TryGetValue(subKey, out var values) && values.Remove(name)) Writes++;
    }

    private void Set(string subKey, string name, object value)
    {
        if (!keys.TryGetValue(subKey, out var values)) throw new InvalidOperationException($"The key '{subKey}' does not exist; it is never created here.");
        values[name] = value;
        Writes++;
    }
}
