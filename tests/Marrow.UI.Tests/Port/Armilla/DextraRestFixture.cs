using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using static Marrow.UI.Tests.Port.Armilla.DextraSmoothRestTests;

namespace Marrow.UI.Tests.Port.Armilla;
internal sealed class DextraRestScene : IDisposable
{
    internal readonly ManualTimeProvider Clock = new();
    private readonly IDisposable time;
    private readonly DextraRestDisplayPeriod display;
    private readonly int hz;
    internal readonly ArmillaMark Mark = new() { MarkSize = 176, Kind = ArmillaKind.Live, State = ArmillaState.Working,
        Nodes = Enum.GetValues<ArmillaShape>().Select(s => new ArmillaNode(s, true)).ToArray() };
    internal readonly Window Window;
    private readonly Window peer;
    internal DextraRestScene(int hz)
    {
        this.hz = hz; time = SwiftClock.Provider.Override(Clock); display = new DextraRestDisplayPeriod(hz);
        Window = new Window { Width = 176, Height = 176, Background = Brushes.Black, Content = Mark };
        peer = new Window { Width = 24, Height = 24, Content = new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Live } };
        Window.Show(); peer.Show();
        ArmillaWindowWatch.For(Window).Focused(true); ArmillaWindowWatch.For(peer).Focused(true);
        for (var i = 0; i < 8; i++) Frame();
    }
    internal void Send(object message) => Visual(Mark).SendHandlerMessage(message);
    internal void Repaint() => Send(ArmillaHandler.Shown.Yes);
    internal byte[] Frame() { Pump(Clock, hz); return Pixels(Window); }
    public void Dispose() { Window.Close(); peer.Close(); Pump(Clock, hz); display.Dispose(); time.Dispose(); }
}

internal sealed class DextraRestDisplayPeriod : IDisposable
{
    private readonly Func<TimeSpan?> previous = RenderFrameSource.PeriodSource;
    internal DextraRestDisplayPeriod(int hz) => RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / hz);
    public void Dispose() => RenderFrameSource.PeriodSource = previous;
}
