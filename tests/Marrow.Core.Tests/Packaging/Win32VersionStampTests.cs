using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;
using Nth.Build;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 7 and 9, VELOPACK_CHECK.md check 12, unit U7 (ED-NEW-inst-u7-4). The packaging script
// stamps a complete version resource into nth.exe, the setup exe and Velopack's Update.exe with the Win32 resource API
// (scripts/lib/Win32VersionStamp.cs, compiled here from the same file). Every file stamped here is a copy in a temp folder; nothing is run.
public sealed class Win32VersionStampTests
{
    private static string Copy(string source, string name)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-stamp-").FullName, name);
        File.Copy(source, path);
        return path;
    }

    // a small native console exe with a version resource, and a managed dll whose resource is the compiler's (language neutral)
    private static string NativeExe() => Path.Combine(Environment.SystemDirectory, "where.exe");

    private static string ManagedDll() => typeof(Win32VersionStampTests).Assembly.Location;

    private static VersionFields Everything() => new()
    {
        FileDescription = "nth Setup", ProductName = "nth", CompanyName = "Example Publisher", FileVersion = "1.0.0.1", ProductVersion = "1.0.0",
        LegalCopyright = "© 2026 Example Publisher. Includes Droppy Code by Jordy Spruit (Droppy), GNU AGPL v3.0.",
        OriginalFilename = "nth-Setup.exe", InternalName = "nth Setup", Comments = "a comment",
    };

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_stamped_exe_says_exactly_what_was_written_in_English_United_States()
    {
        var path = Copy(NativeExe(), "nth-Setup.exe");

        Win32VersionStamp.Stamp(path, Everything());

        var info = FileVersionInfo.GetVersionInfo(path);
        Assert.Equal("nth Setup", info.FileDescription);
        Assert.Equal("nth", info.ProductName);
        Assert.Equal("Example Publisher", info.CompanyName);
        Assert.Equal("1.0.0.1", info.FileVersion);
        Assert.Equal("1.0.0", info.ProductVersion);
        Assert.StartsWith("© 2026 Example Publisher.", info.LegalCopyright, StringComparison.Ordinal);
        Assert.Equal("nth-Setup.exe", info.OriginalFilename);
        Assert.Equal("nth Setup", info.InternalName);
        Assert.Equal("a comment", info.Comments);
        Assert.Contains("English (United States)", info.Language, StringComparison.Ordinal);
        Assert.Equal((1, 0, 0, 1), (info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart));
        Assert.Equal((1, 0, 0), (info.ProductMajorPart, info.ProductMinorPart, info.ProductBuildPart));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_field_left_null_keeps_what_the_file_already_says()
    {
        var path = Copy(NativeExe(), "Squirrel.exe");
        var before = Win32VersionStamp.Read(path);

        Win32VersionStamp.Stamp(path, new VersionFields { FileDescription = "nth Updater", ProductName = "nth", CompanyName = "Example Publisher", ProductVersion = "1.0.0" });

        var after = Win32VersionStamp.Read(path);
        Assert.Equal("nth Updater", after.FileDescription);
        Assert.Equal("1.0.0", after.ProductVersion);
        Assert.Equal(before.FileVersion, after.FileVersion);
        Assert.Equal(before.LegalCopyright, after.LegalCopyright);
        Assert.Equal(before.OriginalFilename, after.OriginalFilename);
        Assert.Equal(before.InternalName, after.InternalName);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stamping_replaces_the_compilers_neutral_language_resource_and_leaves_one_version_resource()
    {
        var path = Copy(ManagedDll(), "nth.dll");
        var languages = typeof(Win32VersionStamp).GetMethod("ExistingLanguages", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain((ushort)0x0409, (List<ushort>)languages.Invoke(null, [path])!); // the compiler writes a language-neutral resource

        Win32VersionStamp.Stamp(path, Everything());

        Assert.Equal([(ushort)0x0409], (List<ushort>)languages.Invoke(null, [path])!);
        Assert.Equal("nth Setup", FileVersionInfo.GetVersionInfo(path).FileDescription);
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        Assert.True(reader.HasMetadata); // still the same managed assembly
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stamping_twice_gives_the_same_resource_and_leaves_the_pe_headers_alone()
    {
        var path = Copy(NativeExe(), "twice.exe");
        PEHeader Header()
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.PEHeader!;
        }
        var subsystem = Header().Subsystem;

        Win32VersionStamp.Stamp(path, Everything());
        var once = Win32VersionStamp.Read(path);
        Win32VersionStamp.Stamp(path, Everything());

        Assert.Equal(subsystem, Header().Subsystem);
        Assert.Equal(once.FileDescription, Win32VersionStamp.Read(path).FileDescription);
        Assert.Equal(once.LegalCopyright, Win32VersionStamp.Read(path).LegalCopyright);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Verify_names_every_field_that_differs()
    {
        var path = Copy(NativeExe(), "verify.exe");
        Win32VersionStamp.Stamp(path, Everything());

        var wrong = Everything();
        wrong.CompanyName = "Someone Else";
        wrong.InternalName = "other";
        var error = Assert.Throws<InvalidOperationException>(() => Win32VersionStamp.Verify(path, wrong));

        Assert.Contains("CompanyName is 'Example Publisher', not 'Someone Else'", error.Message, StringComparison.Ordinal);
        Assert.Contains("InternalName", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FileDescription", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_file_that_is_not_a_pe_is_refused_and_left_as_it_was()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-stamp-").FullName, "plain.exe");
        File.WriteAllText(path, "not an executable");

        Assert.ThrowsAny<Exception>(() => Win32VersionStamp.Stamp(path, Everything()));

        Assert.Equal("not an executable", File.ReadAllText(path));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Merge_puts_every_non_null_field_over_the_current_ones()
    {
        var current = Everything();
        var merged = Win32VersionStamp.Merge(current, new VersionFields { CompanyName = "New Co", Comments = null });

        Assert.Equal("New Co", merged.CompanyName);
        Assert.Equal(current.FileDescription, merged.FileDescription);
        Assert.Equal("a comment", merged.Comments);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_blob_follows_the_VS_VERSIONINFO_layout_with_every_node_on_a_four_byte_boundary()
    {
        var blob = Win32VersionStamp.Build(Everything());

        Assert.Equal("VS_VERSION_INFO", Key(blob, 0));
        Assert.Equal(blob.Length, Length(blob, 0));
        Assert.Equal(52, ValueBytes(blob, 0));
        var fixedInfo = ValueOffset(blob, 0);
        Assert.Equal(0xFEEF04BDu, BitConverter.ToUInt32(blob, fixedInfo));
        Assert.Equal(0x00010000u, BitConverter.ToUInt32(blob, fixedInfo + 4));
        Assert.Equal((1u << 16) | 0u, BitConverter.ToUInt32(blob, fixedInfo + 8)); // file version 1.0 (MS)
        Assert.Equal((0u << 16) | 1u, BitConverter.ToUInt32(blob, fixedInfo + 12)); // .0.1 (LS)
        var children = Children(blob, 0);
        Assert.Equal(["StringFileInfo", "VarFileInfo"], children.Select(c => Key(blob, c)).ToArray());
        Assert.All(children, child => Assert.Equal(0, child % 4));

        var table = Children(blob, children[0])[0];
        Assert.Equal("040904B0", Key(blob, table));
        var strings = Children(blob, table);
        var values = strings.ToDictionary(s => Key(blob, s), s => Encoding.Unicode.GetString(blob, ValueOffset(blob, s), ValueBytes(blob, s) - 2));
        Assert.Equal("nth Setup", values["FileDescription"]);
        Assert.Equal("nth-Setup.exe", values["OriginalFilename"]);
        Assert.Equal("1.0.0", values["ProductVersion"]);
        Assert.Equal(9, values.Count);
        Assert.All(strings, s => Assert.Equal(0, s % 4));

        var variable = Children(blob, children[1])[0];
        Assert.Equal("Translation", Key(blob, variable));
        Assert.Equal([0x09, 0x04, 0xB0, 0x04], blob[ValueOffset(blob, variable)..(ValueOffset(blob, variable) + 4)]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_empty_or_null_field_is_left_out_of_the_blob()
    {
        var blob = Win32VersionStamp.Build(new VersionFields { FileDescription = "nth", Comments = "", CompanyName = null, FileVersion = "2.1.0.7" });

        var table = Children(blob, Children(blob, 0)[0])[0];

        Assert.Equal(["FileDescription", "FileVersion"], Children(blob, table).Select(s => Key(blob, s)).ToArray());
        Assert.Equal((2u << 16) | 1u, BitConverter.ToUInt32(blob, ValueOffset(blob, 0) + 8));
        Assert.Equal((0u << 16) | 7u, BitConverter.ToUInt32(blob, ValueOffset(blob, 0) + 12));
    }

    // ---- a reader for the resource blob: a node is wLength, wValueLength, wType, the key, padding, the value, then its children ----

    private static int Length(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);

    // bytes of value: a text node counts characters, a binary node bytes
    private static int ValueBytes(byte[] data, int offset) => BitConverter.ToUInt16(data, offset + 2) * (BitConverter.ToUInt16(data, offset + 4) == 1 ? 2 : 1);

    private static string Key(byte[] data, int offset)
    {
        var end = offset + 6;
        while (BitConverter.ToUInt16(data, end) != 0) end += 2;
        return Encoding.Unicode.GetString(data, offset + 6, end - offset - 6);
    }

    private static int ValueOffset(byte[] data, int offset) => (offset + 6 + Key(data, offset).Length * 2 + 2 + 3) & ~3;

    private static List<int> Children(byte[] data, int offset)
    {
        var children = new List<int>();
        var end = offset + Length(data, offset);
        for (var next = (ValueOffset(data, offset) + ValueBytes(data, offset) + 3) & ~3; next < end; next = (next + Length(data, next) + 3) & ~3) children.Add(next);
        return children;
    }
}
