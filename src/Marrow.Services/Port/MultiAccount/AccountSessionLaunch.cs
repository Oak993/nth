using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed record AccountSessionLaunch(SessionConfiguration Configuration, ProviderSession Session);
