using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Native libraries"); VELOPACK_CHECK.md finding 3 (ED-NEW-inst-fix-5). A Native
// AOT setup had no Skia, HarfBuzz or ANGLE: IncludeNativeLibrariesForSelfExtract does nothing under AOT. The publish now stores them
// inside the exe with a manifest; these pin the manifest's rules, the folder per build, the whole-or-nothing unpack and the build
// that carries none. Every file lives in a temporary folder standing in for %LOCALAPPDATA% and %TEMP%; nothing is loaded.
public sealed class EmbeddedLibrariesTests : IDisposable
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_manifest_lists_the_three_libraries_by_size_and_hash_and_names_one_folder_per_build()
    {
        var libraries = Libraries();
        var text = Manifest(libraries);

        var read = SetupNatives.ReadManifest(text);

        Assert.Equal(libraries.Select(library => library.Native), read);
        Assert.Equal(SetupNatives.Names.Order(StringComparer.Ordinal), read.Select(native => native.Name));
        var hash = SetupNatives.BuildHash(read);
        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.Equal(hash, SetupNatives.BuildHash(SetupNatives.ReadManifest(text.Replace("\n", "\r\n", StringComparison.Ordinal))));
        var other = read.Select((native, i) => i == 0 ? native with { Length = native.Length + 1 } : native).ToArray();
        Assert.NotEqual(hash, SetupNatives.BuildHash(other));
        Assert.Empty(SetupNatives.ReadManifest(""));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("libSkiaSharp.dll\t10")]
    [InlineData("libSkiaSharp.dll\t10\t{hash}\textra")]
    [InlineData("libSkiaSharp.dll 10 {hash}")]
    [InlineData("vcruntime140.dll\t10\t{hash}")]
    [InlineData("LIBSKIASHARP.DLL\t10\t{hash}")]
    [InlineData("libSkiaSharp.dll\t0\t{hash}")]
    [InlineData("libSkiaSharp.dll\t-10\t{hash}")]
    [InlineData("libSkiaSharp.dll\t1e3\t{hash}")]
    [InlineData("libSkiaSharp.dll\t10\t{HASH}")]
    [InlineData("libSkiaSharp.dll\t10\t{hash63}")]
    [InlineData("libSkiaSharp.dll\t10\t{hash}\nlibSkiaSharp.dll\t10\t{hash}")]
    public void A_damaged_or_foreign_manifest_is_refused(string text)
    {
        var line = text.Replace("{hash63}", Hash[..63], StringComparison.Ordinal).Replace("{hash}", Hash, StringComparison.Ordinal).Replace("{HASH}", Hash.ToUpperInvariant(), StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => SetupNatives.ReadManifest(line));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Each_library_is_unpacked_whole_and_a_second_start_writes_none()
    {
        var libraries = Libraries();
        var folder = SetupFolders.RuntimeFolder(area.LocalAppData, SetupNatives.BuildHash([.. libraries.Select(library => library.Native)]));

        Assert.Equal(3, SetupNatives.Unpack(folder, [.. libraries.Select(library => library.Native)], Open(libraries)));

        foreach (var library in libraries)
        {
            Assert.Equal(library.Bytes, File.ReadAllBytes(Path.Combine(folder, library.Native.Name)));
            Assert.True(SetupNatives.IsWhole(Path.Combine(folder, library.Native.Name), library.Native));
        }
        Assert.Equal(libraries.Select(library => library.Native.Name).Order(StringComparer.Ordinal), Directory.EnumerateFiles(folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal));
        Assert.Equal(0, SetupNatives.Unpack(folder, [.. libraries.Select(library => library.Native)], _ => throw new InvalidOperationException("nothing is read again")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_library_that_is_not_the_listed_one_is_written_again()
    {
        var libraries = Libraries();
        var folder = Path.Combine(area.Temp, "natives");
        SetupNatives.Unpack(folder, [.. libraries.Select(library => library.Native)], Open(libraries));
        var changed = libraries[1];
        var path = Path.Combine(folder, changed.Native.Name);
        var wrong = (byte[])changed.Bytes.Clone();
        wrong[^1] ^= 0xFF;
        File.WriteAllBytes(path, wrong); // same size, other bytes
        Assert.False(SetupNatives.IsWhole(path, changed.Native));

        Assert.Equal(1, SetupNatives.Unpack(folder, [.. libraries.Select(library => library.Native)], Open(libraries)));

        Assert.Equal(changed.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_damaged_or_missing_library_is_refused_and_leaves_no_file_and_no_partial_write()
    {
        var libraries = Libraries();
        var natives = libraries.Select(library => library.Native).ToArray();
        var folder = Path.Combine(area.Temp, "natives");

        Assert.Throws<InvalidDataException>(() => SetupNatives.Unpack(folder, [natives[0]], _ => new MemoryStream(Deflate(libraries[1].Bytes))));
        Assert.Throws<InvalidDataException>(() => SetupNatives.Unpack(folder, [natives[0]], _ => null));

        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Whole_means_the_listed_size_and_hash()
    {
        var library = Libraries()[0];
        var path = Path.Combine(area.Temp, library.Native.Name);

        Assert.False(SetupNatives.IsWhole(path, library.Native));
        Assert.False(SetupNatives.IsWhole(Path.Combine(area.Temp, "missing", library.Native.Name), library.Native));
        File.WriteAllBytes(path, library.Bytes[..^1]);
        Assert.False(SetupNatives.IsWhole(path, library.Native));
        File.WriteAllBytes(path, library.Bytes);
        Assert.True(SetupNatives.IsWhole(path, library.Native));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstallers_copy_unpacks_beside_itself_and_every_other_start_into_the_builds_runtime_folder()
    {
        var copy = Path.Combine(area.UninstallTemp, "nth Uninstall.exe");

        Assert.Equal(area.UninstallTemp, SetupNatives.FolderFor(area.LocalAppData, area.Temp, copy, "4f9c2a0011223344"));
        Assert.Equal(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344"), SetupNatives.FolderFor(area.LocalAppData, area.Temp, Path.Combine(area.Base, "Downloads", "nth Setup.exe"), "4f9c2a0011223344"));
        Assert.Equal(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344"), SetupNatives.FolderFor(area.LocalAppData, area.Temp, Path.Combine(area.UninstallTemp, "sub", "nth Uninstall.exe"), "4f9c2a0011223344"));
        Assert.Equal(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344"), SetupNatives.FolderFor(area.LocalAppData, area.Temp, null, "4f9c2a0011223344"));
        Assert.Equal(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344"), SetupNatives.FolderFor(area.LocalAppData, area.Temp, "nth Uninstall.exe", "4f9c2a0011223344"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_build_that_is_not_an_AOT_publish_carries_none_and_unpacks_nothing()
    {
        Assert.Empty(SetupNatives.Embedded());

        SetupNatives.Prepare(area.LocalAppData, area.Temp, Path.Combine(area.Base, "nth Setup.exe"));

        Assert.False(Directory.Exists(area.SetupRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(area.Temp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_AOT_publish_embeds_the_three_libraries_and_leaves_none_beside_the_exe()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Marrow.Setup.csproj")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("<IncludeNativeLibrariesForSelfExtract>", project, StringComparison.Ordinal);
        Assert.Contains("<Target Name=\"NthEmbedSetupNatives\" BeforeTargets=\"AssignTargetPaths\" DependsOnTargets=\"ResolvePackageAssets\"\n          Condition=\"'$(PublishAot)' == 'true' and '$(RuntimeIdentifier)' != ''\">", project, StringComparison.Ordinal);
        Assert.Contains("<Error Condition=\"'@(_NthSetupNative->Count())' != '3'\"", project, StringComparison.Ordinal);
        Assert.Contains("output.SetMetadata(\"LogicalName\", \"" + SetupNatives.ResourcePrefix + "\" + name);", project, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"$(_NthSetupNativeManifest)\" LogicalName=\"" + SetupNatives.ManifestResource + "\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Target Name=\"NthLeaveEmbeddedNativesOutOfPublish\" AfterTargets=\"Publish\" Condition=\"'@(_NthSetupNative)' != ''\">", project, StringComparison.Ordinal);
        foreach (var name in SetupNatives.Names) Assert.Contains("'%(Filename)%(Extension)' == '" + name + "'", project, StringComparison.Ordinal);
        // the window is built only after the libraries are in place
        var program = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));
        var prepare = program.IndexOf("SetupNatives.Prepare(localAppData, Path.GetTempPath(), Environment.ProcessPath);", StringComparison.Ordinal);
        Assert.True(prepare > 0);
        Assert.True(program.IndexOf("SetupApp.Build()", prepare, StringComparison.Ordinal) > prepare);
    }

    // Three small stand-ins, named as the real ones, listed the way the publish lists them (by name, ordinal).
    private static (SetupNatives.Native Native, byte[] Bytes)[] Libraries() =>
        [.. SetupNatives.Names.Order(StringComparer.Ordinal).Select((name, i) =>
        {
            var bytes = new byte[4096 + i * 777];
            new Random(17 + i).NextBytes(bytes);
            return (new SetupNatives.Native(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
        })];

    // As the build task writes it: name TAB size TAB sha256, one per line.
    private static string Manifest(IEnumerable<(SetupNatives.Native Native, byte[] Bytes)> libraries)
    {
        var text = new StringBuilder();
        foreach (var library in libraries) text.Append(library.Native.Name).Append('\t').Append(library.Native.Length).Append('\t').Append(library.Native.Sha256).Append('\n');
        return text.ToString();
    }

    private static Func<string, Stream?> Open(IEnumerable<(SetupNatives.Native Native, byte[] Bytes)> libraries) =>
        name => libraries.Where(library => library.Native.Name == name).Select(library => (Stream)new MemoryStream(Deflate(library.Bytes))).FirstOrDefault();

    private static byte[] Deflate(byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var deflate = new DeflateStream(memory, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(bytes);
        return memory.ToArray();
    }
}
