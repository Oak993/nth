// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// Where a path in a command points, and what reading, writing or removing it means: inside the mission worktree,
// inside the access folders, outside them, in a credential store, in nth's own folders, or in the registry. A path
// that cannot be placed (a variable with no known value, a POSIX path with no Windows meaning, an 8.3 name, a
// drive-relative path) is treated as outside everything.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public static partial class ActionClassifier
{
    internal enum ProviderKind { None, Registry, Environment, Variable, Function, Alias, Certificate, Other }

    internal static partial class Paths
    {
        private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".bash", ".zsh", ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".mts",
            ".cts", ".rb", ".pl", ".php", ".vbs", ".vbe", ".wsf", ".jse", ".lua", ".r",
        };

        private static readonly HashSet<string> CredentialFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".password-store", "1Password", "Bitwarden", "KeePass", "KeePassXC",
        };

        private static readonly HashSet<string> CredentialFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git-credentials", ".netrc", "_netrc", ".pypirc", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "logins.json",
            "key3.db", "key4.db", "Login Data", "Cookies", "cookies.sqlite", "Local State", "Web Data",
        };

        private static readonly HashSet<string> CredentialExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".kdbx", ".kdb", ".pfx", ".p12", ".keychain", ".ppk",
        };

        private static readonly string[] CredentialPathParts =
        [
            @"\Microsoft\Credentials", @"\Microsoft\Protect", @"\Microsoft\Vault", @"\.docker\config.json",
        ];

        [GeneratedRegex(@"\A/(?:mnt/)?([A-Za-z])(?=/|\z)")]
        private static partial Regex PosixDrive();

        [GeneratedRegex(@"(?:\A|[\\/])[^\\/]*~\d")]
        private static partial Regex ShortName();

        [GeneratedRegex(@"\$\{env:([A-Za-z_][\w()]*)\}|\$env:([A-Za-z_]\w*)|%([A-Za-z_][\w()]*)%|\$\{([A-Za-z_]\w*)\}|\$([A-Za-z_]\w*)", RegexOptions.IgnoreCase)]
        private static partial Regex Variable();

        [GeneratedRegex(@"\A(?<p>[A-Za-z][A-Za-z0-9]+)::?", RegexOptions.IgnoreCase)]
        private static partial Regex ProviderPrefix();

        public static string Extension(string raw)
        {
            var name = LastSegment(raw);
            var dot = name.LastIndexOf('.');
            return dot <= 0 ? "" : name[dot..].ToLowerInvariant();
        }

        public static bool IsScriptExtension(string extension) => ScriptExtensions.Contains(extension);

        public static bool HasSeparator(string raw) => raw.Contains('/', StringComparison.Ordinal) || raw.Contains('\\', StringComparison.Ordinal);

        private static string LastSegment(string raw)
        {
            var cut = raw.LastIndexOfAny(['/', '\\']);
            return cut >= 0 ? raw[(cut + 1)..] : raw;
        }

        /// <summary>A program's name as the tables hold it: the last segment, lower case, with .exe or .com taken off.</summary>
        public static string CommandName(string raw)
        {
            var name = LastSegment(raw).ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal) || name.EndsWith(".com", StringComparison.Ordinal)) name = name[..^4];
            return name;
        }

        public static ProviderKind Provider(string text)
        {
            var t = text;
            if (t.StartsWith(@"Microsoft.PowerShell.Core\", StringComparison.OrdinalIgnoreCase)) t = t[26..];
            if (t.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase)) return ProviderKind.Registry;
            var match = ProviderPrefix().Match(t);
            if (!match.Success) return ProviderKind.None;
            var p = match.Groups["p"].Value.ToLowerInvariant();
            return p switch
            {
                "hklm" or "hkcu" or "hkcr" or "hku" or "hkcc" or "registry" => ProviderKind.Registry,
                "env" => ProviderKind.Environment,
                "variable" => ProviderKind.Variable,
                "function" => ProviderKind.Function,
                "alias" => ProviderKind.Alias,
                "cert" or "wsman" => ProviderKind.Certificate,
                "filesystem" => ProviderKind.None,
                _ => ProviderKind.Other,
            };
        }

        private static string? Env(ClassifierContext context, string name)
        {
            foreach (var (key, value) in context.Environment)
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
            if (string.Equals(name, "HOME", StringComparison.OrdinalIgnoreCase)) return Env(context, "USERPROFILE");
            return null;
        }

        private static string? Expand(string text, string? cwd, ClassifierContext context)
        {
            var failed = false;
            var expanded = Variable().Replace(text, m =>
            {
                var name = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : m.Groups[3].Success ? m.Groups[3].Value
                    : m.Groups[4].Success ? m.Groups[4].Value
                    : m.Groups[5].Value;
                if (string.Equals(name, "PWD", StringComparison.OrdinalIgnoreCase) && cwd is not null) return cwd;
                var value = Env(context, name);
                if (value is null) failed = true;
                return value ?? "";
            });
            if (failed) return null;
            // Anything still only known when it runs: a substitution, a block, a delayed variable, a splat.
            if (expanded.IndexOfAny(['$', '%', '`', '(', ')', '{', '}', '@', '!']) >= 0) return null;
            return expanded;
        }

        /// <summary>The full path a word names, or null when it cannot be placed.</summary>
        public static string? Full(CommandWord word, string? cwd, ClassifierContext context)
        {
            var text = word.Text;
            if (text.Length == 0) return null;
            if (word.Dynamic)
            {
                // Only a known variable can be filled in; a word that is dynamic for any other reason (xargs input,
                // find's {}, a substitution) cannot be placed.
                if (!Variable().IsMatch(text)) return null;
                text = Expand(text, cwd, context) ?? "";
                if (text.Length == 0) return null;
            }
            if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith(@"~\", StringComparison.Ordinal))
            {
                var home = Env(context, "USERPROFILE");
                if (home is null) return null;
                text = home + text[1..];
            }
            if (text.StartsWith(@"Microsoft.PowerShell.Core\FileSystem::", StringComparison.OrdinalIgnoreCase)) text = text[38..];
            else if (text.StartsWith("FileSystem::", StringComparison.OrdinalIgnoreCase)) text = text[12..];
            if (Provider(text) != ProviderKind.None) return null;
            if (text.StartsWith(@"\\?\", StringComparison.Ordinal)) text = text[4..];
            else if (text.StartsWith(@"\\.\", StringComparison.Ordinal) || text.StartsWith("//./", StringComparison.Ordinal)) return null;
            var drive = PosixDrive().Match(text);
            if (drive.Success) text = drive.Groups[1].Value.ToUpperInvariant() + ":" + (text.Length > drive.Length ? text[drive.Length..] : @"\");
            else if (text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal)) return null; // a POSIX root path
            if (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' && (text.Length == 2 || text[2] is not ('\\' or '/')))
                return null; // drive-relative: C:foo
            var wildcard = text.IndexOfAny(['*', '?', '[']);
            if (wildcard >= 0)
            {
                var cut = text.LastIndexOfAny(['/', '\\'], wildcard);
                text = (cut >= 0 ? text[..(cut + 1)] : "") + "wildcard-match";
            }
            if (!Path.IsPathFullyQualified(text))
            {
                if (cwd is null || !Path.IsPathFullyQualified(cwd)) return null;
                if (text.StartsWith('\\') || text.StartsWith('/')) return null; // root-relative: \foo
                try { text = Path.GetFullPath(text, cwd); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
            }
            if (ShortName().IsMatch(text)) return null;
            return PathRules.Normalize(text);
        }

        public static bool IsAgentWritable(string full, ClassifierContext context) =>
            WritableRoots(context).Any(root => PathRules.IsInside(full, root, strict: false));

        private static IEnumerable<string> WritableRoots(ClassifierContext context)
        {
            if (context.MissionWorktree is { } worktree) yield return worktree;
            else foreach (var folder in context.AccessFolders) yield return folder;
            if (context.AssistantFolder is { } own) yield return own;
        }

        private static IEnumerable<string> ReadableRoots(ClassifierContext context)
        {
            if (context.MissionWorktree is { } worktree) yield return worktree;
            foreach (var folder in context.AccessFolders) yield return folder;
            if (context.AssistantFolder is { } own) yield return own;
        }

        public static bool IsCredential(string full)
        {
            var segments = full.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(CredentialFolders.Contains)) return true;
            if (segments.Length > 0 && CredentialFiles.Contains(segments[^1])) return true;
            if (CredentialExtensions.Contains(Extension(full))) return true;
            return CredentialPathParts.Any(part => full.Contains(part, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsProtected(string full, ClassifierContext context) =>
            context.ProtectedRoots.Any(root => PathRules.IsInside(full, root, strict: false));

        private static bool SensitiveHive(string text) =>
            text.Contains(@"\SAM", StringComparison.OrdinalIgnoreCase) || text.Contains(@"\SECURITY", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(":SAM", StringComparison.OrdinalIgnoreCase) || text.EndsWith(":SECURITY", StringComparison.OrdinalIgnoreCase);

        /// <summary>Reading the file or folder a word names.</summary>
        public static Classification Read(CommandWord word, string? cwd, ClassifierContext context)
        {
            switch (Provider(word.Text))
            {
                case ProviderKind.Registry:
                    return SensitiveHive(word.Text)
                        ? new Classification(ActionClass.HardDeny, "reads the registry's account secrets") { Target = word.Text }
                        : new Classification(ActionClass.ReadLocal, "reads the registry") { Target = word.Text };
                case ProviderKind.Environment or ProviderKind.Variable or ProviderKind.Function or ProviderKind.Alias or ProviderKind.Certificate:
                    return new Classification(ActionClass.Work, "reads a PowerShell drive") { Target = word.Text };
            }
            var full = Full(word, cwd, context);
            if (full is null)
                return new Classification(ActionClass.ReadLocal, $"reads something the classifier cannot place: {word.Text}")
                    { Target = word.Text, OutsideAccess = true, ReadsUntrusted = true, Field = "read" };
            if (IsCredential(full)) return new Classification(ActionClass.HardDeny, "reads a credential store") { Target = full };
            if (ReadableRoots(context).Any(root => PathRules.IsInside(full, root, strict: false)))
                return new Classification(ActionClass.ReadLocal, "reads inside the access folders") { Target = full, Field = "read" };
            return new Classification(ActionClass.ReadLocal, $"reads outside the access folders: {full}")
            {
                Target = full, GrantTarget = GrantTarget.Folder(full), OutsideAccess = true, ReadsUntrusted = true, Field = "read",
            };
        }

        /// <summary>Writing (creating, changing or overwriting) the file or folder a word names.</summary>
        public static Classification Write(CommandWord word, string? cwd, ClassifierContext context)
        {
            switch (Provider(word.Text))
            {
                case ProviderKind.Registry:
                    return new Classification(ActionClass.HardDeny, "changes the registry (security settings)") { Target = word.Text };
                case ProviderKind.Certificate:
                    return new Classification(ActionClass.HardDeny, "changes the certificate store") { Target = word.Text };
                case ProviderKind.Function or ProviderKind.Alias:
                    return Unknown("redefines a command");
                case ProviderKind.Environment:
                    return SensitiveVariables.Contains(VariableName(word.Text))
                        ? Unknown($"changes {VariableName(word.Text)}, which decides what later commands run")
                        : new Classification(ActionClass.Work, "sets an environment variable") { Target = word.Text };
                case ProviderKind.Variable:
                    return new Classification(ActionClass.Work, "sets a variable") { Target = word.Text };
                case ProviderKind.Other:
                    return Unknown($"writes to a PowerShell drive the classifier does not know: {word.Text}");
            }
            var full = Full(word, cwd, context);
            if (full is null)
                return new Classification(ActionClass.WriteElsewhere, $"writes somewhere the classifier cannot place: {word.Text}")
                    { Target = word.Text, Field = "write" };
            if (IsCredential(full)) return new Classification(ActionClass.HardDeny, "writes to a credential store") { Target = full };
            if (IsProtected(full, context)) return new Classification(ActionClass.HardDeny, "changes nth's own settings and grants") { Target = full };
            // git's own folder holds hooks and settings that later git commands run: never plain work.
            if (IsAgentWritable(full, context) && !full.Split('\\').Any(s => string.Equals(s, ".git", StringComparison.OrdinalIgnoreCase)))
                return new Classification(ActionClass.Work, "changes files inside the mission's folders") { Target = full };
            return new Classification(ActionClass.WriteElsewhere, $"writes outside the access folders: {full}")
                { Target = full, GrantTarget = GrantTarget.Folder(full), Field = "write" };
        }

        /// <summary>Removing (deleting, moving away, emptying) the file or folder a word names.</summary>
        public static Classification Remove(CommandWord word, string? cwd, ClassifierContext context)
        {
            switch (Provider(word.Text))
            {
                case ProviderKind.Registry:
                    return new Classification(ActionClass.HardDeny, "changes the registry (security settings)") { Target = word.Text };
                case ProviderKind.Certificate:
                    return new Classification(ActionClass.HardDeny, "changes the certificate store") { Target = word.Text };
                case ProviderKind.Function or ProviderKind.Alias:
                    return Unknown("removes a command definition");
                case ProviderKind.Environment or ProviderKind.Variable:
                    return SensitiveVariables.Contains(VariableName(word.Text))
                        ? Unknown($"changes {VariableName(word.Text)}, which decides what later commands run")
                        : new Classification(ActionClass.Work, "removes a variable") { Target = word.Text };
                case ProviderKind.Other:
                    return Unknown($"removes from a PowerShell drive the classifier does not know: {word.Text}");
            }
            var full = Full(word, cwd, context);
            if (full is null)
                return new Classification(ActionClass.Destructive, $"removes something the classifier cannot place: {word.Text}") { Target = word.Text };
            if (IsCredential(full)) return new Classification(ActionClass.HardDeny, "removes from a credential store") { Target = full };
            if (IsProtected(full, context)) return new Classification(ActionClass.HardDeny, "changes nth's own settings and grants") { Target = full };
            if (context.MissionWorktree is { } worktree && PathRules.Normalize(worktree) is { } top && PathRules.IsInside(full, top, strict: true)
                && !full[top.Length..].Split('\\').Any(s => string.Equals(s, ".git", StringComparison.OrdinalIgnoreCase)))
                return new Classification(ActionClass.Work, "removes files inside the mission worktree") { Target = full };
            return new Classification(ActionClass.Destructive, $"removes files outside the mission worktree: {full}") { Target = full };
        }
    }
}
