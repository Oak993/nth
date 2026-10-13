// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2 "Never stored"; unit AS-12; ED-NEW-dextra-as12-5). No Swift twin.
// The one gate every note passes before it is written, whatever route it took (a tool call, the background capture, an import,
// an edit, the owner typing it): "Secrets: keys, passwords, card numbers and one-time codes are caught by the privacy-shield
// patterns before a note is written." A note that holds one is refused whole, not stored with the secret masked.
using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public static partial class MemoryGuard
{
    // A long unbroken run of letters and digits that mixes upper case, lower case and a digit is a key, whatever its prefix.
    [GeneratedRegex(@"(?<![A-Za-z0-9+/=])(?=[A-Za-z0-9+/=]*[A-Z])(?=[A-Za-z0-9+/=]*[a-z])(?=[A-Za-z0-9+/=]*[0-9])[A-Za-z0-9+/=]{24,}(?![A-Za-z0-9+/=])", RegexOptions.CultureInvariant, 250)]
    private static partial Regex KeyLikeRun();

    // 32 or more hexadecimal digits in one run: a hash, a key or a token.
    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])", RegexOptions.CultureInvariant, 250)]
    private static partial Regex HexRun();

    /// <summary>
    /// Whether the text holds a secret: anything the privacy shield (<see cref="FeedShield"/>) or the activity log's masks
    /// (<see cref="AuditRedactor"/>) would change, or a key-like run neither names. Text they cannot finish on counts as holding one.
    /// </summary>
    public static bool HoldsSecret(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (FeedShield.Mask(text) != text) return true;
        if (AuditRedactor.Redact(text) != text) return true;
        try { return KeyLikeRun().IsMatch(text) || HexRun().IsMatch(text); }
        catch (RegexMatchTimeoutException) { return true; }
    }
}
