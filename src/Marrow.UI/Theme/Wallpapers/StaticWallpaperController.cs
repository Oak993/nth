using Avalonia;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The approved Viewer target/tick/state, separated from animated playback and its uniforms.</summary>
internal sealed class StaticWallpaperController(StaticWallpaperData data)
{
    private float[] palette = new float[18], from = new float[18], targetPalette = new float[18];
    private double paletteProgress = 1;
    private double paletteDuration = .55;
    private bool paletteEaseOut;
    // dark, grain, option, focus, Dynamic, hour, perspective, pointer x/y
    private readonly double[] value = new double[9], target = new double[9];
    private static readonly double[] tau = [.16, .09, .09, .09, .16, .16, .16, .18, .18];
    private static readonly double[] epsilon = [.002, .002, .002, .002, .002, .004, .002, .0015, .0015];
    private Vector renderedPointer;
    private bool reduceMotion;
    public bool Busy => paletteProgress < 1 || value.Where((v, i) => v != target[i]).Any();
    public double Grain => value[1];
    public double Option => value[2];

    public bool Target(WallpaperDefinition wall, WallpaperSelection selection, bool dark, WallpaperEnvironment inputs, bool reduced, bool snap)
    {
        var pack = selection.Pack == StaticWallpaperPalette.AccentPack
            ? StaticWallpaperPalette.MatchAccent(data, inputs.Accent.Hex) : wall.Packs.Single(p => p.Id == selection.Pack);
        var pal = WallpaperPalette.For(pack, dark ? 1 : 0);
        if (snap) { palette = pal; targetPalette = (float[])pal.Clone(); paletteProgress = 1; }
        else if (pal.Where((v, i) => Math.Abs(v - targetPalette[i]) > 1e-6).Any())
        {
            paletteEaseOut = paletteProgress < 1; paletteDuration = paletteEaseOut ? .3 : .55;
            from = (float[])palette.Clone(); targetPalette = pal; paletteProgress = 0;
        }
        var options = selection.Options!;
        target[0] = dark ? 1 : 0; target[1] = options["grain"]; target[2] = options["option"];
        target[3] = options["focus"]; target[4] = options["dynamic"];
        // An inactive clock/position seam must not keep the static bake awake.
        if (snap || target[4] > .0005 || value[4] > .0005)
        {
            var hours = inputs.Clock.GetLocalNow() is var now ? now.Hour + now.Minute / 60.0 : 0;
            // sw-page.js's midnight key is 24 when approached from the evening. Real local time must be cyclic too.
            if (!snap)
            {
                while (hours - target[5] > 12) hours -= 24;
                while (hours - target[5] < -12) hours += 24;
            }
            target[5] = hours;
        }
        target[6] = reduced ? 0 : options["persp"];
        var pointer = target[6] > 0 ? new Vector(inputs.Position.Tilt.X, inputs.Position.Tilt.Y) : Vector.Zero;
        target[7] = Math.Clamp(pointer.X, -1, 1); target[8] = Math.Clamp(pointer.Y, -1, 1);
        var forceBake = reduced && !reduceMotion && value[6] > 0 && (value[7] != 0 || value[8] != 0);
        reduceMotion = reduced;
        if (snap) target.CopyTo(value, 0);
        if (reduced) { value[6] = target[6] = value[7] = target[7] = value[8] = target[8] = 0; }
        return forceBake;
    }

    public void Settle()
    {
        targetPalette.CopyTo(palette, 0); paletteProgress = 1; target.CopyTo(value, 0);
    }

    public bool Advance(double seconds, int pixelHeight)
    {
        var changed = false;
        if (paletteProgress < 1)
        {
            paletteProgress = Math.Min(1, paletteProgress + seconds / paletteDuration);
            var k = paletteEaseOut ? 1 - Math.Pow(1 - paletteProgress, 3) : StaticWallpaperPalette.Smooth(paletteProgress);
            for (var i = 0; i < 18; i++) palette[i] = paletteProgress == 1 ? targetPalette[i]
                : (float)(from[i] + ((double)targetPalette[i] - from[i]) * k);
            changed = true;
        }
        for (var i = 0; i < value.Length; i++) if (value[i] != target[i])
        {
            value[i] += (target[i] - value[i]) * (1 - Math.Exp(-seconds / tau[i]));
            if (Math.Abs(value[i] - target[i]) < epsilon[i]) value[i] = target[i];
            if (i < 7) changed = true;
        }
        var travel = (new Vector(value[7], value[8]) - renderedPointer).Length * .015 * data.Depth * pixelHeight / 2 * value[6];
        if (!reduceMotion && value[6] > 0 && travel > 1.0 / 16) changed = true;
        return changed;
    }

    public StaticWallpaperState State(int pixelHeight)
    {
        var dynamic = StaticWallpaperPalette.Dynamic(data, palette, StaticWallpaperPalette.Smooth(value[0]), value[5], value[4]);
        var pointer = new Vector(value[7], value[8]); renderedPointer = pointer;
        if (pointer.Length > 1) pointer /= pointer.Length;
        var amplitude = reduceMotion ? 0 : .015 * data.Depth * value[6];
        var focus = StaticWallpaperPalette.Smooth(Math.Clamp(value[3], 0, 1));
        var sigma = !data.OwnFocus && focus > .004 ? focus * .022 * pixelHeight : 0;
        return dynamic with { Palette = (float[])dynamic.Palette.Clone(), Tilt = new(pointer.X * amplitude, -pointer.Y * amplitude),
            Focus = data.OwnFocus ? focus : 0, Sigma = sigma, Dream = sigma > 0 ? focus : 0 };
    }
}
