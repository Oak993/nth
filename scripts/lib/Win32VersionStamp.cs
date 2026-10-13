#nullable disable
// nth installer PLAN.md sections 7 and 9, unit U7 (ED-NEW-inst-u7-4). Writes a complete version resource (RT_VERSION) into a
// Windows executable with the Win32 resource API: the packaging script stamps nth.exe (the renamed apphost), the setup exe and
// Velopack's Update.exe, so Explorer, Task Manager, SmartScreen and the Apps list never show a foreign name.
//
// Two users compile this one file: scripts/lib/NthPackaging.ps1 (Add-Type, PowerShell 7) and tests/Marrow.Core.Tests (linked
// source). It is plain C# 9 with no nullable annotations so both compile it unchanged. It is a build tool and is never part of
// the app, the setup or the installer.
//
// Layout (SOURCE FACT, the VS_VERSIONINFO structure on learn.microsoft.com): VS_VERSION_INFO { VS_FIXEDFILEINFO,
// StringFileInfo { StringTable "040904B0" { String... } }, VarFileInfo { Translation 0x0409, 1200 } }. Every node starts on a
// 4-byte boundary and its length counts its children but not the padding after it.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Nth.Build
{
    /// <summary>The text fields of a version resource. A null field keeps what the file already says.</summary>
    public sealed class VersionFields
    {
        public string FileDescription;
        public string ProductName;
        public string CompanyName;
        public string FileVersion;
        public string ProductVersion;
        public string LegalCopyright;
        public string OriginalFilename;
        public string InternalName;
        public string Comments;
    }

    public static class Win32VersionStamp
    {
        /// <summary>English (United States), the language PLAN.md section 7 asks for.</summary>
        public const ushort EnglishUnitedStates = 0x0409;

        private const int CodePageUnicode = 1200;
        private const uint LoadLibraryAsDataFile = 0x00000002;
        private static readonly IntPtr VersionResourceType = new IntPtr(16);
        private static readonly IntPtr VersionResourceName = new IntPtr(1);

        /// <summary>What the file says now (null for a field it does not have).</summary>
        public static VersionFields Read(string path)
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new VersionFields
            {
                FileDescription = Empty(info.FileDescription),
                ProductName = Empty(info.ProductName),
                CompanyName = Empty(info.CompanyName),
                FileVersion = Empty(info.FileVersion),
                ProductVersion = Empty(info.ProductVersion),
                LegalCopyright = Empty(info.LegalCopyright),
                OriginalFilename = Empty(info.OriginalFilename),
                InternalName = Empty(info.InternalName),
                Comments = Empty(info.Comments),
            };
        }

        /// <summary>The fields the file will have: every non-null field of <paramref name="change"/> over what the file says now.</summary>
        public static VersionFields Merge(VersionFields current, VersionFields change)
        {
            return new VersionFields
            {
                FileDescription = change.FileDescription ?? current.FileDescription,
                ProductName = change.ProductName ?? current.ProductName,
                CompanyName = change.CompanyName ?? current.CompanyName,
                FileVersion = change.FileVersion ?? current.FileVersion,
                ProductVersion = change.ProductVersion ?? current.ProductVersion,
                LegalCopyright = change.LegalCopyright ?? current.LegalCopyright,
                OriginalFilename = change.OriginalFilename ?? current.OriginalFilename,
                InternalName = change.InternalName ?? current.InternalName,
                Comments = change.Comments ?? current.Comments,
            };
        }

        /// <summary>
        /// Writes the version resource of <paramref name="path"/>: the file's own fields with <paramref name="change"/> over them,
        /// in English (United States), replacing every other language's version resource. Reads the file back and throws when it
        /// does not say what was written. The file must not be running.
        /// </summary>
        public static void Stamp(string path, VersionFields change)
        {
            path = Path.GetFullPath(path);
            var merged = Merge(Read(path), change);
            var blob = Build(merged);
            var languages = ExistingLanguages(path);

            var update = BeginUpdateResource(path, false);
            if (update == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "BeginUpdateResource failed for " + path);
            var ok = false;
            var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
            try
            {
                foreach (var language in languages)
                {
                    if (language == EnglishUnitedStates) continue;
                    if (!UpdateResource(update, VersionResourceType, VersionResourceName, language, IntPtr.Zero, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateResource could not remove language " + language.ToString(CultureInfo.InvariantCulture));
                }
                if (!UpdateResource(update, VersionResourceType, VersionResourceName, EnglishUnitedStates, handle.AddrOfPinnedObject(), (uint)blob.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateResource failed for " + path);
                ok = true;
            }
            finally
            {
                handle.Free();
                if (!EndUpdateResource(update, !ok) && ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "EndUpdateResource failed for " + path);
            }
            Verify(path, merged);
        }

        /// <summary>Throws when the file does not say <paramref name="expected"/> (every non-null field) in English (United States).</summary>
        public static void Verify(string path, VersionFields expected)
        {
            var actual = Read(path);
            var problems = new List<string>();
            Compare(problems, "FileDescription", expected.FileDescription, actual.FileDescription);
            Compare(problems, "ProductName", expected.ProductName, actual.ProductName);
            Compare(problems, "CompanyName", expected.CompanyName, actual.CompanyName);
            Compare(problems, "FileVersion", expected.FileVersion, actual.FileVersion);
            Compare(problems, "ProductVersion", expected.ProductVersion, actual.ProductVersion);
            Compare(problems, "LegalCopyright", expected.LegalCopyright, actual.LegalCopyright);
            Compare(problems, "OriginalFilename", expected.OriginalFilename, actual.OriginalFilename);
            Compare(problems, "InternalName", expected.InternalName, actual.InternalName);
            Compare(problems, "Comments", expected.Comments, actual.Comments);
            var language = FileVersionInfo.GetVersionInfo(path).Language;
            if (language == null || language.IndexOf("English (United States)", StringComparison.Ordinal) < 0)
                problems.Add("Language is '" + language + "', not English (United States)");
            if (problems.Count > 0) throw new InvalidOperationException(path + " does not say what was stamped: " + string.Join("; ", problems));
        }

        /// <summary>The VS_VERSIONINFO bytes for these fields (a null or empty field is left out).</summary>
        public static byte[] Build(VersionFields fields)
        {
            var strings = new List<byte[]>();
            Add(strings, "Comments", fields.Comments);
            Add(strings, "CompanyName", fields.CompanyName);
            Add(strings, "FileDescription", fields.FileDescription);
            Add(strings, "FileVersion", fields.FileVersion);
            Add(strings, "InternalName", fields.InternalName);
            Add(strings, "LegalCopyright", fields.LegalCopyright);
            Add(strings, "OriginalFilename", fields.OriginalFilename);
            Add(strings, "ProductName", fields.ProductName);
            Add(strings, "ProductVersion", fields.ProductVersion);

            var table = Node("040904B0", 1, new byte[0], 0, strings);
            var stringFileInfo = Node("StringFileInfo", 1, new byte[0], 0, new List<byte[]> { table });
            var translation = Node("Translation", 0, TranslationValue(), 4, new List<byte[]>());
            var varFileInfo = Node("VarFileInfo", 1, new byte[0], 0, new List<byte[]> { translation });
            var fixedInfo = FixedFileInfo(fields.FileVersion, fields.ProductVersion);
            return Node("VS_VERSION_INFO", 0, fixedInfo, (ushort)fixedInfo.Length, new List<byte[]> { stringFileInfo, varFileInfo });
        }

        private static void Add(List<byte[]> strings, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var bytes = Encoding.Unicode.GetBytes(value + "\0");
            strings.Add(Node(name, 1, bytes, (ushort)(bytes.Length / 2), new List<byte[]>()));
        }

        // One node: wLength, wValueLength, wType, the key, padding, the value, then each child on a 4-byte boundary.
        private static byte[] Node(string key, ushort type, byte[] value, ushort valueLength, List<byte[]> children)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream, Encoding.Unicode);
            writer.Write((ushort)0);
            writer.Write(valueLength);
            writer.Write(type);
            writer.Write(Encoding.Unicode.GetBytes(key + "\0"));
            Align(stream);
            writer.Write(value);
            foreach (var child in children)
            {
                Align(stream);
                writer.Write(child);
            }
            writer.Flush();
            var bytes = stream.ToArray();
            var length = bytes.Length;
            if (length > ushort.MaxValue) throw new InvalidOperationException("The version resource is too long.");
            bytes[0] = (byte)(length & 0xFF);
            bytes[1] = (byte)((length >> 8) & 0xFF);
            return bytes;
        }

        private static void Align(MemoryStream stream)
        {
            while (stream.Length % 4 != 0) stream.WriteByte(0);
        }

        private static byte[] TranslationValue()
        {
            // the language and the code page, as two WORDs: 0x0409, 1200
            return new byte[] { 0x09, 0x04, (byte)(CodePageUnicode & 0xFF), (byte)(CodePageUnicode >> 8) };
        }

        private static byte[] FixedFileInfo(string fileVersion, string productVersion)
        {
            var file = Numbers(fileVersion);
            var product = Numbers(productVersion ?? fileVersion);
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(0xFEEF04BDu);                                   // dwSignature
            writer.Write(0x00010000u);                                   // dwStrucVersion
            writer.Write(((uint)file[0] << 16) | file[1]);               // dwFileVersionMS
            writer.Write(((uint)file[2] << 16) | file[3]);               // dwFileVersionLS
            writer.Write(((uint)product[0] << 16) | product[1]);         // dwProductVersionMS
            writer.Write(((uint)product[2] << 16) | product[3]);         // dwProductVersionLS
            writer.Write(0x0000003Fu);                                   // dwFileFlagsMask
            writer.Write(0u);                                            // dwFileFlags
            writer.Write(0x00040004u);                                   // dwFileOS: VOS_NT_WINDOWS32
            writer.Write(1u);                                            // dwFileType: VFT_APP
            writer.Write(0u);                                            // dwFileSubtype
            writer.Write(0u);                                            // dwFileDateMS
            writer.Write(0u);                                            // dwFileDateLS
            writer.Flush();
            return stream.ToArray();
        }

        // The first four numbers of a version text ("1.0.0.1", "1.0.0" or "1.2.161+abc"), each clamped to a WORD; missing ones are 0.
        private static ushort[] Numbers(string text)
        {
            var result = new ushort[4];
            if (string.IsNullOrEmpty(text)) return result;
            var matches = Regex.Matches(text, @"\d+", RegexOptions.CultureInvariant);
            for (var i = 0; i < 4 && i < matches.Count; i++)
            {
                ulong value;
                if (!ulong.TryParse(matches[i].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)) value = 0;
                result[i] = (ushort)Math.Min(value, (ulong)ushort.MaxValue);
            }
            return result;
        }

        private static void Compare(List<string> problems, string name, string expected, string actual)
        {
            if (expected == null) return;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                problems.Add(name + " is '" + actual + "', not '" + expected + "'");
        }

        private static string Empty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private delegate bool EnumResourceLanguagesProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);

        // The languages the file's version resource (id 1) exists in now. The file is mapped as data and released again before any update.
        private static List<ushort> ExistingLanguages(string path)
        {
            var languages = new List<ushort>();
            var module = LoadLibraryEx(path, IntPtr.Zero, LoadLibraryAsDataFile);
            if (module == IntPtr.Zero) return languages;
            try
            {
                EnumResourceLanguagesProc callback = delegate (IntPtr m, IntPtr t, IntPtr n, ushort language, IntPtr p)
                {
                    languages.Add(language);
                    return true;
                };
                EnumResourceLanguages(module, VersionResourceType, VersionResourceName, callback, IntPtr.Zero);
                GC.KeepAlive(callback);
            }
            finally
            {
                FreeLibrary(module);
            }
            return languages;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr BeginUpdateResource(string fileName, [MarshalAs(UnmanagedType.Bool)] bool deleteExistingResources);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndUpdateResource(IntPtr update, [MarshalAs(UnmanagedType.Bool)] bool discard);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumResourceLanguagesProc callback, IntPtr parameter);
    }
}
