namespace Marrow.Setup.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8, the uninstall flow; unit U6, ED-NEW-inst-u6-1). The current user's Windows Credential
/// Manager, as far as the uninstaller needs it: the generic credentials whose target name starts with a prefix, and deleting
/// one. Production: <c>Port/WindowsCredentialStore.cs</c> (CredEnumerateW, CredDeleteW). Tests use an in-memory fake; no test
/// reads or deletes a real credential.
/// </summary>
public interface ICredentialStore
{
    /// <summary>The target names of the generic credentials that start with <paramref name="prefix"/> (none when there are none).</summary>
    IReadOnlyList<string> FindGeneric(string prefix);

    /// <summary>Deletes one generic credential; false when it could not be deleted (a credential already gone counts as deleted).</summary>
    bool DeleteGeneric(string targetName);
}

/// <summary>
/// nth's own secrets (the provider keys and MCP values nth's Settings keep). SOURCE FACT (src/Marrow.Services/Port/SecretStore.cs,
/// ED-093 and ED-120; APIKeychain.cs and MCPKeychain.cs): every item nth writes is a generic credential named
/// <c>&lt;service&gt;:&lt;account&gt;:&lt;hash&gt;</c> (a chunk or a carried-over marker adds to that name), and the release build's
/// service is the product's name, <c>nth</c> (OD-0135). So <c>nth:</c> finds them all and nothing else: a development build's
/// service carries a suffix (<c>nth [tag]</c>), and the pre-rename <c>Marrow</c> items are never touched (the owner's separate
/// Marrow app shares that name).
/// </summary>
public static class NthSecrets
{
    public const string TargetPrefix = "nth:";

    /// <summary>Deletes every nth secret; returns how many went and how many stayed.</summary>
    public static (int Removed, int Kept) Remove(ICredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        int removed = 0, kept = 0;
        foreach (var target in store.FindGeneric(TargetPrefix))
        {
            if (!target.StartsWith(TargetPrefix, StringComparison.Ordinal)) continue; // the filter's own match is never trusted alone
            if (store.DeleteGeneric(target)) removed++;
            else kept++;
        }
        return (removed, kept);
    }
}
