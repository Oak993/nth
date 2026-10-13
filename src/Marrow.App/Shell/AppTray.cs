// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4 "One tray icon for nth", 12.6, 16.2; unit AS-6; ED-NEW-dextra-as6-10). No Swift twin.
// The one tray icon of nth. Every feature adds its items through Contribute(section, ...): the Assistant adds Stop Assistant and
// Pause all (Resume all); Tactus adds Pause or Resume Tactus and Stop Tactus; the scheduler its duty switches. The icon shows
// while the window is hidden, always if the owner sets it, or while a contributor says it is live (Tactus shows it while a
// stream is live); it wears one colour and the state of the busiest contributor (Needs you, then Working, then Paused). Stop
// works with the window closed because the items belong to services, not to the window. The tray itself is behind ITrayHost, so
// everything here is tested without a desktop; AvaloniaTrayHost is the real one (the lead's Native tests cover it).
namespace Marrow.App.Tray;

/// <summary>The few states a tray icon shows (the still frames of Armilla, 2.5): one colour, swapped only when the state changes.</summary>
public enum TrayState { Ready, Working, Paused, NeedsYou }

/// <summary>One item of the tray menu. <paramref name="Id"/> is stable (tests and Native tests find items by it).</summary>
public sealed record TrayItem(string Id, string Label, Action Run, bool Enabled = true);

/// <summary>What a feature wants in the menu now.</summary>
public sealed record TrayContribution(IReadOnlyList<TrayItem> Items, bool Live = false, TrayState State = TrayState.Ready);

/// <summary>A feature's items under its section name.</summary>
public sealed record TrayGroup(string Section, IReadOnlyList<TrayItem> Items);

/// <summary>What the tray shows: the icon's state, its tip and the menu groups, each separated from the next.</summary>
public sealed record TrayModel(TrayState State, string ToolTip, IReadOnlyList<TrayGroup> Groups);

/// <summary>The icon itself (Avalonia's, or a test's). Called from any thread; it marshals to the UI thread itself.</summary>
public interface ITrayHost : IDisposable
{
    void Show(TrayModel model);

    void Hide();
}

public sealed class AppTray : IDisposable
{
    public const string OpenId = "nth.open";

    private readonly object gate = new();
    private readonly object showGate = new(); // serialises the calls to the host, so a stale model never lands after a newer one
    private readonly ITrayHost host;
    private readonly Func<bool> windowHidden;
    private readonly Func<bool> alwaysShow;
    private readonly Action? open;
    private readonly string toolTip;
    private readonly List<Section> sections = [];
    private readonly Dictionary<string, Action> actions = [];
    private string? shown;
    private TrayModel? model;
    private bool disposed;

    private sealed class Section(string name, Func<TrayContribution> contribution)
    {
        public string Name { get; } = name;
        public Func<TrayContribution> Contribution { get; } = contribution;
    }

    private sealed class Contributed(AppTray owner, Section section) : IDisposable
    {
        private int ended;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0) owner.Withdraw(section);
        }
    }

    /// <param name="host">The icon.</param>
    /// <param name="windowHidden">Whether nth's window is hidden or closed (the icon shows then).</param>
    /// <param name="alwaysShow">The owner's setting to show the icon always.</param>
    /// <param name="open">Brings nth's window back; adds "Open nth" at the top of the menu.</param>
    /// <param name="toolTip">The icon's tip.</param>
    public AppTray(ITrayHost host, Func<bool> windowHidden, Func<bool>? alwaysShow = null, Action? open = null, string toolTip = "nth")
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.windowHidden = windowHidden ?? throw new ArgumentNullException(nameof(windowHidden));
        this.alwaysShow = alwaysShow ?? (() => false);
        this.open = open;
        this.toolTip = toolTip;
    }

    /// <summary>A contributor's item threw when it was clicked.</summary>
    public event Action<string>? Faulted;

    /// <summary>Whether the icon is shown now.</summary>
    public bool Visible
    {
        get
        {
            lock (gate) return model is not null;
        }
    }

    /// <summary>What the icon shows now; null while it is hidden.</summary>
    public TrayModel? Model
    {
        get
        {
            lock (gate) return model;
        }
    }

    /// <summary>
    /// Adds a feature's items. <paramref name="contribution"/> is asked again at every <see cref="Refresh"/>, so labels flip
    /// (Pause all to Resume all) and the icon follows the feature's state. Dispose to take the items away.
    /// </summary>
    public IDisposable Contribute(string section, Func<TrayContribution> contribution)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentNullException.ThrowIfNull(contribution);
        var added = new Section(section, contribution);
        lock (gate) sections.Add(added);
        Refresh();
        return new Contributed(this, added);
    }

    /// <summary>Looks again at the window, the setting and every contributor, and shows, changes or hides the icon.</summary>
    public void Refresh()
    {
        lock (showGate)
        {
            TrayModel? next;
            lock (gate)
            {
                if (disposed) return;
                next = Build();
                var key = next is null ? null : Key(next);
                if (key == shown) return;
                shown = key;
                model = next;
            }
            if (next is null) host.Hide();
            else host.Show(next);
        }
    }

    /// <summary>Runs an item as a click on it would (for tests and the voice route). False when it is not in the menu.</summary>
    public bool Invoke(string id)
    {
        Action? run;
        lock (gate) actions.TryGetValue(id, out run);
        if (run is null) return false;
        Run(id, run);
        return true;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            sections.Clear();
            actions.Clear();
            model = null;
        }
        host.Dispose();
    }

    private void Withdraw(Section section)
    {
        lock (gate) sections.Remove(section);
        Refresh();
    }

    // Under gate. The menu as it reads now, or null when the icon is hidden.
    private TrayModel? Build()
    {
        var groups = new List<TrayGroup>();
        var live = false;
        var state = TrayState.Ready;
        actions.Clear();
        if (open is not null) groups.Add(new TrayGroup("nth", [Wrap(new TrayItem(OpenId, "Open nth", open))]));
        foreach (var section in sections)
        {
            TrayContribution contribution;
            try { contribution = section.Contribution(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Faulted?.Invoke($"{section.Name} could not add its items: {error.Message}");
                continue;
            }
            live |= contribution.Live;
            state = Busiest(state, contribution.State);
            var items = contribution.Items.Select(Wrap).ToList();
            if (items.Count > 0) groups.Add(new TrayGroup(section.Name, items));
        }
        var visible = live || alwaysShow() || windowHidden();
        return visible ? new TrayModel(state, toolTip, groups) : null;
    }

    // The item the host gets runs through this tray, so the action behind an id is always the latest one.
    private TrayItem Wrap(TrayItem item)
    {
        actions[item.Id] = item.Run;
        return item with { Run = () => Invoke(item.Id) };
    }

    private void Run(string id, Action run)
    {
        try { run(); }
        catch (Exception error) when (error is not OutOfMemoryException) { Faulted?.Invoke($"{id} failed: {error.Message}"); }
    }

    private static TrayState Busiest(TrayState a, TrayState b) => Rank(b) > Rank(a) ? b : a;

    private static int Rank(TrayState state) => state switch
    {
        TrayState.NeedsYou => 3,
        TrayState.Working => 2,
        TrayState.Paused => 1,
        _ => 0,
    };

    // What the host would show, without the delegates: the host is only called when this changes.
    private static string Key(TrayModel m) =>
        m.State + "|" + m.ToolTip + "|" + string.Join(";", m.Groups.Select(g => g.Section + ":" + string.Join(",", g.Items.Select(i => i.Id + "=" + i.Label + (i.Enabled ? "" : "!")))));
}
