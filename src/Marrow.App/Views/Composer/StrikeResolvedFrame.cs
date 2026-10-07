namespace Marrow.App.Views.Composer;

// Ephemeral frame storage, consumed under the liquid's render lock. No inspection copies on the frame path.
public sealed class StrikeResolvedFrame
{
    public StrikeMaskQueue Masks { get; } = new();
    public StrikeLight[] Light { get; } = new StrikeLight[23];
    public StrikeLit[] Lit { get; } = new StrikeLit[175];
    public int LightCount { get; internal set; }
    public int LitCount { get; internal set; }
    public double Opacity { get; internal set; }
    public double LightOpacity { get; internal set; }
    internal void Clear() { Masks.Clear(); LightCount = LitCount = 0; }
    internal void AddLight(StrikeLight light)
    {
        if (LightCount == Light.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike light capacity exceeded");
#else
            return;
#endif
        }
        Light[LightCount++] = light;
    }
    internal void AddLit(StrikeLit lit)
    {
        if (LitCount == Lit.Length)
        {
#if DEBUG
            throw new InvalidOperationException("Strike lit capacity exceeded");
#else
            return;
#endif
        }
        Lit[LitCount++] = lit;
    }
}
