using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Dextra AS-12: a memory store on a throwaway assistant store (under the test data folder, never the owner's
/// %LOCALAPPDATA%), a clock that moves only when a test moves it, and the helpers every memory test uses.
/// </summary>
public sealed class MemoryRig : IDisposable
{
    public const string Main = "dextra";
    public const string Astur = "astur1";
    public const string OtherAstur = "astur2";
    public static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public MemoryRig(Func<MemoryStoreOptions, MemoryStoreOptions>? options = null)
    {
        Clock = new ManualTimeProvider(Start);
        Options = options?.Invoke(BaseOptions()) ?? BaseOptions();
        Store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { Time = Clock });
        Memory = new MemoryStore(Store, Options);
    }

    public ManualTimeProvider Clock { get; }

    public MemoryStoreOptions Options { get; }

    public AssistantStore Store { get; private set; }

    public MemoryStore Memory { get; private set; }

    public AssistantTempRoot Temp => temp;

    public string MemoryRoot => Path.Combine(temp.Store, "memory");

    private MemoryStoreOptions BaseOptions() => new() { Time = Clock, MainAssistantId = () => Main };

    public string Folder(string scope) => Path.Combine(MemoryRoot, scope);

    /// <summary>Every byte of every file under the memory folder (temporary files included), as text: what a scan for leftover words reads.</summary>
    public string AllText()
    {
        if (!Directory.Exists(MemoryRoot)) return "";
        var text = new System.Text.StringBuilder();
        foreach (var path in Directory.EnumerateFiles(MemoryRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            text.Append("== ").Append(Path.GetRelativePath(MemoryRoot, path)).Append('\n').Append(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path))).Append('\n');
        return text.ToString();
    }

    public string[] FileNames(string scope) =>
        Directory.Exists(Folder(scope)) ? [.. Directory.EnumerateFiles(Folder(scope)).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal)] : [];

    public string Read(string scope, string file) => File.ReadAllText(Path.Combine(Folder(scope), file));

    /// <summary>Closes the stores and opens them again on the same folder, as a restart does.</summary>
    public MemoryStore Reopen()
    {
        Store.Dispose();
        Store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { Time = Clock });
        Memory = new MemoryStore(Store, Options);
        return Memory;
    }

    // ---- proposals -------------------------------------------------------------------------------------------------------------

    public static readonly NoteSource ChatSource = new(NoteSourceKind.Chat, "chat:0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a#1", Detail: "Planning");

    /// <summary>A note an assistant drew from a clean turn.</summary>
    public static NoteProposal Clean(string text, NoteKind kind = NoteKind.Fact, string scope = Main, string? topic = null, string? ownerWords = null) =>
        new(text, kind, scope, ChatSource) { Topic = topic, OwnerWords = ownerWords, AuthorName = scope };

    /// <summary>A note drawn from a turn that read a web page.</summary>
    public static NoteProposal FromWeb(string text, NoteKind kind = NoteKind.Fact, string scope = Main, string? ownerWords = null) =>
        Clean(text, kind, scope, ownerWords: ownerWords) with
        {
            Context = TaintContext.Clean.Absorb(TaintedText.Read(ContentOrigin.Web, "https://hostile.example/page", text)),
        };

    /// <summary>A note the owner typed into the Memory view.</summary>
    public static NoteProposal Typed(string text, NoteKind kind = NoteKind.Fact, string scope = MemoryScope.Shared) =>
        new(text, kind, scope, new NoteSource(NoteSourceKind.Owner, "memory view")) { Author = NoteAuthor.Owner, OwnerTyped = true };

    public void Dispose()
    {
        Store.Dispose();
        temp.Dispose();
    }
}
