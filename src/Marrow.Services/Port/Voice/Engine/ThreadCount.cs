// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): whisper.cpp threads (V-2.2).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>V-2.2: whisper.cpp threads = <c>max(1, physicalCores - 2)</c> (RESEARCH 5).</summary>
public static class ThreadCount
{
    public static int For(int physicalCores) => Math.Max(1, physicalCores - 2);
}
