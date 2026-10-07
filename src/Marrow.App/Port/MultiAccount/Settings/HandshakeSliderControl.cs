// PORT-ONLY: T11 step 2. The Handshake point slider of the pill editor, in the effort slider's language: the app's own EffortSlider (a glass tube with the step's liquid tint, the jewel knob, the spring settle) over 21 stops, Smart then 80% to 99%,
// with its value beside it ("Handshake Smart", "Handshake 95%"). When the pill has one point for every step the step sliders are locked: the same slider, dimmed and out of the focus chain, with the reason as its tooltip.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class HandshakeSliderControl : Decorator
{
    private readonly Func<HandshakePoint> read;
    private readonly CzCrossfadeText value = new("") { FontSize = 12, Secondary = true };
    private readonly EffortSlider slider;
    private readonly AnimatedScalar dim;
    private bool locked;

    internal HandshakeSliderControl(Func<HandshakePoint> read, Action<HandshakePoint> write, string title, EffortBrand brand, ModelPulse pulse)
    {
        this.read = read;
        slider = new EffortSlider(HandshakeStops.Count, new Binding<int>(() => { _ = pulse.Revision; return HandshakeStops.IndexOf(read()); }, i => write(HandshakeStops.At(i))), title, false, brand);
        slider.Width = 220;
        slider.VerticalAlignment = VerticalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.SetText(Text());
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slider, value } };
        Child = row;
        dim = new AnimatedScalar(this, 1, v => row.Opacity = v);
        this.Track(() => { _ = pulse.Revision; return Text(); }, text => value.SetText(text));
    }

    internal string ValueShown => value.Text;

    internal EffortSlider Slider => slider;

    internal bool Locked => locked;

    internal double Dimness => dim.Value;

    private string Text() => MultiAccountWords.HandshakeText(HandshakeStops.Text(read()));

    /// <summary>Locks or frees the slider: dimmed over 0.3 s, not focusable, no pointer.</summary>
    internal void Lock(bool on, string? reason)
    {
        if (locked == on) return;
        locked = on;
        slider.IsHitTestVisible = !on;
        slider.Focusable = !on;
        dim.Set(on ? 0.45 : 1, LibraryUi.Ease(this, LibraryUi.Fade));
        ToolTip.SetTip(this, on ? reason : null);
    }
}
