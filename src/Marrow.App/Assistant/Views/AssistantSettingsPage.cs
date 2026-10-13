// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.6 the first-run choices kept in Settings, 4.3 "Answer questions for me", 5.4 the
// brain's reading switches, 12.1 the owner's rules, 12.2 grants revoked from Settings, 12.6 the stop chord; the AS-5 note that
// grant persistence belongs to this page; ED-NEW-dextra-as8-8). No Swift twin.
// Settings › Assistant, in its own page class (Settings is moving into the main window; this class moves with it unchanged):
// the lead sentence, one card (Dextra on, the brain), What it may read, Answer questions for me, Stop (the chord),
// Permissions (every grant kept, each with Revoke) and the rules read-only. Built from the Settings pages' own parts, as
// Ignis's page is. Every setting is DextraSettings' (the one "dextra" key).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Assistant.Views;

/// <summary>What the page reads and calls beyond the settings: the live grants and their revoke, and mission titles.</summary>
public sealed record AssistantSettingsParts
{
    public BrainChoices? Brain { get; init; }

    /// <summary>The grants in force (the running controller's, else the kept ones).</summary>
    public Func<IReadOnlyList<Grant>>? Grants { get; init; }

    /// <summary>Revokes one grant; false when it was not found.</summary>
    public Func<Guid, bool>? Revoke { get; init; }

    public Func<Guid, string?>? MissionTitle { get; init; }

    /// <summary>Raised when the grants change (the controller's GrantsChanged).</summary>
    public Action<Action>? WatchGrants { get; init; }

    public Action<Action>? UnwatchGrants { get; init; }

    /// <summary>Whether the talk chord is registered, and why not (AS-18); null: the voice is not running, no status is shown.</summary>
    public Func<VoiceChordStatus?>? TalkStatus { get; init; }

    /// <summary>Raised when the talk chord's status changes.</summary>
    public Action<Action>? WatchTalk { get; init; }

    public Action<Action>? UnwatchTalk { get; init; }

    /// <summary>The voices installed on this PC (id and name), for the Voice row; null hides the row.</summary>
    public Func<IReadOnlyList<(string Id, string Name)>>? Voices { get; init; }

    /// <summary>The running purchase desk (AS-20), read at every redraw; null or a null answer: Purchases says it is set up while Dextra is on.</summary>
    public Func<PurchaseDesk?>? Purchases { get; init; }
}

public sealed class AssistantSettingsPage : Decorator
{
    /// <summary>The page is built (the SA-1 pattern): listed in the sidebar, found by its search, opened by a request.</summary>
    public const bool IsReady = true;

