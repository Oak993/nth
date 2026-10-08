using Marrow.Services.Port.ModelsPalette;

namespace Marrow.App.Port.ModelsPalette;

// T44: begin validating the immutable pack while the desktop bindings are initialized.
internal sealed class PaletteStartupWork(Func<PaletteReleasePack> read)
{
    private readonly Lazy<Task<PaletteReleasePack>> pending = new(() => Task.Run(read));
    internal void Begin() => _ = pending.Value;
    internal PaletteReleasePack Finish() => pending.Value.GetAwaiter().GetResult();
    internal Task<PaletteReleasePack> FinishAsync() => pending.Value;
}
