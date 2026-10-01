using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ComposerView.swift:831-834 Image(systemName: isRunning ? "stop.fill" : "arrow.up").contentTransition(.symbolEffect(.replace)):
// when a turn starts the arrow goes and the stop square grows in (SymbolReplace; measured 2-chat-use.mov 87.75-88.60 s).
[Collection("Headless")]
public sealed class SendSymbolReplaceTests(HeadlessSession headless)
{
    private static string[] Symbols(Control root) =>
        root.GetLogicalDescendants().OfType<SwiftImage>().Select(image => image.SymbolName ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-COMPOSER-096")]
    [Trait("Req", "UI-COMPOSER-016")]
    public void When_a_turn_starts_the_arrow_is_replaced_by_the_stop_square_in_two_steps() => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
            var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
            var unchanged = new SendDraftState(h.Runtime, true, false, false, false, () => { });
            Assert.Equal(["stop.fill"], Symbols(unchanged)); // first build: nothing to replace

            var replacing = new SendDraftState(h.Runtime, true, false, false, false, () => { }, previousSymbol: "arrow.up");
            Assert.Equal(["stop.fill", "arrow.up"], Symbols(replacing)); // the old arrow over the new square while it goes
            clock.Advance(TimeSpan.FromSeconds(0.4)); frames.Advance(TimeSpan.FromSeconds(0.4));
            Assert.Equal(["stop.fill"], Symbols(replacing));
        });
    });
}
