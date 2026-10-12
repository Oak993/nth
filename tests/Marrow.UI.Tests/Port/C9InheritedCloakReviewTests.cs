using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-c9b-1: the C9 repro now reads the real LookAt classification through fake cloak/owner readers.
// No native calls: both looks are cloaked, so LookAt never needs the native covering-window walk.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class C9InheritedCloakReviewTests(HeadlessSession headless)
{
    [Fact]
    public void An_owned_window_inheriting_its_shell_cloaked_owners_cloak_is_not_seen() => headless.OnUiThread(() =>
    {
        const nint OwnerHandle = 0x7C01, ChildHandle = 0x7C02;
        var owner = new Window { Width = 80, Height = 80, Background = Brushes.Black, Content = new Border() };
        var child = new Window { Width = 80, Height = 80, Background = Brushes.Black, Content = new Border() };
        var hub = new AmbientWindowState.EventHub((_, _, _, _) => 1, _ => { }, _ => true,
            _ => throw new InvalidOperationException("this fake presence supplies its own look"));
        int? Read(nint handle) => handle == OwnerHandle ? 0x2 : 0x4;
        nint OwnerOf(nint handle) => handle == ChildHandle ? OwnerHandle : 0;
        WindowPresence.TestFactory = window => new WindowPresence(window, hub,
            ReferenceEquals(window, owner) ? OwnerHandle : ChildHandle,
            () => AmbientWindowState.EventHub.LookAt(ReferenceEquals(window, owner) ? OwnerHandle : ChildHandle, Read, OwnerOf));
        try
        {
            owner.Show(); child.Show(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(owner, child.Owner);
            Assert.False(WindowPresence.For(owner)!.Seen);
            Assert.False(WindowPresence.For(child)!.Seen, "an inherited cloak does not establish the app-only warm-up exemption");
        }
        finally
        {
            child.Close(); owner.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null;
        }
    });
}
