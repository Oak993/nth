using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

// OD-0116, OD-0122, ED-439: a standby that loads history without any model turn (Codex, Pi), over a scripted fake session.
[Trait("Rule", "R-TEST-4")]
public sealed class InjectedStandbySessionTests
{
    [Fact]
    public async Task The_standby_loads_the_context_as_history_with_no_turn_and_no_read_back()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        Assert.Equal(StandbyKind.Injected, standby.Kind);
        Assert.Equal("standby-thread", standby.SessionId);
        Assert.Null(await standby.ReceiveAsync(Package(), true, CancellationToken.None));
        var injected = Assert.Single(session.Injected);
        Assert.Contains("Exact context", injected, StringComparison.Ordinal);
        Assert.Contains("[nth Handshake]", injected, StringComparison.Ordinal);
        Assert.Empty(session.Inputs);
        Assert.False(standby.ReadBackRequested);
        Assert.Equal(Package().Summary.Text, standby.ReceivedPackage!.Summary.Text);
        Assert.Equal(StandbyKind.Injected, standby.Kind);
    }

    [Theory]
    [InlineData(Bring.Full)]
    [InlineData(Bring.MarrowSummary)]
    [InlineData(Bring.AiSummary)]
    public async Task Bring_decides_the_injected_payload_and_the_briefing_rides_along(Bring bring)
    {
        using var folder = new AccountTestFolder();
        var file = Path.Combine(folder.Path, "archive.json");
        Directory.CreateDirectory(folder.Path);
        File.WriteAllText(file, "{\"all\":\"ARCHIVE-DETAIL\"}");
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(new(bring, new("MECHANICAL-SUMMARY", file, ["Config.cs"]), "ACTUAL-AI-BRIEFING"), false, CancellationToken.None);
        var text = Assert.Single(session.Injected);
        Assert.Contains(bring switch { Bring.Full => "ARCHIVE-DETAIL", Bring.MarrowSummary => "MECHANICAL-SUMMARY", _ => "ACTUAL-AI-BRIEFING" }, text, StringComparison.Ordinal);
        Assert.Contains("ACTUAL-AI-BRIEFING", text, StringComparison.Ordinal);
        if (bring != Bring.Full) Assert.DoesNotContain("ARCHIVE-DETAIL", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_archive_over_the_size_cap_goes_as_Marrows_summary()
    {
        using var folder = new AccountTestFolder();
        var file = Path.Combine(folder.Path, "archive.json");
        Directory.CreateDirectory(folder.Path);
        File.WriteAllText(file, new string('x', InjectedStandbySession.FullArchiveCharacterCap + 1));
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(new(Bring.Full, new("MECHANICAL-SUMMARY", file, ["Config.cs"]), null), false, CancellationToken.None);
        var text = Assert.Single(session.Injected);
        Assert.Contains("MECHANICAL-SUMMARY", text, StringComparison.Ordinal);
        Assert.True(text.Length < 5_000);
    }

    [Fact]
    public async Task An_AI_summary_without_a_real_briefing_is_refused_and_nothing_is_injected()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => standby.ReceiveAsync(Package() with { Bring = Bring.AiSummary }, false, CancellationToken.None));
        Assert.Empty(session.Injected);
    }

    [Fact]
    public async Task A_later_package_injects_only_the_new_turns_and_nothing_when_nothing_changed()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package("first part"), false, CancellationToken.None);
        await standby.ReceiveAsync(Package("first part"), false, CancellationToken.None);
        Assert.Single(session.Injected);
        await standby.ReceiveAsync(Package("first part, then the newest turns"), false, CancellationToken.None);
        Assert.Equal(2, session.Injected.Count);
        Assert.Contains(", then the newest turns", session.Injected[1], StringComparison.Ordinal);
        Assert.DoesNotContain("first part", session.Injected[1], StringComparison.Ordinal);
        await standby.ReceiveAsync(Package("something rewritten"), false, CancellationToken.None);
        Assert.Equal(3, session.Injected.Count);
        Assert.Contains("something rewritten", session.Injected[2], StringComparison.Ordinal);
        Assert.Contains("replaces", session.Injected[2], StringComparison.OrdinalIgnoreCase);
    }

    // The summary Marrow writes carries a header (with a new archive path every build), one block per item, and a trailer; later builds add items and change the trailer.
    private static string Summary(string archive, string files, string[] items, string error) =>
        "nth handoff: recorded context for continuing this task.\nFull conversation, including all shortened output: " + archive + "\nTask:\nBuild it\nChanged files:\n" + files
        + "\nConversation and tool history (chronological):\n" + string.Concat(items.Select((text, index) => "\n[item-" + index + "; turn t1]\n" + text + "\n"))
        + "\nLast error:\n" + error + "\nQueued user instructions:\n";

    [Fact]
    public async Task A_rebuilt_summary_injects_only_the_new_and_changed_entries_and_the_new_archive_path()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package(Summary("old-archive.json", "a.cs", ["User:\nFIRSTITEM", "Assistant:\nPARTIAL"], "None recorded")), false, CancellationToken.None);
        await standby.ReceiveAsync(Package(Summary("new-archive.json", "a.cs", ["User:\nFIRSTITEM", "Assistant:\nPARTIAL AND THEN DONE", "User:\nTHREE"], "None recorded")), false, CancellationToken.None);
        Assert.Equal(2, session.Injected.Count);
        var delta = session.Injected[1];
        Assert.Contains("new-archive.json", delta, StringComparison.Ordinal);
        Assert.Contains("PARTIAL AND THEN DONE", delta, StringComparison.Ordinal);
        Assert.Contains("THREE", delta, StringComparison.Ordinal);
        Assert.DoesNotContain("FIRSTITEM", delta, StringComparison.Ordinal);
        Assert.DoesNotContain("Build it", delta, StringComparison.Ordinal);
        // Nothing but the archive path changed: nothing new to load.
        await standby.ReceiveAsync(Package(Summary("third-archive.json", "a.cs", ["User:\nFIRSTITEM", "Assistant:\nPARTIAL AND THEN DONE", "User:\nTHREE"], "None recorded")), false, CancellationToken.None);
        Assert.Equal(2, session.Injected.Count);
        // A changed trailer and header are loaded too.
        await standby.ReceiveAsync(Package(Summary("fourth-archive.json", "a.cs\nb.cs", ["User:\nFIRSTITEM", "Assistant:\nPARTIAL AND THEN DONE", "User:\nTHREE"], "boom")), false, CancellationToken.None);
        Assert.Equal(3, session.Injected.Count);
        Assert.Contains("b.cs", session.Injected[2], StringComparison.Ordinal);
        Assert.Contains("boom", session.Injected[2], StringComparison.Ordinal);
        Assert.DoesNotContain("THREE", session.Injected[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conversation_with_no_entries_yet_loads_once_and_a_new_archive_path_alone_loads_nothing()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package(Summary("one.json", "", [], "None recorded")), false, CancellationToken.None);
        await standby.ReceiveAsync(Package(Summary("two.json", "", [], "None recorded")), false, CancellationToken.None);
        Assert.Single(session.Injected);
    }

    [Fact]
    public async Task Activity_on_the_waiting_thread_makes_it_a_deferred_delivery_that_keeps_the_package_and_cannot_be_adopted()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package(), false, CancellationToken.None);
        session.Violate("turn/started");
        Assert.Equal(StandbyKind.Deferred, standby.Kind);
        Assert.Equal("turn/started", standby.FaultReason);
        Assert.Null(await standby.ReceiveAsync(Package("newer"), false, CancellationToken.None));
        Assert.Equal("newer", standby.ReceivedPackage!.Summary.Text);
        Assert.Single(session.Injected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => standby.PrepareForAdoptionAsync(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
        Assert.False(session.Ended);
        await standby.DisposeAsync();
        Assert.True(session.Stopped);
    }

    [Fact]
    public async Task A_refused_inject_makes_it_a_deferred_delivery_without_failing_the_handshake()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession { InjectFails = true };
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        Assert.Null(await standby.ReceiveAsync(Package(), false, CancellationToken.None));
        Assert.Equal(StandbyKind.Deferred, standby.Kind);
        Assert.Equal(Package().Summary.Text, standby.ReceivedPackage!.Summary.Text);
        Assert.Equal(1, session.DiscardCalls);
    }

    [Fact]
    public async Task A_CLI_that_cannot_hold_injected_history_fails_the_start_and_is_stopped()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession { StartUnavailable = true };
        await Assert.ThrowsAsync<StandbyUnavailableException>(() => InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None));
        Assert.True(session.Stopped);
        await Assert.ThrowsAsync<NotSupportedException>(() => InjectedStandbySession.StartAsync(new HandoffSession(), Config(folder.Path), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_discards_the_waiting_thread_once_and_adoption_hands_the_live_session_over_without_discarding_it()
    {
        using var folder = new AccountTestFolder();
        var cancelled = new StandbyInjectionSession();
        var standby = await InjectedStandbySession.StartAsync(cancelled, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package(), false, CancellationToken.None);
        await standby.DisposeAsync();
        await standby.DisposeAsync();
        Assert.Equal(1, cancelled.DiscardCalls);
        Assert.Empty(cancelled.Inputs);

        var adopted = new StandbyInjectionSession();
        var keep = await InjectedStandbySession.StartAsync(adopted, Config(folder.Path), CancellationToken.None);
        await keep.ReceiveAsync(Package(), false, CancellationToken.None);
        await keep.PrepareForAdoptionAsync(CancellationToken.None);
        Assert.True(adopted.Ended);
        Assert.Same(adopted, keep.TakeSession());
        Assert.Null(adopted.OnEvent);
        await keep.DisposeAsync();
        Assert.Equal(0, adopted.DiscardCalls);
        Assert.False(adopted.Stopped);
        Assert.Empty(adopted.Inputs);
    }

    [Fact]
    public async Task A_handover_that_fails_makes_it_a_deferred_delivery_and_discards_the_waiting_session()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession { EndFails = true };
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await standby.ReceiveAsync(Package(), false, CancellationToken.None);
        await standby.PrepareForAdoptionAsync(CancellationToken.None);
        Assert.Equal(StandbyKind.Deferred, standby.Kind);
        Assert.Equal(1, session.DiscardCalls);
        Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
    }

    [Fact]
    public async Task Revoked_permission_stops_a_transfer_before_anything_is_injected()
    {
        using var folder = new AccountTestFolder();
        var session = new StandbyInjectionSession();
        var allowed = true;
        await using var standby = await InjectedStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None, canTransfer: () => allowed);
        allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => standby.ReceiveAsync(Package(), false, CancellationToken.None));
        Assert.Empty(session.Injected);
    }

    private static HandoffPackage Package(string text = "Exact context") => new(Bring.MarrowSummary, new(text, null, ["Config.cs"]), null);

    private static SessionConfiguration Config(string directory) => new()
    {
        Provider = ProviderKind.Codex, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + directory.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty, Model = "offline-model", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
