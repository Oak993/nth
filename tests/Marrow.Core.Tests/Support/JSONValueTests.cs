using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/JSONValue.swift (B2 1fbf81e), :1-165. Tests first (IMPLEMENTING.md 1.4).
// Every expectation is transcribed from the Swift lines cited on each test and from records PR-STORE-115 to
// PR-STORE-122; none is taken from a C# implementation.
//
// C# surface these tests fix (R-ENUM-2, R-NAME-1 to R-NAME-5):
//   abstract record JSONValue, private constructor, nested sealed cases
//     Null(), Bool(bool Value), Int(long Value), Double(double Value), String(string Value),
//     Array(<value list of JSONValue>), Object(<value map of string to JSONValue>)            :4-12
//   static JSONValue? Parse(byte[] data), static JSONValue? Parse(string string)              :15-24
//   byte[] Data(bool pretty = false), string CompactString, string PrettyString               :59-66
//   JSONValue? this[string key], JSONValue? this[long index]                                  :68-76
//   the six typed accessors take the owner's name as prefix because each Swift member is named like a nested
//   case (R-NAME-3): JSONValueString (Swift: string), JSONValueInt (int), JSONValueDouble (double),
//   JSONValueBool (bool), JSONValueArray (array), JSONValueObject (object)                    :78-113
//   bool IsNull, string DisplayText, static JSONValue Optional(string? value)                 :115-134
//   literal conformances: implicit conversions from bool, long, double and string; static FromNilLiteral(),
//   FromArrayLiteral(params JSONValue[]), FromDictionaryLiteral(params (string, JSONValue)[])  :137-165
// init(foundation:) and foundationObject (:26-57) have no caller outside this file at B2 (grep of the pinned
// tree), so their behaviour is proved through Parse and Data, which are the only paths that reach them.
public sealed class JSONValueTests
{
    private static JSONValue Nil => JSONValue.FromNilLiteral();

    private static JSONValue[] OneOfEachCase() =>
    [
        Nil,
        new JSONValue.Bool(true),
        new JSONValue.Int(42),
        new JSONValue.Double(2.5),
        new JSONValue.String("7"),
        JSONValue.FromArrayLiteral(1),
        JSONValue.FromDictionaryLiteral(("a", 1)),
    ];

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ---- PR-STORE-115: parsing, fragments allowed, nil on failure, seven cases -------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-115")] // Core/Support/JSONValue.swift:4-12
    [Trait("Rule", "R-ENUM-3")]
    public void The_tree_has_exactly_the_seven_cases_null_bool_int_double_string_array_object()
    {
        var names = UnionCoverage.Cases<JSONValue>().Select(t => t.Name).ToArray();
        Assert.Equal(["Array", "Bool", "Double", "Int", "Null", "Object", "String"], names);
        UnionCoverage.AssertCovers(OneOfEachCase());
    }

