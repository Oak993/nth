using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

internal sealed class DeferredPopoverMotion : IPopoverMotion
{
    internal int Opens { get; private set; }
    internal int Closes { get; private set; }
    internal Action? Finished { get; private set; }
    public void Open(Control content) => Opens++;
    public Action Close(Control content, Action closed) { Closes++; Finished = closed; return () => Finished = null; }
}
