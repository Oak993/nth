namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. NSString/URL path-component semantics with both Windows separators ('/' and '\'), under the recorded
/// path rules (ED-019, ED-036, ED-038, ED-045). Never System.IO.Path for a Swift path component: its
/// trailing-separator rules differ ("a/b/" has the last component "b" in Foundation and "" in Path.GetFileName).
/// The rest of the path identity rules (absoluteness, normalization, containment) are the first unit after
/// the foundation (IMPLEMENTING.md 4.3). Source: draft A (proto-A SwiftPath.cs).
/// </summary>
public static class SwiftPath
{
    private static bool IsSeparator(char c) => c is '/' or '\\';

    /// <summary><c>(path as NSString).lastPathComponent</c>: "a/b/" is "b", "/" is "/", "" is "".</summary>
    public static string LastPathComponent(string path)
    {
        if (path.Length == 0) return "";
        var end = path.Length;
        while (end > 0 && IsSeparator(path[end - 1])) end--;
        if (end == 0) return path[..1];
        var start = end;
        while (start > 0 && !IsSeparator(path[start - 1])) start--;
        return path[start..end];
    }
}