    [Theory]
    [Trait("Req", "PR-STORE-115")] // Core/Support/JSONValue.swift:15-20
    [InlineData("42", 42L)]
    [InlineData("-7", -7L)]
    [InlineData("0", 0L)]
    public void A_bare_integer_at_the_top_level_parses_as_a_fragment(string text, long expected)
    {
        Assert.Equal(new JSONValue.Int(expected), JSONValue.Parse(Utf8(text)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-115")] // Core/Support/JSONValue.swift:15-20
    public void Bare_string_boolean_null_and_fractional_fragments_parse()
    {
        Assert.Equal(new JSONValue.String("hi"), JSONValue.Parse(Utf8("\"hi\"")));
        Assert.Equal(new JSONValue.Bool(true), JSONValue.Parse(Utf8("true")));
        Assert.Equal(new JSONValue.Bool(false), JSONValue.Parse(Utf8("false")));
        Assert.Equal(new JSONValue.Double(1.5), JSONValue.Parse(Utf8("1.5")));
        var parsedNull = JSONValue.Parse(Utf8("null"));
        Assert.NotNull(parsedNull);
        Assert.IsType<JSONValue.Null>(parsedNull);
    }

    [Theory]
    [Trait("Req", "PR-STORE-115")] // Core/Support/JSONValue.swift:16-18
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[1,")]
    [InlineData("tru")]
    [InlineData("nul")]
    [InlineData("{\"a\" 1}")]
    [InlineData("{a:1}")]
    [InlineData("'x'")]
    [InlineData("\"unterminated")]
    [InlineData("1 2")]
    // :16 passes only [.fragmentsAllowed], not .json5Allowed: trailing commas, comments and non-finite literals are
    // malformed, so a parser configured with AllowTrailingCommas or CommentHandling.Skip fails these.
    [InlineData("[1,]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("/*c*/1")]
    [InlineData("// c\n1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Malformed_input_returns_nil_without_throwing(string text)
    {
        Assert.Null(JSONValue.Parse(Utf8(text)));
        Assert.Null(JSONValue.Parse(text));
    }

    [Fact]
    [Trait("Req", "PR-STORE-115")] // Core/Support/JSONValue.swift:22-24
    public void Parsing_a_string_is_parsing_its_UTF8_bytes()
    {
        // U+2019 RIGHT SINGLE QUOTATION MARK, U+00E9 LATIN SMALL LETTER E WITH ACUTE
        const string text = "{\"k\":[\"couldn\U00002019t\",\"caf\U000000E9\",3]}";
        var fromString = JSONValue.Parse(text);
        var fromBytes = JSONValue.Parse(Utf8(text));
        Assert.NotNull(fromString);
        Assert.Equal(fromBytes, fromString);
        Assert.Equal(new JSONValue.String("couldn\U00002019t"), fromString!["k"]![0]); // U+2019
        Assert.Equal(new JSONValue.String("caf\U000000E9"), fromString["k"]![1]); // U+00E9
        Assert.Equal(new JSONValue.Int(3), fromString["k"]![2]);
    }

    // ---- PR-STORE-116: bridging, boolean versus integer versus double -------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-116")] // Core/Support/JSONValue.swift:28-35
    public void A_JSON_true_is_a_bool_never_the_integer_one()
    {
        var parsed = JSONValue.Parse("[true,1,false,0]");
        Assert.NotNull(parsed);
        Assert.IsType<JSONValue.Bool>(parsed![0]);
        Assert.Equal(new JSONValue.Bool(true), parsed[0]);
        Assert.NotEqual(new JSONValue.Int(1), parsed[0]);
        Assert.IsType<JSONValue.Int>(parsed[1]);
        Assert.Equal(new JSONValue.Int(1), parsed[1]);
        Assert.NotEqual(new JSONValue.Bool(true), parsed[1]);
        Assert.Equal(new JSONValue.Bool(false), parsed[2]);
        Assert.Equal(new JSONValue.Int(0), parsed[3]);
        Assert.Null(parsed[0]!.JSONValueInt); // :83-90, a bool is not an int to the accessor either
        Assert.Null(parsed[1]!.JSONValueBool); // :100-103
    }

    [Fact]
    [Trait("Req", "PR-STORE-116")] // Core/Support/JSONValue.swift:26-45
    public void A_Claude_stream_payload_bridges_recursively_by_runtime_type()
    {
        // Fixture (R-TEST-8): trimmed Claude stream-json lines. Each line is parsed whole by
        // Core/Support/StdioProcess.swift:149 (JSONValue.parse(line)) and read through JSONValue in
        // Services/Providers/ClaudeSession.swift: the "user" line's tool_result block at :549-555 (type, tool_use_id,
        // block["is_error"]?.bool at :554, content through resultText at :777-780); the "result" line at :596
        // (message["is_error"]?.bool, message["subtype"]?.string), :600-601 (errors, result), :829-832 (usage token
        // counts by .int) and :834 (modelUsage contextWindow by .int); the rate_limit_event line at :375-378
        // (status, resetsAt by .double). Only fields the Swift reads are kept; the values are samples.
        var user = JSONValue.Parse(
            "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_01\"," +
            "\"is_error\":true,\"content\":[{\"type\":\"text\",\"text\":\"Exit code 1\"}]}]}}");
        var expectedUser = JSONValue.FromDictionaryLiteral(
            ("type", "user"),
            ("message", JSONValue.FromDictionaryLiteral(
                ("content", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
                    ("type", "tool_result"),
                    ("tool_use_id", "toolu_01"),
                    ("is_error", true),
                    ("content", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "Exit code 1"))))))))));
        Assert.Equal(expectedUser, user);
        var block = user!["message"]!["content"]![0]!;
        Assert.IsType<JSONValue.Object>(block);
        Assert.IsType<JSONValue.Array>(user["message"]!["content"]);
        Assert.IsType<JSONValue.Bool>(block["is_error"]);
        Assert.True(block["is_error"]!.JSONValueBool is true); // :554
        Assert.IsType<JSONValue.Array>(block["content"]);

        // The error result: "is_error":true and "input_tokens":1 side by side in one payload.
        var result = JSONValue.Parse(
            "{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"\"," +
            "\"errors\":[\"x\"],\"usage\":{\"input_tokens\":1,\"cache_read_input_tokens\":2048," +
            "\"cache_creation_input_tokens\":0,\"output_tokens\":12},\"modelUsage\":{\"opus\":{\"contextWindow\":200000}}}");
        var expectedResult = JSONValue.FromDictionaryLiteral(
            ("type", "result"),
            ("subtype", "error_during_execution"),
            ("is_error", true),
            ("result", ""),
            ("errors", JSONValue.FromArrayLiteral("x")),
            ("usage", JSONValue.FromDictionaryLiteral(
                ("input_tokens", 1),
                ("cache_read_input_tokens", 2048),
                ("cache_creation_input_tokens", 0),
                ("output_tokens", 12))),
            ("modelUsage", JSONValue.FromDictionaryLiteral(("opus", JSONValue.FromDictionaryLiteral(("contextWindow", 200000))))));
        Assert.Equal(expectedResult, result);
        Assert.IsType<JSONValue.Bool>(result!["is_error"]);
        Assert.Equal(new JSONValue.Bool(true), result["is_error"]);
        Assert.NotEqual(new JSONValue.Int(1), result["is_error"]);
        Assert.True(result["is_error"]!.JSONValueBool is true); // :596
        Assert.Null(result["is_error"]!.JSONValueInt);
        Assert.IsType<JSONValue.Int>(result["usage"]!["input_tokens"]);
        Assert.Equal(new JSONValue.Int(1), result["usage"]!["input_tokens"]);
        Assert.NotEqual(new JSONValue.Bool(true), result["usage"]!["input_tokens"]);
        Assert.Null(result["usage"]!["input_tokens"]!.JSONValueBool);
        Assert.Equal(1L, result["usage"]!["input_tokens"]!.JSONValueInt); // :829
        Assert.IsType<JSONValue.Int>(result["usage"]!["cache_creation_input_tokens"]);
        Assert.IsType<JSONValue.Int>(result["modelUsage"]!["opus"]!["contextWindow"]);
        Assert.Equal(200000L, result["modelUsage"]!["opus"]!["contextWindow"]!.JSONValueInt); // :834
        Assert.Equal("error_during_execution", result["subtype"]!.JSONValueString); // :596

        var rateLimit = JSONValue.Parse(
            "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"rejected\",\"resetsAt\":1758650400.5}}");
        var expectedRateLimit = JSONValue.FromDictionaryLiteral(
            ("type", "rate_limit_event"),
            ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "rejected"), ("resetsAt", 1758650400.5))));
        Assert.Equal(expectedRateLimit, rateLimit);
        Assert.IsType<JSONValue.Double>(rateLimit!["rate_limit_info"]!["resetsAt"]);
        Assert.Equal(1758650400.5, rateLimit["rate_limit_info"]!["resetsAt"]!.JSONValueDouble); // :378
    }

