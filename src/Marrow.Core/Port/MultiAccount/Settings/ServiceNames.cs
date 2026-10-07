// PORT-ONLY: T11 step 2. The name of a service as the page shows it ("Claude Code", not the model family's "Claude").
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Settings;

public static class ServiceNames
{
    public static string Title(ProviderKind service) => service == ProviderKind.Claude ? "Claude Code" : service.DisplayName;
}
