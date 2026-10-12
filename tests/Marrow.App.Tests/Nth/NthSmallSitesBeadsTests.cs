using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Nth;

// Calculi WU6 (DESIGN.md sections 6, 7, 9 and 12; ED-NEW-calculi-wu6-1): the small sites. Each place has the section 9 size, family and rate; every running
// sidebar row animates, each on its own phase; the Hydra chip keeps one set of beads through its rebuilds; a hidden host asks for nothing; Reduce Motion
// holds the formation still with no fades.
[Collection("Headless")]
public sealed class NthSmallSitesBeadsTests(HeadlessSession headless)
{
    private const double Slot = 1 / 60.0;

    private static void Pump(ManualTimeProvider time, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            time.Advance(TimeSpan.FromSeconds(Slot));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Avalonia.Controls.Window Show(Control content, bool calm = false)
    {
        var window = new Avalonia.Controls.Window { Width = 160, Height = 220, Content = content, Background = Avalonia.Media.Brushes.Black };
        if (calm) EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        window.Show();
        return window;
    }

    private static double Travel(in NthBeadsSample a, in NthBeadsSample b)
    {
        var most = 0.0;
        for (var i = 0; i < 3; i++)
        {
            double dx = (a[i].X + a.FlowX) - (b[i].X + b.FlowX), dy = (a[i].Y + a.FlowY) - (b[i].Y + b.FlowY);
            most = Math.Max(most, Math.Sqrt(dx * dx + dy * dy) / NthBeadsGeometry.BeadWidth);
        }
        return most;
    }

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 9's table: size, family and frame rate of each small site
    [InlineData(NthBeadsPlace.SidebarRow, 12.0, true, true, 2, NthBeadsState.Tool)]
    [InlineData(NthBeadsPlace.HydraChip, 12.0, true, true, 2, NthBeadsState.Hydra)]
    [InlineData(NthBeadsPlace.HydraHeadFooter, 12.0, true, true, 2, NthBeadsState.Hydra)]
    [InlineData(NthBeadsPlace.HydraBadge, 12.0, true, true, 2, NthBeadsState.Hydra)]
    [InlineData(NthBeadsPlace.TasksChip, 12.0, true, true, 2, NthBeadsState.Hydra)]
    [InlineData(NthBeadsPlace.BackgroundRow, 16.0, true, true, 2, NthBeadsState.Background)]
    [InlineData(NthBeadsPlace.HelperCapsule, 14.0, false, false, 1, NthBeadsState.Thinking)]
    [InlineData(NthBeadsPlace.TactusPill, 14.0, false, false, 1, NthBeadsState.Tactus)]
    public void Each_place_has_the_size_family_and_rate_the_table_gives(NthBeadsPlace place, double size, bool quiet, bool ambient, int every, NthBeadsState state) =>
        headless.OnUiThread(() =>
        {
            var spec = NthBeadsSites.SpecOf(place);
            Assert.Equal((size, quiet, ambient, state), (spec.Size, spec.Quiet, spec.Ambient, spec.State));
            var slot = new NthBeadsSlot(place, "k");
            var window = Show(slot);
            try
            {
                Assert.Equal(new Size(size, size), slot.Bounds.Size);
                var beads = slot.Beads;
                Assert.Equal(size, beads.GlyphSize); Assert.Equal(quiet, beads.Quiet); Assert.Equal(ambient, beads.Ambient);
                Assert.Equal(every, beads.Current.Every);
                Assert.Equal(state, beads.Current.State);
                Assert.True(slot.IsRunning);
                // the beads never clip: the slot, and the control inside it, draw past their box into the row's padding
                Assert.False(slot.ClipToBounds); Assert.False(beads.ClipToBounds);
                // below 14 DIP the painter is flat (section 9)
                Assert.Equal(size < NthBeadsPainter.FlatBelow, NthBeadsPainter.IsFlat(size, false));
            }
            finally { window.Close(); }
        });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 6: the Tactus pill holds Clavis, the background row Pedes with Pulsus, the chips Tripus
    public void The_families_of_the_places_run_on_the_hubs_the_states_name() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var hubs = new (NthBeadsPlace Place, NthBeadsHub Hub)[]
        {
            (NthBeadsPlace.SidebarRow, NthBeadsHub.Pedes), (NthBeadsPlace.HydraChip, NthBeadsHub.Tripus), (NthBeadsPlace.TasksChip, NthBeadsHub.Tripus),
            (NthBeadsPlace.BackgroundRow, NthBeadsHub.Pedes), (NthBeadsPlace.TactusPill, NthBeadsHub.Clavis), (NthBeadsPlace.HelperCapsule, NthBeadsHub.Arcus),
        };
        foreach (var (place, hub) in hubs)
        {
            var slot = new NthBeadsSlot(place, "hub-" + place);
            var window = Show(slot);
            try
            {
                Pump(time, 60 * 6);
                var driver = slot.Beads.Painter!.Driver;
                Assert.Equal(NthBeadsMode.Running, driver.Engine!.Mode);
                var sample = driver.Sample;
                Assert.Equal(hub, sample.Hub);
                Assert.Equal(1, sample.B0.O); Assert.Equal(1, sample.B1.O); Assert.Equal(1, sample.B2.O); // opacity exactly 1 between entrance and exit
            }
            finally { window.Close(); }
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; sections 7 and 9: every running sidebar row animates, each on its own phase, so six at once read as calm
    public void Six_running_sidebar_rows_all_animate_each_on_its_own_phase() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var column = new StackPanel { Spacing = 4 };
        var marks = Enumerable.Range(0, 6).Select(i => NthWorkingMarks.Working(Guid.Parse($"6f0000{i:00}-1111-4222-8333-444455556666").ToString())).ToList();
        foreach (var mark in marks) column.Children.Add(mark);
        var control = new NthBeadsSlot(NthBeadsPlace.HelperCapsule, "control-60"); // a 60 Hz site in the same window: the rate the rows are measured against
        column.Children.Add(control);
        var window = Show(column);
        try
        {
            Pump(time, 60 * 2);
            var frames = 60 * 24;
            // the engines' clocks step only where the real handler advances them (OnAnimationFrameUpdate, after the pacer's beat at the frame's rate)
            var steps = new int[marks.Count]; var controlSteps = 0;
            var clocks = marks.Select(mark => mark.Beads.Painter!.Driver.Engine!.Clock).ToArray(); var controlClock = control.Beads.Painter!.Driver.Engine!.Clock;
            var seen = marks.Select(_ => new List<NthBeadsSample>()).ToList();
            var worst30 = 0.0;
            for (var f = 0; f < frames; f++)
            {
                Pump(time, 1);
                for (var m = 0; m < marks.Count; m++)
                {
                    var driver = marks[m].Beads.Painter!.Driver;
                    seen[m].Add(driver.Sample);
                    if (f > 0 && f % 2 == 0) worst30 = Math.Max(worst30, Travel(seen[m][f], seen[m][f - 2])); // a 30 Hz client updates every second slot
                    if (driver.Engine!.Clock != clocks[m]) { steps[m]++; clocks[m] = driver.Engine.Clock; }
                }
                if (control.Beads.Painter!.Driver.Engine!.Clock != controlClock) { controlSteps++; controlClock = control.Beads.Painter.Driver.Engine!.Clock; }
            }
            for (var m = 0; m < marks.Count; m++)
            {
                var driver = marks[m].Beads.Painter!.Driver;
                Assert.Equal(NthBeadsMode.Running, driver.Engine!.Mode);
                Assert.True(driver.Moving, $"row {m} asks for frames");
                Assert.True(marks[m].Beads.FramesDrawn > frames / 2 - 60, $"row {m} was drawn {marks[m].Beads.FramesDrawn} times");
                // advanced at 30 Hz through the real handler (the divisor reaches the compositor): every second slot, while a 60 Hz site in the same
                // window advances in every slot (its redraws do not speed the rows up)
                Assert.True(controlSteps > frames * .95, $"the 60 Hz control advanced in {controlSteps} of {frames} slots");
                Assert.InRange(steps[m] / (double)controlSteps, .45, .55);
                // it animates: in every second of the run the beads travel (none holds a still formation that looks hung)
                for (var second = 0; second < 24; second++)
                {
                    double moved = 0;
                    for (var f = second * 60 + 1; f < (second + 1) * 60; f++) moved = Math.Max(moved, Travel(seen[m][f], seen[m][second * 60]));
                    Assert.True(moved > .04, $"row {m} is still in second {second} ({moved:0.000} bead widths)");
                }
            }
            // each on its own phase: no two rows agree on all their beads for most of the run
            for (var a = 0; a < marks.Count; a++)
                for (var c = a + 1; c < marks.Count; c++)
                {
                    var apart = 0;
                    for (var f = 0; f < frames; f++) if (Travel(seen[a][f], seen[c][f]) > .04) apart++;
                    Assert.True(apart > frames / 3, $"rows {a} and {c} are apart in only {apart} of {frames} frames");
                }
            Assert.True(worst30 <= .5, $"largest step of a 30 Hz row {worst30:0.000} bead widths (gate 0.5)");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 7: rows that begin at the same instant with other keys differ; rows that begin at other times differ
    public void Rows_begun_together_or_apart_do_not_move_together() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var first = NthWorkingMarks.Working("first"); var second = NthWorkingMarks.Working("second");
        var column = new StackPanel { Children = { first, second } };
        var window = Show(column);
        try
        {
            Pump(time, 60 * 5);
            var third = NthWorkingMarks.Working("first"); // the same key, begun 5 s later
            column.Children.Add(third);
            Pump(time, 60 * 5);
            var apart = 0;
            for (var f = 0; f < 60 * 8; f++)
            {
                Pump(time, 1);
                var a = first.Beads.Painter!.Driver.Sample; var b = second.Beads.Painter!.Driver.Sample; var c = third.Beads.Painter!.Driver.Sample;
                if (Travel(a, c) > .04 && Travel(a, b) > .04) apart++;
            }
            Assert.True(apart > 60 * 2, $"{apart} frames apart");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 8: Reduce Motion holds one still formation; no frames; opacity 1
    public void With_Reduce_Motion_a_row_holds_its_formation_still_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var mark = NthWorkingMarks.Working("calm-row");
        var window = Show(mark, calm: true);
        try
        {
            Pump(time, 60 * 3);
            Assert.True(mark.Beads.Current.Calm);
            var driver = mark.Beads.Painter!.Driver;
            Assert.False(driver.Moving);
            var requests = mark.Beads.FrameRequests;
            var before = driver.Sample;
            Pump(time, 60 * 3);
            Assert.Equal(requests, mark.Beads.FrameRequests);
            Assert.Equal(0, Travel(before, driver.Sample), 9);
            // Pedes held still, three clear beads, each at scale 1 and opacity 1
            Assert.Equal(NthBeadsHub.Pedes, driver.Sample.Hub);
            for (var i = 0; i < 3; i++) { Assert.Equal(1, driver.Sample[i].O); Assert.Equal(1, driver.Sample[i].S, 6); }
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 11: a mark whose row is hidden asks for no frames, and goes on from the same turn when shown
    public void A_row_that_is_hidden_asks_for_nothing_and_goes_on_when_shown() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var mark = NthWorkingMarks.Working("hidden-row");
        var window = Show(mark);
        try
        {
            Pump(time, 60 * 3);
            Assert.True(mark.Beads.IsMoving);
            mark.IsVisible = false;
            Pump(time, 4);
            var requests = mark.Beads.FrameRequests; var clock0 = mark.Beads.Painter!.Driver.Engine!.Clock;
            Pump(time, 60 * 5);
            Assert.False(mark.Beads.IsMoving);
            Assert.Equal(requests, mark.Beads.FrameRequests);
            Assert.Equal(clock0, mark.Beads.Painter.Driver.Engine!.Clock);
            mark.IsVisible = true;
            Pump(time, 30);
            Assert.True(mark.Beads.IsMoving);
            Assert.Equal(1, mark.Beads.Current.Turn); // the same turn: nothing replays
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 12: the helper capsule's beads follow its running state; it never waits for them
    public void The_helper_capsule_begins_and_ends_with_its_state_and_a_second_run_is_a_new_turn() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var box = new SubagentWorkingBox("Helper", false);
        var window = Show(box);
        try
        {
            var slot = Assert.Single(box.GetVisualDescendants().OfType<NthBeadsSlot>());
            Assert.False(box.IsVisible); Assert.False(slot.IsRunning);
            box.Update("Helper", true);
            Assert.True(box.IsVisible); Assert.True(slot.IsRunning); Assert.Equal(1, slot.Beads.Current.Turn);
            Pump(time, 90);
            // the box was built hidden and the slot attached under it: showing the box (an ancestor) shows the beads, with no input event (ED-NEW-calculi-rv-1)
            Assert.True(slot.Beads.Current.Shown, "the beads know their host is shown");
            Assert.True(slot.Beads.IsMoving, "the beads move once the box shows");
            Assert.True(slot.Beads.Painter!.Driver.Sample[0].S > .5, "the entrance has grown the beads");
            box.Update("Helper", true); // the same state again: nothing happens
            Assert.Equal(1, slot.Beads.Current.Turn);
            box.Update("Helper", false);
            Pump(time, 90);
            Assert.False(box.IsVisible);
            Assert.False(slot.IsRunning); Assert.Equal(NthBeadsExit.Finish, slot.Beads.Current.Exit);
            box.Update("Helper", true);
            Pump(time, 90);
            Assert.True(slot.IsRunning); Assert.Equal(2, slot.Beads.Current.Turn);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; section 9: the send badge's beads sit in its 14 point content; its morph to the count never moves the badge
    public void The_send_badge_shows_the_beads_while_sending_and_the_count_after_at_one_size() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var badge = new HydraSendWorkingBadge(2, true);
        var window = Show(badge);
        try
        {
            window.UpdateLayout();
            var slot = Assert.Single(badge.GetVisualDescendants().OfType<NthBeadsSlot>());
            Assert.True(slot.IsRunning);
            var sending = badge.Bounds.Size;
            badge.Update(2, false);
            window.UpdateLayout();
            Assert.False(slot.IsRunning);
            Assert.Empty(badge.GetVisualDescendants().OfType<NthBeadsSlot>()); // the count took its place
            Assert.Equal(sending.Height, badge.Bounds.Height, 1); // the same height: the beads' overhang never grew the badge
            badge.Update(2, true);
            window.UpdateLayout();
            Assert.True(slot.IsRunning); Assert.Equal(2, slot.Beads.Current.Turn);
            Assert.Equal(sending, badge.Bounds.Size);
        }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu6-1; sections 9 and 13.1 on the sites: each place's engine stays in the envelope and under the per-frame travel gate at its rate
    [InlineData(NthBeadsPlace.SidebarRow)]
    [InlineData(NthBeadsPlace.HydraChip)]
    [InlineData(NthBeadsPlace.BackgroundRow)]
    [InlineData(NthBeadsPlace.HelperCapsule)]
    [InlineData(NthBeadsPlace.TactusPill)]
    public void Each_places_engine_is_calm_inside_the_envelope_and_under_the_travel_gate_at_its_rate(NthBeadsPlace place)
    {
        var spec = NthBeadsSites.SpecOf(place);
        var every = NthBeadsFrame.EveryFor(spec.Size, spec.Ambient, spec.FullRate);
        var gate = .25 * every; // 0.25 bead widths per 60 Hz frame, 0.5 at 30 Hz (section 11)
        for (var seed = 1u; seed <= 8; seed++)
        {
            var engine = new NthBeadsEngine(seed, spec.Quiet);
            engine.SetState(spec.State, 0);
            engine.Enter(NthBeadsEntrance.Ortus, 0, seed);
            var steps = 60 * 40 / every;
            NthBeadsSample last = default;
            for (var i = 1; i <= steps; i++)
            {
                engine.Update(i * every * Slot);
                var s = engine.Frame();
                Assert.True(s.Present);
                for (var b = 0; b < 3; b++)
                {
                    var a = s[b];
                    Assert.Equal(1, a.O); // no fades between entrance and exit
                    Assert.InRange(a.X - NthBeadsGeometry.BeadRadius * a.S, NthBeadsGeometry.EnvelopeX0, NthBeadsGeometry.EnvelopeX1);
                    Assert.InRange(a.X + NthBeadsGeometry.BeadRadius * a.S, NthBeadsGeometry.EnvelopeX0, NthBeadsGeometry.EnvelopeX1);
                    Assert.InRange(a.Y - NthBeadsGeometry.BeadRadius * a.S, NthBeadsGeometry.EnvelopeY0, NthBeadsGeometry.EnvelopeY1);
                    Assert.InRange(a.Y + NthBeadsGeometry.BeadRadius * a.S, NthBeadsGeometry.EnvelopeY0, NthBeadsGeometry.EnvelopeY1);
                }
                // the entrance is judged by its own gates (stress.js leaves it out of the travel gate too)
                if (i > 1 && last.Present && s.Kind != NthBeadsMoveKind.Enter && last.Kind != NthBeadsMoveKind.Enter) Assert.True(Travel(s, last) <= gate, $"seed {seed}, step {i}: {Travel(s, last):0.000} bead widths (gate {gate})");
                last = s;
            }
        }
    }
}