    [Fact]
    [Trait("Req", "PR-STORE-116")] // Core/Support/JSONValue.swift:42-43
    public void A_JSON_null_member_bridges_to_the_null_case_and_stays_present()
    {
        var parsed = JSONValue.Parse("{\"a\":null}");
        var member = parsed!["a"];
        Assert.NotNull(member);
        Assert.IsType<JSONValue.Null>(member);
        Assert.Single(parsed.JSONValueObject!);
    }

    [Fact]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-09")]
    public void An_integral_number_written_with_a_point_or_exponent_bridges_as_a_double()
    {
        // UNVERIFIED O-09: whether JSONSerialization gives a float-typed NSNumber for 1.0 and 1e2 (so :31-32
        // makes .double) is not settled; PR-STORE-116's windows_path says so. Parked choice: lexical form.
        Assert.Equal(new JSONValue.Double(1.0), JSONValue.Parse("1.0"));
        Assert.Equal(new JSONValue.Double(100.0), JSONValue.Parse("1e2"));
        Assert.IsType<JSONValue.Double>(JSONValue.Parse("[1.0]")![0]);
    }

    // ---- PR-STORE-117: serialisation ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-65
    public void Compact_output_allows_fragments_and_leaves_forward_slashes_unescaped()
    {
        Assert.Equal(Utf8("\"a/b\""), new JSONValue.String("a/b").Data());
        Assert.Equal("\"https://example.com/x/y\"", new JSONValue.String("https://example.com/x/y").CompactString);
        Assert.Equal("42", new JSONValue.Int(42).CompactString);
        Assert.Equal("-7", new JSONValue.Int(-7).CompactString);
        Assert.Equal("true", new JSONValue.Bool(true).CompactString);
        Assert.Equal("false", new JSONValue.Bool(false).CompactString);
        Assert.Equal(Utf8("null"), Nil.Data());
        Assert.Equal("null", Nil.CompactString);
    }

    [Fact]
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-65
    public void Compact_containers_have_no_whitespace_and_slashes_stay_unescaped()
    {
        Assert.Equal("[1,\"x/y\",true,null]", JSONValue.FromArrayLiteral(1, "x/y", true, Nil).CompactString);
        Assert.Equal("{\"url\":\"https://a.b/c\"}", JSONValue.FromDictionaryLiteral(("url", "https://a.b/c")).CompactString);
        Assert.Equal("{\"k\":[1,{\"x\":null}]}",
            JSONValue.FromDictionaryLiteral(("k", JSONValue.FromArrayLiteral(1, JSONValue.FromDictionaryLiteral(("x", Nil))))).CompactString);
        // PiSession.swift:443, :446 and :621 compare compactString with "{}".
        Assert.Equal("{}", JSONValue.FromDictionaryLiteral().CompactString);
        Assert.Equal("[]", JSONValue.FromArrayLiteral().CompactString);
    }

