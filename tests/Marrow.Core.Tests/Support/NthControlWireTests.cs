using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The wire of nth's control
// pipe, the one thing the app and nth Setup must read the same way: the four requests, the answers (ok, busy, status,
// refused), the framing, and the rule that nothing on the wire is trusted. The same source file is compiled into nth Setup,
// so these tests prove what both sides read and write.
public sealed class NthControlWireTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Each_request_is_one_compact_line_with_the_version_first()
    {
        Assert.Equal("{\"v\":1,\"cmd\":\"status\"}", NthControlRequest.Status.ToJson());
        Assert.Equal("{\"v\":1,\"cmd\":\"quit\",\"mode\":\"if-idle\"}", NthControlRequest.QuitIfIdle.ToJson());
        Assert.Equal("{\"v\":1,\"cmd\":\"quit\",\"mode\":\"stop\"}", NthControlRequest.QuitAndStop.ToJson());
        Assert.Equal("{\"v\":1,\"cmd\":\"withdraw\"}", NthControlRequest.Withdraw.ToJson());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_request_reads_back_as_the_request_that_was_written()
    {
        foreach (var request in new[] { NthControlRequest.Status, NthControlRequest.QuitIfIdle, NthControlRequest.QuitAndStop, NthControlRequest.Withdraw })
            Assert.Equal(request, NthControlRequest.Parse(Utf8(request.ToJson())));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("quit")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"cmd\":\"status\"}")]
    [InlineData("{\"v\":2,\"cmd\":\"status\"}")]
    [InlineData("{\"v\":\"1\",\"cmd\":\"status\"}")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"cmd\":\"restart\"}")]
    [InlineData("{\"v\":1,\"cmd\":\"STATUS\"}")]
    [InlineData("{\"v\":1,\"cmd\":\"quit\"}")]
    [InlineData("{\"v\":1,\"cmd\":\"quit\",\"mode\":\"now\"}")]
    [InlineData("{\"v\":1,\"cmd\":\"status\"} {}")]
    [InlineData("{\"v\":1,\"cmd\":\"status\"}x")]
    [InlineData("{\"v\":1,\"cmd\":\"status\",}")]
    [InlineData("{\"v\":1,/*a*/\"cmd\":\"status\"}")]
    public void Anything_that_is_not_a_version_1_request_is_not_a_request(string text) =>
        Assert.Null(NthControlRequest.Parse(Utf8(text)));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_request_with_an_extra_field_is_still_a_request_so_a_newer_installer_can_add_fields()
    {
        Assert.Equal(NthControlRequest.Status, NthControlRequest.Parse(Utf8("{\"v\":1,\"cmd\":\"status\",\"note\":\"hi\"}")));
        Assert.Equal(NthControlRequest.QuitAndStop, NthControlRequest.Parse(Utf8("{\"mode\":\"stop\",\"cmd\":\"quit\",\"v\":1}")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_status_answer_lists_the_work_in_a_fixed_field_order()
    {
        var response = new NthControlResponse(
            NthControlReply.Status,
            4242,
            @"C:\nth\current\nth.exe",
            [new NthWorkItem("Refactor the sign-in module", NthWorkKind.Thread, 240), new NthWorkItem("Ana", NthWorkKind.Merge, 61)],
            2);

        Assert.Equal(
            "{\"v\":1,\"reply\":\"status\",\"pid\":4242,\"exe\":\"C:\\\\nth\\\\current\\\\nth.exe\",\"work\":[{\"name\":\"Refactor the sign-in module\",\"kind\":\"thread\",\"seconds\":240},{\"name\":\"Ana\",\"kind\":\"merge\",\"seconds\":61}],\"count\":2}",
            response.ToJson());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_ok_a_deferred_ok_and_a_refusal_carry_no_work_list()
    {
        Assert.Equal(
            "{\"v\":1,\"reply\":\"ok\",\"pid\":7,\"exe\":\"nth.exe\"}",
            new NthControlResponse(NthControlReply.Ok, 7, "nth.exe", [new NthWorkItem("x", NthWorkKind.Thread, 1)], 1).ToJson());
        Assert.Equal(
            "{\"v\":1,\"reply\":\"ok\",\"pid\":7,\"exe\":\"nth.exe\",\"deferred\":\"merge\"}",
            new NthControlResponse(NthControlReply.Ok, 7, "nth.exe", [], 0, NthWorkKind.Merge).ToJson());
        Assert.Equal(
            "{\"v\":1,\"reply\":\"refused\",\"pid\":7,\"exe\":\"nth.exe\",\"reason\":\"not-allowed\"}",
            NthControlResponse.Refused(7, "nth.exe", "not-allowed").ToJson());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_kind_of_answer_reads_back_as_written()
    {
        NthControlResponse[] answers =
        [
            new(NthControlReply.Ok, 11, "a.exe", [], 0),
            new(NthControlReply.Ok, 11, "a.exe", [], 0, NthWorkKind.Merge),
            new(NthControlReply.Busy, 11, "a.exe", [new NthWorkItem("One", NthWorkKind.Thread, 5), new NthWorkItem("Two", NthWorkKind.Merge, 0)], 2),
            new(NthControlReply.Status, 11, "a.exe", [], 0),
            NthControlResponse.Refused(11, "a.exe", "starting"),
        ];

        foreach (var answer in answers)
        {
            var read = NthControlResponse.Parse(Utf8(answer.ToJson()));
            Assert.NotNull(read);
            Assert.Equal(answer.Reply, read.Reply);
            Assert.Equal(answer.Pid, read.Pid);
            Assert.Equal(answer.Exe, read.Exe);
            Assert.Equal(answer.Work, read.Work);
            Assert.Equal(answer.WorkCount, read.WorkCount);
            Assert.Equal(answer.Deferred, read.Deferred);
            Assert.Equal(answer.Reason, read.Reason);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_list_is_capped_at_twenty_but_the_count_still_says_how_many_run()
    {
        var work = Enumerable.Range(1, 30).Select(i => new NthWorkItem("Thread " + i, NthWorkKind.Thread, i)).ToList();

        var sent = new NthControlResponse(NthControlReply.Busy, 1, "nth.exe", work, 30);
        var read = NthControlResponse.Parse(Utf8(sent.ToJson()));

        Assert.NotNull(read);
        Assert.Equal(NthControl.MaxWorkItems, read.Work.Count);
        Assert.Equal(30, read.WorkCount);
        Assert.Equal("Thread 1", read.Work[0].Name);
        Assert.Equal("Thread 20", read.Work[^1].Name);

        // An answer that lists more than the cap is cut on reading too, and a count below the list is raised to the list.
        var oversize = "{\"v\":1,\"reply\":\"busy\",\"work\":[" + string.Join(',', work.Select(item => "{\"name\":\"" + item.Name + "\",\"kind\":\"thread\",\"seconds\":1}")) + "],\"count\":3}";
        var cut = NthControlResponse.Parse(Utf8(oversize));
        Assert.NotNull(cut);
        Assert.Equal(NthControl.MaxWorkItems, cut.Work.Count);
        Assert.Equal(NthControl.MaxWorkItems, cut.WorkCount);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_on_the_wire_is_trusted_names_are_cleaned_numbers_are_not_negative_and_odd_words_end_the_answer()
    {
        var read = NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"busy\",\"pid\":-5,\"exe\":\"x\",\"work\":[{\"name\":\"a\\u202Eb\\u0007  c\",\"kind\":\"thread\",\"seconds\":-9},{\"name\":\"\\u200B\",\"kind\":\"merge\"}],\"count\":-1}"));

        Assert.NotNull(read);
        Assert.Equal(0, read.Pid);
        Assert.Equal("ab c", read.Work[0].Name);
        Assert.Equal(0, read.Work[0].Seconds);
        Assert.Equal(NthControl.UnnamedWork, read.Work[1].Name);
        Assert.Equal(0, read.Work[1].Seconds);
        Assert.Equal(2, read.WorkCount);

        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":2,\"reply\":\"ok\"}")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"reply\":\"ok\"}")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"maybe\"}")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"ok\",\"deferred\":\"thread-ish\"}")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"busy\",\"work\":[{\"name\":\"x\",\"kind\":\"cron\"}]}")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"busy\",\"work\":[{\"kind\":\"thread\"}]}")));
        Assert.Null(NthControlResponse.Parse(Utf8("not json")));
        Assert.Null(NthControlResponse.Parse(Utf8("{\"v\":1,\"reply\":\"ok\"} trailing")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_name_is_made_safe_to_put_in_a_window()
    {
        Assert.Equal("Refactor the sign-in module", NthControl.CleanName("  Refactor   the\tsign-in\nmodule "));
        Assert.Equal("ab", NthControl.CleanName("a\u202Eb"));      // right-to-left override
        Assert.Equal("xy", NthControl.CleanName("x\u200By"));      // zero-width space
        Assert.Equal("ctrlbell", NthControl.CleanName("ctrl\u0007bell"));
        Assert.Equal("Ana-María Østergård", NthControl.CleanName("Ana-María Østergård"));
        Assert.Equal(string.Empty, NthControl.CleanName(" \u200B\u2066 "));

        var long200 = new string('a', 200);
        var cut = NthControl.CleanName(long200);
        Assert.Equal(NthControl.MaxNameLength, cut.Length);
        Assert.EndsWith("\u2026", cut, StringComparison.Ordinal);

        // The cut never splits an emoji in two.
        var withEmoji = new string('a', 118) + "\U0001F600" + new string('b', 10);
        var safe = NthControl.CleanName(withEmoji);
        Assert.Equal(new string('a', 118) + "\u2026", safe);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_pipe_is_named_for_the_user_and_a_development_build_has_a_name_of_its_own()
    {
        const string sid = "S-1-5-21-111-222-333-1001";

        Assert.Equal(@"nth-S-1-5-21-111-222-333-1001-control", NthControl.PipeName(sid));
        Assert.Equal(@"nth-dev-S-1-5-21-111-222-333-1001-control", NthControl.PipeName(sid, development: true));
        Assert.Throws<ArgumentException>(() => NthControl.PipeName(" "));
        if (OperatingSystem.IsWindows()) Assert.StartsWith("S-1-5-", NthControl.CurrentUserSid(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_message_is_a_four_byte_little_endian_length_and_that_many_bytes()
    {
        Assert.Equal([2, 0, 0, 0, (byte)'{', (byte)'}'], NthControl.Frame("{}"));
        Assert.Throws<InvalidDataException>(() => NthControl.Frame(string.Empty));
        Assert.Throws<InvalidDataException>(() => NthControl.Frame(new string('x', NthControl.MaxMessageBytes + 1)));

        using var good = new MemoryStream(NthControl.Frame("{\"v\":1}"));
        Assert.Equal(Utf8("{\"v\":1}"), await NthControl.ReadFrameAsync(good, CancellationToken.None));
        using var none = new MemoryStream();
        Assert.Null(await NthControl.ReadFrameAsync(none, CancellationToken.None));
        using var shortBody = new MemoryStream([5, 0, 0, 0, 1, 2]);
        Assert.Null(await NthControl.ReadFrameAsync(shortBody, CancellationToken.None));
        using var zero = new MemoryStream([0, 0, 0, 0]);
        Assert.Null(await NthControl.ReadFrameAsync(zero, CancellationToken.None));
        using var negative = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 1]);
        Assert.Null(await NthControl.ReadFrameAsync(negative, CancellationToken.None));
        using var huge = new MemoryStream([0x01, 0x00, 0x01, 0x00, 1]); // 65,537 bytes
        Assert.Null(await NthControl.ReadFrameAsync(huge, CancellationToken.None));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_acknowledgement_is_one_ACK_byte_written_after_a_reply_was_read()
    {
        Assert.Equal(0x06, NthControl.AckByte);
        using var wire = new MemoryStream();

        await NthControl.WriteAckAsync(wire, CancellationToken.None);

        Assert.Equal([NthControl.AckByte], wire.ToArray());
    }
}
