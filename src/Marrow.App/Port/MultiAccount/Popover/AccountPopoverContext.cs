// PORT-ONLY: MA-3b-4, account dialog dependencies, kept in App; T15's Core models are read-only to this job.
using Marrow.Core.Port.Accounts;
using Marrow.Core.Models;
namespace Marrow.App.Port.MultiAccount.Popover;
internal sealed record AccountPopoverContext(AccountStore Store, Func<ProviderKind, IReadOnlyList<ModelOption>> Models);
