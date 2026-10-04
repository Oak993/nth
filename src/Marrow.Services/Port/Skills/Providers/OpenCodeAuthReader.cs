// PORT-ONLY: SK-11d / WU-9, skills-PLAN.md 2.2 source 1 and review 7(2): a streaming reader whose only result is yes/no.
using System.Security.Cryptography;
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Providers;

public static class OpenCodeAuthReader
{
    /// <summary>True when any top-level entry has "type":"wellknown". No value is kept, logged or put in an error.</summary>
    public static bool WellKnownPresent(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new OpenCodeConfigUnreadableException("The auth file cannot be read."); }
        try { return Scan(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool Scan(byte[] bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new OpenCodeConfigUnreadableException("The auth file is not an object.");
            var found = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (!reader.Read()) break;
                if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); continue; }
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var isType = reader.ValueTextEquals("type"u8);
                    if (!reader.Read()) throw new OpenCodeConfigUnreadableException("The auth file cannot be parsed.");
                    if (isType && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("wellknown"u8)) found = true;
                    reader.Skip();
                }
            }
            return found;
        }
        catch (JsonException) { throw new OpenCodeConfigUnreadableException("The auth file cannot be parsed."); }
        catch (InvalidOperationException) { throw new OpenCodeConfigUnreadableException("The auth file cannot be parsed."); }
    }
}
