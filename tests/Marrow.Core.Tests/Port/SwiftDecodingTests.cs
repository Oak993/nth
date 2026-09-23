using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Core.Port.SwiftDecoding;

namespace Marrow.Core.Tests.Port;

// R-CODE reading rules (IMPLEMENTING.md 3.8). Source: draft A (proto-A SwiftDecodingTests) and draft C (Lenient).
public sealed class SwiftDecodingTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(Encoding.UTF8.GetBytes(text)).RootElement.Clone();

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    [Trait("Decision", "ED-072")]
    public void Synthesized_fields_are_strict_even_with_a_declared_default()
    {
        var ok = UserMessageSample.Read(Json("{\"text\":\"hi\",\"attachments\":[]}"));
        Assert.Null(ok.HydraHeads);
        var missing = Assert.Throws<DecodingError>(() => UserMessageSample.Read(Json("{\"text\":\"hi\"}")));
        Assert.Equal("keyNotFound", missing.Kind);
        Assert.Equal("valueNotFound", Assert.Throws<DecodingError>(() => UserMessageSample.Read(Json("{\"text\":null,\"attachments\":[]}"))).Kind);
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    public void Default_System_Text_Json_binding_is_too_lenient()
    {
        // The pitfall: default binding keeps the C# default for a missing key where Swift throws keyNotFound.
        var bound = JsonSerializer.Deserialize<LenientBindingProbe>("{\"text\":\"hi\"}")!;
        Assert.Empty(bound.Attachments);
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    public void DecodeIfPresent_gives_nil_for_missing_or_null_but_throws_on_a_wrong_type()
    {
        Assert.Null(UserMessageSample.Read(Json("{\"text\":\"hi\",\"attachments\":[],\"hydraBrief\":null}")).HydraBrief);
        Assert.Equal("typeMismatch", Assert.Throws<DecodingError>(() => UserMessageSample.Read(Json("{\"text\":\"hi\",\"attachments\":[],\"hydraBrief\":1}"))).Kind);
    }

    [Theory]
    [Trait("Rule", "R-CODE-2")]
    [InlineData("{}")]
    [InlineData("{\"x\":null}")]
    [InlineData("{\"x\":\"1\"}")]
    [InlineData("{\"x\":false}")]
    [InlineData("{\"x\":[]}")]
    [InlineData("{\"x\":1.5}")]
    public void Value_falls_back_lazily_and_freshly_on_missing_null_or_wrong_type(string json)
    {
        var calls = 0;
        Assert.Equal(42, Value(Json(json), "x", ReadInt, () => { calls++; return 42L; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    public void A_custom_initializer_keeps_its_own_strict_and_lenient_split()
    {
        using var runtime = new TestRuntime();
        var project = ProjectSample.Read(Json("{\"id\":\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\",\"path\":\"C:/code/app/\",\"addedAt\":7}"));
        Assert.Equal("app", project.Name);                  // name missing: the lastPathComponent default
        Assert.Equal(SwiftDate.Now, project.AddedAt);       // addedAt of the wrong type: .now, evaluated then
        Assert.Throws<DecodingError>(() => ProjectSample.Read(Json("{\"path\":\"x\"}"))); // id is strict
    }

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    public void A_lenient_list_drops_the_bad_element_and_keeps_the_rest()
    {
        var steps = LenientList(Json("{\"items\":[\"a\",1,null,\"b\"]}"), "items", ReadString, () => ValueList<string>.Empty);
        Assert.Equal(["a", "b"], steps);
        Assert.Empty(LenientList(Json("{\"items\":{}}"), "items", ReadString, () => ValueList<string>.Empty));
    }

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    [Trait("Rule", "R-ERR-1")]
    public void Value_swallows_every_thrown_error_and_propagates_traps()
    {
        var root = Json("{\"x\":1}");
        foreach (var error in new Exception[]
        {
            new DecodingError("dataCorrupted", "bad"), new EncodingError("thrown"),
            new FormatException("thrown"), new InvalidOperationException("thrown"),
            new ArgumentException("thrown"), new System.IO.IOException("thrown"),
            new Exception("thrown"), new CancellationError(),
        })
            Assert.Equal(5L, Value<long>(root, "x", _ => throw error, () => 5L));

        Assert.Throws<SwiftTrapException>(() => Value<long>(root, "x", _ => throw SwiftTraps.Fatal("trap"), () => 5L));
        Assert.Throws<OverflowException>(() => Value<long>(root, "x", _ => throw new OverflowException(), () => 5L));
        Assert.Throws<IndexOutOfRangeException>(() => Value<long>(root, "x", _ => throw new IndexOutOfRangeException(), () => 5L));
        Assert.Throws<InvalidCastException>(() => Value<long>(root, "x", _ => throw new InvalidCastException(), () => 5L));
    }

    [Fact]
    [Trait("Rule", "R-CODE-2")]
    [Trait("Rule", "R-ERR-1")]
    public void LenientList_drops_each_thrown_error_and_keeps_the_other_elements()
    {
        var root = Json("{\"items\":[1,2,3]}");
        foreach (var error in new Exception[]
        {
            new DecodingError("dataCorrupted", "bad"), new EncodingError("thrown"),
            new FormatException("thrown"), new InvalidOperationException("thrown"),
            new ArgumentException("thrown"), new System.IO.IOException("thrown"),
            new Exception("thrown"), new CancellationError(),
        })
        {
            var values = LenientList(root, "items", element => element.GetInt32() == 2 ? throw error : ReadInt(element),
                () => ValueList<long>.Empty);
            Assert.Equal([1L, 3L], values);
        }

        Assert.Throws<SwiftTrapException>(() => LenientList(root, "items", _ => throw SwiftTraps.Fatal("trap"), () => ValueList<long>.Empty));
        Assert.Throws<OverflowException>(() => LenientList(root, "items", _ => throw new OverflowException(), () => ValueList<long>.Empty));
        Assert.Throws<IndexOutOfRangeException>(() => LenientList(root, "items", _ => throw new IndexOutOfRangeException(), () => ValueList<long>.Empty));
        Assert.Throws<InvalidCastException>(() => LenientList(root, "items", _ => throw new InvalidCastException(), () => ValueList<long>.Empty));
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    public void Int_decoding_follows_JSONDecoder()
    {
        Assert.Equal(1, ReadInt(Json("1.0")));
        Assert.Equal(100, ReadInt(Json("1e2")));
        Assert.Equal("dataCorrupted", Assert.Throws<DecodingError>(() => ReadInt(Json("1.5"))).Kind);
        Assert.Equal("typeMismatch", Assert.Throws<DecodingError>(() => ReadInt(Json("\"1\""))).Kind);
        Assert.Equal("typeMismatch", Assert.Throws<DecodingError>(() => ReadBool(Json("1"))).Kind);
    }

    [Fact]
    [Trait("Rule", "R-ENUM-2")]
    [Trait("Rule", "R-CODE-1")]
    public void An_enum_with_associated_values_round_trips_as_case_then_underscore_zero()
    {
        var todos = new ContentSample.Todos(new(["one", "two"]));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) todos.Write(writer);
        var json = SwiftJson.WriteString(JsonDocument.Parse(stream.ToArray()).RootElement, SwiftJsonStyle.Storage);
        Assert.Equal("{\"todos\":{\"_0\":[\"one\",\"two\"]}}", json);
        Assert.Equal(todos, ContentSample.Read(Json(json)));
        Assert.Throws<DecodingError>(() => ContentSample.Read(Json("{\"todos\":{\"_0\":[]},\"user\":{}}")));
    }

    [Fact]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-09")]
    public void JSONSerialization_numbers_classify_by_lexical_form()
    {
        Assert.True(SwiftJSONNumber.IsInt(Json("1"), out var one));
        Assert.Equal(1, one);
        Assert.False(SwiftJSONNumber.IsInt(Json("1.0"), out _));
        Assert.False(SwiftJSONNumber.IsInt(Json("1e2"), out _));
    }
}
