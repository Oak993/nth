using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1, 12.6, 16.1; ED-NEW-dextra-as19-1, -2, -8): the recorder while a recording runs. A frame on every click
// and one a second; the element under each click; what was typed, with a password and anything secret kept as "[secret]" and never on disk;
// nothing from a private window or from nth's own; a time that counts only the time it ran; the 60-minute cap; a lease on the one stop chord
// (no chord, no recording); and an end that keeps what was recorded whether the owner, the chord, Stop all, the cap or a full disk ended it.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-2")]
public sealed class TeachRecorderTests : IDisposable
{
    private readonly TeachRecorderRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly TeachElement Send = new("outlook", "New message - Outlook", "button", "Send");

    private static readonly TeachElement Subject = new("outlook", "New message - Outlook", "edit", "Subject");

    private static readonly TeachElement Password = new("chrome", "Sign in - Chrome", "edit", "Password") { IsPassword = true };

    [Fact]
    public async Task A_recording_starts_with_the_hooks_on_a_lease_on_the_stop_chord_and_a_manifest()
    {
        var start = rig.Start();
        Assert.True(start.Started);
        Assert.Equal(TeachRecordingState.Recording, start.Info!.State);
        Assert.True(rig.Input.Installed);
        Assert.True(rig.Stop.Armed);
        Assert.Equal(["teach:" + start.Info.Id], rig.Stop.Scopes);
        Assert.Equal(start.Info, rig.Recordings.Info(start.Info.Id));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_click_is_an_event_with_the_element_under_it_and_a_frame()
    {
        rig.Start();
        rig.Probe.Under[(10, 20)] = Send;
        rig.Frames.Nothing = true;
        rig.Clock.Advance(TimeSpan.FromSeconds(4));
        await rig.Settle();
        rig.Frames.Nothing = false;
        rig.Input.Click(10, 20);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal((1, TimeSpan.FromSeconds(4), TeachEventKind.Click, "click"), (e.Seq, e.At, e.Kind, e.Verb));
        Assert.Equal(Send, e.Element);
        Assert.Equal("frames/0001.jpg", e.Frame);
        Assert.NotNull(rig.Recordings.Frame(rig.Recorder.Id!, e.Frame!));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_click_where_nothing_answers_falls_back_to_the_window_in_front()
    {
        rig.Start();
        rig.Input.Click(1, 1, "double-click");
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal(("double-click", "notepad", ""), (e.Verb, e.Element!.App, e.Element.Name));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task Typing_into_a_field_keeps_what_the_field_held()
    {
        rig.Start();
        rig.Probe.Focus = new TeachFocus(Subject, "Weekly report");
        rig.Input.Type(13);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal((TeachEventKind.Type, "Weekly report", 13, false), (e.Kind, e.Text, e.Length, e.Secret));
        Assert.Equal(Subject, e.Element);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_password_is_a_secret_whatever_the_field_holds_and_is_nowhere_on_disk()
    {
        rig.Start();
        rig.Probe.Focus = new TeachFocus(Password, "hunter2-Is-Not-Kept");
        rig.Input.Type(19);
        await rig.Recorder.Stop();
        var e = Assert.Single(rig.Events());
        Assert.Equal((TeachWords.Secret, 19, true), (e.Text, e.Length, e.Secret));
        Assert.True(e.Element!.IsPassword);
        Assert.DoesNotContain("hunter2", rig.EverythingOnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_the_privacy_patterns_catch_is_a_secret_in_an_ordinary_field_and_not_on_disk()
    {
        rig.Start();
        rig.Probe.Focus = new TeachFocus(Subject, "sk-abcdefghijklmnopqrstuvwxyz123456");
        rig.Input.Type(35);
        await rig.Recorder.Stop();
        var e = Assert.Single(rig.Events());
        Assert.Equal((TeachWords.Secret, true), (e.Text, e.Secret));
        Assert.DoesNotContain("sk-abcdef", rig.EverythingOnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_where_nth_cannot_look_up_the_field_keeps_only_the_length()
    {
        rig.Start();
        rig.Probe.Focus = null;
        rig.Input.Type(8);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal((null, 8, false), (e.Text, e.Length, e.Secret));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task The_text_is_read_from_the_field_that_had_the_focus_when_typing_began_not_the_one_that_has_it_when_typing_ends()
    {
        rig.Start();
        var read = 0;
        rig.Probe.Focus = new TeachFocus(Subject, () =>
        {
            read++;
            return "Weekly report";
        });
        rig.Input.TypeStarted();
        await rig.Settle();
        rig.Probe.Focus = new TeachFocus(Send, () => "the wrong field");
        rig.Input.Type(13);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal(("Weekly report", Subject), (e.Text, e.Element));
        Assert.Equal(1, read);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_password_field_is_never_read_at_all()
    {
        rig.Start();
        var read = 0;
        rig.Probe.Focus = new TeachFocus(Password, () =>
        {
            read++;
            return "hunter2";
        });
        rig.Input.TypeStarted();
        rig.Input.Type(7);
        await rig.Settle();
        Assert.Equal(0, read);
        Assert.True(Assert.Single(rig.Events()).Secret);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_field_that_cannot_be_read_keeps_only_the_length_and_the_recording_goes_on()
    {
        rig.Start();
        rig.Probe.Focus = new TeachFocus(Subject, () => throw new InvalidOperationException("the app hung"));
        rig.Input.Type(5);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal((null, 5), (e.Text, e.Length));
        Assert.Contains("teach recording: InvalidOperationException", rig.Faults);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task Typing_that_had_not_ended_when_the_recording_is_paused_or_stopped_is_kept()
    {
        rig.Start();
        rig.Probe.Focus = new TeachFocus(Subject, "Weekly");
        rig.Input.Tail = new TeachInput.Typing(6);
        rig.Recorder.Pause();
        await rig.Settle();
        var first = Assert.Single(rig.Events());
        Assert.Equal(("Weekly", 6), (first.Text, first.Length));
        rig.Recorder.Resume();
        await rig.Settle();
        rig.Probe.Focus = new TeachFocus(Subject, "Weekly report");
        rig.Input.Tail = new TeachInput.Typing(7);
        await rig.Recorder.Stop();
        Assert.Equal(["Weekly", "Weekly report"], rig.Events().Select(e => e.Text));
    }

    [Fact]
    public async Task A_shortcut_is_a_chord_in_the_window_in_front()
    {
        rig.Start();
        rig.Input.Chord("Ctrl+S");
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal((TeachEventKind.Shortcut, "Ctrl+S", "notepad"), (e.Kind, e.Text, e.Element!.App));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_click_in_a_private_window_is_a_private_event_with_no_element_and_no_frame_and_the_stretch_is_one()
    {
        rig.Start();
        rig.Probe.Under[(5, 5)] = new TeachElement("bitwarden", "Vault", "button", "Copy password");
        rig.Probe.Under[(6, 6)] = new TeachElement("chrome", "Bank - Incognito", "button", "Pay");
        rig.Input.Click(5, 5);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        rig.Input.Click(6, 6);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal(TeachEventKind.Private, e.Kind);
        Assert.Null(e.Element);
        Assert.Null(e.Frame);
        rig.Clock.Advance(TeachRecorder.PrivateStretch);
        rig.Input.Click(5, 5);
        await rig.Settle();
        Assert.Equal(2, rig.Events().Count);
        Assert.DoesNotContain("Copy password", rig.EverythingOnDisk(), StringComparison.Ordinal);
        Assert.DoesNotContain("Incognito", rig.EverythingOnDisk(), StringComparison.Ordinal);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task Typing_or_a_shortcut_in_a_private_window_is_a_private_event_too()
    {
        rig.Start();
        rig.Probe.Front = new TeachElement("1password", "Unlock", "", "");
        rig.Probe.Focus = new TeachFocus(new TeachElement("1password", "Unlock", "edit", "Master password"), "correct horse");
        rig.Input.Type(13);
        rig.Clock.Advance(TeachRecorder.PrivateStretch);
        rig.Input.Chord("Ctrl+V");
        await rig.Recorder.Stop();
        Assert.All(rig.Events(), e => Assert.Equal(TeachEventKind.Private, e.Kind));
        Assert.DoesNotContain("correct horse", rig.EverythingOnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_the_owner_does_in_nth_itself_is_not_a_step_so_the_strip_does_not_record_its_own_buttons()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = new TeachElement("Marrow.App", "Recording", "button", "Pause");
        rig.Probe.Front = new TeachElement("Marrow.App", "nth", "", "");
        rig.Input.Click(1, 1);
        rig.Input.Chord("Ctrl+S");
        rig.Input.Type(3);
        await rig.Settle();
        Assert.Empty(rig.Events());
        Assert.Equal(0, rig.Frames.Calls);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_frame_is_kept_each_second_unless_it_is_the_same_picture_or_a_private_window_is_in_front()
    {
        rig.Start();
        await rig.Run(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Recorder.Status.Frames);
        rig.Frames.Fixed = [1, 2, 3];
        await rig.Run(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Recorder.Status.Frames);
        await rig.Run(TimeSpan.FromSeconds(3));
        Assert.Equal(2, rig.Recorder.Status.Frames);
        rig.Frames.Fixed = [9, 9, 9];
        rig.Probe.Front = new TeachElement("keepass", "Database", "", "");
        await rig.Run(TimeSpan.FromSeconds(2));
        Assert.Equal(2, rig.Recorder.Status.Frames);
        rig.Probe.Front = new TeachElement("notepad", "Untitled", "", "");
        await rig.Run(TimeSpan.FromSeconds(1));
        Assert.Equal(3, rig.Recorder.Status.Frames);
        Assert.Equal(3, (await rig.Recorder.Stop()).Frames);
    }

    [Fact]
    public async Task A_frame_on_every_click_is_kept_even_when_it_matches_the_one_before()
    {
        rig.Start();
        rig.Frames.Fixed = [7, 7, 7];
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        rig.Input.Click(1, 1);
        await rig.Settle();
        Assert.Equal(["frames/0001.jpg", "frames/0002.jpg"], rig.Events().Select(e => e.Frame));
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task When_the_screen_cannot_be_taken_the_click_is_kept_without_a_frame()
    {
        rig.Start();
        rig.Frames.Nothing = true;
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        await rig.Settle();
        Assert.Null(Assert.Single(rig.Events()).Frame);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task The_time_counts_only_while_it_runs_and_nothing_is_heard_while_it_is_paused()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = Send;
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        rig.Recorder.Pause();
        await rig.Settle();
        Assert.Equal(TeachRecordingState.Paused, rig.Recorder.State);
        Assert.False(rig.Input.Installed);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        rig.Input.Click(1, 1);
        Assert.Equal(TimeSpan.FromSeconds(10), rig.Recorder.Elapsed);
        rig.Recorder.Resume();
        await rig.Settle();
        Assert.True(rig.Input.Installed);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        rig.Input.Click(1, 1);
        await rig.Settle();
        var e = Assert.Single(rig.Events());
        Assert.Equal(TimeSpan.FromSeconds(15), e.At);
        var ended = await rig.Recorder.Stop();
        Assert.Equal(TimeSpan.FromSeconds(15), ended.Elapsed);
    }

    [Fact]
    public async Task Pause_and_resume_when_not_in_that_state_do_nothing()
    {
        rig.Start();
        rig.Recorder.Resume();
        await rig.Settle();
        Assert.Equal(1, rig.Input.Starts);
        rig.Recorder.Pause();
        rig.Recorder.Pause();
        await rig.Settle();
        Assert.Equal(1, rig.Input.Stops);
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task A_recording_ends_itself_at_sixty_minutes_of_recorded_time_and_keeps_what_it_has()
    {
        rig.Start();
        rig.Frames.Nothing = true;
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        await rig.Settle();
        rig.Recorder.Pause();
        await rig.Settle();
        rig.Clock.Advance(TimeSpan.FromMinutes(90)); // a pause does not count
        rig.Recorder.Resume();
        await rig.Settle();
        Assert.Equal(TeachRecordingState.Recording, rig.Recorder.State);
        rig.Clock.Advance(TimeSpan.FromMinutes(59));
        await rig.Settle();
        Assert.Equal(TeachRecordingState.Recording, rig.Recorder.Status.State);
        Assert.True(rig.Recorder.Status.CountingDown);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Recorder.Status.Remaining);
        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        await rig.Settle();
        var ended = Assert.Single(rig.Ended);
        Assert.Equal((TeachRecordingState.Recorded, TeachStopReason.Cap), (ended.State, ended.Ended!.Value));
        Assert.Equal(1, ended.Events);
        Assert.False(rig.Input.Installed);
        Assert.False(rig.Stop.Armed);
    }

    [Fact]
    public async Task A_recording_left_paused_for_two_hours_ends_itself_with_what_it_has()
    {
        rig.Start();
        rig.Recorder.Pause();
        await rig.Settle();
        rig.Clock.Advance(TeachLimits.PausedIdle - TimeSpan.FromSeconds(1));
        await rig.Settle();
        Assert.Empty(rig.Ended);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.Settle();
        Assert.Equal(TeachStopReason.PausedTooLong, Assert.Single(rig.Ended).Ended);
    }

    [Fact]
    public async Task The_stop_chord_ends_the_recording_and_so_does_stop_all_and_the_lease_is_given_back()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        await rig.Settle();
        rig.Stop.Press(StopSource.Chord);
        var ended = await rig.Recorder.Stop(); // already stopping: the same end
        Assert.Equal(TeachStopReason.StopKey, ended.Ended);
        Assert.Equal(1, ended.Events);
        Assert.False(rig.Stop.Armed);
        Assert.Empty(rig.Stop.Scopes);
        Assert.Equal(["register Ctrl+Alt+Esc", "unregister"], rig.Host.Calls);
    }

    [Fact]
    public async Task Stop_all_from_the_header_ends_it_with_its_own_reason()
    {
        rig.Start();
        rig.Stop.Press(StopSource.Header);
        await rig.Settle();
        Assert.Equal(TeachStopReason.StopAll, Assert.Single(rig.Ended).Ended);
    }

    [Fact]
    public async Task A_stop_chord_another_app_holds_means_no_recording_and_nothing_left_behind()
    {
        rig.Host.Taken.Add(Marrow.Core.Port.PcControl.Pill.PcHotkey.StopDefault);
        var start = rig.Start();
        Assert.False(start.Started);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Esc is taken. Change it in Settings", start.Refusal);
        Assert.False(rig.Input.Installed);
        Assert.Equal(0, rig.Input.Starts);
        Assert.Empty(rig.Recordings.List(out _));
        await rig.Settle();
    }

    [Fact]
    public async Task Windows_refusing_the_hook_means_no_recording_and_nothing_left_behind()
    {
        rig.Input.FailStart = new IOException("hook");
        var start = rig.Start();
        Assert.False(start.Started);
        Assert.Equal("Windows would not let nth watch the mouse and keyboard, so nothing was recorded.", start.Refusal);
        Assert.Empty(rig.Recordings.List(out _));
        Assert.False(rig.Stop.Armed);
        await rig.Settle();
    }

    [Fact]
    public async Task A_recorder_records_once()
    {
        rig.Start();
        Assert.Throws<InvalidOperationException>(() => rig.Start());
        await rig.Recorder.Stop();
    }

    [Fact]
    public async Task The_end_writes_the_manifest_with_the_counts_and_the_state_recorded()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = Send;
        rig.Frames.Nothing = true;
        rig.Clock.Advance(TimeSpan.FromSeconds(3));
        await rig.Settle();
        rig.Frames.Nothing = false;
        rig.Input.Click(1, 1);
        rig.Probe.Focus = new TeachFocus(Subject, "x");
        rig.Input.Type(1);
        var ended = await rig.Recorder.Stop();
        Assert.Equal((TeachRecordingState.Recorded, TeachStopReason.Owner, 2, 1, TimeSpan.FromSeconds(3)), (ended.State, ended.Ended!.Value, ended.Events, ended.Frames, ended.Elapsed));
        Assert.Equal(ended, rig.Recordings.Info(ended.Id));
        Assert.Equal(ended, Assert.Single(rig.Ended));
        Assert.True(ended.FrameBytes > 0);
    }

    [Fact]
    public async Task Stopping_twice_is_one_end()
    {
        rig.Start();
        var first = await rig.Recorder.Stop();
        var second = await rig.Recorder.Stop();
        Assert.Equal(first, second);
        Assert.Single(rig.Ended);
    }

    [Fact]
    public async Task Ending_the_app_while_it_runs_ends_the_recording_and_keeps_it()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        rig.Recorder.Dispose();
        var ended = Assert.Single(rig.Ended);
        Assert.Equal((TeachRecordingState.Recorded, TeachStopReason.Closed, 1), (ended.State, ended.Ended!.Value, ended.Events));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_full_disk_or_a_closed_store_ends_the_recording_cleanly_and_says_so()
    {
        rig.Start();
        rig.Probe.Under[(1, 1)] = Send;
        rig.Input.Click(1, 1);
        await rig.Settle();
        rig.Store.Dispose();
        rig.Input.Click(1, 1);
        await rig.Settle();
        Assert.Contains("teach recording: ObjectDisposedException", rig.Faults);
        Assert.False(rig.Input.Installed);
        Assert.False(rig.Stop.Armed);
    }

    [Fact]
    public async Task The_events_cap_and_the_frame_size_cap_end_the_recording_cleanly()
    {
        using var small = new TeachRecorderRig(maxEvents: 2);
        small.Start();
        small.Probe.Under[(1, 1)] = Send;
        for (var i = 0; i < 4; i++) small.Input.Click(1, 1);
        await small.Settle();
        var ended = Assert.Single(small.Ended);
        Assert.Equal((TeachStopReason.Full, 2), (ended.Ended!.Value, ended.Events));

        using var heavy = new TeachRecorderRig(softBytes: 100, hardBytes: 300);
        heavy.Start();
        heavy.Probe.Under[(1, 1)] = Send;
        heavy.Frames.Size = 160;
        heavy.Input.Click(1, 1);
        heavy.Input.Click(1, 1);
        await heavy.Settle();
        Assert.Equal(TeachStopReason.Full, Assert.Single(heavy.Ended).Ended);
    }

    [Fact]
    public async Task Past_the_soft_limit_only_the_frames_on_clicks_are_kept_and_the_owner_is_told_once()
    {
        using var soft = new TeachRecorderRig(softBytes: 100, hardBytes: 100_000);
        soft.Start();
        soft.Probe.Under[(1, 1)] = Send;
        soft.Frames.Size = 160;
        soft.Input.Click(1, 1);
        await soft.Settle();
        await soft.Run(TimeSpan.FromSeconds(3));
        Assert.Equal(1, soft.Recorder.Status.Frames);
        Assert.Single(soft.Recorder.Status.Notes);
        soft.Input.Click(1, 1);
        await soft.Settle();
        Assert.Equal(2, soft.Recorder.Status.Frames);
        await soft.Recorder.Stop();
    }

    [Fact]
    public async Task A_page_the_browser_opens_loses_its_query_and_a_password_typed_there_is_a_secret()
    {
        using var web = new TeachRecorderRig(browser: true);
        web.Start();
        Assert.True(web.Browser!.Subscribed);
        web.Browser.Raise(new TeachBrowserStep("open", "https://shop.example/cart?token=abc123&x=1#frag", null, null));
        web.Browser.Raise(new TeachBrowserStep("type", null, "hunter2", new TeachElement("browser", "Sign in", "edit", "Password")) { IsPassword = true, Length = 7 });
        web.Browser.Raise(new TeachBrowserStep("click", "javascript:alert(1)", "Place order", new TeachElement("browser", "Cart", "button", "Place order")));
        web.Browser.Raise(new TeachBrowserStep("type", null, "Sam", new TeachElement("browser", "Cart", "edit", "Name")) { Length = 3 });
        await web.Settle();
        var events = web.Events();
        Assert.Equal("https://shop.example/cart", events[0].Url);
        Assert.Equal((TeachWords.Secret, true, 7), (events[1].Text, events[1].Secret, events[1].Length));
        Assert.Null(events[2].Url);
        Assert.Equal("Sam", events[3].Text);
        await web.Recorder.Stop();
        Assert.False(web.Browser.Subscribed);
        Assert.DoesNotContain("hunter2", web.EverythingOnDisk(), StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc123", web.EverythingOnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_address_keeps_only_the_scheme_the_host_and_the_path()
    {
        Assert.Equal("https://a.example/x/y", TeachRecorder.PageAddress("https://a.example/x/y?q=1#z"));
        Assert.Equal("http://a.example/", TeachRecorder.PageAddress("http://a.example"));
        Assert.Null(TeachRecorder.PageAddress("file:///C:/secret.txt"));
        Assert.Null(TeachRecorder.PageAddress("javascript:alert(1)"));
        Assert.Null(TeachRecorder.PageAddress("not a url"));
        Assert.Null(TeachRecorder.PageAddress(null));
    }
}
