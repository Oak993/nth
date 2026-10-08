// PORT-ONLY: T44 preserves the envelope's exact raw-data checksum without a full-table JSON DOM.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Port.ModelsPalette;

internal static class PaletteJsonEnvelope
{
    internal static (int? Schema, string? Hash, ReadOnlyMemory<byte>? Data) Read(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Missing envelope object.");
        int? schema = null;
        string? hash = null;
        ReadOnlyMemory<byte>? data = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Missing envelope property.");
            var property = reader.GetString();
            if (!reader.Read()) throw new JsonException("Missing envelope value.");
            var start = checked((int)reader.TokenStartIndex);
            if (property == "schema") schema = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) ? value : null;
            else if (property == "hash") hash = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            reader.Skip();
            if (property == "data") data = bytes.AsMemory(start, checked((int)reader.BytesConsumed) - start);
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) throw new JsonException("Invalid envelope end.");
        return (schema, hash, data);
    }

    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static byte[] Encode<T>(T data)
    {
        using var body = new MemoryStream();
        // Async serialization flushes bounded chunks; SerializeToUtf8Bytes/Serialize(string) retain
        // a whole snapshot's growing buffer in ArrayPool.Shared after it has been written.
        JsonSerializer.SerializeAsync(body, data).GetAwaiter().GetResult();
        var raw = body.GetBuffer().AsSpan(0, checked((int)body.Length));
        var prefix = Encoding.UTF8.GetBytes("{\"schema\":1,\"hash\":\"" + Digest(raw) + "\",\"data\":");
        var encoded = new byte[prefix.Length + raw.Length + 1];
        prefix.CopyTo(encoded, 0);
        raw.CopyTo(encoded.AsSpan(prefix.Length));
        encoded[^1] = (byte)'}';
        return encoded;
    }
}
