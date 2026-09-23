using System.Reflection;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// DroppyCode/Core/Support/ObservedValue.swift (B2, 1fbf81e). Every expectation below is read from that file and from
// record PR-STORE-128: "@MainActor @Observable final" with one "var value: Value" set by "init(_ value: Value)",
// and the doc comment at :4-5 ("a change to any other item never re-renders it").
// A "render" here is what SwiftUI does for a row body: evaluate it under observation tracking, and evaluate it again
// when something it read changes (R-OBS-4, ObservationTracking.Track). The rows read ChatThreadSample, the trimmed
// shape of DroppyCode/Core/Models/Library.swift:121-126 ChatThread ("struct ChatThread: Codable, Identifiable,
// Hashable, Sendable {" ... "var id: UUID" ... "var title: String") kept in Fixtures/ObservationSamples.cs. ChatThread
// is Hashable, so Equatable: record equality models it, and two equal values really are equal Swift values.
//
// The Req("PR-STORE-128") traits in this file prove the whole of PR-STORE-128 as split by the ruling on STOP
// Core.Support.ObservedValue #1. Cell creation in AppModel.init and the sync that writes only changed items
// (AppModel.swift:137-140, :148-177) are PR-STORE-171, owned by App.AppModel.
public sealed class ObservedValueTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000A");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000B");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000C");

    [Fact]
    [Trait("Req", "PR-STORE-128")] // DroppyCode/Core/Support/ObservedValue.swift:4-5, :9
    public void Renaming_one_thread_re_renders_only_the_row_that_reads_its_cell()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var cells = new[]
            {
                new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha")),
                new ObservedValue<ChatThreadSample?>(new ChatThreadSample(B, "Beta")),
                new ObservedValue<ChatThreadSample?>(new ChatThreadSample(C, "Gamma")),
            };
            var renders = new int[3];
            var titles = new string?[3];
            for (var row = 0; row < cells.Length; row++) RenderRow(cells, row, renders, titles);
            Assert.Equal([1, 1, 1], renders);
            Assert.Equal<IEnumerable<string?>>(new string?[] { "Alpha", "Beta", "Gamma" }, titles);

            cells[1].Value = cells[1].Value! with { Title = "Beta 2" };
            Assert.Equal([1, 2, 1], renders);
            Assert.Equal<IEnumerable<string?>>(new string?[] { "Alpha", "Beta 2", "Gamma" }, titles);

            cells[1].Value = cells[1].Value! with { Title = "Beta 3" };
            Assert.Equal([1, 3, 1], renders);
            Assert.Equal("Beta 3", titles[1]);

            // The thread in the last row goes away: its cell is set to nil, and only that row renders again.
            cells[2].Value = null;
            Assert.Equal([1, 3, 2], renders);
            Assert.Null(titles[2]);
            Assert.Equal("Alpha", titles[0]);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-128")] // DroppyCode/Core/Support/ObservedValue.swift:4-5, :9
    public void A_binding_to_one_cell_is_told_only_about_that_cell()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var alpha = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha"));
            var beta = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(B, "Beta"));
            using var alphaLog = new ChangeLog(alpha);
            using var betaLog = new ChangeLog(beta);

            beta.Value = new ChatThreadSample(B, "Beta 2");

            Assert.Empty(alphaLog.Names);
            Assert.Equal(["Value"], betaLog.Names);
            Assert.Equal("Alpha", alpha.Value!.Title);
            Assert.Equal("Beta 2", beta.Value!.Title);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-128")] // DroppyCode/Core/Support/ObservedValue.swift:4-5, :9
    public void A_view_that_reads_two_cells_follows_both_and_ignores_a_third()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var first = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha"));
            var second = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(B, "Beta"));
            var other = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(C, "Gamma"));
            var renders = 0;
            void Render()
            {
                renders++;
                ObservationTracking.Track(() => (first.Value?.Title, second.Value?.Title), Render);
            }
            Render();
            Assert.Equal(1, renders);

            other.Value = new ChatThreadSample(C, "Gamma 2");
            Assert.Equal(1, renders);

            first.Value = new ChatThreadSample(A, "Alpha 2");
            Assert.Equal(2, renders);

            second.Value = new ChatThreadSample(B, "Beta 2");
            Assert.Equal(3, renders);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-128")] // DroppyCode/Core/Support/ObservedValue.swift:8-13
    public void The_box_holds_exactly_one_value_set_by_its_initializer()
    {
        var type = typeof(ObservedValue<ChatThreadSample?>);
        Assert.True(type.IsSealed); // :8 final
        Assert.True(type.IsSubclassOf(typeof(MainActorModel))); // :6-7 @MainActor @Observable, R-OBS-6

        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;
        var property = Assert.Single(type.GetProperties(declared));
        Assert.Equal("Value", property.Name); // :9 var value
        Assert.Equal(typeof(ChatThreadSample), property.PropertyType);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsPublic); // var, internal in Swift: readable and writable by the app
        var field = Assert.Single(type.GetFields(declared)); // one stored property, one backing field (R-OBS-1)
        Assert.Equal(typeof(ChatThreadSample), field.FieldType);

        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        var parameter = Assert.Single(constructor.GetParameters()); // :11 init(_ value: Value)
        Assert.Equal(typeof(ChatThreadSample), parameter.ParameterType);

        using var runtime = new TestRuntime();
        var read = runtime.OnMain(() => new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha")).Value);
        Assert.Equal(new ChatThreadSample(A, "Alpha"), read); // :12 self.value = value
        Assert.Equal(7, runtime.OnMain(() => new ObservedValue<int>(7).Value));
    }

    [Fact]
    [Trait("Req", "PR-STORE-128")] // DroppyCode/Core/Support/ObservedValue.swift:6
    public async Task A_write_off_the_main_actor_traps()
    {
        using var runtime = new TestRuntime();
        var cell = runtime.OnMain(() => new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha")));
        runtime.OnMain(() => cell.Value = new ChatThreadSample(A, "Alpha 2"));
        Assert.Equal("Alpha 2", runtime.OnMain(() => cell.Value!.Title));

        await Assert.ThrowsAsync<SwiftTrapException>(() => Task.Run(() => cell.Value = new ChatThreadSample(A, "Off main")));
    }

    [Fact]
    [Trait("Rule", "R-OBS-3")]
    [Trait("Decision", "ED-070")]
    [Trait("Oracle", "O-05")]
    public void Every_assignment_notifies_because_the_value_type_is_unconstrained()
    {
        // UNVERIFIED (O-05, R-TEST-6): this expectation is the port's parked rule, not a Swift fact.
        // ObservedValue.swift:8-9 puts no Equatable constraint on Value, so R-OBS-3 ports the property with
        // SetAlways, which tells trackers and bindings on every assignment. Whether Swift 6.2 Observation notifies
        // for an equal value is open item O-05, and the Swift file does not say. The two values below are equal
        // (ChatThread is Hashable, so Equatable, Library.swift:121; the record compares by fields), so this test
        // asserts the parked rule and carries no Req trait. When O-05 settles, the expectation is replaced by the
        // settled value.
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var cell = new ObservedValue<ChatThreadSample?>(new ChatThreadSample(A, "Alpha"));
            using var log = new ChangeLog(cell);
            var fired = 0;
            ObservationTracking.Track(() => cell.Value, () => fired++);

            cell.Value = new ChatThreadSample(A, "Alpha");

            Assert.Equal(["Value"], log.Names);
            Assert.Equal(1, fired);
            cell.Value = new ChatThreadSample(A, "Alpha");
            Assert.Equal(["Value", "Value"], log.Names);
        });
    }

    private static void RenderRow(ObservedValue<ChatThreadSample?>[] cells, int row, int[] renders, string?[] titles)
    {
        renders[row]++;
        titles[row] = ObservationTracking.Track(
            () => cells[row].Value?.Title,
            () => RenderRow(cells, row, renders, titles)).Value;
    }
}
