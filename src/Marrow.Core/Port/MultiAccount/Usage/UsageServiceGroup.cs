// PORT-ONLY: usage service groups :4308-4310.
using Marrow.Core.Models;
namespace Marrow.Core.Port.MultiAccount.Usage;
public sealed record UsageServiceGroup(ProviderKind Service, string Title, IReadOnlyList<UsageAccountRow> Rows);
