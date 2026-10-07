// PORT-ONLY: MA-3b-12. W supplies thread actions and its recorded handoff, never the Settings example.
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
namespace Marrow.App.Port.MultiAccount.Cards;
internal sealed record MultiAccountCardContext(Func<Task>? SwitchNow, Func<HandedOverExample?>? HandedOver, TimeZoneInfo? Zone = null);
