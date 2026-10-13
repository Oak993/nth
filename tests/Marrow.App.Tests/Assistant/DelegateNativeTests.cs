using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.UI.Port;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant;

// Owed native checks (never run by the unit agent; the lead runs them alone, with focus, and no other typing): DESIGN.md rev 5, 4.3 "has an Override button" and the lean
// rules' new-control items for AS-22. On a real Win32 window with a real keyboard: Override opens its field and the field holds keyboard focus; Escape closes the field and
// focus goes back to Override (not to the page); a typed correction and Enter send it, and the row then reads Corrected with focus on the row, never on a button that is gone.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-11")]
public sealed class DelegateNativeTests(WindowManagerNativeSession native)
{
    private const byte VkReturn = 0x0D;
    private const byte VkEscape = 0x1B;
    private const byte VkT = 0x54;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    private static void Tap(byte key)
    {
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, KeyUp, 0);
        Thread.Sleep(250);
    }

    [Fact]
    public void On_a_real_keyboard_Override_opens_a_focused_field_Escape_returns_to_it_and_Enter_sends()
    {
        native.Run(_ =>
        {
            FakeAnswered source = null!;
            AnsweredList list = null!;
            var window = native.OnUi(() =>
            {
                source = new FakeAnswered();
                source.Add(FakeAnswered.Record());
                list = new AnsweredList(source);
                var host = new Avalonia.Controls.Window { Width = 640, Height = 420, ShowInTaskbar = false };
                native.Register(host);
                host.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(host));
                host.Content = list;
                host.Show();
                host.Activate();
                return host;
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: the test window has no HWND."), native.ThreadId));
            var row = native.OnUi(() => list.Rows.Single());

            native.OnUi(() => row.OverrideButton!.Focus(NavigationMethod.Tab));
            N.Until(() => native.OnUi(() => row.OverrideButton!.IsFocused), "Override never took focus");
            Tap(VkReturn);                                                                         // Enter on the focused button opens the field
            N.Until(() => native.OnUi(() => row.Field!.IsFocused), "the correction field never took focus");

            Tap(VkEscape);
            N.Until(() => native.OnUi(() => row.OverrideButton!.IsFocused), "Escape did not return focus to Override");
            Assert.False(native.OnUi(() => row.FieldOpen));

            Tap(VkReturn);
            N.Until(() => native.OnUi(() => row.Field!.IsFocused), "the field did not open again");
            Tap(VkT);
            Tap(VkReturn);
            N.Until(() => native.OnUi(() => source.Overrides.Count == 1), "Enter never sent the correction");
            Assert.Equal("t", native.OnUi(() => source.Overrides[0].Correction));
            N.Until(() => native.OnUi(() => list.Rows.Single().OverrideButton is null), "the row never read Corrected");
            Assert.IsNotType<Button>(native.OnUi(() => window.FocusManager?.GetFocusedElement()));
        }, nameof(On_a_real_keyboard_Override_opens_a_focused_field_Escape_returns_to_it_and_Enter_sends), typeof(DelegateNativeTests));
    }
}
