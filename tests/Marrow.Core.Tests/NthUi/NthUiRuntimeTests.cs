using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, state and recompute (web surfaces W15; ED-NEW-web-w15-1, traceability alias
// ED-NEW-webnthui-1): 7.2 state, let and actions; "a stepper recomputes a table with no model call" (0, 10); 7.4 "State
// changes recompute every let and re-render only nodes that read it"; the editable graph (a drag sets the bound list,
// every let recomputes, the rest follows; Up and Down move a point by the axis step). The canvas's live answer gives
// the numbers: $101, $24, $9 and $134 at 3 instances, 40 GB a day, Europe.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiRuntimeTests
{
    [Fact]
    public void The_canvas_live_answer_computes_its_table_and_recomputes_with_no_model_call()
    {
        var document = NthUiParser.Parse(NthUiSamples.CanvasHosting);
        var runtime = new NthUiRuntime(document);
        var card = document.Roots[0];
        var table = card.Children.Single(n => n.Kind == "table");
        Assert.Equal(["$101", "$24", "$9", "$134"], runtime.Table(table).Rows.Select(r => r.Cells[1]));
        Assert.Equal(["Item", "Monthly"], runtime.Table(table).Columns);
        Assert.True(runtime.Table(table).Rows[3].Total);

        var stepper = card.Children.Single(n => n.Kind == "stepper");
        var changed = runtime.StepInput(stepper, +1);
        Assert.Equal(4, runtime.Get("instances").AsNumber);
        var rows = table.Children;
        Assert.Equal(new[] { stepper.Key, rows[0].Key, rows[3].Key, card.Children.Single(n => n.Kind == "chart").Key }.Order(StringComparer.Ordinal),
            changed.Order(StringComparer.Ordinal));
        Assert.Equal(["$134", "$24", "$9", "$167"], runtime.Table(table).Rows.Select(r => r.Cells[1]));

        var segmented = card.Children.Single(n => n.Kind == "segmented");
        Assert.Equal([("eu", "Europe"), ("us", "US"), ("ap", "Asia")], runtime.Options(segmented));
        Assert.Empty(runtime.SetInput(segmented, NthUiValue.Of("mars")));
        runtime.SetInput(segmented, NthUiValue.Of("us"));
        var slider = card.Children.Single(n => n.Kind == "slider");
        runtime.SetInput(slider, NthUiValue.Of(203));
        Assert.Equal(200, runtime.Get("traffic").AsNumber);
        runtime.SetInput(stepper, NthUiValue.Of(99));
        Assert.Equal(["$736", "$120", "$9", "$865"], runtime.Table(table).Rows.Select(r => r.Cells[1]));
        var chart = runtime.Chart(card.Children.Single(n => n.Kind == "chart"));
        Assert.Equal("bar", chart.Type);
        Assert.Equal(["Compute", "Egress", "Storage"], chart.Labels);
        Assert.Equal([735.84, 120, 9], chart.Series.Single().Values.Select(v => Math.Round(v!.Value, 2)));
    }

    [Fact]
    public void Copy_as_table_send_and_followups_come_back_as_effects()
    {
        var document = NthUiParser.Parse(NthUiSamples.CanvasHosting);
        var runtime = new NthUiRuntime(document);
        var actions = document.Roots[0].Children.Single(n => n.Kind == "actions");
        var copy = runtime.Run(actions.Children[0]);
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Copy, "Item\tMonthly\nCompute\t$101\nEgress\t$24\nStorage\t$9\nTotal per month\t$134"), Assert.Single(copy.Effects));
        Assert.Empty(copy.Changed);
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Send, "Plan for twice the traffic"), Assert.Single(runtime.Run(actions.Children[1]).Effects));
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Send, "Add a CDN"), Assert.Single(runtime.Run(document.Roots[1].Children[1]).Effects));
    }

    [Fact]
    public void A_let_recomputes_only_the_nodes_that_read_it()
    {
        var document = NthUiParser.Parse("""
            state a = 1
            state b = 10
            let twice = a * 2
            let both = twice + b
            text = "a is " + a
            text = "b is " + b
            text = "both " + both
            text "constant"
            """);
        var runtime = new NthUiRuntime(document);
        var texts = document.Nodes.Where(n => n.Kind == "text").ToArray();
        Assert.Equal(["a is 1", "b is 10", "both 12", "constant"], texts.Select(runtime.Text));
        Assert.Equal([texts[0].Key, texts[2].Key], runtime.Set("a", NthUiValue.Of(5)).Order(StringComparer.Ordinal));
        Assert.Equal("both 20", runtime.Text(texts[2]));
        Assert.Equal([texts[1].Key, texts[2].Key], runtime.Set("b", NthUiValue.Of(0)).Order(StringComparer.Ordinal));
        Assert.Empty(runtime.Set("b", NthUiValue.Of(0)));
        Assert.Empty(runtime.Set("twice", NthUiValue.Of(3)));
        Assert.Empty(runtime.Set("a", NthUiValue.Of("not a number")));
        Assert.Empty(runtime.Set("a", NthUiValue.Error("x")));
        Assert.Equal("both 10", runtime.Text(texts[2]));
    }

    [Fact]
    public void Lets_that_loop_or_read_nothing_show_a_dash()
    {
        var document = NthUiParser.Parse("let a = b + 1\nlet b = a + 1\nlet c = nowhere * 2\ntext = a\ntext = c\n");
        var runtime = new NthUiRuntime(document);
        Assert.Equal("b depends on itself", runtime.Get("b").AsText);
        Assert.Equal(["—", "—"], document.Nodes.Where(n => n.Kind == "text").Select(runtime.Text));
        var chain = string.Concat(Enumerable.Range(0, 100).Select(i => $"let v{i + 1} = v{i} + 1\n")) + "state v0 = 0\n";
        var deep = new NthUiRuntime(NthUiParser.Parse(chain));
        Assert.Equal(10, deep.Get("v10").AsNumber);
        Assert.True(deep.Get("v100").IsError);
    }

    [Fact]
    public void Streaming_declares_states_as_they_arrive_and_controls_bring_defaults()
    {
        var parser = new NthUiParser();
        parser.Feed("card \"Pancakes\"\n  text = fmt(people * 50, \"0\") + \" g\"\n");
        var runtime = new NthUiRuntime(parser.Document);
        var text = parser.Document.Nodes[1];
        Assert.Equal("—", runtime.Text(text));
        parser.Feed("  stepper people min=1 max=12\n");
        Assert.Equal([text.Key, parser.Document.Nodes[2].Key], runtime.Sync().Order(StringComparer.Ordinal));
        Assert.Equal("50 g", runtime.Text(text));
        parser.Feed("  state people = 4\n");
        runtime.Sync();
        Assert.Equal("200 g", runtime.Text(text));
        runtime.StepInput(parser.Document.Nodes[2], +1);
        parser.Feed("  state other = 1\n");
        runtime.Sync();
        Assert.Equal(5, runtime.Get("people").AsNumber);

        var defaults = new NthUiRuntime(NthUiParser.Parse("""
            toggle t
            textfield f
            select s options="x:Ex,y:Why"
            slider n min=-5 max=5
            date d
            chips bind=c
              chip "A" selected
              chip "B"
            chips single bind=one
              chip "P"
              chip "Q" selected
            """));
        Assert.Equal(["false", "", "x", "-5", "", "A", "Q"], new[] { "t", "f", "s", "n", "d", "c", "one" }.Select(n => defaults.Get(n).Display()));
        Assert.Equal(NthUiStateType.Selection, defaults.TypeOf("c"));
    }

    [Fact]
    public void Steppers_and_sliders_clamp_and_snap_and_dates_stay_in_range()
    {
        var document = NthUiParser.Parse(NthUiSamples.Form + "\n  slider level min=0 max=1 step=0.1\n  slider free min=0 max=50\n");
        var runtime = new NthUiRuntime(document);
        var node = (string name) => document.Nodes.Single(n => n.Name == name && n.Role == NthUiRole.Input);
        runtime.SetInput(node("level"), NthUiValue.Of(0.73));
        Assert.Equal(0.7, runtime.Get("level").AsNumber);
        runtime.StepInput(node("level"), +1);
        Assert.Equal(0.8, runtime.Get("level").AsNumber);
        Assert.Equal(0.5, runtime.InputStep(node("free")));
        Assert.Equal(1, runtime.InputStep(node("guests")));
        runtime.SetInput(node("guests"), NthUiValue.Of(0));
        Assert.Equal(1, runtime.Get("guests").AsNumber);
        runtime.StepInput(node("guests"), -1);
        Assert.Equal(1, runtime.Get("guests").AsNumber);
        Assert.Empty(runtime.SetInput(node("day"), NthUiValue.Of("12 October")));
        runtime.SetInput(node("day"), NthUiValue.Of("2026-01-01"));
        Assert.Equal("2026-10-12", runtime.Get("day").AsText);
        runtime.SetInput(node("name"), NthUiValue.Of(new string('x', 5000)));
        Assert.Equal(NthUiRuntime.MaxFieldLength, runtime.Get("name").AsText.Length);
        runtime.SetInput(node("name"), NthUiValue.Of("Ada"));
        runtime.SetInput(node("time"), NthUiValue.Of("late"));
        runtime.Run(node("outside"));
        var submit = runtime.Run(document.Roots[0]);
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Send, "Book a table\nName: Ada\nGuests: 1\nDay: 2026-10-12\nTime: 8 pm\nOutside if the weather allows: true\nlevel: 0.8\nfree: 0"),
            Assert.Single(submit.Effects));
    }

    [Fact]
    public void Dragging_an_editable_chart_point_sets_the_bound_list_and_everything_follows()
    {
        var document = NthUiParser.Parse(NthUiSamples.EditableChart);
        var runtime = new NthUiRuntime(document);
        var chart = document.Nodes.Single(n => n.Kind == "chart");
        var stats = document.Nodes.Where(n => n.Kind == "stat").ToArray();
        var data = runtime.Chart(chart);
        Assert.True(data.Editable);
        Assert.Equal("demand", data.Bind);
        Assert.Equal(["Jan", "Feb", "Mar", "Apr", "May", "Jun"], data.Labels);
        Assert.Equal([12.0, 18, 25, 30, 28, 35], data.Series.Single().Values.Select(v => v!.Value));
        Assert.Equal(1, data.Step);
        Assert.Equal("148", runtime.Display(stats[0], "value"));

        var changed = runtime.SetPoint(chart, 5, 47.6);
        Assert.Equal(new[] { chart.Key, stats[0].Key, stats[1].Key }.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
        Assert.Equal("161", runtime.Display(stats[0], "value"));
        Assert.Equal("48", runtime.Display(stats[1], "value"));
        runtime.SetPoint(chart, 0, 75);
        Assert.Equal(60, runtime.Get("demand").Items[0].AsNumber);
        runtime.NudgePoint(chart, 1, -1);
        Assert.Equal(17, runtime.Get("demand").Items[1].AsNumber);
        Assert.Empty(runtime.SetPoint(chart, 9, 1));
        Assert.Empty(runtime.SetPoint(chart, 0, double.NaN));

        var fixedChart = NthUiParser.Parse("chart line y=[1, 2]\n");
        Assert.Empty(new NthUiRuntime(fixedChart).SetPoint(fixedChart.Roots[0], 0, 5));
        Assert.Equal(5, NthUiRuntime.NiceStep(4.2));
        Assert.Equal(0.2, NthUiRuntime.NiceStep(0.17));
        Assert.Equal(10, NthUiRuntime.NiceStep(8));
    }

    [Fact]
    public void Chips_checklists_tabs_and_steps_keep_their_choice()
    {
        var filter = NthUiParser.Parse(NthUiSamples.Filter);
        var runtime = new NthUiRuntime(filter);
        var chips = filter.Nodes.Where(n => n.Kind == "chip").ToArray();
        var table = filter.Nodes.Single(n => n.Kind == "table");
        Assert.Equal([true, false, false], chips.Select(runtime.IsSelected));
        Assert.Equal(["C#"], runtime.Table(table).Rows.Select(r => r.Cells[0]));
        runtime.Run(chips[2]);
        Assert.Equal(["C#", "Rust"], runtime.Table(table).Rows.Select(r => r.Cells[0]));
        runtime.Run(chips[0]);
        Assert.Equal("1 shown", runtime.Text(filter.Nodes[^1]));

        var list = NthUiParser.Parse(NthUiSamples.Checklist);
        var checks = new NthUiRuntime(list);
        var items = list.Nodes.Where(n => n.Kind == "item").ToArray();
        Assert.Equal([true, false, false], items.Select(checks.IsChecked));
        checks.Run(items[1]);
        checks.Run(items[0]);
        Assert.Equal("changelog", checks.Get("done").Display());
        Assert.Equal("1 of 3 done", checks.Text(list.Nodes[^1]));

        var tabsDoc = NthUiParser.Parse(NthUiSamples.Tabs);
        var tabs = new NthUiRuntime(tabsDoc);
        Assert.Equal("You are looking at starter", tabs.Text(tabsDoc.Nodes[^1]));
        tabs.Run(tabsDoc.Nodes.Where(n => n.Kind == "tab").Last());
        Assert.Equal("Team", tabs.SelectedTab(tabsDoc.Roots[0])!.Text);
        Assert.Equal("You are looking at team", tabs.Text(tabsDoc.Nodes[^1]));

        var stepsDoc = NthUiParser.Parse(NthUiSamples.Steps);
        var steps = new NthUiRuntime(stepsDoc);
        var owner = stepsDoc.Roots[1];
        Assert.Equal(0, steps.CurrentStep(owner));
        steps.MoveStep(owner, +1);
        steps.MoveStep(owner, +1);
        steps.MoveStep(owner, +1);
        Assert.Equal(2, steps.CurrentStep(owner));
        steps.Run(owner.Children[0]);
        Assert.Equal(0, steps.Get("step").AsNumber);

        var unbound = NthUiParser.Parse("checklist\n  item \"a\"\n  item \"b\" done\n");
        var own = new NthUiRuntime(unbound);
        own.Run(unbound.Nodes[1]);
        Assert.Equal([true, true], unbound.Nodes.Skip(1).Select(own.IsChecked));
    }

    [Fact]
    public void Buttons_run_their_actions_in_order_and_hidden_ones_do_nothing()
    {
        var document = NthUiParser.Parse(NthUiSamples.Actions);
        var runtime = new NthUiRuntime(document);
        var card = document.Roots[0];
        var actions = card.Children.Single(n => n.Kind == "actions").Children;
        var button = actions[0];
        Assert.False(runtime.IsVisible(button));
        Assert.Same(NthUiActionResult.None, runtime.Run(button));
        runtime.Run(card.Children.Single(n => n.Kind == "toggle"));
        Assert.True(runtime.Get("approved").AsBool);
        var canary = card.Children.Single(n => n.Kind == "table").Children[1];
        Assert.Equal("Approved", runtime.Cells(canary)[1].AsText);

        var promote = runtime.Run(button);
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Call, "", Server: "shipyard", Tool: "promote", Json: "{\"release\":\"r42\",\"channel\":\"stable\"}"), Assert.Single(promote.Effects));
        Assert.False(runtime.Get("approved").AsBool);
        Assert.Contains(canary.Key, promote.Changed);

        Assert.Equal(new NthUiEffect(NthUiEffectKind.Open, "https://example.com/releases/r42"), Assert.Single(runtime.Run(actions[1]).Effects));
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Export, "Check,Status\r\nTests,Passed\r\nCanary,Waiting\r\n", "csv"), Assert.Single(runtime.Run(actions[2]).Effects));
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Context, "release", Json: "{\"release\":\"r42\",\"approved\":false}"), Assert.Single(runtime.Run(actions[3]).Effects));
        Assert.Equal(new NthUiEffect(NthUiEffectKind.Send, "Roll back r42"), Assert.Single(runtime.Run(actions[4]).Effects));
        runtime.Run(actions[5]);
        Assert.True(runtime.Get("approved").AsBool);
        Assert.Equal("Release r42\nI checked the staging build: true\nCheck\tStatus\nTests\tPassed\nCanary\tApproved", runtime.ViewText());
    }

    [Fact]
    public void Saved_state_restores_before_and_after_its_declarations_arrive()
    {
        var first = new NthUiRuntime(NthUiParser.Parse(NthUiSamples.DesignHosting));
        first.Set("instances", NthUiValue.Of(7));
        first.Set("region", NthUiValue.Of("ap"));
        var saved = first.SaveState();
        Assert.Equal("{\"instances\":7,\"region\":\"ap\",\"traffic\":40}", saved);

        var parser = new NthUiParser();
        parser.Feed("card \"Hosting estimate\"\n");
        var second = new NthUiRuntime(parser.Document);
        second.LoadState(saved);
        parser.Feed(NthUiSamples.DesignHosting[(NthUiSamples.DesignHosting.IndexOf('\n', StringComparison.Ordinal) + 1)..]);
        parser.Complete();
        second.Sync();
        Assert.Equal(7, second.Get("instances").AsNumber);
        Assert.Equal("ap", second.Get("region").AsText);
        Assert.Equal(40, second.Get("traffic").AsNumber);
        Assert.Empty(second.LoadState("[1, 2]"));
    }

    [Fact]
    public void Mortgage_savings_and_tip_samples_compute_the_textbook_numbers()
    {
        var mortgage = NthUiParser.Parse(NthUiSamples.Mortgage);
        var m = new NthUiRuntime(mortgage);
        Assert.Equal(["$1,667", "$200,249"], mortgage.Nodes.Where(n => n.Kind == "stat").Select(n => m.Display(n, "value")));
        var balance = m.Chart(mortgage.Nodes.Single(n => n.Kind == "chart"));
        Assert.Equal(25, balance.Labels.Count);
        Assert.Equal(0, Math.Round(balance.Series[0].Values[^1]!.Value, 6));

        var savings = NthUiParser.Parse(NthUiSamples.Savings);
        var s = new NthUiRuntime(savings);
        var table = s.Table(savings.Nodes.Single(n => n.Kind == "table"));
        Assert.Equal(["Year", "Saved", "Balance"], table.Columns);
        Assert.Equal(10, table.Rows.Count);
        Assert.Equal(["10", "$24,000", "$30,186"], table.Rows[^1].Cells);

        var tip = NthUiParser.Parse(NthUiSamples.Tip);
        var t = new NthUiRuntime(tip);
        Assert.Equal("$32.20", t.Display(tip.Nodes.Single(n => n.Kind == "stat"), "value"));
        Assert.Equal("Tip 15% included", t.Text(tip.Nodes[^1]));
        var segmented = tip.Nodes.Single(n => n.Kind == "segmented");
        Assert.Equal([("0.1", "10%"), ("0.15", "15%"), ("0.2", "20%")], t.Options(segmented));
        t.SetInput(segmented, NthUiValue.Of("0.2"));
        Assert.Equal("$33.60", t.Display(tip.Nodes.Single(n => n.Kind == "stat"), "value"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_scatter_chart_plots_y_against_the_numbers_in_x_and_other_charts_keep_x_as_labels()
    {
        var inline = NthUiParser.Parse("chart scatter x=[10, 20, 40] y=[1, 2, 3]\nchart scatter series=\"A,B\" x=[1, 2] y=[[3, 4], [5, 6]]\nchart scatter x=\"a,b\" y=[1, 2]\nchart line x=[10, 20, 40] y=[1, 2, 3]\n");
        var runtime = new NthUiRuntime(inline);
        var charts = inline.Nodes.Where(n => n.Kind == "chart").ToArray();
        Assert.Equal([10.0, 20, 40], runtime.Chart(charts[0]).Series[0].X);
        var two = runtime.Chart(charts[1]);
        Assert.Equal([[1.0, 2], [1.0, 2]], two.Series.Select(s => s.X));
        Assert.Null(runtime.Chart(charts[2]).Series[0].X);
        var line = runtime.Chart(charts[3]);
        Assert.Null(line.Series[0].X);
        Assert.Equal(["10", "20", "40"], line.Labels);
        var seriesLines = NthUiParser.Parse(NthUiSamples.Scatter);
        Assert.Equal([199.0, 349, 499, 799, 999], new NthUiRuntime(seriesLines).Chart(seriesLines.Nodes.Single(n => n.Kind == "chart")).Series[0].X);
    }
}
