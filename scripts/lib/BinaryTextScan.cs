#nullable disable
// nth installer PLAN.md section 9 (build check 8: "no user-visible Marrow in the setup's strings"), unit U7 (ED-NEW-inst-u7-6).
// Finds a word in a binary file as ASCII/UTF-8 text and as UTF-16 text, which is how a native-AOT exe holds its string
// literals and its version resource. Used by scripts/lib/NthPackaging.ps1 (Add-Type) and linked into tests/Marrow.Core.Tests.
// Plain C# 9, no nullable annotations, so both compile it unchanged. A build tool: never part of the app or the setup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nth.Build
{
    public static class BinaryTextScan
    {
        /// <summary>
        /// The places in <paramref name="path"/> where <paramref name="word"/> stands as text (ASCII or UTF-16, any case), as a short
        /// readable context each, except where the word is the first part of a dotted name such as Marrow.Core.Support (a namespace
        /// or an assembly name, which a user never sees). The file is read in blocks.
        /// </summary>
        public static List<string> FindWord(string path, string word)
        {
            return FindWord(path, word, true, true);
        }

        /// <summary>
        /// As above, but each encoding on its own. A native-AOT exe keeps its string literals and its version resource as UTF-16, which
        /// is what a user can see; its type metadata is UTF-8 and stores every namespace segment as a name of its own ("Marrow" of
        /// Marrow.Core), which a user never sees.
        /// </summary>
        public static List<string> FindWord(string path, string word, bool ascii, bool utf16)
        {
            var found = new List<string>();
            var asciiBytes = Encoding.ASCII.GetBytes(word);
            var wideBytes = Encoding.Unicode.GetBytes(word);
            const int block = 1 << 20;
            var keep = 64;                       // bytes carried over between blocks so a match and its context are never cut
            var buffer = new byte[block + keep];
            var carried = 0;
            long offset = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read;
                while ((read = stream.Read(buffer, carried, block)) > 0)
                {
                    var length = carried + read;
                    var last = read < block;     // the final block: the context may end at the end of the data
                    var limit = last ? length : length - keep;
                    if (ascii) Scan(buffer, length, limit, offset - carried, asciiBytes, 1, found);
                    if (utf16) Scan(buffer, length, limit, offset - carried, wideBytes, 2, found);
                    if (last) { carried = 0; break; }
                    Buffer.BlockCopy(buffer, length - keep, buffer, 0, keep);
                    offset += read;
                    carried = keep;
                }
                // A full final read carries its last bytes into a zero-byte EOF read. Scan that tail once.
                if (ascii && carried > 0) Scan(buffer, carried, carried, offset - carried, asciiBytes, 1, found);
                if (utf16 && carried > 0) Scan(buffer, carried, carried, offset - carried, wideBytes, 2, found);
            }
            return found;
        }

        private static void Scan(byte[] data, int length, int limit, long origin, byte[] pattern, int width, List<string> found)
        {
            for (var i = 0; i + pattern.Length <= length && i < limit; i++)
            {
                if (!Matches(data, i, pattern, width)) continue;
                var after = i + pattern.Length;
                if (FollowsAsDottedName(data, length, after, width)) continue;
                found.Add(Context(data, length, i, pattern.Length, width) + " @" + (origin + i).ToString("X", System.Globalization.CultureInfo.InvariantCulture));
                i += pattern.Length - 1;
            }
        }

        private static bool Matches(byte[] data, int at, byte[] pattern, int width)
        {
            // every pattern byte is compared ignoring ASCII case; a UTF-16 pattern's zero bytes must be zero
            for (var k = 0; k < pattern.Length; k++)
            {
                var a = data[at + k];
                var b = pattern[k];
                if (a == b) continue;
                if (width == 1 || k % 2 == 0)
                {
                    if (a >= 'A' && a <= 'Z' && a + 32 == b) continue;
                    if (a >= 'a' && a <= 'z' && a - 32 == b) continue;
                }
                return false;
            }
            return true;
        }

        // ".X" right after the word, X an ASCII letter: Marrow.Core, Marrow.Setup
        private static bool FollowsAsDottedName(byte[] data, int length, int at, int width)
        {
            if (at + 2 * width > length) return false;
            if (data[at] != '.' || (width == 2 && data[at + 1] != 0)) return false;
            var letter = data[at + width];
            if (width == 2 && data[at + width + 1] != 0) return false;
            return (letter >= 'A' && letter <= 'Z') || (letter >= 'a' && letter <= 'z');
        }

        // up to 16 characters on each side of the match, non-printable bytes shown as '.'
        private static string Context(byte[] data, int length, int at, int size, int width)
        {
            var from = Math.Max(0, at - 16 * width);
            var to = Math.Min(length, at + size + 16 * width);
            var text = new StringBuilder();
            for (var i = from; i < to; i += width)
            {
                var c = data[i];
                text.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
            }
            return (width == 2 ? "utf16:" : "ascii:") + text;
        }
    }
}
