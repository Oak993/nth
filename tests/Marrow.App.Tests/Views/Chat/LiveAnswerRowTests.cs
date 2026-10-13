using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Web;
using Marrow.Testing;
using Marrow.UI.NthUi;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.1, 7.4; 13.3 "NthUiView: progressive streaming renders"; ED-NEW-web-w24-6). The row the
// timeline shows for a live answer: an nth-ui view that builds while its lines arrive and is replaced with a crossfade when the model shows it
// again, the reader's states kept on the row, a note where a newer view replaced it or the call failed, and an HTML widget as a card that opens
// in Tabula until W19's inline host takes its place. Headless windows, a manual frame clock, a thread runtime on a throwaway store; no provider.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w24-6")]
public sealed class LiveAnswerRowTests(HeadlessSession headless)
{
    private const string Hosting =
        "card \"Hosting estimate\" icon=server\n"
        + "  state instances = 3\n"
        + "  stepper instances label=\"Instances\" min=1 max=24\n"
        + "  stat \"Compute\" = instances * 34\n"
        + "followups\n"
        + "  chip \"Add a CDN\"\n";

    private static ToolLiveView Live(string text = "", bool complete = false, int version = 1, string id = "hosting") =>
        new() { Id = id, Text = text, Complete = complete, Version = version, Title = "Hosting estimate" };

