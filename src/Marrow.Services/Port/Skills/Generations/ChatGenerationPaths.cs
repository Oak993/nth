// PORT-ONLY: SK-9 PLAN 2.1 records a chat's moved generation path across restarts. Disk data names candidates, never deletion authority.
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

internal sealed class ChatGenerationPaths(StorePaths paths)
{
    private string FilePath => Path.Combine(paths.Skills, "chat-paths.json");
    private Dictionary<string, string> Read()
    {
        StorePaths.EnsureAncestors(FilePath);
        if (!File.Exists(FilePath)) return new(StringComparer.Ordinal);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(FilePath)) ?? throw new InvalidDataException("Invalid chat generation paths.");
        foreach (var (key, value) in data)
        {
            var slash = key.IndexOf('/', StringComparison.Ordinal);
            if (slash < 1) throw new InvalidDataException("Invalid chat generation key.");
            GenerationFilters.RequireProvider(key[..slash]);
            var chat = key[(slash + 1)..];
            StoreManifest.RequireRelative(chat);
            if (chat.Contains('/', StringComparison.Ordinal)) throw new InvalidDataException("Invalid chat id.");
            var canonical = "chat-" + chat;
            if (value != canonical && !(value.StartsWith(canonical + "-", StringComparison.Ordinal) && value.Length == canonical.Length + 17 && value[^16..].All(char.IsAsciiHexDigit)))
                throw new InvalidDataException("Invalid moved generation name.");
        }
        return data;
    }
    public string Get(string provider, string chat) => Read().GetValueOrDefault(provider + "/" + chat, "chat-" + chat);
    public void Set(string provider, string chat, string folderName)
    {
        var data = Read();
        data[provider + "/" + chat] = folderName;
        Directory.CreateDirectory(paths.Skills);
        FileOps.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(data));
    }
}
