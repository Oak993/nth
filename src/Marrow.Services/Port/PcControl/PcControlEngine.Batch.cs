// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.2,
// 2.11, 2.12; critique C10, C21, C26, C29). pc_batch: up to 50 steps across windows in one call, each checked like a call
// of its own and each going through its own window's gates and app stream. In order by default: the first error, mismatch
// or unverified step, or a step that opened a window its expect did not name, stops the batch. With parallel, each
// window's steps run in order and the windows run side by side; a failure stops only its own window's remaining steps. A
// step's expect window_opens with bind names the window it opened ("$dlg") for later steps, and a pc_read step's bind
// carries its text ("$total") into a later pc_type or pc_set_value without sending it to the model. Steps skip the quiet
// window when their check already holds and nothing structural is pending (the batch settle rule); the batch settles once
// at the end, then looks at each window once.
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    private sealed record BatchStep(int Number, string Tool, JsonObject Args, string Window);

    private sealed record BatchOutcome(BatchStep Step, PcResult? Result, string? NotRun)
    {
        public bool Failed => NotRun is null && Result is { Ok: false };
    }

    // The names a batch binds: windows opened by a step, texts read by a step. Each is awaited by the steps that use it.
    private sealed class BatchBinds
    {
        private readonly Dictionary<string, TaskCompletionSource<nint>> windows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<string>> texts = new(StringComparer.Ordinal);
        private readonly Dictionary<nint, PcObservation?> starts = [];
        private readonly Lock gate = new();

        public TaskCompletionSource<nint> Window(string name)
        {
            lock (gate)
            {
                if (!windows.TryGetValue(name, out var source)) windows[name] = source = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
                return source;
            }
        }

        public TaskCompletionSource<string> Text(string name)
        {
            lock (gate)
            {
                if (!texts.TryGetValue(name, out var source)) texts[name] = source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                return source;
            }
        }

        /// <summary>The window's look before the batch first touched it (element numbers mean what they meant there).</summary>
        public PcObservation? Start(nint window, Func<PcObservation?> latest)
        {
            lock (gate)
            {
                if (!starts.TryGetValue(window, out var start)) starts[window] = start = latest();
                return start;
            }
        }

        public IReadOnlyList<nint> Touched
        {
            get
            {
                lock (gate) return [.. starts.Keys];
            }
        }

        public PcObservation? StartOf(nint window)
        {
            lock (gate) return starts.GetValueOrDefault(window);
        }
    }

    private async Task<PcResult> Batch(JsonObject args, PcCall call)
    {
        if (args["actions"] is not JsonArray actions) throw new PcArgsException("actions must be a list of 2 to " + Count(PcToolSchemas.MaxBatchSteps) + " steps");
        if (PcBatchPlan.Problem(actions) is { } problem) throw new PcArgsException(problem);
        var main = Window(PcArgs.Str(args, "window"));
        var observe = PcArgs.Str(args, "observe") ?? "diff";
        if (observe is not ("diff" or "full" or "screenshot" or "none")) throw new PcArgsException("observe must be diff, full, screenshot or none");
        var parallel = PcArgs.Bool(args, "parallel", false);
        var steps = actions.Select((node, i) => (JsonObject)node!).Select((step, i) =>
            new BatchStep(i + 1, PcArgs.Str(step, "tool")!, step, PcArgs.Str(step, "window") ?? PcWindowRef.Format(main))).ToList();
        var binds = new BatchBinds();
        var outcomes = new BatchOutcome?[steps.Count];
        if (parallel)
        {
            var groups = steps.GroupBy(s => s.Window).Select(g => RunGroup(g.ToList(), binds, outcomes, call)).ToList();
            await Task.WhenAll(groups).ConfigureAwait(false);
        }
        else
        {
            await RunGroup(steps, binds, outcomes, call).ConfigureAwait(false);
        }
        call.Timings.Mark("steps");
        // The batch settles once at its end, then looks at each window it touched.
        var report = new StringBuilder();
        foreach (var outcome in outcomes.Where(o => o is not null)) report.Append(Line(outcome!)).Append('\n');
        var failed = outcomes.FirstOrDefault(o => o?.Failed == true);
        var notRun = outcomes.Count(o => o?.NotRun is not null) + outcomes.Count(o => o is null);
        if (notRun > 0) report.Append(Count(notRun)).Append(notRun == 1 ? " step did not run\n" : " steps did not run\n");
        PcImage? image = null;
        IReadOnlyList<PcImage> details = [];
        PcWindowLine? line = null;
        if (observe != "none" || failed is not null)
        {
            foreach (var window in binds.Touched)
            {
                if (parts.Catalog.Window(window) is not { } info)
                {
                    report.Append(PcWindowRef.Format(window)).Append(": the window closed\n");
                    continue;
                }
                await settle.Quiet(window, PcSettle.CapMs, call.Token).ConfigureAwait(false);
                if (observe == "screenshot" && window == main)
                {
                    var looked = await InWindow(window, PcCapability.See, call, (live, inner) => Observe(live, new JsonObject { ["window"] = PcWindowRef.Format(window), ["screenshot"] = "yes" }, inner))
                        .ConfigureAwait(false);
                    report.Append(PcWindowRef.Format(window)).Append(":\n").Append(looked.Body).Append('\n');
                    image = looked.Image;
                    details = looked.Details;
                    line = looked.Window;
                    continue;
                }
                var tree = await Tree(window, fresh: false, call.Token).ConfigureAwait(false);
                if (tree is null) continue;
                var start = binds.StartOf(window);
                var observation = Record(info, tree, CachedVersion(window), null);
                var tail = observe == "full" || start is null ? tree.Render(null, 120) : TreeDiff.Compute(start.Tree, tree, start.Id) ?? "many changes: call pc_observe with full";
                report.Append(PcWindowRef.Format(window)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(info.Title, 80))).Append(":\n").Append(tail).Append('\n');
                if (window == main) line = Line(info, observation);
            }
            call.Timings.Mark("observe");
        }
        line ??= parts.Catalog.Window(main) is { } current ? Line(current, Latest(main)) : null;
        var body = report.ToString().TrimEnd('\n');
        if (failed?.Result is { } bad)
        {
            var reason = "step " + Count(failed.Step.Number) + " (" + failed.Step.Tool + "): " + bad.Reason;
            return bad.Error is { } code
                ? PcResult.Fail(code, reason, line) with { Body = body }
                : PcResult.Unverified(reason, line, body);
        }
        return PcResult.Success(line, body, image) with { Details = details };
    }

    // One window's steps in order (or the whole batch in order); the first failure ends them.
    private async Task RunGroup(IReadOnlyList<BatchStep> steps, BatchBinds binds, BatchOutcome?[] outcomes, PcCall call)
    {
        var stopped = false;
        foreach (var step in steps)
        {
            if (stopped)
            {
                outcomes[step.Number - 1] = new BatchOutcome(step, null, "an earlier step in its window failed");
                Unbind(step, binds);
                continue;
            }
            var outcome = await RunStep(step, binds, call).ConfigureAwait(false);
            outcomes[step.Number - 1] = outcome;
            call.Wait.Done(Line(outcome));
            if (outcome.NotRun is not null || !outcome.Result!.Ok)
            {
                stopped = true;
                Unbind(step, binds);
            }
        }
    }

    // A step that will not run never binds its names: the steps waiting on them do not run either.
    private static void Unbind(BatchStep step, BatchBinds binds)
    {
        if (PcArgs.Expect(step.Args)?.Bind is { } window) binds.Window(window).TrySetCanceled();
        if (step.Args["bind"] is not null && PcArgs.Str(step.Args, "bind") is { } text) binds.Text(text).TrySetCanceled();
    }

    private async Task<BatchOutcome> RunStep(BatchStep step, BatchBinds binds, PcCall call)
    {
        nint handle;
        if (step.Window.StartsWith('$'))
        {
            try { handle = await binds.Window(step.Window).Task.WaitAsync(call.Token).ConfigureAwait(false); }
            catch (TaskCanceledException) when (!call.Token.IsCancellationRequested) { return new BatchOutcome(step, null, "the step that opens " + step.Window + " did not open it"); }
        }
        else
        {
            handle = Window(step.Window);
        }
        var args = (JsonObject)step.Args.DeepClone();
        args.Remove("tool");
        args.Remove("bind");
        args["window"] = PcWindowRef.Format(handle);
        foreach (var field in new[] { "text", "value" })
        {
            if (args[field] is JsonValue value && value.TryGetValue<string>(out var name) && PcArgs.IsName(name))
            {
                try { args[field] = await binds.Text(name).Task.WaitAsync(call.Token).ConfigureAwait(false); }
                catch (TaskCanceledException) when (!call.Token.IsCancellationRequested) { return new BatchOutcome(step, null, "the pc_read that binds " + name + " did not run"); }
            }
        }
        var spec = PcToolSchemas.Find(step.Tool)!;
        var start = binds.Start(handle, () => Latest(handle));
        var inner = new PcCall(new PcTimings(parts.Clock), new PcAuditDraft(step.Tool), call.Token, call.Wait) { InBatch = true, Base = start };
        var result = await Mapped(() => InWindow(handle, spec.ReadOnly ? PcCapability.See : PcCapability.Act, inner, (info, c) => Step(step.Tool, info, args, c))).ConfigureAwait(false);
        if (result.Ok && step.Args["bind"] is not null && PcArgs.Str(step.Args, "bind") is { } bound)
            binds.Text(bound).TrySetResult(inner.Output.ReadText ?? "");
        if (result.Ok && PcArgs.Expect(step.Args) is { Bind: { } name2 } expect)
        {
            var match = inner.Output.Opened.FirstOrDefault(w => expect.WindowOpens is null || PcVerify.Fits(w, expect.WindowOpens));
            if (match is null) result = PcResult.Fail(PcErrorCode.ExpectFailed, "no window opened to bind " + name2, result.Window) with { Body = result.Body };
            else binds.Window(name2).TrySetResult(match.Handle);
        }
        // A window the step's expect did not name is an unexpected state: the batch stops there and reports it (DESIGN.md
        // 2.12: a batch never moves on after an unexpected state; section 5.5 late dialog).
        if (result.Ok && PcArgs.Expect(step.Args)?.WindowOpens is null && inner.Output.Opened.Count > 0)
        {
            var first = inner.Output.Opened[0];
            result = PcResult.Fail(PcErrorCode.ExpectFailed, "a window opened that the step did not expect: " + PcWindowRef.Format(first.Handle) + " "
                + PcWindowLine.Quote(TreeFormatter.Clean(first.Title, 80)) + "; look at it before going on", result.Window) with { Body = result.Body };
        }
        WriteAudit(inner, result);
        return new BatchOutcome(step, result, null);
    }

    // "3. pc_click w:1a: ok: pressed button "Save"" for the report; a read's bound text is not repeated.
    private static string Line(BatchOutcome outcome)
    {
        var step = outcome.Step;
        var text = new StringBuilder(Count(step.Number)).Append(". ").Append(step.Tool).Append(' ').Append(step.Window).Append(": ");
        if (outcome.NotRun is { } why) return text.Append("not run: ").Append(why).ToString();
        var result = outcome.Result!;
        if (!result.Ok) return text.Append(result.Render().Split('\n')[0]).ToString();
        var first = result.Body?.Split('\n')[0] ?? "done";
        if (step.Tool == PcToolSchemas.Read && step.Args["bind"] is not null) first = "read into " + PcArgs.Str(step.Args, "bind");
        var rung = result.Body?.Split('\n').FirstOrDefault(l => l.StartsWith("rung: ", StringComparison.Ordinal));
        return text.Append("ok: ").Append(first.StartsWith("done: ", StringComparison.Ordinal) ? first[6..] : first).Append(rung is null ? "" : " (" + rung + ")").ToString();
    }
}
