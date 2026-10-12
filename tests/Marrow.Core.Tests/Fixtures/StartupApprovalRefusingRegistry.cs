using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

// C4c I13: only nth's StartupApproved delete is refused; all other writes use the in-memory hive.
public sealed class StartupApprovalRefusingRegistry(IUserRegistry inner) : IUserRegistry
{
    public bool RefuseApprovalDeletion { get; set; } = true;
    public List<(string Key, string Name)> DeletionAttempts { get; } = [];

    public bool KeyExists(string subKey) => inner.KeyExists(subKey);
    public void CreateKey(string subKey) => inner.CreateKey(subKey);
    public object? GetValue(string subKey, string name) => inner.GetValue(subKey, name);
    public void SetString(string subKey, string name, string value) => inner.SetString(subKey, name, value);
    public void SetDword(string subKey, string name, int value) => inner.SetDword(subKey, name, value);

    public void DeleteValue(string subKey, string name)
    {
        DeletionAttempts.Add((subKey, name));
        if (RefuseApprovalDeletion
            && string.Equals(subKey, UninstallEntry.StartupApprovedRunKeyPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(name, UninstallEntry.SignInValueName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("StartupApproved delete refused") { HResult = unchecked((int)0x80070005) };
        inner.DeleteValue(subKey, name);
    }
}
