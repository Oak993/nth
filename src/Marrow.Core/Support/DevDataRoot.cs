// PORT-ONLY (ED-211): none of this file has a B3 Swift counterpart. It is a development affordance for a manual
// Debug launch, so a developer can run the packaged app against a throwaway data folder instead of the real
// %LOCALAPPDATA%\Marrow Code / %USERPROFILE%\.marrow-dev / Windows Credential Manager (STOP Boot #4,
// tests/Marrow.App.Tests/AppDelegateThirdTests.cs:305-321). It exists only under #if DEBUG; a Release build never
// compiles the redirect branch, so a Release binary is byte-for-byte the code that existed before this file.
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Core.Support;

/// <summary>
/// The Debug-only MARROW_DEV_DATA_ROOT redirect (ED-211). Every production site that used to resolve
/// <c>Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)</c> directly now calls
/// <see cref="LocalApplicationData"/> instead; a Release build, or a Debug build with the variable unset, empty, or
/// naming a directory that does not exist, gets the exact same value as before this redirect existed.
/// </summary>
public static class DevDataRoot
{
    /// <summary>The one variable this redirect reads. Never read outside #if DEBUG.</summary>
    public const string VariableName = "MARROW_DEV_DATA_ROOT";

    /// <summary>
    /// The effective %LOCALAPPDATA% for this process. In a Debug build, when <see cref="VariableName"/> names a
    /// directory that already exists, that directory; otherwise (Release, or Debug with the variable unset, empty,
    /// or naming a directory that is not there) the real special folder, unchanged.
    /// </summary>
    public static string LocalApplicationData()
    {
#if DEBUG
        if (ActiveRoot() is { } root) return root;
#endif
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>Whether the redirect is active for this process right now (Debug build, variable set to an existing directory).</summary>
    public static bool IsRedirected
    {
        get
        {
#if DEBUG
            return ActiveRoot() is not null;
#else
            return false;
#endif
        }
    }

#if DEBUG
    private static string? ActiveRoot()
    {
        var value = Environment.GetEnvironmentVariable(VariableName);
        return !string.IsNullOrEmpty(value) && Directory.Exists(value) ? value : null;
    }
#endif

    /// <summary>
    /// The 8 lowercase hex characters of SHA-256 over the redirect's own directory path (ordinal, case as given),
    /// stable for one dev root and distinct across different ones. Only meaningful while <see cref="IsRedirected"/>
    /// is true; throws otherwise, since there is then no active root to identify.
    /// </summary>
    private static string Tag()
    {
#if DEBUG
        var root = ActiveRoot() ?? throw new InvalidOperationException("DevDataRoot is not redirected.");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(root));
        return Convert.ToHexStringLower(digest)[..8];
#else
        throw new InvalidOperationException("DevDataRoot is not redirected.");
#endif
    }

    /// <summary>
    /// A Win32 kernel-object name (a single-instance mutex, a reopen event) distinct per dev root, so two dev
    /// launches on different roots never contend with each other or signal each other's reopen. The tag is hex, so
    /// it never needs backslash escaping in the object name. Unchanged (identity) when not redirected.
    /// </summary>
    public static string KernelObjectName(string baseName) => IsRedirected ? baseName + "." + Tag() : baseName;

    /// <summary>
    /// A Windows Credential Manager service name distinct per dev root, so two dev launches on different roots never
    /// read or overwrite each other's stored provider keys, and neither touches the real "Marrow" / "Marrow Dev"
    /// entries. Unchanged (identity) when not redirected.
    /// </summary>
    public static string CredentialService(string baseService) => IsRedirected ? baseService + " [" + Tag() + "]" : baseService;
}
