// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1, 7.2 "App event"; unit AS-10; ED-NEW-dextra-as10-11). No Swift twin.
// Turns what the app already says on the live feed, and what its missions do, into the few events a duty can wait for: a chat
// finished, a head failed, a usage window reset, an account above a share, a mission finished. The feed's usage lines are built by
// AccountWindowWatcher in fixed words, so the account, the window and the share are read back from those words; a test feeds the
// real watcher's lines through here, so a change in its wording fails a test and not a duty.
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public static partial class DutyEvents
{
    [GeneratedRegex(@"^(?<account>.+?): (?<window>.+?) reset, from (?<from>\d+)% to (?<to>\d+)%$", RegexOptions.CultureInvariant)]
    private static partial Regex Reset();

    [GeneratedRegex(@"^(?<account>.+?): (?<window>.+?) at (?<percent>\d+)%, (?:up|down) \d+%$", RegexOptions.CultureInvariant)]
    private static partial Regex At();

    [GeneratedRegex(@"^(?<account>.+?): usage limit reached", RegexOptions.CultureInvariant)]
    private static partial Regex Limit();

    /// <summary>The duty event a feed line stands for, or null when no duty can wait for it.</summary>
    public static DutyEvent? FromFeed(FeedEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.Kind)
        {
            case FeedEventKind.TurnFinished:
                return new DutyEvent(AppEventKind.ChatFinished, e.Summary) { Mission = e.MissionId };
            case FeedEventKind.HeadFinished when e.Severity == FeedSeverity.Error:
                return new DutyEvent(AppEventKind.HeadFailed, e.Summary) { Mission = e.MissionId };
            case FeedEventKind.AccountWindowReset when Reset().Match(e.Summary) is { Success: true } reset:
                return new DutyEvent(AppEventKind.WindowReset, e.Summary) { Account = reset.Groups["account"].Value, Window = reset.Groups["window"].Value };
            case FeedEventKind.AccountWindow when At().Match(e.Summary) is { Success: true } at:
                return new DutyEvent(AppEventKind.AccountAbove, e.Summary)
                {
                    Account = at.Groups["account"].Value,
                    Window = at.Groups["window"].Value,
                    Percent = double.Parse(at.Groups["percent"].Value, CultureInfo.InvariantCulture),
                };
            case FeedEventKind.AccountLimitReached when Limit().Match(e.Summary) is { Success: true } limit:
                return new DutyEvent(AppEventKind.AccountAbove, e.Summary) { Account = limit.Groups["account"].Value, Percent = 100 };
            default:
                return null;
        }
    }

    /// <summary>A mission reached Done: the event "a mission finished". A duty's own missions are told apart by the scheduler.</summary>
    public static DutyEvent MissionFinished(MissionRecord record) =>
        new(AppEventKind.MissionFinished, $"“{record.Mission.Title}” finished") { Mission = record.Id };
}
