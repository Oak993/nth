// PORT-ONLY CZ-1: retargetable premultiplied sRGB token/colour transition (CZ-14.1, same mixing as ED-250).
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzColor
{
    private readonly CzNumber progress;
    private readonly Action<Color> changed;
    private Color from;
    public Color Value { get; private set; }
    public Color Target { get; private set; }
    public CzColor(CzMotion motion, Color initial, Action<Color> changed)
    {
        Value = Target = initial; this.changed = changed;
        progress = new CzNumber(motion, 1, p => { Value = SwiftColor.PremultipliedMix(from, Target, p); changed(Value); });
    }
    public void To(Color target, Animation animation)
    {
        if (target == Target) return;
        from = Value; Target = target; progress.Jump(0); progress.To(1, animation);
    }
    public void Jump(Color color) { from = Value = Target = color; progress.Jump(1); changed(color); }
}