    private DextraSettings? settings;
    private AssistantSettingsParts parts = new();
    private AssistantRows? rows;
    private SettingsSwitch? onSwitch;
    private PurchaseSettingsSection? purchases;
    private CzMenuButton? chordMenu;
    private CzMenuButton? talkMenu;
    private CzMenuButton? voiceMenu;
    private readonly TextBlock talkStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 8), IsVisible = false };
    private readonly StackPanel grantList = new() { Spacing = 0 };
    private readonly List<(Grant Grant, Button Revoke)> grantRows = [];
    private int queued;

    /// <summary>The page Settings opens: the app's Dextra host (its settings, its controller's grants).</summary>
    public AssistantSettingsPage()
    {
        AttachedToVisualTree += (_, _) =>
        {
            if (settings is not null) { Watch(true); return; }
            AppModel model;
            try { model = AppEnvironment.GetAppModel(this); }
            catch (InvalidOperationException) { return; }
            var host = model.AssistantHost;
            Build(host.Settings, new AssistantSettingsParts
            {
                Brain = BrainChoices.Of(model),
                Grants = () => host.Controller?.Grants ?? host.Settings.Value.Grants,
                Revoke = id => host.Controller is { } controller ? controller.RevokeGrant(id, Marrow.Core.Port.SwiftClock.Provider.Current.GetUtcNow()) : ForgetKept(host.Settings, id),
                MissionTitle = id => host.Controller?.Runner.Find(id)?.Mission.Title,
                Purchases = () => host.Controller?.Purchases,
                WatchGrants = changed => { if (host.Controller is { } c) c.GrantsChanged += changed; },
                UnwatchGrants = changed => { if (host.Controller is { } c) c.GrantsChanged -= changed; },
                TalkStatus = () => host.Voice?.Chord.Status,
                WatchTalk = changed => { if (host.Voice is { } v) v.Chord.StatusChanged += _ => changed(); },
                Voices = () => [.. new WinRtSpeechSynth().Voices().Where(SpeechVoices.IsLocal).Select(v => (v.Id, v.Name))],
            });
            Watch(true);
        };
        DetachedFromVisualTree += (_, _) => Watch(false);
    }

    /// <summary>A page over the given settings and parts (tests).</summary>
    public AssistantSettingsPage(DextraSettings settings, AssistantSettingsParts? parts = null)
    {
        Build(settings, parts ?? new AssistantSettingsParts());
        AttachedToVisualTree += (_, _) => Watch(true);
        DetachedFromVisualTree += (_, _) => Watch(false);
    }

    public SettingsSwitch OnSwitch => onSwitch!;

    public CzMenuButton ChordMenu => chordMenu!;

    /// <summary>The talk chord's menu (AS-18).</summary>
    public CzMenuButton TalkMenu => talkMenu!;

    /// <summary>The Voice menu, or null while the page was given no voices.</summary>
    public CzMenuButton? VoiceMenu => voiceMenu;

    /// <summary>The line under the talk chord that says why it is not registered (shown only then).</summary>
    public TextBlock TalkStatusText => talkStatus;

    internal AssistantRows Rows => rows!;

    /// <summary>Settings › Assistant › Purchases (AS-20).</summary>
    public PurchaseSettingsSection Purchases => purchases!;

    /// <summary>The Revoke button of each grant shown, in order.</summary>
    public IReadOnlyList<(Grant Grant, Button Revoke)> GrantRows => grantRows;

    /// <summary>The line shown while no grant is kept, or null.</summary>
    public TextBlock? NoGrantsText { get; private set; }

    private void Build(DextraSettings dextra, AssistantSettingsParts given)
    {
        settings = dextra;
        parts = given;
        rows = new AssistantRows(dextra, given.Brain);
        onSwitch = new SettingsSwitch(new Binding<bool>(() => dextra.Value.On, on => dextra.Update(p => p with { On = on })));
        AutomationProperties.SetName(onSwitch, AssistantSettingsWords.RowOn);

        var chords = AssistantSettingsWords.Chords.ToList();
        if (!chords.Contains(dextra.Chord)) chords.Add(dextra.Chord);
        chordMenu = new CzMenuButton(AssistantSettingsWords.RowChord, [.. chords.Select(c => (c, c))], dextra.Chord);
        chordMenu.SelectionChanged += id => dextra.Update(p => p with { StopChord = id == DextraSettings.DefaultStopChord ? null : id });

        var talks = AssistantSettingsWords.TalkChords.ToList();
        if (!talks.Contains(dextra.TalkChord)) talks.Add(dextra.TalkChord);
        talkMenu = new CzMenuButton(AssistantSettingsWords.RowTalk, [.. talks.Select(c => (c, c))], dextra.TalkChord);
        talkMenu.SelectionChanged += id => dextra.Update(p => p with { TalkChord = id == DextraSettings.DefaultTalkChord ? null : id });
        SwiftFont.System(12).Apply(talkStatus);
        talkStatus.Foreground = Chrome.SecondaryText.Brush(talkStatus);
        AutomationProperties.SetLiveSetting(talkStatus, AutomationLiveSetting.Polite);

        var lead = new TextBlock { Text = AssistantSettingsWords.Lead, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        SwiftFont.Subheadline.Apply(lead);
        lead.Foreground = Chrome.SecondaryText.Brush(lead);

        var main = new StackPanel();
        main.Children.Add(new ChromeRow(AssistantSettingsWords.RowOn, () => onSwitch, AssistantSettingsWords.RowOnDetail));
        main.Children.Add(new ChromeRowDivider());
        main.Children.Add(rows.BrainRows());

        purchases = new PurchaseSettingsSection(given.Purchases ?? (() => null), given.MissionTitle);

        var stop = new StackPanel();
        stop.Children.Add(new ChromeRow(AssistantSettingsWords.RowChord, () => chordMenu, AssistantSettingsWords.RowChordDetail));

        var voicePanel = new StackPanel();
        voicePanel.Children.Add(new ChromeRow(AssistantSettingsWords.RowTalk, () => talkMenu, AssistantSettingsWords.RowTalkDetail));
        voicePanel.Children.Add(talkStatus);
        if (given.Voices?.Invoke() is { Count: > 0 } installed)
        {
            voiceMenu = new CzMenuButton(AssistantSettingsWords.RowVoice, [("", AssistantSettingsWords.Automatic), .. installed.Select(v => (v.Id, v.Name))], dextra.Value.VoiceName ?? "");
            voiceMenu.SelectionChanged += id => dextra.Update(p => p with { VoiceName = id.Length == 0 ? null : id });
            voicePanel.Children.Add(new ChromeRowDivider());
            voicePanel.Children.Add(new ChromeRow(AssistantSettingsWords.RowVoice, () => voiceMenu, AssistantSettingsWords.RowVoiceDetail));
        }

        Child = new StackPanel
        {
            Spacing = Chrome.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { lead, new ChromeCard(() => main) } },
                new ChromeSection(AssistantSettingsWords.Reading, () => new ChromeCard(() => rows.ReadingRows())),
                new ChromeSection(AssistantSettingsWords.Delegate, () => new ChromeCard(() => rows.DelegateRows())),
                new ChromeSection(AssistantSettingsWords.Stop, () => new ChromeCard(() => stop)),
                new ChromeSection(AssistantSettingsWords.Voice, () => new ChromeCard(() => voicePanel)),
                new ChromeSection(AssistantSettingsWords.Permissions, () => new ChromeCard(() => grantList)),
                purchases,
                new ChromeSection(AssistantSettingsWords.Rules, () => new ChromeCard(() => AssistantRows.RulesList(null))),
            },
        };
        ShowGrants();
    }

    private bool watching;

    private void Watch(bool on)
    {
        if (settings is null || on == watching) return;
        watching = on;
        if (on)
        {
            settings.Changed += Queue;
            parts.WatchGrants?.Invoke(Queue);
            parts.WatchTalk?.Invoke(Queue);
            Refresh();
        }
        else
        {
            settings.Changed -= Queue;
            parts.UnwatchGrants?.Invoke(Queue);
            parts.UnwatchTalk?.Invoke(Queue);
        }
    }

    // Changes can come from any thread (a grant made by a card's answer); the page redraws once on the UI thread.
    private void Queue()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref queued, 0);
            if (watching) Refresh(); // a page that went away meanwhile redraws when it is shown again
        });
    }

    private void Refresh()
    {
        rows?.Show();
        if (settings is not null)
        {
            chordMenu?.Show(settings.Chord);
            talkMenu?.Show(settings.TalkChord);
            voiceMenu?.Show(settings.Value.VoiceName ?? "");
        }
        ShowTalkStatus();
        purchases?.Show();
        ShowGrants();
    }

    // A talk chord another app holds (or that cannot be registered) is said under its row, with the way out (2.4: "shows in Settings with Change").
    private void ShowTalkStatus()
    {
        var line = parts.TalkStatus?.Invoke()?.Line;
        talkStatus.Text = line ?? "";
        talkStatus.IsVisible = !string.IsNullOrEmpty(line);
    }

    private void ShowGrants()
    {
        grantList.Children.Clear();
        grantRows.Clear();
        NoGrantsText = null;
        var now = Marrow.Core.Port.SwiftClock.Provider.Current.GetUtcNow();
        IReadOnlyList<Grant> source = parts.Grants?.Invoke() ?? (IReadOnlyList<Grant>?)settings?.Value.Grants ?? [];
        var grants = source.Where(g => DextraSettings.Keeps(g, now)).ToList();
        if (grants.Count == 0)
        {
            var none = new TextBlock { Text = AssistantSettingsWords.NoGrants, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12) };
            SwiftFont.System(13).Apply(none);
            none.Foreground = Chrome.SecondaryText.Brush(none);
            NoGrantsText = none;
            grantList.Children.Add(none);
            return;
        }
        foreach (var grant in grants)
        {
            if (grantList.Children.Count > 0) grantList.Children.Add(new ChromeRowDivider());
            var line = AssistantSettingsWords.GrantLine(grant);
            var (revoke, shown) = TaskLook.Small(AssistantSettingsWords.Revoke, () => RevokeOne(grant.Id), name: "Revoke " + line);
            grantRows.Add((grant, revoke));
            grantList.Children.Add(new ChromeRow(line, () => shown, AssistantSettingsWords.GrantScope(grant, parts.MissionTitle)));
        }
    }

    private void RevokeOne(Guid id)
    {
        parts.Revoke?.Invoke(id);
        Refresh();
    }

    private static bool ForgetKept(DextraSettings settings, Guid id)
    {
        if (settings.Value.Grants.All(g => g.Id != id)) return false;
        settings.Update(p => p with { Grants = new Marrow.Core.Port.ValueList<Grant>(p.Grants.Where(g => g.Id != id)) });
        return true;
    }
}
