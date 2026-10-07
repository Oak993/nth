// PORT-ONLY: T11 step 2. The Handshake point slider of the pill editor, in the effort slider's language: the app's own EffortSlider (a glass tube with the step's liquid tint, the jewel knob, the spring settle) over 21 stops, Smart then 80% to 99%,
// with its value beside it ("Handshake Smart", "Handshake 95%"). When the pill has one point for every step the step sliders are locked: the same slider, dimmed and out of the focus chain, with the reason as its tooltip.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
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
    private readonly CzCrossfadeText value = new("") { FontSize = 11, Secondary = true };
    private readonly EffortSlider slider;
    private readonly AnimatedScalar dim;
    private bool locked;

    internal HandshakeSliderControl(Func<HandshakePoint> read, Action<HandshakePoint> write, string title, EffortBrand brand, ModelPulse pulse)
    {
        this.read = read;
        slider = new EffortSlider(HandshakeStops.Count, new Binding<int>(() => { _ = pulse.Revision; return HandshakeStops.IndexOf(read()); }, i => write(HandshakeStops.At(i))), title, false, brand, compactHandshake: true);
        slider.Focusable = true;
        slider.KeyDown += (_, e) =>
        {
            if (locked || e.KeyModifiers != KeyModifiers.None) return;
            var at = HandshakeStops.IndexOf(read());
            int? next = e.Key switch
            {
                Key.Right or Key.Up => Math.Min(HandshakeStops.Count - 1, at + 1),
                Key.Left or Key.Down => Math.Max(0, at - 1),
                Key.Home => 0,
                Key.End => HandshakeStops.Count - 1,
                _ => null,
            };
            if (next is { } index) { write(HandshakeStops.At(index)); e.Handled = true; }
        };
        slider.Width = 112;
        slider.VerticalAlignment = VerticalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.SetText(Text());
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, value } };
        Child = row;
        dim = new AnimatedScalar(this, 1, v => row.Opacity = v);
        this.Track(() => { _ = pulse.Revision; return Text(); }, text => value.SetText(text));
    }

    internal string ValueShown => value.Text;

    internal EffortSlider Slider => slider;

    internal bool Locked => locked;

    internal double Dimness => dim.Value;

    private bool showPrefix = true;
    internal bool ShowPrefix { get => showPrefix; set { showPrefix = value; this.value.SetText(Text()); } }
    private string Text() => showPrefix ? MultiAccountWords.HandshakeText(HandshakeStops.Text(read())) : HandshakeStops.Text(read());

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
