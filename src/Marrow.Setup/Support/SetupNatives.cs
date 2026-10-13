using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 5, "Native libraries"; VELOPACK_CHECK.md finding 3; ED-NEW-inst-fix-5). A Native AOT
/// setup is one exe, but Skia, HarfBuzz and ANGLE are native libraries no linker takes in (IncludeNativeLibrariesForSelfExtract
/// does nothing under AOT). The Native AOT publish therefore stores them inside the exe, Deflate-compressed, with a manifest of
/// their names, sizes and SHA-256s (Marrow.Setup.csproj, target NthEmbedSetupNatives), and leaves none beside it. Before the
/// window is built they are unpacked to <c>%LOCALAPPDATA%\nth Setup\runtime\&lt;build hash&gt;\</c> (a folder per build, claimed while
/// the setup runs, swept by unit U9 once no setup uses it), or beside the uninstaller's temporary copy in <c>%TEMP%\nth Uninstall</c>
/// (so a copy leaves one folder behind, not two), each written to a <c>.partial</c> file and moved into place only when its
/// size and hash match. A library already there is used only if it matches. All of them are then loaded by full path, and
/// SkiaSharp's, HarfBuzzSharp's and Avalonia.Win32's imports are routed to those modules, so nothing is looked up on the
/// search path. A build that is not a Native AOT publish (the build output, the tests) carries none: its libraries sit beside it.
/// </summary>
public static class SetupNatives
{
    public const string ResourcePrefix = "SetupNative/";
    public const string ManifestResource = ResourcePrefix + "manifest.txt";
    public const string PartialSuffix = ".partial";

    /// <summary>The only libraries the setup unpacks (and the sweeps accept as its own).</summary>
    public static IReadOnlyList<string> Names { get; } = ["av_libglesv2.dll", "libHarfBuzzSharp.dll", "libSkiaSharp.dll"];

    /// <summary>One library: its file name, its size and the lower-case hex SHA-256 of its bytes.</summary>
    public sealed record Native(string Name, long Length, string Sha256);

    // Held for the life of the process: the claim keeps other setups' sweeps out of the folder whose libraries this one runs.
    private static FolderClaim? held;

    /// <summary>The manifest this build carries, read; empty when it carries none.</summary>
    public static IReadOnlyList<Native> Embedded()
    {
        using var stream = Open(ManifestResource);
        if (stream is null) return [];
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ReadManifest(reader.ReadToEnd());
    }

