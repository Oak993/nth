using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 13.1 solver fuzz (web surfaces W2; ED-NEW-web-w2-1, traceability alias ED-NEW-webpanes-1): 10,000 random
// workspaces (pane sets, orders, groups, ratios, collapsed flags, workspace sizes from 320 to 3840) keep every invariant
// of PaneLayoutChecks.Problems: no overlap, nothing outside the workspace, every visible pane between its kind's minimum
// and maximum, the chat at least 400, every gutter's value inside its range. Collapse then restore returns the exact
// size before the collapse, or the default when that was the minimum. Fixed seeds, so a failure reproduces.
[Trait("Decision", "ED-NEW-webpanes-1")]
public sealed class PaneLayoutFuzzTests
{
    [Fact]
    public void Ten_thousand_random_workspaces_keep_every_invariant()
    {
        var random = new Random(31415);
        var problems = new List<string>();
        var modes = new int[2];
        for (var i = 0; i < 10_000 && problems.Count < 20; i++)
        {
            var state = PaneLayoutChecks.RandomState(random);
            double w = 320 + random.NextDouble() * 3520, h = 320 + random.NextDouble() * 3520;
            var ltr = PaneLayout.Solve(state, w, h);
            modes[(int)ltr.Mode]++;
            problems.AddRange(PaneLayoutChecks.Problems(state, ltr).Select(p => $"#{i} {w:0.##}x{h:0.##}: {p}"));
            var rtl = PaneLayout.Solve(state, w, h, rightToLeft: true);
            problems.AddRange(PaneLayoutChecks.Problems(state, rtl).Select(p => $"#{i} rtl: {p}"));
            problems.AddRange(PaneLayoutChecks.MirrorProblems(ltr, rtl).Select(p => $"#{i} mirror: {p}"));
        }
        PaneLayoutChecks.AssertNone(problems);
        Assert.True(modes[0] > 5000 && modes[1] > 100, $"tiles {modes[0]}, narrow {modes[1]}");
    }

    [Fact]
    public void Collapse_then_restore_returns_the_exact_size_before_or_the_default_from_the_minimum()
    {
        var random = new Random(27182);
        var checkedCases = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var state = PaneLayoutChecks.RandomState(random) with { Collapsed = Marrow.Core.Port.ValueSet<string>.Empty };
            double w = 320 + random.NextDouble() * 3520, h = 320 + random.NextDouble() * 3520;
            var before = PaneLayout.Solve(state, w, h);
            foreach (var gutter in before.Gutters.Where(g => g.CanCollapse))
            {
                var collapsed = PaneLayout.Collapse(state, w, h, gutter.Key);
                var folded = PaneLayout.Solve(collapsed, w, h);
                Assert.True(folded.Gutter(gutter.Key)!.IsCollapsed, $"#{i} {gutter.Key} did not collapse");
                PaneLayoutChecks.AssertNone(PaneLayoutChecks.Problems(collapsed, folded));
                var restored = PaneLayout.Solve(collapsed.Restore(gutter.Key), w, h).Gutter(gutter.Key)!;
                var expected = PaneLayout.AtCollapsibleMinimum(gutter)
                    ? PaneLayout.Solve(state.WithoutRatio(gutter.Key), w, h).Gutter(gutter.Key)!.Value
                    : gutter.Value;
                Assert.True(restored.Value == expected, $"#{i} {gutter.Key}: restored {restored.Value}, expected {expected}");
                checkedCases++;
            }
        }
        Assert.True(checkedCases > 5000, $"{checkedCases} collapses checked");
    }

    [Fact]
    public void Random_drags_track_the_pointer_inside_the_range_and_keep_every_invariant()
    {
        var random = new Random(16180);
        var problems = new List<string>();
        for (var i = 0; i < 3000 && problems.Count < 20; i++)
        {
            var state = PaneLayoutChecks.RandomState(random);
            double w = 320 + random.NextDouble() * 3520, h = 320 + random.NextDouble() * 3520;
            var origin = state;
            for (var step = 0; step < 6; step++)
            {
                var gutters = PaneLayout.Solve(state, w, h).Gutters;
                if (gutters.Count == 0) break;
                var g = gutters[random.Next(gutters.Count)];
                var span = g.Maximum - g.Minimum;
                var value = g.Minimum - 100 + random.NextDouble() * (span + 200);
                state = PaneLayout.Drag(state, w, h, g.Key, value, origin);
                var after = PaneLayout.Solve(state, w, h);
                problems.AddRange(PaneLayoutChecks.Problems(state, after).Select(p => $"#{i}.{step} drag {g.Key} to {value}: {p}"));
                var moved = after.Gutter(g.Key);
                if (g.IsCollapsed || moved is null || moved.IsCollapsed || value < g.NormalMinimum || value > g.NormalMaximum) continue;
                // 1:1 inside the range; the chat can only gain (it takes what capped cells leave).
                var ok = g.Key == PaneGutterKeys.Chat ? moved.Value >= value - 1e-6 : PaneLayoutChecks.Near(moved.Value, value, 1e-6);
                if (!ok) problems.Add($"#{i}.{step} {g.Key}: asked {value}, got {moved.Value}");
            }
        }
        PaneLayoutChecks.AssertNone(problems);
    }
}
