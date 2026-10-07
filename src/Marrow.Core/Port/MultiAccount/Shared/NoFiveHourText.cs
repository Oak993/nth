// PORT-ONLY: multi-account steps 3b-5, job 0. "No 5-hour limit" (multi-account.html .no5t) is a fact the service reports (AccountUsageLine.NoFiveHour), never a guess from the plan's name.
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Port.MultiAccount.Shared;

public static class NoFiveHourText
{
    /// <summary>The page's words when the usage line flags an account with no 5-hour window; null when it does not (or when nothing was reported).</summary>
    public static string? For(AccountUsageLine? usage) => usage is { NoFiveHour: true } ? MultiAccountWords.NoFiveHour : null;
}
