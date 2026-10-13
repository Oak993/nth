using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-20")]
public sealed class WebReviewLiveAnswerStateTests(HeadlessSession headless)
{
    [Fact]
    public void The_debounce_tick_saves_the_readers_state_before_stopping_its_timer() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var live = new ToolLiveView { Id = "review", Complete = true, Version = 1,
            Text = "card \"Review\"\n  state instances = 3\n  stepper instances min=1 max=24\n" };
        h.Main.OnMain(() =>
        {
            h.Runtime.Rehearse(new ProviderEvent.ToolStarted("live-review", new ToolCall { ToolCallKind = ToolCall.Kind.Other,
                Title = "Live answers", Payload = new ToolPayload { LiveView = live } }));
            h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-review", new ToolUpdate { Status = ToolCall.Status.Completed }));
        });
        var entry = h.Main.OnMain(() => h.Runtime.Entries.Single(e => e.Id == "live-review"));
        var row = new LiveAnswerRow(entry, h.Runtime);
        var window = Show(row);
        try
        {
            row.View!.LoadState("{\"instances\":12}");
            // Fire the actual timer callback deterministically; no wall-clock wait or provider.
            h.Main.OnMain(() => Invoke(row, "OnSaveTick", null!, EventArgs.Empty));
            Assert.Equal("{\"instances\":12}", ThreadRuntime.LiveViewIn(entry)!.State);
            Assert.Equal(1, ThreadRuntime.LiveViewIn(entry)!.Version);
        }
        finally { Close(window); }
    });
}
