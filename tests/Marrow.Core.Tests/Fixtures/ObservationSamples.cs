using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

// Trimmed shapes of real Swift types, only to prove the conventions (not port code). Source: draft C
// (proto-C Samples.cs), rewritten on the consolidated foundation.

/// <summary>Core/Models/Provider.swift:3-17 (B2), trimmed. Raw values are the case names; order is declaration order.</summary>
public enum ProviderKindSample
{
    [RawValue("codex")] Codex,
    [RawValue("claude")] Claude,
    [RawValue("opencodeGo")] OpencodeGo,
    [RawValue("catppuccinMocha")] CatppuccinMocha,
}

/// <summary>Core/Support/ObservedValue.swift:6-14 (B2): an unconstrained Value, so every set notifies (SetAlways).</summary>
public sealed class ObservedValueSample<TValue> : MainActorModel
{
    private TValue value;

    public ObservedValueSample(TValue value) => this.value = value; // :11-13

    public TValue Value { get => Read(value); set => SetAlways(ref this.value, value); } // :9
}

/// <summary>App/AppSettings.swift (B2), trimmed: keys :118/:121/:132, didSets :189-191, init :705-722.</summary>
public sealed class SettingsSample : ObservableModel
{
    public const string DefaultProviderKey = "defaultProvider";
    public const string NotifyKey = "notifyWhenFinished";
    public const string SidebarActivityViewKey = "sidebarActivityView";

    private readonly ISettingsStore defaults; // @ObservationIgnored
    private ProviderKindSample defaultProvider;
    private bool notifyWhenFinished;
    private readonly bool sidebarActivityView;

    public SettingsSample()
    {
        defaults = UserDefaults.Standard.Current;
        // Backing fields, not properties: didSet does not run inside Swift's init, so loading writes nothing back.
        defaultProvider = SwiftEnum<ProviderKindSample>.FromRawValue(defaults.String(DefaultProviderKey) ?? "") ?? ProviderKindSample.Codex;
        notifyWhenFinished = Plist.AsBool(defaults.Object(NotifyKey)) ?? true; // object(forKey:) as? Bool ?? true
        sidebarActivityView = defaults.Bool(SidebarActivityViewKey); // bool(forKey:): absent is false
    }

    public ProviderKindSample DefaultProvider
    {
        get => Read(defaultProvider);
        set => Set(ref defaultProvider, value, _ => defaults.Set(DefaultProviderKey, SwiftEnum<ProviderKindSample>.RawValue(defaultProvider)));
    }

    public bool NotifyWhenFinished
    {
        get => Read(notifyWhenFinished);
        set => Set(ref notifyWhenFinished, value, _ => defaults.Set(NotifyKey, notifyWhenFinished));
    }

    public bool SidebarActivityView => Read(sidebarActivityView);
}

/// <summary>A @MainActor @Observable model: every tracked set asserts the main actor.</summary>
public sealed class MainActorCounterSample : MainActorModel
{
    private int count;

    public int Count { get => Read(count); set => Set(ref count, value); }
}

/// <summary>Core/Models/Library.swift ChatThread, trimmed to what the tracking test needs.</summary>
public sealed record ChatThreadSample(Guid Id, string Title);

/// <summary>
/// App/AppModel.swift (B2), trimmed: :29-44 lists and their cells, :133-190 sync then write cells, :288-294 lookups,
/// :705-712 updateThread (work on a copy, compare, then store and save).
/// </summary>
public sealed class ThreadListModel : MainActorModel
{
    private ValueList<ChatThreadSample> threads = ValueList<ChatThreadSample>.Empty;
    private readonly Dictionary<Guid, ObservedValueSample<ChatThreadSample?>> threadCells = []; // @ObservationIgnored
    private Guid? selectedThreadID;

    public int ScheduledSaves { get; private set; } // stands in for scheduleSave()

    public ValueList<ChatThreadSample> Threads
    {
        get => Read(threads);
        private set => Set(ref threads, value, _ => Write(Sync(threadCells, threads)));
    }

    public Guid? SelectedThreadID { get => Read(selectedThreadID); set => Set(ref selectedThreadID, value); }

    public void Load(IEnumerable<ChatThreadSample> list) => Threads = new(list);

    public ChatThreadSample? Thread(Guid id) => threadCells.TryGetValue(id, out var cell) ? cell.Value : null;

    // AppModel.swift:292-294: a computed property reading an @ObservationIgnored dictionary's CELL.
    public ChatThreadSample? SelectedThread => Computed(() => SelectedThreadID is { } id ? Thread(id) : null);

    public string SelectedTitle => Computed(() => SelectedThread?.Title ?? "");

    public void UpdateThread(Guid id, Func<ChatThreadSample, ChatThreadSample> change)
    {
        var index = threads.ToList().FindIndex(thread => thread.Id == id);
        if (index < 0) return;
        var thread = change(threads[index]);
        if (thread == threads[index]) return;
        Threads = threads.SetItem(index, thread);
        ScheduledSaves++;
    }

    // "Settle the dictionary, then write cells" (AppModel.swift:150-190): kept even though C# would not trap.
    private static List<(ObservedValueSample<ChatThreadSample?> Cell, ChatThreadSample? Value)> Sync(
        Dictionary<Guid, ObservedValueSample<ChatThreadSample?>> cells, IReadOnlyList<ChatThreadSample> values)
    {
        var writes = new List<(ObservedValueSample<ChatThreadSample?>, ChatThreadSample?)>();
        var seen = new HashSet<Guid>();
        foreach (var value in values)
        {
            seen.Add(value.Id);
            if (cells.TryGetValue(value.Id, out var cell))
            {
                if (cell.Value != value) writes.Add((cell, value));
            }
            else cells[value.Id] = new(value);
        }
        foreach (var id in cells.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            writes.Add((cells[id], null));
            cells.Remove(id);
        }
        return writes;
    }

    private static void Write(List<(ObservedValueSample<ChatThreadSample?> Cell, ChatThreadSample? Value)> writes)
    {
        foreach (var (cell, value) in writes) cell.Value = value;
    }
}

/// <summary>The pitfall R-VAL-1 forbids: a model as a mutable class aliases on copy.</summary>
public sealed class MutableThread
{
    public string Title { get; set; } = "";
}
