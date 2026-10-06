// OD-0111 / ED-360: this source is compiled by Core and by the read-only PowerShell preflight.
// C# 5 and framework APIs keep Windows PowerShell 5.1's Add-Type supported.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.SelfUpdate
{
    public enum ReleaseNotesCategory { NewFeatures, BugFixes, Refinements }

    /// <summary>The source About digest's exact line, header, bullet and inline-markdown rules.</summary>
    public static class ReleaseNotesParser
    {
        private static readonly HashSet<char> BulletMarkers = new HashSet<char> { '-', '\u2010', '\u2013', '\u2014', '\u2212', '*', '•' };

        public static Dictionary<ReleaseNotesCategory, List<string>> Parse(string raw)
        {
            var collected = new Dictionary<ReleaseNotesCategory, List<string>>();
            ReleaseNotesCategory? current = null;
            foreach (var sourceLine in raw.Split('\n'))
            {
                var line = sourceLine.Trim();
                if (line.Length == 0 || line == "---" || line.StartsWith("<", StringComparison.Ordinal)) continue;
                var bullet = BulletBody(line);
                if (bullet.Length > 0)
                {
                    if (current.HasValue)
                    {
                        var clean = StripInlineMarkdown(bullet);
                        if (clean.Length > 0)
                        {
                            var category = current.Value;
                            if (!collected.ContainsKey(category)) collected[category] = new List<string>();
                            collected[category].Add(clean);
                        }
                    }
                    continue;
                }
                var recognized = CategoryForHeader(line);
                if (recognized.HasValue) current = recognized.Value;
                else if (IsHeader(line)) current = null;
            }
            return collected;
        }

        private static string BulletBody(string line)
        {
            if (line.Length < 2 || !BulletMarkers.Contains(line[0]) || (line[1] != ' ' && line[1] != '\t')) return string.Empty;
            return line.Substring(1).Trim();
        }

        private static bool IsHeader(string line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var index = 0;
                while (index < line.Length && line[index] == '#') index++;
                return index == line.Length || line[index] == ' ' || line[index] == '\t';
            }
            return line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal) && line.Length > 4;
        }

        private static ReleaseNotesCategory? CategoryForHeader(string line)
        {
            var text = line;
            var index = 0;
            while (index < text.Length && text[index] == '#') index++;
            text = text.Substring(index).Trim();
            if (text.StartsWith("**", StringComparison.Ordinal)) text = text.Substring(2);
            if (text.EndsWith("**", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
            text = text.Trim(' ', '\t', ':');
            switch (text.ToLowerInvariant())
            {
                case "new features": case "new": return ReleaseNotesCategory.NewFeatures;
                case "bug fixes": case "fixes": case "bugs": return ReleaseNotesCategory.BugFixes;
                case "refinements": case "improvements": return ReleaseNotesCategory.Refinements;
                default: return null;
            }
        }

        private static string StripInlineMarkdown(string text)
        {
            var result = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1", RegexOptions.CultureInvariant);
            result = Regex.Replace(result, @"\*\*([^*]+)\*\*", "$1", RegexOptions.CultureInvariant);
            result = Regex.Replace(result, @"`([^`]+)`", "$1", RegexOptions.CultureInvariant);
            return result.Trim();
        }
    }
}
