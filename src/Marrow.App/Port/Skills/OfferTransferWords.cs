// PORT-ONLY: RR-44 transfer counts, English file-size words. ED-198 / SwiftFormatters.cs:479-560.
using System.Globalization;

namespace Marrow.App.Port.Skills;

/// <summary>The English file-byte rules used by this English surface, without loading the Windows locale runtime.</summary>
internal static class OfferTransferWords
{
    private static readonly long[] Ceilings = [999, 999_499, 999_949_999, 999_994_999_999, 999_994_999_999_999, long.MaxValue];
    private static readonly string[] Units = ["bytes", "kB", "MB", "GB", "TB", "PB"];

    public static string Bytes(long bytes)
    {
        bytes = Math.Max(0, bytes);
        if (bytes == 0) return "Zero kB";
        if (bytes == 1) return "1 byte";
        var unit = 0;
        while (unit < Ceilings.Length - 1 && bytes >= Ceilings[unit]) unit++;
        var digits = unit < 2 ? 0 : unit == 2 ? 1 : 2;
        var value = Math.Round(bytes / Math.Pow(1000, unit), digits, MidpointRounding.ToEven);
        var format = digits == 0 ? "#,##0" : digits == 1 ? "#,##0.#" : "#,##0.##";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
