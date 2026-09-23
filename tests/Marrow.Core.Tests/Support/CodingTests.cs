using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// Source: DroppyCode/Core/Support/Coding.swift (B2 1fbf81e), :1-34. The storage coders (JSONEncoder.storage :19-26,
// JSONDecoder.storage :28-34) and value(_:default:) (:3-8). Expected values come from those lines, the records
// PR-STORE-017, PR-STORE-018 and PR-STORE-020, and the R-CODE rules of IMPLEMENTING.md 3.8; never from running the port.
// PR-STORE-017 is only partly proven here. Its verification method is a byte-compare against a macOS-produced file, and
// it depends on UNK-0003, which OD-0001 closed as BLOCKED_ON_ACCESS: no such file exists. The PR-STORE-017 Req tests
// prove only the settled subset: sorting of lowercase ASCII keys, array order, one compact line, slash escaping in
// values and keys, and ISO 8601 date text. They do not prove the key collation, the non-ASCII and control escaping or
// the Double text the record names; those stay UNVERIFIED under UNK-0003, and PR-STORE-017 must not be reported as
// fully VERIFIED on the strength of these tests. The tests that pin those parts carry no Req trait: an expectation
// that rests on an open item carries an Oracle trait (R-TEST-6); one set by a port rule or decision alone carries that
// Rule or Decision trait.
// Lenient (:10-17) is proven in CodingLenientTests; Int decoding in CodingIntegerTests.
public sealed class CodingTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Nested(int depth) => new string('[', depth) + new string(']', depth);

    // ---- JSONEncoder.storage (:19-26) ----

    [Fact]
    [Trait("Req", "PR-STORE-017")] // Core/Support/Coding.swift:21-23
    public void Storage_sorts_keys_at_every_level_keeps_array_order_is_one_compact_line_and_escapes_slashes()
    {
        // Sibling keys are plain lowercase ASCII letters, where an ordinal sort and Foundation's collation agree
        // (R-CODE-9); "a/b" is the only key of its object, so its place rests on no collation (O-01). Every string is
        // printable ASCII, so no expectation here rests on the open escaping items (O-12, UNK-0003).
        var payload = new Dictionary<string, object?>
        {
            ["z"] = new object[]
            {
                new Dictionary<string, object?> { ["z"] = 2, ["a"] = "a/b" },
                new Dictionary<string, object?> { ["b"] = false, ["a"] = 1 },
            },
            ["m"] = new Dictionary<string, object?> { ["y"] = "line", ["b"] = new[] { 3, 1, 2 } },
            ["k"] = new Dictionary<string, object?> { ["a/b"] = 1 }, // a key goes through the same string writer (:21)
            ["a"] = "https://host/path",
        };
        var json = Text(Coding.Encode(payload));
        Assert.Equal(
            "{\"a\":\"https:\\/\\/host\\/path\",\"k\":{\"a\\/b\":1},\"m\":{\"b\":[3,1,2],\"y\":\"line\"}," +
            "\"z\":[{\"a\":\"a\\/b\",\"z\":2},{\"a\":1,\"b\":false}]}", json);
        Assert.DoesNotContain(" : ", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-STORE-017")] // Core/Support/Coding.swift:23
    public void Text_holding_line_breaks_still_saves_as_one_line()
    {
        // :23 sets no .prettyPrinted, and JSON never carries a raw line break inside a string, so the file has no
        // CR or LF byte whatever escape form the breaks take (that form is O-12, pinned by an Oracle test below).
        var payload = new Dictionary<string, object?> { ["b"] = new[] { "one\ntwo", "three\r\nfour" }, ["a"] = "x\ry" };
        var bytes = Coding.Encode(payload);
        Assert.DoesNotContain((byte)'\n', bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
        var reloaded = Coding.Decode<JsonElement>(bytes);
        Assert.Equal("x\ry", reloaded.GetProperty("a").GetString());
        Assert.Equal("one\ntwo", reloaded.GetProperty("b")[0].GetString());
        Assert.Equal("three\r\nfour", reloaded.GetProperty("b")[1].GetString());
    }

    [Fact]
    [Trait("Req", "PR-STORE-017")] // Core/Support/Coding.swift:23
    public void Successive_saves_of_the_same_content_are_byte_identical_whatever_the_insertion_order()
    {
        var first = new Dictionary<string, object?> { ["title"] = "t", ["id"] = 1, ["archived"] = true, ["path"] = "/p" };
        var second = new Dictionary<string, object?> { ["path"] = "/p", ["archived"] = true, ["title"] = "t", ["id"] = 1 };
        Assert.Equal(Coding.Encode(first), Coding.Encode(first));
        Assert.Equal(Coding.Encode(first), Coding.Encode(second));
        Assert.Equal("{\"archived\":true,\"id\":1,\"path\":\"\\/p\",\"title\":\"t\"}", Text(Coding.Encode(second)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-017")] // Core/Support/Coding.swift:22
    public void Dates_are_written_as_ISO8601_in_UTC_with_whole_seconds_and_a_Z()
    {
        var date = new DateTimeOffset(2026, 9, 22, 5, 0, 1, TimeSpan.FromHours(3)).AddMilliseconds(500);
        Assert.Equal("\"2026-09-22T02:00:01Z\"", Text(Coding.Encode(date)));
        Assert.Equal("\"1970-01-01T00:00:00Z\"", Text(Coding.Encode(DateTimeOffset.UnixEpoch)));
        var inside = new Dictionary<string, object?> { ["createdAt"] = date, ["b"] = 1 };
        Assert.Equal("{\"b\":1,\"createdAt\":\"2026-09-22T02:00:01Z\"}", Text(Coding.Encode(inside)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-6")]
    public void A_date_before_1970_drops_its_fraction_toward_the_past()
    {
        // FoundationISO8601.Format: whole seconds by floor. Truncation toward 1970 would write 1970-01-01T00:00:00Z.
        var date = new DateTimeOffset(1969, 12, 31, 23, 59, 59, TimeSpan.Zero).AddMilliseconds(500);
        Assert.Equal("\"1969-12-31T23:59:59Z\"", Text(Coding.Encode(date)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void Non_ASCII_text_is_written_as_raw_UTF8_bytes()
    {
        // Emoji, U+2028, U+2029, U+FEFF, private use, U+0085, NBSP and DEL are not C0 controls: raw UTF-8, no escape.
        const string value = "caf\U000000E9 \U0001F600 \U00002028\U00002029\U0000FEFF\U0000E000\U00000085\U000000A0\U0000007F";
        var bytes = Coding.Encode(value);
        Assert.Equal(Encoding.UTF8.GetBytes("\"" + value + "\""), bytes);
        Assert.Equal(new byte[] { 0x22, 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xF0, 0x9F, 0x98, 0x80, 0x20, 0xE2, 0x80, 0xA8 }, bytes[..15]);
        Assert.DoesNotContain((byte)'\\', bytes);
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Oracle", "O-12")]
    public void Controls_use_the_short_escapes_and_lowercase_hex_for_the_rest()
    {
        // Parked O-12 choice: short forms for backspace, tab, newline, form feed and carriage return; lowercase \u00xx.
        Assert.Equal("\"\\b\\t\\n\\f\\r\\u0001\\u001f\\u0000\\\"\\\\\"",
            Text(Coding.Encode("\b\t\n\f\r\U00000001\U0000001F\U00000000\"\\")));
    }

    [Fact]
    [Trait("Rule", "R-CODE-9")]
    [Trait("Oracle", "O-01")]
    public void Keys_with_case_digits_and_underscores_sort_ordinally_until_the_oracle_answers()
    {
        // Parked O-01 choice (SwiftJson.SortedKeyOrder, ordinal). Foundation is believed to put "a" before "B" and
        // "item9" before "item10"; this pins the parked order so that a change of comparer is a deliberate one.
        var payload = new Dictionary<string, int>
        {
            ["a"] = 1, ["B"] = 2, ["item10"] = 3, ["item9"] = 4, ["_x"] = 5, ["0A"] = 6, ["09"] = 7, ["Z"] = 8, ["z"] = 9,
        };
        Assert.Equal("{\"09\":7,\"0A\":6,\"B\":2,\"Z\":8,\"_x\":5,\"a\":1,\"item10\":3,\"item9\":4,\"z\":9}", Text(Coding.Encode(payload)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-5")]
    [Trait("Decision", "ED-072")]
    public void Doubles_are_written_as_Swift_text_without_a_trailing_point_zero()
    {
        // Double.description with ".0" removed; System.Text.Json alone writes 1E-05.
        Assert.Equal("[0.5,1e-05,1,100,0.1]", Text(Coding.Encode(new[] { 0.5, 0.00001, 1.0, 100.0, 0.1 })));
    }

    [Fact]
    [Trait("Rule", "R-CODE-5")]
    [Trait("Oracle", "O-03")]
    public void A_Double_of_1e16_stays_in_positional_form_under_the_parked_exponent_threshold()
    {
        // Parked O-03 choice: exponent form only above 2^54 (SwiftNumber.ExponentialAbove).
        Assert.Equal("[10000000000000000]", Text(Coding.Encode(new[] { 1e16 })));
    }

    [Fact]
    [Trait("Rule", "R-CODE-5")]
    public void A_non_finite_Double_is_an_EncodingError()
    {
        Assert.Throws<EncodingError>(() => Coding.Encode(double.NaN));
        Assert.Throws<EncodingError>(() => Coding.Encode(new Dictionary<string, double> { ["x"] = double.PositiveInfinity }));
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    public void An_explicit_nil_is_written_as_null_and_only_a_synthesized_optional_is_left_out()
    {
        // Swift writes a nil held in a [String: String?] as null (Optional encodes nil); leaving a key out is the
        // synthesized encodeIfPresent of a model, which the model's own writer does (R-CODE-3), not the shared coder.
        var values = new Dictionary<string, string?> { ["b"] = "x", ["a"] = null };
        Assert.Equal("{\"a\":null,\"b\":\"x\"}", Text(Coding.Encode(values)));
        // ChatThread (Library.swift:121) has a synthesized encode(to:): worktreePath (:135) and sortOrder (:149) are
        // nil, so neither key is written.
        var thread = new ChatThreadCodingSample(Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef"), "t", DateTimeOffset.UnixEpoch, null, null);
        Assert.Equal(
            "{\"createdAt\":\"1970-01-01T00:00:00Z\",\"id\":\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\",\"title\":\"t\"}",
            Text(Coding.Encode(thread)));
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Rule", "R-CODE-7")]
    [Trait("Decision", "ED-072")]
    public void A_model_goes_through_the_storage_coder_with_Swift_bytes_end_to_end()
    {
        // ChatThreadCodingSample is ChatThread (Library.swift:121-265) trimmed to id :124, title :126, createdAt :127,
        // worktreePath :135 and sortOrder :149 (a Double? holding 1e-05); its writer is the hand-written one of R-CODE-3.
        // The five keys start with distinct lowercase letters, so ordinal order and Foundation's collation agree.
        var id = Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef");
        var created = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero).AddTicks(9_999_999);
        var thread = new ChatThreadCodingSample(id, "caf\U000000E9 \U0001F600 a/b", created, "/Users/me/wt", 0.00001);
        var bytes = Coding.Encode(thread);
        Assert.Equal(
            "{\"createdAt\":\"2026-09-22T10:00:00Z\",\"id\":\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\",\"sortOrder\":1e-05," +
            "\"title\":\"caf\U000000E9 \U0001F600 a\\/b\",\"worktreePath\":\"\\/Users\\/me\\/wt\"}", Text(bytes));
        var reloaded = Coding.Decode<ChatThreadCodingSample>(bytes)!;
        Assert.Equal(thread with { CreatedAt = created.AddTicks(-9_999_999) }, reloaded); // the fraction is lost, as in Swift
    }

    [Fact]
    [Trait("Rule", "R-CODE-7")]
    public void UUIDs_are_written_as_uppercase_uuidString_and_read_only_in_the_36_character_form()
    {
        var id = Guid.Parse("0123abcd-89ab-cdef-0123-456789abcdef");
        Assert.Equal("\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\"", Text(Coding.Encode(id)));
        Assert.Equal(id, Coding.Decode<Guid>("\"0123abcd-89ab-cdef-0123-456789abcdef\""u8));
        Assert.Equal(id, Coding.Decode<Guid>("\"0123ABCD-89AB-CDEF-0123-456789ABCDEF\""u8));
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<Guid>("\"{0123abcd-89ab-cdef-0123-456789abcdef}\""u8));
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<Guid>("\"0123abcd89abcdef0123456789abcdef\""u8));
    }

    [Fact]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-24")]
    public void The_storage_coders_handle_nesting_past_64_levels_up_to_512()
    {
        // System.Text.Json stops at 64 by default; Foundation is believed to stop at 512 (O-24).
        foreach (var depth in new[] { 70, 512 })
        {
            var bytes = Encoding.UTF8.GetBytes(Nested(depth));
            var element = Coding.Decode<JsonElement>(bytes);
            Assert.Equal(JsonValueKind.Array, element.ValueKind);
            Assert.Equal(Nested(depth), Text(Coding.Encode(element)));
        }
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<JsonElement>(Encoding.UTF8.GetBytes(Nested(513))));
    }

    // ---- JSONDecoder.storage (:28-34) ----

    [Fact]
    [Trait("Req", "PR-STORE-018")] // Core/Support/Coding.swift:20, :29
    public void Both_storage_coders_are_one_shared_instance_built_once()
    {
        // :20 and :29 are `static let` constants, each holding one lazily built coder. Coding.StorageOptions carries
        // the converters of both directions (Encode and Decode), so every read gives the same object.
        Assert.Same(Coding.StorageOptions, Coding.StorageOptions);
    }

    [Fact]
    [Trait("Req", "PR-STORE-018")] // Core/Support/Coding.swift:31
    public void Dates_are_read_as_ISO8601()
    {
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 2, 0, 1, TimeSpan.Zero), Coding.Decode<DateTimeOffset>("\"2026-09-22T02:00:01Z\""u8));
        Assert.Equal(DateTimeOffset.UnixEpoch, Coding.Decode<DateTimeOffset>("\"1970-01-01T00:00:00Z\""u8));
        using var document = JsonDocument.Parse("{\"createdAt\":\"2026-09-22T02:00:01Z\"}");
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 2, 0, 1, TimeSpan.Zero),
            Coding.Value(document.RootElement, "createdAt", SwiftDecoding.ReadStorageDate, () => DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [Trait("Req", "PR-STORE-018")] // Core/Support/Coding.swift:31
    [InlineData("\"2026-09-22\"")]
    [InlineData("\"09/22/2026\"")]
    [InlineData("\"22 September 2026\"")]
    [InlineData("\"yesterday\"")]
    [InlineData("\"\"")]
    [InlineData("123")]
    [InlineData("true")]
    public void A_date_that_is_not_ISO8601_fails_the_strict_decode(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Coding.Decode<DateTimeOffset>(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [Trait("Req", "PR-STORE-018")] // Core/Support/Coding.swift:31; the failure case
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    [InlineData("{\"date\":\"yesterday\"}")]
    [InlineData("{\"date\":123}")]
    [InlineData("{\"date\":\"2026-09-22\"}")]
    [InlineData("{\"date\":\"09/22/2026\"}")]
    public void A_date_that_is_not_ISO8601_reads_as_the_callers_default(string json)
    {
        using var document = JsonDocument.Parse(json);
        var fallback = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(fallback, Coding.Value(document.RootElement, "date", SwiftDecoding.ReadStorageDate, () => fallback));
    }

    [Fact]
    [Trait("Rule", "R-CODE-6")]
    [Trait("Oracle", "O-02")]
    public void Fractional_seconds_are_accepted_until_the_oracle_answers()
    {
        // Parked O-02 choice (FoundationISO8601.StorageDate): a fraction is read, where Swift may give the default.
        var expected = new DateTimeOffset(2026, 9, 22, 5, 0, 1, TimeSpan.Zero).AddMilliseconds(500);
        Assert.Equal(expected, Coding.Decode<DateTimeOffset>("\"2026-09-22T05:00:01.500Z\""u8));
        using var document = JsonDocument.Parse("{\"date\":\"2026-09-22T05:00:01.500Z\"}");
        Assert.Equal(expected, Coding.Value(document.RootElement, "date", SwiftDecoding.ReadStorageDate, () => DateTimeOffset.UnixEpoch));
    }

    [Fact]
    [Trait("Rule", "R-CODE-6")]
    [Trait("Oracle", "O-17")]
    public void Only_the_parked_ISO8601_shape_with_Z_or_plus_HH_colon_MM_is_read_until_the_oracle_answers()
    {
        // Parked O-17 choice (FoundationISO8601, the ISO8601FormatStyle parse the storage decoder stands for): a "T",
        // then "Z" or "+HH:MM". A colon-less offset, a space, a lowercase "t", no zone, or a field out of range (which
        // a calendar might roll over) is not read. Only the macOS oracle can say which of these Swift reads.
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 2, 0, 1, TimeSpan.Zero), Coding.Decode<DateTimeOffset>("\"2026-09-22T05:00:01+03:00\""u8));
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 8, 0, 1, TimeSpan.Zero), Coding.Decode<DateTimeOffset>("\"2026-09-22T05:00:01-03:00\""u8));
        foreach (var text in new[]
        {
            "2026-09-22T05:00:01+0300", "2026-09-22 05:00:01Z", "2026-09-22t05:00:01Z", "2026-09-22T05:00:01",
            "2026-99-99T99:99:99Z", "2026-02-30T00:00:00Z",
        })
        {
            Assert.ThrowsAny<JsonException>(() => Coding.Decode<DateTimeOffset>(Encoding.UTF8.GetBytes("\"" + text + "\"")));
            using var document = JsonDocument.Parse("{\"date\":\"" + text + "\"}");
            Assert.Equal(DateTimeOffset.UnixEpoch, Coding.Value(document.RootElement, "date", SwiftDecoding.ReadStorageDate, () => DateTimeOffset.UnixEpoch));
        }
    }

    // ---- value(_:default:) (:3-8) ----

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:5-6
    [InlineData("{}")]
    [InlineData("{\"x\":null}")]
    [InlineData("{\"x\":\"1\"}")]
    [InlineData("{\"x\":false}")]
    [InlineData("{\"x\":[]}")]
    [InlineData("{\"x\":{}}")]
    [InlineData("{\"x\":1.5}")]
    [InlineData("{\"y\":1}")]
    public void A_missing_key_a_null_and_a_type_mismatch_all_give_the_default_evaluated_once(string json)
    {
        var calls = 0;
        var result = Coding.Value<long>(Root(json), "x", SwiftDecoding.ReadInt, () => { calls++; return 42L; });
        Assert.Equal(42L, result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:5-6
    [InlineData("{\"x\":0}", 0L)]
    [InlineData("{\"x\":-7}", -7L)]
    [InlineData("{\"x\":1.0}", 1L)]
    [InlineData("{\"x\":1e3}", 1000L)]
    public void A_present_value_of_the_right_type_is_returned_and_the_default_is_never_evaluated(string json, long expected)
    {
        Assert.Equal(expected, Coding.Value<long>(Root(json), "x", SwiftDecoding.ReadInt, () => throw new InvalidOperationException("the autoclosure must not run")));
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:5
    public void The_default_is_evaluated_afresh_on_every_miss()
    {
        var root = Root("{}");
        var calls = 0;
        var first = Coding.Value<ValueList<string>>(root, "x", element => SwiftDecoding.ReadArray(element, SwiftDecoding.ReadString), () => { calls++; return new ValueList<string>(["d"]); });
        var second = Coding.Value<ValueList<string>>(root, "x", element => SwiftDecoding.ReadArray(element, SwiftDecoding.ReadString), () => { calls++; return new ValueList<string>(["d"]); });
        Assert.Equal(2, calls);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    public void Keys_are_matched_exactly_and_case_sensitively()
    {
        var root = Root("{\"x\":0,\"X\":99}");
        Assert.Equal(0L, Coding.Value(root, "x", SwiftDecoding.ReadInt, () => -1L));
        Assert.Equal(99L, Coding.Value(root, "X", SwiftDecoding.ReadInt, () => -1L));
        Assert.Equal(-1L, Coding.Value(root, "missing", SwiftDecoding.ReadInt, () => -1L));
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    public void One_bad_element_inside_a_strict_array_falls_back_the_whole_field()
    {
        // A plain [Int] is not Lenient: one bad element fails the whole decode, and value() gives the default.
        var root = Root("{\"values\":[1,\"bad\",3]}");
        Assert.Equal(new ValueList<long>([9]), Coding.Value(root, "values", element => SwiftDecoding.ReadArray(element, SwiftDecoding.ReadInt), () => new ValueList<long>([9])));
        var nulls = Root("{\"values\":[1,null,3]}");
        Assert.Equal(new ValueList<long>([9]), Coding.Value(nulls, "values", element => SwiftDecoding.ReadArray(element, SwiftDecoding.ReadInt), () => new ValueList<long>([9])));
    }

    [Theory]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6; Core/Models/Library.swift:234-237
    [InlineData("{}")]
    [InlineData("{\"runtimeMode\":null}")]
    [InlineData("{\"runtimeMode\":1}")]
    // Provider.swift:161-165: RuntimeMode decodes only from its exact raw value, so RuntimeMode(rawValue: "3") is nil.
    // A numeric string must not map to a case by ordinal (the .NET Enum.Parse hazard would turn "3" into FullAccess).
    [InlineData("{\"runtimeMode\":\"3\"}")]
    [InlineData("{\"runtimeMode\":\"1\"}")]
    [InlineData("{\"runtimeMode\":true}")]
    [InlineData("{\"runtimeMode\":[\"fullAccess\"]}")]
    [InlineData("{\"runtimeMode\":{\"fullAccess\":{}}}")]
    [InlineData("{\"runtimeMode\":\"FullAccess\"}")]
    [InlineData("{\"runtimeMode\":\"full_access\"}")]
    [InlineData("{\"runtimeMode\":\"root\"}")]
    [InlineData("{\"runtimeMode\":\"\"}")]
    public void An_unreadable_runtime_mode_is_neither_an_error_nor_a_permissive_value(string json)
    {
        Assert.Equal(RuntimeModeSample.Supervised,
            Coding.Value(Root(json), "runtimeMode", SwiftDecoding.ReadRawValue<RuntimeModeSample>, () => RuntimeModeSample.Supervised));
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6; Core/Models/Provider.swift:161-165
    public void A_readable_runtime_mode_is_kept()
    {
        Assert.Equal(RuntimeModeSample.FullAccess,
            Coding.Value(Root("{\"runtimeMode\":\"fullAccess\"}"), "runtimeMode", SwiftDecoding.ReadRawValue<RuntimeModeSample>, () => RuntimeModeSample.Supervised));
        Assert.Equal(RuntimeModeSample.AutoAcceptEdits,
            Coding.Value(Root("{\"runtimeMode\":\"autoAcceptEdits\"}"), "runtimeMode", SwiftDecoding.ReadRawValue<RuntimeModeSample>, () => RuntimeModeSample.Supervised));
    }

    [Fact]
    [Trait("Req", "PR-STORE-020")] // Core/Support/Coding.swift:6
    [Trait("Rule", "R-ERR-1")]
    public void Any_thrown_error_gives_the_default_but_a_trap_still_propagates()
    {
        // try? swallows every thrown error, whatever its type; a trap (fatalError, overflow) is not an error.
        var root = Root("{\"x\":1}");
        Assert.Equal(5L, Coding.Value<long>(root, "x", _ => throw new DecodingError("dataCorrupted", "bad"), () => 5L));
        Assert.Equal(5L, Coding.Value<long>(root, "x", _ => throw new EncodingError("thrown by a nested initializer"), () => 5L));
        Assert.Equal(5L, Coding.Value<long>(root, "x", _ => throw new FormatException("thrown"), () => 5L));
        Assert.Equal(5L, Coding.Value<long>(root, "x", _ => throw new InvalidOperationException("thrown"), () => 5L));
        Assert.Throws<SwiftTrapException>(() => Coding.Value<long>(root, "x", _ => throw SwiftTraps.Fatal("trap"), () => 5L));
        Assert.Throws<OverflowException>(() => Coding.Value<long>(root, "x", _ => throw new OverflowException(), () => 5L));
    }

    [Fact]
    [Trait("Rule", "R-CODE-1")]
    public void A_number_too_large_for_a_Double_gives_the_default()
    {
        // JSONDecoder refuses a parsed number that is not finite, so value() gives the default rather than infinity.
        Assert.Equal(7.0, Coding.Value(Root("{\"x\":1e400}"), "x", SwiftDecoding.ReadDouble, () => 7.0));
        Assert.Equal(7.0, Coding.Value(Root("{\"x\":-1e400}"), "x", SwiftDecoding.ReadDouble, () => 7.0));
        Assert.Equal(0.5, Coding.Value(Root("{\"x\":0.5}"), "x", SwiftDecoding.ReadDouble, () => 7.0));
    }
}
