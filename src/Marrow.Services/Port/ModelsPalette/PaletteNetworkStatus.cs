// PORT-ONLY: AC-3 network trigger seam, captured at construction; ED-617.
using System.Net.NetworkInformation;
namespace Marrow.Services.Port.ModelsPalette;
public interface IPaletteNetworkStatus {event Action<bool>? AvailabilityChanged;}
internal sealed class PaletteNetworkStatus : IPaletteNetworkStatus,IDisposable
{
    public event Action<bool>? AvailabilityChanged;
    public PaletteNetworkStatus()=>NetworkChange.NetworkAvailabilityChanged+=Changed;
    private void Changed(object? sender,NetworkAvailabilityEventArgs e)=>AvailabilityChanged?.Invoke(e.IsAvailable);
    public void Dispose()=>NetworkChange.NetworkAvailabilityChanged-=Changed;
}
