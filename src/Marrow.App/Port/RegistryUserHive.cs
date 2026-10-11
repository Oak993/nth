using Marrow.Core.Support;
using Microsoft.Win32;

namespace Marrow.App.Port;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, unit U5, ED-NEW-inst-u5-1). The production
/// <see cref="IUserRegistry"/> over a hive root: <c>Registry.CurrentUser</c> in the app's and the setup's <c>Program.cs</c>
/// only, a private hive loaded from a temporary file (<c>RegLoadAppKey</c>) in the tests. It never creates a key, so it
/// cannot make an Apps-list entry appear. This file is also compiled into src/Marrow.Setup.
/// </summary>
public sealed class RegistryUserHive(RegistryKey root) : IUserRegistry
{
    private readonly RegistryKey root = root ?? throw new ArgumentNullException(nameof(root));

    public bool KeyExists(string subKey)
    {
        using var key = root.OpenSubKey(subKey);
        return key is not null;
    }

    public object? GetValue(string subKey, string name)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void SetString(string subKey, string name, string value)
    {
        using var key = Writable(subKey);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetDword(string subKey, string name, int value)
    {
        using var key = Writable(subKey);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string subKey, string name)
    {
        using var key = root.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private RegistryKey Writable(string subKey) =>
        root.OpenSubKey(subKey, writable: true) ?? throw new InvalidOperationException($"The key '{subKey}' does not exist; it is never created here.");
}
