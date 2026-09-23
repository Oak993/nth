using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/Coding.swift (B2 1fbf81e), :10-17. Lenient<Element>: `value = try? Element(from:)`, so
// an element that fails to decode becomes nil and never fails the surrounding array. Every call site reads
// `container.value(.k, default: [Lenient<T>]()).compactMap(\.value)` (Library.swift:275-276, Timeline.swift:359-363):
// that whole expression is Coding.LenientList (R-CODE-2).
public sealed class CodingLenientTests
{
    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    public void A_bad_element_is_dropped_and_the_rest_survive_in_their_order()
    {
        var root = Root("{\"k\":[1,\"bad\",3,null,2.5,{},[],true,4]}");
        var list = Coding.LenientList(root, "k", SwiftDecoding.ReadInt, () => throw new InvalidOperationException("the default must not run"));
        Assert.Equal(new[] { 1L, 3L, 4L }, list.ToArray());
    }

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    public void A_project_list_keeps_every_readable_project_as_the_library_decoder_does()
    {
        // Library.swift:275 with Project.init(from:) (Library.swift:81-91, ProjectSample): id and path are strict.
        const string json =
            "{\"projects\":[" +
            "{\"id\":\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\",\"path\":\"\\/a\",\"name\":\"A\",\"addedAt\":\"2026-09-22T00:00:00Z\"}," +
            "{\"path\":\"\\/b\",\"name\":\"B\",\"addedAt\":\"2026-09-22T00:00:00Z\"}," +
            "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"path\":7,\"name\":\"C\",\"addedAt\":\"2026-09-22T00:00:00Z\"}," +
            "{\"id\":\"not-a-uuid\",\"path\":\"\\/d\",\"name\":\"D\",\"addedAt\":\"2026-09-22T00:00:00Z\"}," +
            "\"not a project\",null," +
            "{\"id\":\"11111111-2222-3333-4444-555555555555\",\"path\":\"\\/e\",\"name\":\"E\",\"addedAt\":\"2026-09-23T00:00:00Z\"}" +
            "]}";
        var projects = Coding.LenientList(Root(json), "projects", ProjectSample.Read, () => ValueList<ProjectSample>.Empty);
        Assert.Equal(
            new[]
            {
                new ProjectSample(Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef"), "/a", "A", new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero)),
                new ProjectSample(Guid.Parse("11111111-2222-3333-4444-555555555555"), "/e", "E", new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero)),
            },
            projects.ToArray());
    }

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6, through value(_:default:)
    [Trait("Rule", "R-CODE-2")]
    [InlineData("{}")]
    [InlineData("{\"k\":null}")]
    [InlineData("{\"k\":{}}")]
    [InlineData("{\"k\":\"[1,2]\"}")]
    [InlineData("{\"k\":1}")]
    [InlineData("{\"K\":[1,2]}")]
    public void A_missing_null_or_non_array_field_gives_the_default_evaluated_once(string json)
    {
        var calls = 0;
        var list = Coding.LenientList(Root(json), "k", SwiftDecoding.ReadInt, () => { calls++; return new ValueList<long>([-1]); });
        Assert.Equal(new[] { -1L }, list.ToArray());
        Assert.Equal(1, calls);
    }

    [Theory]
    [Trait("Rule", "R-CODE-2")]
    [InlineData("{\"k\":[]}")]
    [InlineData("{\"k\":[\"a\",null,1.5]}")]
    public void An_empty_or_wholly_unreadable_array_gives_an_empty_list_and_not_the_default(string json)
    {
        // Every element decodes as a Lenient holding nil, so compactMap leaves nothing; the array itself was read.
        var list = Coding.LenientList(Root(json), "k", SwiftDecoding.ReadInt, () => throw new InvalidOperationException("the default must not run"));
        Assert.Empty(list);
    }
}
