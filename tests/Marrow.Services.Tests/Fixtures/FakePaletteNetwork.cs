// PORT-ONLY: fake-only palette pipeline fixture, separated for unchanged traceability guards.
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.Services.Tests.Fixtures;
internal sealed class FakePaletteNetwork : IPaletteNetworkStatus
    {public event Action<bool>? AvailabilityChanged;public void Raise(bool available)=>AvailabilityChanged?.Invoke(available);}
