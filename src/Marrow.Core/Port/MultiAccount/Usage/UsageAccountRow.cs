// PORT-ONLY: Usage B :4297-4346, current and grouped account data.
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Popover;
namespace Marrow.Core.Port.MultiAccount.Usage;
public sealed record UsageAccountRow(ThreadAccount Account, bool Active, bool Dimmed, string Summary,
    PopoverUsageBar FiveHour, PopoverUsageBar Week, string FiveHourReset, string WeekReset, string? NoFiveHourText);
