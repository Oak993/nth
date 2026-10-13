using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-19: a teach recordings repository on a throwaway assistant store (never the owner's %LOCALAPPDATA%) and a clock that moves only when a test moves it.</summary>
public sealed class TeachRig : IDisposable
{
    public const string Assistant = "dextra";
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public TeachRig()
    {
        Clock = new ManualTimeProvider(Start);
        Store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { Time = Clock });
        Recordings = new TeachRecordings(Store, Assistant);
    }

    public ManualTimeProvider Clock { get; }

    public AssistantStore Store { get; }

    public TeachRecordings Recordings { get; }

    public string TeachRoot => Path.Combine(temp.Store, "teach");

    public string RecordingFolder(string id) => Path.Combine(TeachRoot, Assistant, id);

    public static TeachEvent Click(int seq, double seconds, string name = "Save", string app = "notepad") =>
        new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Click) { Verb = "click", Element = new TeachElement(app, "Untitled - Notepad", "button", name) };

    public static byte[] Jpeg(int size = 64) => [.. Enumerable.Range(0, size).Select(i => (byte)(i % 251))];

    public void Dispose() => Store.Dispose();
}
