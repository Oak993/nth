// PORT-ONLY: Dextra (DESIGN.md rev 5, 10; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The few words and the one clock format that the recorder, the draft and the views share.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class TeachWords
{
    /// <summary>What a password, a key or a card number is recorded as. It is the same mask the feed and the activity log use.</summary>
    public const string Secret = "[secret]";

    /// <summary>"2:14", or "1:02:14" from an hour on: a position in a recording.</summary>
    public static string Clock(TimeSpan time)
    {
        var whole = (long)Math.Max(0, Math.Floor(time.TotalSeconds));
        var hours = whole / 3600;
        var minutes = whole / 60 % 60;
        var seconds = whole % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}