    [Fact]
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-65
    public void Compact_output_escapes_quote_and_backslash_and_writes_non_ASCII_and_HTML_characters_raw()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", new JSONValue.String("a\"b\\c").CompactString);
        // :60 writes with [.fragmentsAllowed, .withoutEscapingSlashes] only, so every scalar but the quote, the
        // backslash and the controls (O-12, not asserted here) is written raw:
        // U+2019 RIGHT SINGLE QUOTATION MARK = E2 80 99; U+00E9 LATIN SMALL LETTER E WITH ACUTE = C3 A9;
        // U+1F600 GRINNING FACE (supplementary plane) = F0 9F 98 80; < > & ' + = 3C 3E 26 27 2B.
        const string text = "\U00002019\U000000E9\U0001F600<>&'+"; // U+2019, U+00E9, U+1F600
        var bytes = new JSONValue.String(text).Data();
        Assert.Equal(
            new byte[] { 0x22, 0xE2, 0x80, 0x99, 0xC3, 0xA9, 0xF0, 0x9F, 0x98, 0x80, 0x3C, 0x3E, 0x26, 0x27, 0x2B, 0x22 },
            bytes);
        Assert.Equal(bytes, Utf8(new JSONValue.String(text).CompactString));
        // Negative control 1 (R-TEST-4): System.Text.Json's default encoder escapes the non-ASCII and the HTML-sensitive
        // characters, so it cannot stand in.
        Assert.NotEqual("\"" + text + "\"", JsonSerializer.Serialize(text));
        // Negative control 2 (R-CODE-3): the relaxed encoder writes U+1F600 as a surrogate-pair escape, not raw.
        var relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Assert.Equal("\"\\uD83D\\uDE00\"", JsonSerializer.Serialize("\U0001F600", relaxed)); // U+1F600
        Assert.NotEqual(new byte[] { 0x22, 0xF0, 0x9F, 0x98, 0x80, 0x22 }, Utf8(JsonSerializer.Serialize("\U0001F600", relaxed)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-66
    public void Pretty_output_is_multi_line_with_keys_sorted_and_slashes_unescaped()
    {
        var value = JSONValue.FromDictionaryLiteral(("b", 1), ("c", "x/y"), ("a", JSONValue.FromArrayLiteral(true, Nil)));
        var pretty = value.PrettyString;
        Assert.Contains("\n", pretty, StringComparison.Ordinal);
        var a = pretty.IndexOf("\"a\"", StringComparison.Ordinal);
        var b = pretty.IndexOf("\"b\"", StringComparison.Ordinal);
        var c = pretty.IndexOf("\"c\"", StringComparison.Ordinal);
        Assert.True(a >= 0 && a < b && b < c, pretty);
        Assert.Contains("\"x/y\"", pretty, StringComparison.Ordinal);
        Assert.DoesNotContain("\\/", pretty, StringComparison.Ordinal);
        Assert.Equal(Utf8(pretty), value.Data(pretty: true));
        Assert.Equal(value, JSONValue.Parse(pretty)); // pretty output is still the same JSON tree
    }

    [Fact]
    [Trait("Req", "PR-STORE-117")] // Core/Support/JSONValue.swift:59-65
    public void Data_without_arguments_is_the_compact_form()
    {
        var value = JSONValue.FromDictionaryLiteral(("k", JSONValue.FromArrayLiteral(1, "a/b")));
        Assert.Equal(Utf8("{\"k\":[1,\"a/b\"]}"), value.Data());
        Assert.Equal(value.Data(), value.Data(pretty: false));
        Assert.Equal(Utf8(value.CompactString), value.Data());
    }

    [Theory]
    [Trait("Rule", "R-CODE-4")]
    [Trait("Oracle", "O-25")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_the_writer_refuses_falls_back_to_the_four_bytes_null_for_the_whole_output(double value)
    {
        // UNVERIFIED (R-TEST-6), so this test proves no requirement: PR-STORE-117's failure fallback rests on it.
        // Core/Support/JSONValue.swift:62 is `(try? JSONSerialization.data(...)) ?? Data("null".utf8)`, and `try?`
        // catches only a thrown Swift error. Whether Darwin JSONSerialization throws for a non-finite NSNumber (so the
        // fallback gives "null") or raises an Objective-C NSInvalidArgumentException (an uncatchable crash) is not
        // settled; swift-corelibs-foundation throws. No Swift site reaches this path (.double(NaN) is built only at
        // :32 and :150, and parsed JSON holds no NaN). "O-25" is the id proposed for this new oracle item; it is not
        // yet in IMPLEMENTING.md Appendix B. Parked choice: the whole write, compact and pretty, is the bytes "null".
        Assert.Equal(new byte[] { 0x6E, 0x75, 0x6C, 0x6C }, new JSONValue.Double(value).Data());
        var nested = JSONValue.FromDictionaryLiteral(("ok", 1), ("bad", JSONValue.FromArrayLiteral(value)));
        Assert.Equal(Utf8("null"), nested.Data());
        Assert.Equal(Utf8("null"), nested.Data(pretty: true));
        Assert.Equal("null", nested.CompactString);
        Assert.Equal("null", nested.PrettyString);
    }

    [Fact]
    [Trait("Rule", "R-CODE-4")]
    [Trait("Oracle", "O-04")]
    public void Pretty_layout_is_two_space_indent_with_space_colon_space()
    {
        // UNVERIFIED O-04: JSONSerialization's pretty layout is not settled for this port. Parked choice (SwiftJson
        // writer): two-space indent, "key" : value, keys sorted.
        var value = JSONValue.FromDictionaryLiteral(("c", "x/y"), ("a", 1), ("b", JSONValue.FromArrayLiteral(true, Nil)));
        Assert.Equal("{\n  \"a\" : 1,\n  \"b\" : [\n    true,\n    null\n  ],\n  \"c\" : \"x/y\"\n}", value.PrettyString);
    }

    [Fact]
    [Trait("Rule", "R-CODE-4")]
    [Trait("Oracle", "O-04")]
    public void Pretty_empty_containers_print_an_empty_line_between_their_brackets()
    {
        // UNVERIFIED O-04, parked choice: an empty container prints its open bracket, an empty line, and its close
        // bracket at the member's indent.
        var value = JSONValue.FromDictionaryLiteral(("a", JSONValue.FromArrayLiteral()), ("b", JSONValue.FromDictionaryLiteral()));
        Assert.Equal("{\n  \"a\" : [\n\n  ],\n  \"b\" : {\n\n  }\n}", value.PrettyString);
        Assert.Equal("[\n\n]", JSONValue.FromArrayLiteral().PrettyString);
    }

    // ---- PR-STORE-118: typed accessors ----------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:78-113
    public void Every_accessor_over_every_case()
    {
        var samples = OneOfEachCase();
        JSONValue nul = samples[0], boolean = samples[1], integer = samples[2], dbl = samples[3], str = samples[4],
            arr = samples[5], obj = samples[6];

        // string :78-81, strict
        Assert.Equal("7", str.JSONValueString);
        foreach (var v in new[] { nul, boolean, integer, dbl, arr, obj }) Assert.Null(v.JSONValueString);

        // int :83-90: .int as is, finite .double truncated, .string parsed, the rest nil
        Assert.Equal(42L, integer.JSONValueInt);
        Assert.Equal(2L, dbl.JSONValueInt);
        Assert.Equal(7L, str.JSONValueInt);
        foreach (var v in new[] { nul, boolean, arr, obj }) Assert.Null(v.JSONValueInt);

        // double :92-98: .int widened, .double as is, the rest (a string too) nil
        Assert.Equal(42.0, integer.JSONValueDouble);
        Assert.Equal(2.5, dbl.JSONValueDouble);
        foreach (var v in new[] { nul, boolean, str, arr, obj }) Assert.Null(v.JSONValueDouble);

        // bool :100-103, strict
        Assert.True(boolean.JSONValueBool is true);
        foreach (var v in new[] { nul, integer, dbl, str, arr, obj }) Assert.Null(v.JSONValueBool);

        // array :105-108, strict
        Assert.Equal(new JSONValue[] { new JSONValue.Int(1) }, arr.JSONValueArray!.ToArray());
        foreach (var v in new[] { nul, boolean, integer, dbl, str, obj }) Assert.Null(v.JSONValueArray);

        // object :110-113, strict
        var members = obj.JSONValueObject!;
        Assert.Single(members);
        Assert.Equal(new JSONValue.Int(1), members["a"]);
        foreach (var v in new[] { nul, boolean, integer, dbl, str, arr }) Assert.Null(v.JSONValueObject);
    }

    [Fact]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:83-98
    public void A_token_count_sent_as_a_string_is_read_by_int_but_not_by_double()
    {
        var count = new JSONValue.String("1234");
        Assert.Equal(1234L, count.JSONValueInt);
        Assert.Null(count.JSONValueDouble);
    }

    [Theory]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:86
    [InlineData(2.9, 2L)]
    [InlineData(-2.9, -2L)]
    [InlineData(0.5, 0L)]
    [InlineData(-0.5, 0L)]
    [InlineData(1e15, 1000000000000000L)]
    [InlineData(-9223372036854775808.0, long.MinValue)]
    public void Int_truncates_a_finite_double_toward_zero(double value, long expected)
    {
        Assert.Equal(expected, new JSONValue.Double(value).JSONValueInt);
    }

    [Theory]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:86
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Int_of_a_non_finite_double_is_nil(double value)
    {
        Assert.Null(new JSONValue.Double(value).JSONValueInt);
    }

    [Theory]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:86
    [Trait("Rule", "R-NUM-4")]
    [InlineData(9223372036854775808.0)]
    [InlineData(1e300)]
    [InlineData(-1e19)]
    public void Int_of_a_finite_double_outside_the_Int_range_traps_as_Int_init_does(double value)
    {
        // isFinite passes, then Swift Int(value) traps for a value it cannot represent (stdlib semantics).
        Assert.Throws<SwiftTrapException>(() => new JSONValue.Double(value).JSONValueInt);
    }

    [Theory]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:87
    [Trait("Rule", "R-NUM-1")]
    [InlineData("1234", 1234L)]
    [InlineData("-12", -12L)]
    [InlineData("+12", 12L)]
    [InlineData("0012", 12L)]
    [InlineData("-0", 0L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Int_parses_a_string_with_the_standard_integer_parser(string text, long expected)
    {
        Assert.Equal(expected, new JSONValue.String(text).JSONValueInt);
    }

    [Theory]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:87
    [Trait("Rule", "R-NUM-1")]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData("0x10")]
    [InlineData("12_000")]
    [InlineData("1,000")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    [InlineData("\U0000FF11")] // U+FF11 FULLWIDTH DIGIT ONE
    [InlineData("\U00000661")] // U+0661 ARABIC-INDIC DIGIT ONE
    [InlineData("true")]
    public void Int_of_a_string_the_integer_parser_rejects_is_nil(string text)
    {
        Assert.Null(new JSONValue.String(text).JSONValueInt);
    }

    [Fact]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:92-98
    public void Double_widens_an_integer_and_passes_a_double_through_unchanged()
    {
        Assert.Equal(-7.0, new JSONValue.Int(-7).JSONValueDouble);
        Assert.Equal(9223372036854775808.0, new JSONValue.Int(long.MaxValue).JSONValueDouble); // Double(Int.max) rounds to 2^63
        Assert.Equal(0.1, new JSONValue.Double(0.1).JSONValueDouble);
        Assert.True(double.IsNaN(new JSONValue.Double(double.NaN).JSONValueDouble!.Value));
        Assert.Equal(double.PositiveInfinity, new JSONValue.Double(double.PositiveInfinity).JSONValueDouble);
        Assert.Null(new JSONValue.String("2.5").JSONValueDouble);
        Assert.Null(new JSONValue.Bool(true).JSONValueDouble);
    }

    [Fact]
    [Trait("Req", "PR-STORE-118")] // Core/Support/JSONValue.swift:78-81, :100-113
    public void String_bool_array_and_object_never_coerce()
    {
        Assert.Null(new JSONValue.Int(1).JSONValueBool);
        Assert.Null(new JSONValue.String("true").JSONValueBool);
        Assert.Null(new JSONValue.Int(1).JSONValueString);
        Assert.Null(new JSONValue.Bool(true).JSONValueString);
        Assert.Null(Nil.JSONValueString);
        Assert.Null(new JSONValue.String("[1]").JSONValueArray);
        Assert.Null(new JSONValue.String("{}").JSONValueObject);
        Assert.Null(JSONValue.FromDictionaryLiteral().JSONValueArray);
        Assert.Null(JSONValue.FromArrayLiteral().JSONValueObject);
        Assert.Empty(JSONValue.FromArrayLiteral().JSONValueArray!);
        Assert.Empty(JSONValue.FromDictionaryLiteral().JSONValueObject!);
    }

    // ---- PR-STORE-119: subscripts and isNull ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-119")] // Core/Support/JSONValue.swift:68-71
    public void Keying_an_object_finds_the_member_and_keying_anything_else_is_nil()
    {
        var obj = JSONValue.FromDictionaryLiteral(("a", 1), ("0", "zero"));
        Assert.Equal(new JSONValue.Int(1), obj["a"]);
        Assert.Equal(new JSONValue.String("zero"), obj["0"]);
        Assert.Null(obj["missing"]);
        Assert.Null(obj["A"]);
        foreach (var v in new[] { Nil, new JSONValue.Bool(true), new JSONValue.Int(1), new JSONValue.Double(1.5),
                     new JSONValue.String("a"), JSONValue.FromArrayLiteral("a") })
        {
            Assert.Null(v["a"]);
            Assert.Null(v["0"]);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-119")] // Core/Support/JSONValue.swift:73-76
    public void Indexing_is_nil_out_of_range_and_on_anything_but_an_array()
    {
        var arr = JSONValue.FromArrayLiteral("x", 2);
        Assert.Equal(new JSONValue.String("x"), arr[0]);
        Assert.Equal(new JSONValue.Int(2), arr[1]);
        Assert.Null(arr[2]);
        Assert.Null(arr[-1]);
        Assert.Null(arr[long.MaxValue]);
        Assert.Null(arr[long.MinValue]);
        Assert.Null(JSONValue.FromArrayLiteral()[0]);
        var obj = JSONValue.FromDictionaryLiteral(("0", 1));
        Assert.Null(obj[0]);
        foreach (var v in new[] { Nil, new JSONValue.Bool(true), new JSONValue.Int(1), new JSONValue.Double(1.5),
                     new JSONValue.String("abc") })
        {
            Assert.Null(v[0]);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-119")] // Core/Support/JSONValue.swift:68-71, :115-118
    public void A_missing_key_and_an_explicit_null_are_distinguishable()
    {
        var parsed = JSONValue.Parse("{\"present\":null}")!;
        Assert.Null(parsed["absent"]);
        var present = parsed["present"];
        Assert.NotNull(present);
        Assert.True(present!.IsNull);
    }

    [Fact]
    [Trait("Req", "PR-STORE-119")] // Core/Support/JSONValue.swift:115-118
    public void IsNull_is_true_only_for_the_null_case()
    {
        var samples = OneOfEachCase();
        Assert.True(samples[0].IsNull);
        foreach (var v in samples.Skip(1)) Assert.False(v.IsNull);
        Assert.False(new JSONValue.String("null").IsNull);
        Assert.False(new JSONValue.String("").IsNull);
        Assert.False(new JSONValue.Int(0).IsNull);
        Assert.False(new JSONValue.Bool(false).IsNull);
        Assert.False(JSONValue.FromArrayLiteral().IsNull);
        Assert.False(JSONValue.FromDictionaryLiteral().IsNull);
    }

    [Fact]
    [Trait("Req", "PR-STORE-119")] // Core/Support/JSONValue.swift:70
    [Trait("Rule", "R-STR-2")]
    public void Keying_compares_keys_as_Swift_strings_so_canonically_equivalent_keys_match()
    {
        // A Swift Dictionary<String, _> finds a key by String ==, which is canonical equivalence:
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE equals e followed by U+0301 COMBINING ACUTE ACCENT.
        var parsed = JSONValue.Parse("{\"caf\U000000E9\":1}")!; // U+00E9
        Assert.Equal(new JSONValue.Int(1), parsed["cafe\U00000301"]); // U+0301
        Assert.Equal(new JSONValue.Int(1), parsed["caf\U000000E9"]); // U+00E9
    }

    // ---- PR-STORE-120: displayText --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-120")] // Core/Support/JSONValue.swift:121-125
    public void Null_is_empty_a_string_is_itself_unquoted_and_a_bool_is_its_literal()
    {
        Assert.Equal("", Nil.DisplayText);
        Assert.Equal("a/b \"q\"", new JSONValue.String("a/b \"q\"").DisplayText);
        Assert.Equal("", new JSONValue.String("").DisplayText);
        Assert.Equal("couldn\U00002019t\U00002026", new JSONValue.String("couldn\U00002019t\U00002026").DisplayText); // U+2019 RIGHT SINGLE QUOTATION MARK, U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal("true", new JSONValue.Bool(true).DisplayText);
        Assert.Equal("false", new JSONValue.Bool(false).DisplayText);
    }

    [Theory]
    [Trait("Req", "PR-STORE-120")] // Core/Support/JSONValue.swift:126
    [InlineData(42L, "42")]
    [InlineData(-7L, "-7")]
    [InlineData(0L, "0")]
    [InlineData(1000000L, "1000000")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void An_integer_renders_as_its_plain_decimal_description(long value, string expected)
    {
        Assert.Equal(expected, new JSONValue.Int(value).DisplayText);
    }

    [Theory]
    [Trait("Req", "PR-STORE-120")] // Core/Support/JSONValue.swift:127
    [Trait("Rule", "R-NUM-2")]
    [InlineData(1.0, "1.0")]
    [InlineData(100.0, "100.0")]
    [InlineData(2.5, "2.5")]
    [InlineData(-3.0, "-3.0")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(123456789.0, "123456789.0")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(1e20, "1e+20")]
    [InlineData(double.NaN, "nan")]
    [InlineData(double.PositiveInfinity, "inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    public void A_double_renders_through_the_Swift_description_keeping_a_trailing_point_zero(double value, string expected)
    {
        Assert.Equal(expected, new JSONValue.Double(value).DisplayText);
    }

    [Fact]
    [Trait("Req", "PR-STORE-120")] // Core/Support/JSONValue.swift:127
    public void Negative_zero_renders_with_its_sign()
    {
        Assert.Equal("-0.0", new JSONValue.Double(Math.CopySign(0.0, -1.0)).DisplayText);
        Assert.Equal("0.0", new JSONValue.Double(0.0).DisplayText);
    }

    [Fact]
    [Trait("Req", "PR-STORE-120")] // Core/Support/JSONValue.swift:128
    public void Arrays_and_objects_render_as_the_pretty_sorted_string()
    {
        var obj = JSONValue.FromDictionaryLiteral(("path", "/tmp/x"), ("limit", 10), ("all", true));
        Assert.Equal(obj.PrettyString, obj.DisplayText);
        Assert.NotEqual(obj.CompactString, obj.DisplayText);
        var arr = JSONValue.FromArrayLiteral(1, "a/b", JSONValue.FromDictionaryLiteral(("z", 1), ("y", 2)));
        Assert.Equal(arr.PrettyString, arr.DisplayText);
        Assert.NotEqual(arr.CompactString, arr.DisplayText);
        Assert.Equal(JSONValue.FromArrayLiteral().PrettyString, JSONValue.FromArrayLiteral().DisplayText);
        Assert.Equal(JSONValue.FromDictionaryLiteral().PrettyString, JSONValue.FromDictionaryLiteral().DisplayText);
    }

    // ---- PR-STORE-121: optional(_:) -------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-121")] // Core/Support/JSONValue.swift:132-134
    public void An_absent_string_becomes_null_and_a_present_one_a_string()
    {
        Assert.IsType<JSONValue.Null>(JSONValue.Optional(null));
        Assert.Equal(new JSONValue.String("x"), JSONValue.Optional("x"));
        Assert.Equal(new JSONValue.String(""), JSONValue.Optional(""));
    }

    [Fact]
    [Trait("Req", "PR-STORE-121")] // Core/Support/JSONValue.swift:132-134, :59-65
    public void An_absent_optional_field_is_written_as_an_explicit_null_not_omitted()
    {
        // Fixture (R-TEST-8): the set_model control request of Services/Providers/ClaudeSession.swift:224,
        // `["subtype": "set_model", "model": .optional(wire)]`, where wire is nil for the "default" model (:223)
        // and otherwise the model id ("opus", ProviderRegistry.swift:73). The two-key compact order is Swift
        // Dictionary order, unspecified (R-CODE-4, R-COLL-2), so no whole two-key string is asserted.
        var payload = JSONValue.FromDictionaryLiteral(("subtype", "set_model"), ("model", JSONValue.Optional(null)));
        Assert.NotNull(payload["model"]);
        Assert.True(payload["model"]!.IsNull);
        Assert.IsType<JSONValue.Null>(payload["model"]);
        var compact = payload.CompactString;
        Assert.Contains("\"model\":null", compact, StringComparison.Ordinal); // the key is written, not omitted
        Assert.Contains("\"subtype\":\"set_model\"", compact, StringComparison.Ordinal);
        var reparsed = JSONValue.Parse(payload.Data());
        Assert.NotNull(reparsed);
        Assert.Equal(2, reparsed!.JSONValueObject!.Count);
        Assert.NotNull(reparsed["model"]);
        Assert.True(reparsed["model"]!.IsNull);
        Assert.Equal(payload, reparsed);

        var present = JSONValue.FromDictionaryLiteral(("subtype", "set_model"), ("model", JSONValue.Optional("opus")));
        Assert.Equal(new JSONValue.String("opus"), present["model"]);
        Assert.Contains("\"model\":\"opus\"", present.CompactString, StringComparison.Ordinal);
        Assert.Equal(present, JSONValue.Parse(present.Data()));

        // One key has one order, so its bytes are fixed.
        var single = JSONValue.FromDictionaryLiteral(("model", JSONValue.Optional(null)));
        Assert.Equal("{\"model\":null}", single.CompactString);
        Assert.Equal(Utf8("{\"model\":null}"), single.Data());
    }

    // ---- PR-STORE-122: literal conformances and duplicate keys ----------------------------------------------

    [Fact]
    [Trait("Req", "PR-STORE-122")] // Core/Support/JSONValue.swift:161-165
    public void A_repeated_key_in_a_dictionary_literal_keeps_the_last_value()
    {
        var value = JSONValue.FromDictionaryLiteral(("a", 1), ("b", 2), ("a", 3));
        Assert.Equal(2, value.JSONValueObject!.Count);
        Assert.Equal(new JSONValue.Int(3), value["a"]);
        Assert.Equal(new JSONValue.Int(2), value["b"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("b", 2), ("a", 3)), value);
    }

    [Fact]
    [Trait("Req", "PR-STORE-122")] // Core/Support/JSONValue.swift:163
    [Trait("Rule", "R-STR-2")]
    public void Canonically_equivalent_keys_in_a_dictionary_literal_are_one_key_and_the_last_value_wins()
    {
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE; e plus U+0301 COMBINING ACUTE ACCENT: the same Swift String.
        var value = JSONValue.FromDictionaryLiteral(("\U000000E9", 1), ("e\U00000301", 2));
        Assert.Single(value.JSONValueObject!);
        Assert.Equal(new JSONValue.Int(2), value["\U000000E9"]); // U+00E9
        Assert.Equal(new JSONValue.Int(2), value["e\U00000301"]); // U+0301
    }

    [Fact]
    [Trait("Req", "PR-STORE-122")] // Core/Support/JSONValue.swift:137-159
    public void Nil_true_1_1point0_s_and_array_literals_build_their_cases()
    {
        Assert.IsType<JSONValue.Null>(JSONValue.FromNilLiteral());
        JSONValue t = true;
        JSONValue f = false;
        JSONValue one = 1;
        JSONValue oneDouble = 1.0;
        JSONValue big = 9223372036854775807L;
        JSONValue s = "s";
        Assert.Equal(new JSONValue.Bool(true), t);
        Assert.Equal(new JSONValue.Bool(false), f);
        Assert.Equal(new JSONValue.Int(1), one); // an integer literal is .int (:145-147), never .double
        Assert.IsType<JSONValue.Int>(one);
        Assert.Equal(new JSONValue.Double(1.0), oneDouble); // a float literal is .double (:149-151)
        Assert.IsType<JSONValue.Double>(oneDouble);
        Assert.Equal(new JSONValue.Int(long.MaxValue), big);
        Assert.Equal(new JSONValue.String("s"), s);
        var arr = JSONValue.FromArrayLiteral(1, "s", true, JSONValue.FromNilLiteral(), 1.0);
        Assert.IsType<JSONValue.Array>(arr);
        Assert.Equal(5, arr.JSONValueArray!.Count);
        Assert.Equal(new JSONValue.Int(1), arr[0]);
        Assert.Equal(new JSONValue.String("s"), arr[1]);
        Assert.Equal(new JSONValue.Bool(true), arr[2]);
        Assert.True(arr[3]!.IsNull);
        Assert.Equal(new JSONValue.Double(1.0), arr[4]);
    }

    [Fact]
    [Trait("Req", "PR-STORE-122")] // Core/Support/JSONValue.swift:157-165
    public void Array_and_dictionary_literals_keep_element_order_and_compare_by_content()
    {
        Assert.Equal(JSONValue.FromArrayLiteral(1, 2), JSONValue.FromArrayLiteral(1, 2));
        Assert.NotEqual(JSONValue.FromArrayLiteral(1, 2), JSONValue.FromArrayLiteral(2, 1));
        Assert.Equal(JSONValue.FromDictionaryLiteral(("a", 1), ("b", 2)), JSONValue.FromDictionaryLiteral(("b", 2), ("a", 1)));
        Assert.Equal(JSONValue.FromArrayLiteral(1, 2).GetHashCode(), JSONValue.FromArrayLiteral(1, 2).GetHashCode());
        Assert.Equal(JSONValue.Parse("[1,2]"), JSONValue.FromArrayLiteral(1, 2));
    }
}
