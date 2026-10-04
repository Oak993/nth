// PORT-ONLY: first-release feature unit SK-7. The text of addons\state.json: canonical, strict, and never read in part.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Port.Skills.State;

/// <summary>
/// ENGINEERING DECISIONS (ED-295): the file is indented, sorted and ends in a line feed, so one state is one text; it is read strictly, as SK-1's catalogue is
/// (unknown, missing, null, duplicate and wrongly cased members, numbers for enums, comments and trailing commas all fail closed), and what it describes must be a
/// valid <see cref="AddonsState"/>. Dates are UTC instants written to the second or finer, ending in Z. An Available item is not written.
/// </summary>
public static class AddonsStateJson
{
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = true,
        NewLine = "\n",
        Converters = { new StrictEnumConverterFactory() },
    };

    public static string Serialize(AddonsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var file = new StateFile
        {
            Schema = AddonsState.SchemaVersion,
            Items = state.Items.Select(r => new ItemDto { Id = r.Id, State = r.State, InstalledPin = r.InstalledPin, TargetPin = r.TargetPin, Failure = r.Failure }).ToList(),
            AcceptedTerms = state.AcceptedTerms.Select(t => new TermsDto { LicenceSha256 = t.LicenceSha256, ItemId = t.ItemId, AcceptedAt = t.AcceptedAt.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture) }).ToList(),
            Declined = state.Declined.Select(c => new DeclinedDto { ChatId = c.ChatId, ItemIds = c.ItemIds }).ToList(),
            Found = state.Found.Select(f => new FoundDto { ItemId = f.ItemId, Path = f.Path }).ToList(),
        };
        return JsonSerializer.Serialize(file, Options) + "\n";
    }

    public static AddonsState Parse(string json)
    {
        StateFile file;
        try
        {
            file = JsonSerializer.Deserialize<StateFile>(json, Options) ?? throw new StateFormatException("The state file is null.");
        }
        catch (JsonException error)
        {
            throw new StateFormatException("The state file does not match its schema: " + error.Message, error);
        }
        if (file.Schema != AddonsState.SchemaVersion) throw new StateFormatException($"The state file has schema {file.Schema}; this build reads {AddonsState.SchemaVersion}.");
        RejectNullEntries(file.Items, "items");
        RejectNullEntries(file.AcceptedTerms, "acceptedTerms");
        RejectNullEntries(file.Declined, "declined");
        RejectNullEntries(file.Found, "found");
        return AddonsState.Restore(
            file.Items.Select(i => new ItemRecord(i.Id, i.State, i.InstalledPin, i.TargetPin, i.Failure)),
            file.AcceptedTerms.Select(t => new AcceptedTerm(t.LicenceSha256, t.ItemId, ParseDate(t.AcceptedAt))),
            file.Declined.Select(c => new ChatDeclined(c.ChatId, c.ItemIds)),
            file.Found.Select(f => new FoundOnPc(f.ItemId, f.Path)));
    }

    // Nullable annotations do not reach list elements, so a null entry is refused here rather than surfacing as a NullReferenceException.
    private static void RejectNullEntries<T>(IReadOnlyList<T> list, string name) where T : class
    {
        if (list.Any(entry => entry is null)) throw new StateFormatException($"The state file's {name} list holds a null entry.");
    }

    private static DateTimeOffset ParseDate(string text) =>
        DateTimeOffset.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : throw new StateFormatException($"'{text}' is not a UTC date of the form 2026-10-04T12:00:00Z.");

    private sealed record StateFile
    {
        public required int Schema { get; init; }

        public required IReadOnlyList<ItemDto> Items { get; init; }

        public required IReadOnlyList<TermsDto> AcceptedTerms { get; init; }

        public required IReadOnlyList<DeclinedDto> Declined { get; init; }

        public required IReadOnlyList<FoundDto> Found { get; init; }
    }

    private sealed record ItemDto
    {
        public required string Id { get; init; }

        public required ItemState State { get; init; }

        public required string? InstalledPin { get; init; }

        public required string? TargetPin { get; init; }

        public required string? Failure { get; init; }
    }

    private sealed record TermsDto
    {
        public required string LicenceSha256 { get; init; }

        public required string ItemId { get; init; }

        public required string AcceptedAt { get; init; }
    }

    private sealed record DeclinedDto
    {
        public required string ChatId { get; init; }

        public required IReadOnlyList<string> ItemIds { get; init; }
    }

    private sealed record FoundDto
    {
        public required string ItemId { get; init; }

        public required string Path { get; init; }
    }

    /// <summary>An enum is its own camelCase word, exactly (the stock converter reads any case); the same rule as SK-1's catalogue loader.</summary>
    private sealed class StrictEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly Dictionary<string, T> ByWord = Enum.GetValues<T>().ToDictionary(Word, v => v, StringComparer.Ordinal);

        private static string Word(T value)
        {
            var name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name[1..];
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException($"A {typeof(T).Name} must be a string.");
            var text = reader.GetString()!;
            return ByWord.TryGetValue(text, out var value) ? value : throw new JsonException($"'{text}' is not a {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(Word(value));
    }
}
