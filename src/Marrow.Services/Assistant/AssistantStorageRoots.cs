// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, sections 12.5 and 15.3; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-1: the storage seam the assistant's files go through, with the default-path fallback of 16.2.
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.Services.Assistant;

/// <summary>
/// Where the assistant keeps its files (DESIGN.md 15.3, 12.5). Two roots, because they move differently:
/// <see cref="AssistantRoot"/> is the <c>Assistant</c> folder under the storage location (OD-0131, T43: the owner may
/// move it to another drive), and <see cref="AuditDirectory"/> always stays under %LOCALAPPDATA% (12.5, "logs stay in
/// LOCALAPPDATA per T43"), so moving the storage location never moves the audit log.
/// </summary>
public interface IAssistantStorageRoots
{
    /// <summary>The <c>Assistant</c> folder: assistants, memory, missions, feed, teach and worktrees (15.3).</summary>
    string AssistantRoot { get; }

    /// <summary>The audit log's folder, <c>%LOCALAPPDATA%\nth\assistant\audit</c> in production (12.5).</summary>
    string AuditDirectory { get; }
}

/// <summary>Two fixed folders: a test's temporary roots, or T43's binding once its storage location lands.</summary>
public sealed record FixedAssistantStorageRoots(string AssistantRoot, string AuditDirectory) : IAssistantStorageRoots;

/// <summary>
/// The 16.2 fallback while T43's storage seam is not in the tree (ED-NEW-dextra-as3-1): both roots under
/// <see cref="Storage.Locations"/>' root (%LOCALAPPDATA%\nth in production, a test's own folder in a test), read on
/// every access so a scoped override of <see cref="Storage.Locations"/> is always seen. The store is
/// <c>&lt;root&gt;\Assistant</c>; the audit log is <c>&lt;root&gt;\assistant\audit</c>, the path 12.5 names. Windows
/// paths compare without case, so today the audit folder sits inside the store's folder; T43's binding moves only
/// <see cref="AssistantRoot"/>, and its move must leave <c>audit</c> where it is.
/// </summary>
public sealed class DefaultPathAssistantStorageRoots : IAssistantStorageRoots
{
    public string AssistantRoot => Path.Combine(Storage.Locations.Current.Root, "Assistant");

    public string AuditDirectory => Path.Combine(Storage.Locations.Current.Root, "assistant", "audit");
}

/// <summary>
/// The one binding point (16.2: "switching is one binding when T43 lands"): the app calls
/// <c>AssistantStorage.Roots.SetProcessDefault(...)</c> with T43's roots; a test overrides it for its own flow.
/// </summary>
public static class AssistantStorage
{
    public static Ambient<IAssistantStorageRoots> Roots { get; } = new(new DefaultPathAssistantStorageRoots());
}
