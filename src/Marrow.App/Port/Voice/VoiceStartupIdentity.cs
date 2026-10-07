// PORT-ONLY: VT-15, V-10.5. Read only the key fields once at startup; a valid timing record does not require loading the VAD or the speech engine again.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

internal static class VoiceStartupIdentity
{
    internal static HardwareKeyInputs? Read(IHardwareSource source, string app, string runtime)
    {
        try
        {
            var adapter = source.Adapters().Where(a => !a.IsSoftware).OrderByDescending(a => a.DedicatedBytes).FirstOrDefault();
            return new HardwareKeyInputs(adapter?.Description ?? "", adapter is null ? "" : source.DriverVersion(adapter),
                source.ProcessorName().Trim(), (int)(source.TotalPhysicalBytes() / (1L << 30)), app, runtime);
        }
        catch (Exception) { return null; } // an unavailable key leaves preparation stale; the next probe reports the actual failure
    }
}
