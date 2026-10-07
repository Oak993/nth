// PORT-ONLY: AccountPanel :2687-2718, plain row values for job 2.
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Shared;
namespace Marrow.Core.Port.MultiAccount.Popover;
public sealed record AccountPopoverRow(ThreadAccount Account, bool Active, bool Pinned, string? Chip, bool Dimmed, string ResetText,
    PopoverUsageBar FiveHour, PopoverUsageBar Week, string? NoFiveHourText, string? LocalText, AccountConfigText Config, string HandshakeText, string HandshakeAria);