    private static ProviderEvent Start(string id, ToolLiveView view) =>
        new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = "Live answers", Payload = new ToolPayload { LiveView = view } });

    private static ProviderEvent Update(string id, ToolLiveView view, ToolCall.Status? status = null) =>
        new ProviderEvent.ToolUpdated(id, new ToolUpdate { Payload = new ToolPayload { LiveView = view }, Status = status });

    private static TimelineEntry EntryOf(ThreadRuntimePart12Harness h, string id) => h.Main.OnMain(() => h.Runtime.Entries.Single(entry => entry.Id == id));

    private static string[] Texts(Avalonia.Visual root) => [.. Mounted<TextBlock>(root).Select(text => text.Text ?? "")];

    private static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window, ManualFrameSource clock, double milliseconds = 0)
    {
        h.Main.OnMain(() => { });
        if (milliseconds > 0) Animate(h.Main, clock, milliseconds);
        Settle(window);
    }

    [Fact]
    public void A_streaming_row_builds_the_view_while_its_lines_arrive_and_ends_complete() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = (ManualFrameSource)FrameClock.Source.Current;
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live())));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            Assert.NotNull(row.View);
            Assert.Empty(row.View!.Document.Nodes);

            var seen = new List<int>();
            for (var length = 20; length < Hosting.Length + 20; length += 20)
            {
                var text = Hosting[..Math.Min(length, Hosting.Length)];
                h.Main.OnMain(() => h.Runtime.Rehearse(Update("live-1", Live(text))));
                Pump(h, window, clock, 30);
                Assert.Equal(text, row.ShownText);
                seen.Add(row.View.Document.Nodes.Count);
            }
            Assert.True(seen.Distinct().Count() > 3, "the view grew node by node: " + string.Join(",", seen));
            Assert.Equal(seen.Order(), seen); // never fewer than before
            Assert.False(row.View.Document.IsComplete);

            h.Main.OnMain(() => h.Runtime.Rehearse(Update("live-1", Live(Hosting, complete: true), ToolCall.Status.Completed)));
            Pump(h, window, clock, 300);
            Assert.True(row.View.Document.IsComplete);
            Assert.Contains("Hosting estimate", Texts(row));
            Assert.Contains("Add a CDN", Texts(row));
        }
        finally { Close(window); }
    });

    [Fact]
    public void A_saved_row_opens_complete_as_the_reader_left_it_without_arrival_motion() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = (ManualFrameSource)FrameClock.Source.Current;
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live(Hosting, complete: true) with { State = "{\"instances\":8}" })));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-1", new ToolUpdate { Status = ToolCall.Status.Completed })));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            Assert.True(row.View!.Document.IsComplete);
            Assert.Equal(8, row.View.Runtime.Get("instances").AsNumber);
            Assert.False(row.View.IsMoving);
            Assert.Contains("272", Texts(row)); // 8 x 34, drawn from the saved state
        }
        finally { Close(window); }
    });

    [Fact]
    public void A_view_shown_again_replaces_the_old_one_with_a_crossfade_and_keeps_the_readers_choices_and_an_update_of_states_keeps_the_view() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = (ManualFrameSource)FrameClock.Source.Current;
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live(Hosting, complete: true))));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-1", new ToolUpdate { Status = ToolCall.Status.Completed })));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            var first = row.View!;
            first.Runtime.Set("instances", NthUiValue.Of(6));

            // A patch that only sets a state: the same view, the new value.
            h.Main.OnMain(() => h.Runtime.Rehearse(Update("live-1", Live(Hosting, complete: true, version: 2) with { State = "{\"instances\":10}" })));
            Pump(h, window, clock, 50);
            Assert.Same(first, row.View);
            Assert.Equal(10, first.Runtime.Get("instances").AsNumber);

            // The view rewritten: a new view, drawn with the reader's states, the old one fading under it.
            const string Other = "card \"Smaller\"\n  state instances = 1\n  stat \"Count\" = instances\n";
            first.Runtime.Set("instances", NthUiValue.Of(4));
            h.Main.OnMain(() => h.Runtime.Rehearse(Update("live-1", Live(Other, complete: true, version: 3))));
            Pump(h, window, clock, 0);
            Assert.NotSame(first, row.View);
            Assert.Equal(Other, row.ShownText);
            Assert.Equal(4, row.View!.Runtime.Get("instances").AsNumber);
            Assert.Equal(2, Mounted<NthUiView>(row).Count()); // both views are mounted while the swap runs
            Pump(h, window, clock, 400);
            Assert.Single(Mounted<NthUiView>(row));
            Assert.Contains("Smaller", Texts(row));
        }
        finally { Close(window); }
    });

    [Fact]
    public void A_superseded_row_is_a_note_a_failed_row_is_a_calm_line_and_a_row_that_is_neither_is_the_view() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var clock = (ManualFrameSource)FrameClock.Source.Current;
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live(Hosting, complete: true))));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            Assert.NotNull(row.View);
            h.Main.OnMain(() => h.Runtime.Rehearse(Update("live-1", Live(Hosting, complete: true) with { Superseded = true })));
            Pump(h, window, clock);
            Assert.Null(row.View);
            Assert.Contains("Hosting estimate was replaced by a newer version below.", Texts(row));
            Assert.Empty(Mounted<NthUiView>(row));
        }
        finally { Close(window); }

        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-2", Live("", complete: true, id: "x") with { Problem = "No line of the view could be read as a component, so nothing was shown." })));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-2", new ToolUpdate { Status = ToolCall.Status.Failed })));
        var failed = new LiveAnswerRow(EntryOf(h, "live-2"), h.Runtime);
        var failedWindow = Show(failed);
        try
        {
            Assert.Null(failed.View);
            Assert.Contains("This live answer could not be shown. No line of the view could be read as a component, so nothing was shown.", Texts(failed));
        }
        finally { Close(failedWindow); }
    });

    [Fact]
    public void The_readers_states_are_saved_on_the_row_so_the_view_opens_as_they_left_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live(Hosting, complete: true))));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-1", new ToolUpdate { Status = ToolCall.Status.Completed })));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            row.View!.LoadState("{\"instances\":12}"); // stands for the reader moving the stepper: the view reports a state change
            h.Main.OnMain(row.FlushState);
            var saved = ThreadRuntime.LiveViewIn(EntryOf(h, "live-1"))!;
            Assert.Equal("{\"instances\":12}", saved.State);
            Assert.Equal(1, saved.Version); // the reader's own change is not a new version of the view

            // The row opened again from the saved entry shows the state.
            var again = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
            var second = Show(again);
            try { Assert.Equal(12, again.View!.Runtime.Get("instances").AsNumber); }
            finally { Close(second); }
        }
        finally { Close(window); }
    });

    [Fact]
    public void An_html_widget_is_a_card_that_opens_it_in_Tabula_until_the_inline_host_replaces_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        const string Page = "<html><head><title>Pong</title></head><body>go</body></html>";
        var view = new ToolLiveView { Id = "pong", Kind = LiveViewKind.Html, Text = Page, Complete = true, Title = "Pong", Height = 360 };
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", view)));
        var opened = new List<(Guid, string)>();
        using var artifacts = new LiveArtifactScope();
        artifacts.Host.OpenRequested += (thread, id) => opened.Add((thread, id));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            Assert.Null(row.View);
            Assert.Contains("Pong", Texts(row));
            Assert.Contains("This widget runs in a sandbox. Open it in Tabula to use it.", Texts(row));
            var button = Mounted<Button>(row).Single(b => Texts(b).Contains("Open in Tabula"));
            Click(window, button);
            Assert.Equal([(h.Runtime.ThreadID, "widget-pong")], opened);
            var saved = artifacts.Store.Read(h.Runtime.ThreadID, "widget-pong");
            Assert.Equal((ArtifactKind.Html, Page, "Pong"), (saved.Artifact.Kind, saved.Text, saved.Artifact.Title));

            // Pressing it again with the same page adds no version.
            Click(window, button);
            Assert.Equal(1, artifacts.Store.Read(h.Runtime.ThreadID, "widget-pong").Version.Number);
            Assert.Equal(2, opened.Count);
        }
        finally { Close(window); }

        // W19's inline host takes the card's place through the seam.
        var host = new Border { Height = 40 };
        using var seam = LiveAnswerHtml.Host.Override((live, runtime) => live.IsHtml ? host : null);
        var replaced = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var replacedWindow = Show(replaced);
        try { Assert.Same(host, replaced.Child); }
        finally { replacedWindow.Content = null; Close(replacedWindow); }
    });

    [Fact]
    public void What_a_view_asks_of_its_host_goes_to_the_reader_s_links_and_the_chat_and_the_next_message() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() => h.Runtime.Rehearse(Start("live-1", Live(Hosting, complete: true))));
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolUpdated("live-1", new ToolUpdate { Status = ToolCall.Status.Completed })));
        var opened = new List<string>();
        var sent = new List<string>();
        using var link = LiveAnswerEffects.OpenLink.Override(url => { opened.Add(url); return true; });
        using var send = LiveAnswerEffects.Send.Override((_, text) => sent.Add(text));
        var row = new LiveAnswerRow(EntryOf(h, "live-1"), h.Runtime);
        var window = Show(row);
        try
        {
            var view = row.View!;
            Raise(view, new NthUiEffect(NthUiEffectKind.Open, "https://example.com/a"));
            Raise(view, new NthUiEffect(NthUiEffectKind.Send, "Add a CDN"));
            Raise(view, new NthUiEffect(NthUiEffectKind.Call, "", Server: "shipyard", Tool: "promote", Json: "{\"release\":\"r42\"}"));
            Raise(view, new NthUiEffect(NthUiEffectKind.Call, "", Server: "shipyard", Tool: "ping", Json: "{}"));
            Raise(view, new NthUiEffect(NthUiEffectKind.Context, "plan", Json: "{\"region\":\"eu\"}"));
            Raise(view, new NthUiEffect(NthUiEffectKind.Context, "size", Json: "3"));
            Assert.Equal(["https://example.com/a"], opened);
            Assert.Equal(["Add a CDN", "Call the tool shipyard.promote with {\"release\":\"r42\"}.", "Call the tool shipyard.ping."], sent);

            // The context rides with the next message and only that one.
            var carried = (string?)Invoke(h.Runtime, "TakeLiveContext");
            Assert.Equal("The reader's live view carries this with their message (set by their own controls):\n{\"plan\": {\"region\":\"eu\"}, \"size\": 3}", carried);
            Assert.Null(Invoke(h.Runtime, "TakeLiveContext"));
        }
        finally { Close(window); }
    });

    private static void Raise(NthUiView view, NthUiEffect effect) => Invoke(view, "Raise", effect);
}
