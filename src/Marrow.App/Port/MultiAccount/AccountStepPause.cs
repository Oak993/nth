using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

// The provider is suspended inside its own tool loop; a timeline notification never creates this proof.
internal sealed record AccountStepPause(ProviderSession Session, long Epoch, Guid Turn);
