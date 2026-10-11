// PORT-ONLY: first-release feature unit SK-1. Reads the embedded catalogue and refuses anything the schema does not describe.
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Port.Skills.Catalogue;

public sealed class CatalogueFormatException : Exception
{
    public CatalogueFormatException(string message) : base(message)
    {
    }

    public CatalogueFormatException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Loads <c>catalogue.json</c>, compiled into this assembly and never fetched (ONDEMAND-PLAN.md 2.3). Strict on purpose (ED-294): an unknown
/// key, a missing key, a null where the schema allows none, a duplicate key, a number or a stranger where an enum word belongs, all fail closed.
/// </summary>
public static class SkillsCatalogueLoader
{
    public const string ResourceName = "Marrow.Core.SkillsCatalogue.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new StrictEnumConverterFactory() },
    };

    public static SkillsCatalogue LoadEmbedded()
    {
        using var stream = typeof(SkillsCatalogue).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new CatalogueFormatException("The embedded catalogue is missing: " + ResourceName);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    public static SkillsCatalogue Parse(string json)
    {
        try
        {
            var catalogue = JsonSerializer.Deserialize<SkillsCatalogue>(json, Options)
                ?? throw new CatalogueFormatException("The catalogue is null.");
            RejectNullElements(catalogue);
            return catalogue;
        }
        catch (JsonException error)
        {
            throw new CatalogueFormatException("The catalogue does not match its schema: " + error.Message, error);
        }
    }

    /// <summary>
    /// REVIEW fix (unit/integ-sk): <c>RespectNullableAnnotations</c> does not reach the elements of a list, so a <c>null</c> inside one passed the
    /// reader and crashed the gate later. Every list element is checked here, so it fails closed as the other format errors do.
    /// </summary>
    private static void RejectNullElements(SkillsCatalogue catalogue)
    {
        static void Check<T>(IReadOnlyList<T?> list, string where)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] is null) throw new CatalogueFormatException($"The catalogue does not match its schema: {where}[{i}] is null.");
        }
        Check(catalogue.Items, "items");
        foreach (var item in catalogue.Items)
        {
            var at = "item " + item.Id + ": ";
            Check(item.Needs, at + "needs");
            Check(item.Provides, at + "provides");
            Check(item.Artifacts, at + "artifacts");
            Check(item.Files, at + "files");
            Check(item.Skills, at + "skills");
            Check(item.Folders, at + "folders");
            Check(item.Packages, at + "packages");
            foreach (var skill in item.Skills) Check(skill.Needs, at + "skills " + skill.Folder + " needs");
            if (item.Install is { } install)
            {
                Check(install.Archives, at + "install archives");
                Check(install.NodeTree, at + "install nodeTree");
                Check(install.Markers, at + "install markers");
                Check(install.Shims, at + "install shims");
                Check(install.Exports, at + "install exports");
                foreach (var shim in install.Shims) Check(shim.Arguments, at + "install shim " + shim.Name + " arguments");
            }
        }
    }

    /// <summary>An enum is its own camelCase word, exactly: not a number, not another case (the stock converter reads any case).</summary>
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
