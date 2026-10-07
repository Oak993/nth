using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;
using Xunit;

namespace Marrow.Core.Tests.Port.MultiAccount.Popover;

// PORT-ONLY: page AccountPanel :2674-2797; OD-0115/0116/0117/0119.
public sealed class AccountPopoverModelTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Page_header_footer_and_rows_keep_priority_even_when_exhausted()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Usage[a.Id] = new(1, DateTimeOffset.Parse("2026-10-07T15:40:00Z", System.Globalization.CultureInfo.InvariantCulture), .83, null, false, false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new AccountPopoverModel(port, rig.Accounts, zone: TimeZoneInfo.Utc);
        Assert.Equal("Claude Code accounts", model.Header);
        Assert.Equal("Drag to set the order", model.OrderHint);
        Assert.Equal("Priority order: row 1 starts new threads", model.Subtitle);
        Assert.Equal("New threads start on Claude Max 1 · when it runs out, Claude Max 2", model.Footer);
        Assert.Equal("Skipped", model.Rows[0].Chip);
        Assert.True(model.Rows[0].Dimmed);
        Assert.Equal("resets 3:40 PM", model.Rows[0].ResetText);
        Assert.False(model.Rows[0].Active);
        Assert.True(model.Rows[1].Active);
        Assert.Equal("100%", model.Rows[0].FiveHour.Percent);
        Assert.True(model.Rows[0].FiveHour.Out);
        Assert.False(model.Rows[0].FiveHour.Hot);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(.899, false)] [InlineData(.9, true)] [InlineData(1, false)] [InlineData(1.2, false)]
    public void Five_hour_hot_threshold_and_clamp_follow_the_page(double used, bool hot)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Codex);
        rig.Usage[a.Id] = new(used, null, .9, null, false, false);
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, ProviderKind.Codex), rig.Accounts);
        Assert.Equal(hot, model.Rows[0].FiveHour.Hot);
        Assert.Equal(Math.Min(1, used), model.Rows[0].FiveHour.Fraction);
        Assert.True(model.Rows[0].Week.Hot);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Off_and_no_five_hour_are_report_driven_and_unknown_usage_is_not_zero()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Codex, "Pro");
        var b = rig.ConnectAnother(ProviderKind.Codex, "Plus"); rig.Accounts.SetEnabled(a.Id, false);
        rig.Usage[b.Id] = new(null, null, .5, DateTimeOffset.Parse("2026-10-08T07:00:00Z", System.Globalization.CultureInfo.InvariantCulture), true, false);
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, ProviderKind.Codex), rig.Accounts, zone: TimeZoneInfo.Utc);
        Assert.Equal("Off", model.Rows[0].Chip); Assert.Equal("", model.Rows[0].ResetText);
        Assert.Null(model.Rows[0].FiveHour.Fraction); Assert.Equal("", model.Rows[0].FiveHour.Percent);
        Assert.Null(model.Rows[0].NoFiveHourText);
        Assert.Equal("No 5-hour limit", model.Rows[1].NoFiveHourText);
        Assert.Equal("resets 7:00 AM", model.Rows[1].ResetText);
        Assert.Equal("New threads start on Codex 2", model.Footer);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Local_words_defaults_and_pill_gate_match_the_source()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Local);
        rig.Accounts.Rename(a.Id, "Qwen Coder 32B");
        using var model = new AccountPopoverModel(new FakeThreadAccountPort(rig, ProviderKind.Local), rig.Accounts, connectedCount: () => 1, localServer: _ => "Ollama · this PC");
        Assert.Equal("Local models", model.Header);
        Assert.Equal("No limit · Stays on this PC", model.Rows[0].LocalText);
        Assert.Equal("Ollama · this PC", model.Rows[0].ResetText);
        Assert.Equal("Never runs out", model.Rows[0].HandshakeText);
        Assert.Equal("New threads start on Qwen Coder 32B · local models never run out", model.Footer);
        Assert.False(model.CanCreatePill);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Reorder_moves_only_connected_slots_and_changes_the_same_store_as_Settings()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var hidden = rig.Accounts.Add(ProviderKind.Claude);
        var b = rig.ConnectAnother(ProviderKind.Claude); var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new AccountPopoverModel(port, rig.Accounts);
        model.MoveUp(b.Id); port.Refresh();
        Assert.Equal(new[] { b.Id, hidden.Id, a.Id }, rig.Accounts.ForService(ProviderKind.Claude).Select(x => x.Id));
        Assert.Equal(new[] { b.Id, a.Id }, model.Rows.Select(x => x.Account.Id));
        Assert.Equal(1, model.Rows[0].Account.Number); Assert.True(model.CanCreatePill);
        model.MoveDown(b.Id); port.Refresh(); Assert.Equal(a.Id, model.Rows[0].Account.Id);
        model.MoveUp(a.Id); Assert.Equal(a.Id, rig.Accounts.ForService(ProviderKind.Claude)[0].Id);
        Assert.Throws<ArgumentException>(() => model.Reorder([a.Id, a.Id]));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Pin_and_remember_lines_use_only_the_thread_seam_and_never_set_switching_defaults()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new AccountPopoverModel(port, rig.Accounts);
        Assert.Equal("This thread follows Settings", model.RememberText); Assert.Null(model.PinnedText);
        await model.PinAsync(b.Id); Assert.Equal("This thread: Claude 2 · Back to order", model.PinnedText);
        model.BackToOrder(); Assert.Null(model.PinnedText);
        rig.Decisions.SetThread(port.ThreadId, DecisionKind.OnLimit, DecisionValues.Switch);
        port.Refresh(); Assert.Equal("This thread remembers: Switch automatically · Use Settings", model.RememberText);
        model.UseSettings(); Assert.Equal("This thread follows Settings", model.RememberText);
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.OnLimit)); Assert.False(rig.Handshake.Enabled);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Handshake_aria_and_config_line_are_page_exact_and_fast_is_a_bolt_only()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude);
        rig.Accounts.SetDefaults(a.Id, new() { Model = "Opus 5.5", Effort = "high", FastMode = true, HandshakePoint = new(95) });
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        using var model = new AccountPopoverModel(port, rig.Accounts);
        Assert.Equal("At 95%", model.Rows[0].HandshakeText);
        Assert.Equal("Handshake point for Claude: At 95%", model.Rows[0].HandshakeAria);
        Assert.Equal("Opus 5.5 · High", model.Rows[0].Config.Text); Assert.True(model.Rows[0].Config.Bolt);
        rig.Accounts.SetDefaults(a.Id, new()); port.Refresh();
        Assert.Equal("Handshake point for Claude: Automatically, like all accounts", model.Rows[0].HandshakeAria);
    }
}
