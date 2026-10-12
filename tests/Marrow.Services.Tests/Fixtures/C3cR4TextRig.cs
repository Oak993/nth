using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Fixtures;

// Engine and R4 on managed fakes: each Unicode array updates the app's live field.
internal sealed class C3cR4TextRig
{
    public PcBorrowRig Borrow { get; } = new PcBorrowRig().ProveAll();
    public PcEngineHarness Harness { get; } = new() { Settings = PcSettings.Default with { Enabled = true, RealInput = true } };
    public PcFakeApp App { get; }
    public PcFakeNode Field { get; }

    public C3cR4TextRig(bool growing = false)
    {
        App = Harness.Notepad(PcBorrowRig.Target, 42, @"c:\windows\mspaint.exe");
        Field = App.Root.Children[0];
        Field.NativeWindow = PcBorrowRig.TargetControl;
        Harness.OverrideLease = Borrow.Lease;
        Harness.OverrideLadder = new PcActionLadder([new PcBorrowRung(() => Harness.Settings, Borrow.Lease, Harness.Desktop)], Borrow.Capabilities);
        Borrow.Host.OnSend = events =>
        {
            var text = new string(events.Where(e => (e.Flags & PcInputPlan.KEY_UNICODE) != 0 && (e.Flags & PcInputPlan.KEY_UP) == 0).Select(e => (char)e.Scan).ToArray());
            if (text.Length == 0) return;
            Field.Value += text;
            if (growing) Field.Bounds = Field.Bounds with { Height = Field.Bounds.Height + 20 };
            Harness.Desktop.Bump(App.Handle);
        };
    }

    // These healthy immediate borrows need no fake time: callers advance only the explicit closed gate's poll.
    public Task<PcResult> Drive(Task<PcResult> action) => action.WaitAsync(TimeSpan.FromSeconds(5));
}
