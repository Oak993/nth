// PORT-ONLY: Foundation's NSString and file URL path semantics have no .NET twin (System.IO.Path differs at roots, trailing separators, extensions and a bare backslash), and Windows needs one absoluteness predicate, one normalization and one comparison (R-PATH-1).
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. NSString and file URL path semantics with both Windows separators ('/' and '\'), under the recorded path
/// rules: ED-019, ED-026, ED-036, ED-038, ED-039 and ED-045 as ruled by ED-137 to ED-142. Never System.IO.Path for a
/// Swift path operation: its trailing-separator, root, extension and rooted-backslash rules differ.
/// A path has a root as written (kept verbatim, so an absolute input stays absolute, ED-142): a drive root "C:\",
/// a UNC root "\\server\share", a long root "\\?\C:\" or "\\?\UNC\server\share" or "\\?\", or a bare separator
/// (the NSString "/" analogue, never absolute, ED-036). Standardization is purely lexical: it never reads the file
/// system and never resolves a junction, symbolic link or /private, for existing and missing paths alike (ED-141).
/// Source: draft A (proto-A SwiftPath.cs) for LastPathComponent; the rest is F.PathIdentity.
/// </summary>
public static class SwiftPath
{
    private const string LongPrefix = @"\\?\";
    private const int CstrEqual = 2; // CSTR_EQUAL

    /// <summary>One component and the first separator of the run in front of it, as written.</summary>
    private readonly record struct Component(char? Lead, string Text);

    private static bool IsSeparator(char c) => c is '/' or '\\';

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>ED-036: /^[A-Za-z]:[\\/]/ at <paramref name="at"/>.</summary>
    private static bool IsDriveRooted(string p, int at) =>
        p.Length >= at + 3 && IsAsciiLetter(p[at]) && p[at + 1] == ':' && IsSeparator(p[at + 2]);

    private static bool HasLongPrefix(string p) => p.StartsWith(LongPrefix, StringComparison.Ordinal);

    /// <summary>ED-139: a leading \\?\UNC\ with "UNC" matched ASCII case-insensitively.</summary>
    private static bool HasLongUncPrefix(string p) =>
        HasLongPrefix(p) && p.Length >= 8 && p[4] is 'U' or 'u' && p[5] is 'N' or 'n' && p[6] is 'C' or 'c' && p[7] == '\\';

    /// <summary>
    /// ED-036: the length of "server[\\/]share" (/[^\\/]+[\\/][^\\/]+/, the share taken whole) starting at
    /// <paramref name="at"/>, or 0 when the text there does not have that form.
    /// </summary>
    private static int ServerShareLength(string p, int at)
    {
        var i = at;
        while (i < p.Length && !IsSeparator(p[i])) i++;
        if (i == at || i >= p.Length) return 0;
        var shareStart = i + 1;
        var j = shareStart;
        while (j < p.Length && !IsSeparator(p[j])) j++;
        return j == shareStart ? 0 : j - at;
    }

    private static bool IsUncRooted(string p) => p.Length >= 2 && p[0] == '\\' && p[1] == '\\' && ServerShareLength(p, 2) > 0;

    /// <summary>The length of the root as written (see the type note); 0 for a relative path.</summary>
    private static int RootLength(string p)
    {
        if (HasLongPrefix(p))
        {
            if (HasLongUncPrefix(p))
            {
                // ED-139 + ED-036: \\?\UNC\server\share is the same path as \\server\share, so its root is the whole
                // share: the upward walk (AppModel+Hydra.swift:332-340) ends there and ".." in standardization
                // (AppModel+Hydra.swift:323, :326; TouchedPaths.swift:23, :25) never climbs above it. ED-142: the
                // root keeps its \\?\UNC\ lead-in. Pinned by the long-UNC root rows in SwiftPathTests.
                var share = ServerShareLength(p, 8);
                if (share > 0) return 8 + share;
            }
            return IsDriveRooted(p, 4) ? 7 : 4;
        }
        if (IsDriveRooted(p, 0)) return 3;
        if (IsUncRooted(p)) return 2 + ServerShareLength(p, 2);
        return p.Length > 0 && IsSeparator(p[0]) ? 1 : 0;
    }

    /// <summary>The components after <paramref name="start"/>: empty components (doubled and trailing separators) fold away,
    /// as the NSString path methods fold them.</summary>
    private static List<Component> Components(string p, int start)
    {
        var list = new List<Component>();
        var i = start;
        while (i < p.Length)
        {
            char? lead = null;
            if (IsSeparator(p[i]))
            {
                lead = p[i];
                while (i < p.Length && IsSeparator(p[i])) i++;
            }
            if (i >= p.Length) break;
            var begin = i;
            while (i < p.Length && !IsSeparator(p[i])) i++;
            list.Add(new Component(lead, p[begin..i]));
        }
        return list;
    }

    /// <summary>The root, then each component behind its own separator (none directly after a root that ends in one);
    /// a component with no separator of its own takes <paramref name="join"/>.</summary>
    private static string Build(string root, List<Component> components, char join)
    {
        var text = new StringBuilder(root);
        var rootEndsInSeparator = root.Length == 0 || IsSeparator(root[^1]);
        for (var i = 0; i < components.Count; i++)
        {
            if (i > 0 || !rootEndsInSeparator) text.Append(components[i].Lead ?? join);
            text.Append(components[i].Text);
        }
        return text.ToString();
    }

    /// <summary>The separator a join inserts: the path's own last separator, else the Windows one (no decision fixes it;
    /// every comparison goes through <see cref="Normalize"/>).</summary>
    private static char JoinSeparator(string p)
    {
        for (var i = p.Length - 1; i >= 0; i--)
        {
            if (IsSeparator(p[i])) return p[i];
        }
        return '\\';
    }

    // ---- NSString forms ---------------------------------------------------------------------------------------------

    /// <summary><c>(path as NSString).lastPathComponent</c> and <c>URL.lastPathComponent</c>: "a/b/" is "b", "/" is "/",
    /// "" is "". Callers: Shell.swift:188, DiffParser.swift:22, AppModel.swift:519, AppModel+Hydra.swift:1094.</summary>
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

    /// <summary><c>(path as NSString).deletingLastPathComponent</c>: "/tmp/lock/" is "/tmp", "/tmp" is "/", "/" stays "/",
    /// "a" is "". A root is its own fixed point (ED-036: the walk at AppModel+Hydra.swift:332-340 ends at "C:\" or
    /// "\\server\share"), and the root keeps its spelling (ED-142). Callers: DiffParser.swift:24, Git.swift:467,
    /// AppModel+Hydra.swift:338.</summary>
    public static string DeletingLastPathComponent(string path)
    {
        var rootLength = RootLength(path);
        var components = Components(path, rootLength);
        if (components.Count == 0) return path[..rootLength];
        components.RemoveAt(components.Count - 1);
        return Build(path[..rootLength], components, JoinSeparator(path));
    }

    /// <summary><c>(path as NSString).appendingPathComponent(component)</c>: exactly one separator between; the component
    /// is never rooted, so a bare leading separator is joined onto the root (ED-036, ED-038, ED-045); "" plus a component
    /// is the component. Callers: TouchedPaths.swift:24, AppModel+HydraMerge.swift:140, :253, :496, :499, :540,
    /// ThreadRuntime.swift:3376.</summary>
    public static string AppendingPathComponent(string path, string component)
    {
        if (path.Length == 0) return component;
        var rootLength = RootLength(path);
        var components = Components(path, rootLength);
        var added = Components(component, 0);
        if (added.Count > 0) added[0] = added[0] with { Lead = null };
        components.AddRange(added);
        return Build(path[..rootLength], components, JoinSeparator(path));
    }

    /// <summary><c>(path as NSString).pathExtension</c>: the text after the last "." of the last component, case kept;
    /// "" when there is no "." or it is the first character. Callers: AttachmentPreview.swift:15, :89, :130, :150.</summary>
    public static string PathExtension(string path)
    {
        var last = LastPathComponent(path);
        var dot = last.LastIndexOf('.');
        return dot <= 0 ? "" : last[(dot + 1)..];
    }

    /// <summary><c>(path as NSString).expandingTildeInPath</c> under ED-036: a leading "~" that is the whole string or is
    /// followed by either separator becomes %USERPROFILE%; every other form (a "~name" user that does not exist) is left
    /// as it is. Callers: TouchedPaths.swift:22, AppModel+Hydra.swift:321, AppModel+HydraMerge.swift:191,
    /// NativeFileTools.swift:24.</summary>
    public static string ExpandingTildeInPath(string path)
    {
        if (path.Length == 0 || path[0] != '~') return path;
        if (path.Length > 1 && !IsSeparator(path[1])) return path;
        var home = Environment.GetEnvironmentVariable("USERPROFILE");
        return string.IsNullOrEmpty(home) ? path : home + path[1..];
    }

    /// <summary><c>(path as NSString).standardizingPath</c>, lexical (ED-141): the tilde expanded, "." and empty
    /// components removed, ".." resolved when the path has a root (".." at the root stays at the root) and kept in a
    /// relative path, no trailing separator except the root's own. Callers: TouchedPaths.swift:23, :25,
    /// AppModel+Hydra.swift:323, :326, :332, AppModel+HydraMerge.swift:191, :192, :202.</summary>
    public static string StandardizingPath(string path) => Standardize(ExpandingTildeInPath(path));

    private static string Standardize(string path)
    {
        var rootLength = RootLength(path);
        var kept = new List<Component>();
        foreach (var component in Components(path, rootLength))
        {
            if (component.Text == ".") continue;
            if (component.Text == ".." && rootLength > 0)
            {
                if (kept.Count > 0) kept.RemoveAt(kept.Count - 1);
                continue;
            }
            kept.Add(component);
        }
        return Build(path[..rootLength], kept, JoinSeparator(path));
    }

    // ---- file URL forms (each on the path string of a file URL, returning what Swift reads back) ----------------------

    /// <summary><c>URL(fileURLWithPath: path).pathExtension</c>: swift-foundation's String.pathExtension on the last
    /// component (ED-140, oracle item O-42). "" when the component ends in ".", when its last "." is its first character,
    /// when the stem is "." or "..", or when the extension holds an invalid extension scalar; otherwise the text after the
    /// last ".", case kept. Callers: Storage.swift:99, :184, Wallpaper.swift:127, DownloadsPopover.swift:70,
    /// AppUpdater.swift:317, TokenLedger.swift:421.</summary>
    public static string FileURLPathExtension(string path)
    {
        // UNVERIFIED O-42: macOS URL.pathExtension equals swift-foundation's String.pathExtension.
        var last = LastPathComponent(path);
        if (last.Length == 0 || last[^1] == '.') return "";
        var dot = last.LastIndexOf('.');
        if (dot <= 0) return "";
        var stem = last[..dot];
        if (stem is "." or "..") return "";
        var extension = last[(dot + 1)..];
        foreach (var scalar in extension.EnumerateRunes())
        {
            if (IsInvalidExtensionScalar(scalar.Value)) return "";
        }
        return extension;
    }

    /// <summary>ED-140: invalidExtensionScalars: U+0020, "/", U+061C, U+200E, U+200F, U+202A to U+202E, U+2066 to U+2069.</summary>
    private static bool IsInvalidExtensionScalar(int value) =>
        value is 0x20 or 0x2F or 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;

    /// <summary><c>URL(fileURLWithPath: path).deletingLastPathComponent().path</c>: the parent without a trailing
    /// separator; at a root, the root itself (ED-140, oracle item O-43). Callers: NativeFileTools.swift:86,
    /// MCPProviderConfig.swift:241, ProviderRegistry.swift:262, Git.swift:71, ThreadRuntime.swift:1971.</summary>
    public static string FileURLDeletingLastPathComponentPath(string path) =>
        DeletingLastPathComponent(path); // UNVERIFIED O-43: URL.deletingLastPathComponent() at the root returns the root itself.

    /// <summary><c>URL(fileURLWithPath: path).appendingPathComponent(component).path</c>: a multi-component string is
    /// appended whole with exactly one separator between, no trailing separator, ".." kept for the standardization that
    /// follows. Callers: Shell.swift:410, NativeFileTools.swift:25, Storage.swift:100.</summary>
    public static string FileURLAppendingPathComponentPath(string path, string component) => AppendingPathComponent(path, component);

    /// <summary><c>URL(fileURLWithPath: path, relativeTo: directory).standardizedFileURL.path</c>, lexical (ED-141): a
    /// path that is not absolute (IsAbsolute, so a bare leading separator too) is joined onto the directory (the current
    /// directory when none is given, as URL(fileURLWithPath:) does); then "." and ".." segments are removed (".." above
    /// the root stays at the root) and the trailing separator dropped except at the root. No tilde expansion. Callers:
    /// TouchedPaths.swift:8-9, TurnDiff.swift:8, AppModel.swift:517, NativeFileTools.swift:22, :26,
    /// ThreadRuntime.swift:3518.</summary>
    public static string StandardizedFileURLPath(string path, string? relativeTo)
    {
        var absolute = IsAbsolute(path) ? path : AppendingPathComponent(relativeTo ?? Environment.CurrentDirectory, path);
        return Standardize(absolute);
    }

    // ---- the one predicate, normalization and comparison ------------------------------------------------------------

    /// <summary>
    /// ED-036/ED-045 as ruled by ED-138: absolute iff /^[A-Za-z]:[\\/]/, /^\\\\[^\\/]+[\\/][^\\/]+/, or a \\?\ prefix.
    /// A bare leading separator is not absolute. It replaces only <c>hasPrefix("/")</c> (TouchedPaths.swift:24,
    /// TurnDiff.swift:34, :41, AppModel+Hydra.swift:329, AppModel+HydraMerge.swift:498, :537, NativeFileTools.swift:23,
    /// ThreadRuntime.swift:3376); the "~" and "://" clauses stay at those call sites as the Swift writes them.
    /// ED-173 adds a fourth form, a UNC path spelled with forward slashes, /^\/\/[^\\/]+[\\/][^\\/]+/: TurnDiff.swift:34
    /// and :41 test <c>hasPrefix("/")</c> on reportedPath's output (TurnDiff.swift:8-9), which TouchedPaths.normalize
    /// forward-slashes (ED-019 (3)), so a UNC file outside the root arrives as "//srv/share/..."; a lone "//" or
    /// "//server" without a share stays false, as the backslash form does.
    /// </summary>
    public static bool IsAbsolute(string path) =>
        IsDriveRooted(path, 0) || HasLongPrefix(path) || IsUncRooted(path) || IsForwardSlashUncRooted(path);

    /// <summary>ED-173: /^\/\/[^\\/]+[\\/][^\\/]+/.</summary>
    private static bool IsForwardSlashUncRooted(string p) =>
        p.Length >= 2 && p[0] == '/' && p[1] == '/' && ServerShareLength(p, 2) > 0;

    /// <summary>
    /// ED-045 as amended by ED-139: a leading \\?\UNC\ becomes //, any other \\?\ prefix is stripped, backslashes become
    /// forward slashes and a drive letter is upper-cased. Nothing else changes: the rest of the case, trailing separators
    /// and dot segments are left to the comparison and to standardization.
    /// </summary>
    public static string Normalize(string path)
    {
        var text = HasLongUncPrefix(path) ? "//" + path[8..] : HasLongPrefix(path) ? path[4..] : path;
        var chars = text.Replace('\\', '/').ToCharArray();
        if (chars.Length >= 2 && chars[1] == ':' && chars[0] is >= 'a' and <= 'z') chars[0] = (char)(chars[0] - ('a' - 'A'));
        return new string(chars);
    }

    /// <summary>
    /// ProviderSession.swift:194-196 and TouchedPaths.swift:26-28: the prefix is the root ending in one separator, and
    /// the result is the rest of the path, or nil when the path is not under the prefix. ED-019/026/039 as ruled by
    /// ED-137: the prefix test runs on the normalized forms with kernel32 CompareStringOrdinal (bIgnoreCase) over the
    /// prefix's UTF-16 length, and the tail returned is the caller's own text (ED-019(4)). An empty tail is "", not nil:
    /// TouchedPaths.swift:28 applies nilIfEmpty itself.
    /// </summary>
    public static string? TailUnder(string path, string root)
    {
        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(root);
        var prefix = normalizedRoot.EndsWith('/') ? normalizedRoot : normalizedRoot + "/";
        if (normalizedPath.Length < prefix.Length || !EqualIgnoringCase(normalizedPath, prefix, prefix.Length)) return null;
        // Normalize changes the length only at the front (the \\?\ forms), so the tail maps back by the difference.
        return path[(path.Length - normalizedPath.Length + prefix.Length)..];
    }

    /// <summary>
    /// <c>(p as NSString).standardizingPath == standard</c> (AppModel+Hydra.swift:326): both sides standardized
    /// (lexically, ED-141) and normalized, then compared with kernel32 CompareStringOrdinal (bIgnoreCase) (ED-137).
    /// </summary>
    public static bool SamePath(string a, string b)
    {
        var x = Normalize(StandardizingPath(a));
        var y = Normalize(StandardizingPath(b));
        return x.Length == y.Length && (x.Length == 0 || EqualIgnoringCase(x, y, x.Length));
    }

    /// <summary>ED-137: the leading <paramref name="length"/> UTF-16 units of both strings compare equal under the Win32
    /// ordinal case-insensitive table, which equals NTFS on every BMP case pair. Never OrdinalIgnoreCase (wider than
    /// NTFS), a lowercase fold or the current culture.</summary>
    private static bool EqualIgnoringCase(string a, string b, int length)
    {
        // PORT-ONLY test-host portability: kernel32 exists only on Windows (see LoginEnvironment.SameEntry).
        if (!OperatingSystem.IsWindows()) return MemoryExtensions.Equals(a.AsSpan(0, length), b.AsSpan(0, length), StringComparison.OrdinalIgnoreCase);
        var result = CompareStringOrdinal(a, length, b, length, bIgnoreCase: true);
        if (result == 0) throw new InvalidOperationException($"CompareStringOrdinal failed with error {Marshal.GetLastPInvokeError()}.");
        return result == CstrEqual;
    }

    // Reads only cchCount units of each string, so a prefix is compared without a copy.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CompareStringOrdinal(string lpString1, int cchCount1, string lpString2, int cchCount2,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreCase);
}