    /// <summary>
    /// The manifest's lines: <c>name TAB size TAB sha256</c>. Only the three known names, each once, with a plain size and a
    /// 64-digit lower-case hash; anything else is an <see cref="InvalidDataException"/> (a damaged or foreign build).
    /// </summary>
    public static IReadOnlyList<Native> ReadManifest(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var natives = new List<Native>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length != 3 || !Names.Contains(parts[0], StringComparer.Ordinal) || natives.Any(native => native.Name == parts[0])
                || !long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length) || length <= 0
                || parts[2].Length != 64 || parts[2].AsSpan().ContainsAnyExcept("0123456789abcdef"))
                throw new InvalidDataException($"The setup's list of native libraries is damaged: '{line}'.");
            natives.Add(new Native(parts[0], length, parts[2]));
        }
        return natives;
    }

    /// <summary>The build hash that names the runtime folder: the first 16 hex digits of the SHA-256 of the manifest's lines.</summary>
    public static string BuildHash(IReadOnlyList<Native> natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        var text = string.Concat(natives.Select(native => native.Name + "\t" + native.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" + native.Sha256 + "\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>
    /// Where the libraries go: beside the uninstaller's temporary copy when this process is that copy, else
    /// <c>%LOCALAPPDATA%\nth Setup\runtime\&lt;build hash&gt;</c>.
    /// </summary>
    public static string FolderFor(string localAppData, string tempPath, string? processPath, string buildHash)
    {
        var copyFolder = SetupFolders.UninstallTemp(tempPath);
        if (processPath is not null && Path.IsPathFullyQualified(processPath)
            && string.Equals(Path.GetDirectoryName(Path.GetFullPath(processPath)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(copyFolder)), StringComparison.OrdinalIgnoreCase))
            return copyFolder;
        return SetupFolders.RuntimeFolder(localAppData, buildHash);
    }

    /// <summary>
    /// Unpacks and loads this build's libraries (nothing when it carries none). Throws when they cannot be put in place or
    /// loaded; the window cannot be shown without them.
    /// </summary>
    public static void Prepare(string localAppData, string tempPath, string? processPath)
    {
        var natives = Embedded();
        if (natives.Count == 0) return;
        var folder = FolderFor(localAppData, tempPath, processPath, BuildHash(natives));
        var copy = string.Equals(folder, SetupFolders.UninstallTemp(tempPath), StringComparison.OrdinalIgnoreCase);
        if (!copy) held ??= TryClaim(folder);
        if (copy || held is not null) Unpack(folder, natives, name => Open(ResourcePrefix + name));
        else if (natives.Any(native => !IsWhole(Path.Combine(folder, native.Name), native)))
            throw new IOException($"Another nth Setup is using '{folder}' and its libraries are not complete yet.");
        Route(Load(folder, natives));
    }

    /// <summary>Writes every library that is missing or not the one listed, from <paramref name="open"/> (its Deflate stream). Returns how many it wrote.</summary>
    public static int Unpack(string folder, IReadOnlyList<Native> natives, Func<string, Stream?> open)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(open);
        Directory.CreateDirectory(folder);
        var written = 0;
        foreach (var native in natives)
        {
            var path = Path.Combine(folder, native.Name);
            if (IsWhole(path, native)) continue;
            var partial = path + PartialSuffix;
            try
            {
                using (var source = open(native.Name) ?? throw new InvalidDataException($"The setup does not carry {native.Name}."))
                using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
                using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    inflate.CopyTo(file);
                    file.Flush(flushToDisk: true);
                }
                if (!IsWhole(partial, native)) throw new InvalidDataException($"{native.Name} did not unpack whole.");
                File.Move(partial, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
            written++;
        }
        return written;
    }

    /// <summary>Whether <paramref name="path"/> holds exactly the listed library (size, then SHA-256).</summary>
    public static bool IsWhole(string path, Native native)
    {
        ArgumentNullException.ThrowIfNull(native);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16);
            return file.Length == native.Length && Convert.ToHexStringLower(SHA256.HashData(file)) == native.Sha256;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static FolderClaim? TryClaim(string folder)
    {
        try { return FolderClaim.Take(folder); }
        catch (IOException) when (File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)))
        {
            return null; // another setup of this build holds it: its libraries are used as they are, if whole
        }
    }

    private static Dictionary<string, nint> Load(string folder, IReadOnlyList<Native> natives)
    {
        var loaded = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        foreach (var native in natives) loaded[native.Name] = NativeLibrary.Load(Path.Combine(folder, native.Name));
        return loaded;
    }

    // DllImport("libSkiaSharp"), ("libHarfBuzzSharp") and ("av_libglesv2.dll") find the loaded modules; any other import goes the usual way.
    private static void Route(Dictionary<string, nint> loaded)
    {
        foreach (var assembly in new[] { typeof(SkiaSharp.SKObject).Assembly, typeof(HarfBuzzSharp.Blob).Assembly, typeof(Avalonia.Win32PlatformOptions).Assembly })
        {
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, (name, _, _) =>
                    loaded.TryGetValue(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll", out var module) ? module : 0);
            }
            catch (InvalidOperationException)
            {
                // the assembly routes its own imports; the module loaded above answers a lookup by name all the same
            }
        }
    }

    private static Stream? Open(string resource) => typeof(SetupNatives).Assembly.GetManifestResourceStream(resource);
}
