// PORT-ONLY: T11 step 2. The Handshake point as the page offers it: a slider of Smart, then 80% to 99% (21 stops), and the menu's five points. HandshakePoint (Core.Port.Accounts) is the stored value.
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Accounts;

namespace Marrow.Core.Port.MultiAccount.Settings;

public static class HandshakeStops
{
    public static readonly IReadOnlyList<int> MenuPoints = [90, 95, 97, 98, 99];

    public static int Count => 21;

    public static HandshakePoint At(int index) => index <= 0 ? HandshakePoint.Smart : new HandshakePoint(79 + Math.Min(index, 20));

    public static int IndexOf(HandshakePoint point) => point.Percentage is { } percent ? Math.Clamp(percent - 79, 1, 20) : 0;

    public static string Text(HandshakePoint point) => point.Percentage is { } percent ? percent.ToString(CultureInfo.InvariantCulture) + "%" : "Smart";

    /// <summary>"Automatically near N% on this plan": the engine's own plan-aware start (a 5x, 10x or 20x plan 98%, others 95%), so what Settings says is what the Handshake does.</summary>
    public static int PlanStart(string? plan) => plan is { Length: > 0 } && Regex.IsMatch(plan, @"(?<!\d)(5|10|20)\s*[x×](?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? 98 : 95;

    public static string NearText(string? plan) => MultiAccountWords.Near(PlanStart(plan));
}
