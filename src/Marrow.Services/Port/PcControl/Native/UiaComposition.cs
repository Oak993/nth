// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-44; DESIGN.md 2.7 "IME composition", critique C4). The second
// composition signal: UI Automation TextEditPattern.GetActiveComposition on the focused element, where the control supports
// the pattern, read only while the foreground language is served by an IME or a TSF text service. A read, never an action:
// it neither focuses nor selects anything. Its own UI Automation client on its own MTA worker (the IME signals' thread is
// STA for TSF, and a hung app must not stall it); each read waits at most 500 ms. Never used by automated tests.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class UiaComposition : IDisposable
{
    private const int TextEditPattern = 10032;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(500);

    private PcWorkerThread worker = new("Tactus composition");
    private IUIAutomation2? client;

    /// <summary>
    /// Whether the focused element of process <paramref name="process"/> has an active composition; null when the focused
    /// element is another process's, does not support the pattern, or could not be read in time.
    /// </summary>
    public async Task<bool?> Open(int process)
    {
        if (worker.Stuck)
        {
            // A hung app keeps the old thread; reads go on from a fresh one.
            worker.Dispose();
            worker = new PcWorkerThread("Tactus composition");
        }
        try
        {
            return await worker.Run(() => Read(process), ReadTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (error is PcUiaTimeoutException or COMException or InvalidCastException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose() => worker.Dispose();

    private bool? Read(int process)
    {
        client ??= Create();
        if (client.GetFocusedElement(out var focused) != 0 || focused is null) return null;
        focused.GetCurrentPropertyValue(UiaIds.ProcessId, out var owner);
        if (owner is not int pid || pid != process) return null;
        if (focused.GetCurrentPattern(TextEditPattern, out var pattern) != 0 || pattern is not IUIAutomationTextEditPattern edit) return null;
        if (edit.GetActiveComposition(out var range) != 0) return null;
        if (range is null) return false;
        return range.GetText(64, out var text) == 0 && !string.IsNullOrEmpty(text);
    }

    private static IUIAutomation2 Create()
    {
        var type = Type.GetTypeFromCLSID(UiaIds.CUIAutomation8, throwOnError: true)!;
        var created = (IUIAutomation2)Activator.CreateInstance(type)!;
        created.put_ConnectionTimeout(500);
        created.put_TransactionTimeout(500);
        return created;
    }
}

// IUIAutomationTextEditPattern: IUIAutomationTextPattern's six methods, then GetActiveComposition and GetConversionTarget.
[ComImport, Guid("17E21576-996C-4870-99D9-BFF323380C06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextEditPattern
{
    void Slot01_RangeFromPoint();
    void Slot02_RangeFromChild();
    void Slot03_GetSelection();
    void Slot04_GetVisibleRanges();
    void Slot05_get_DocumentRange();
    void Slot06_get_SupportedTextSelection();
    [PreserveSig] int GetActiveComposition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationTextRange? range);
    void Slot08_GetConversionTarget();
}
