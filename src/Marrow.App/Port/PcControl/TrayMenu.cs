// PORT-ONLY: Tactus, PC control (OD-0145, OD-0146, ED-NEW-pcctl-15; DESIGN.md 10.1, 10.2 path 8, 10.10). The tray's
// Tactus menu: "Pause Tactus" or "Resume Tactus" (every app; it resumes only when every live app is paused, as the chord
// does), "Stop Tactus" (all, also over fullscreen), and "Answer Tactus" while an ask waits and no Answer key is set. nth
// has no tray icon of its own, so Tactus shows one while a stream is live (a chat used Tactus this turn, or an app is
// paused, or Pause all is on), wearing the Cuspis mark in its Ready, Working or Paused drawing, and removes it when Tactus
// is idle.
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Mark;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;
using Microsoft.Win32;
using SkiaSharp;

namespace Marrow.App.Port.PcControl;

/// <summary>One item of the tray's Tactus menu.</summary>
public sealed record TactusTrayItem(string Label, Action Run);

public static class TactusTrayMenu
{
    public const string PauseLabel = "Pause Tactus";
    public const string ResumeLabel = "Resume Tactus";
    public const string StopLabel = "Stop Tactus";
    public const string AnswerLabel = "Answer Tactus";

    /// <summary>The menu as it reads now. <paramref name="answer"/> is offered only while <paramref name="asksWaiting"/> holds.</summary>
    public static IReadOnlyList<TactusTrayItem> Items(PcPauseControl pause, PcStopControl stop, Func<bool>? asksWaiting = null, Action? answer = null)
    {
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(stop);
        var live = pause.Live;
        var resumes = ChordFilter.Resumes(live.Count, live.Count(pause.IsPaused), pause.Latched);
        var items = new List<TactusTrayItem>
        {
            resumes
                ? new TactusTrayItem(ResumeLabel, () => pause.Resume(PcScope.All, PcPauseSource.Tray))
                : new TactusTrayItem(PauseLabel, () => pause.Pause(PcScope.All, PcPauseSource.Tray)),
            new TactusTrayItem(StopLabel, stop.Tray),
        };
        if (answer is not null && asksWaiting?.Invoke() == true) items.Add(new TactusTrayItem(AnswerLabel, answer));
        return items;
    }

    /// <summary>The mark's drawing for the tray: Paused when every live app is, Working while a call runs, else Ready.</summary>
    public static TactusSmallState State(PcPauseControl pause, PcLanes lanes)
    {
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(lanes);
        var live = pause.Live;
        if ((live.Count > 0 && live.All(pause.IsPaused)) || (live.Count == 0 && pause.Latched)) return TactusSmallState.Paused;
        return lanes.Active.Count > 0 ? TactusSmallState.Working : TactusSmallState.Ready;
    }

    /// <summary>Whether Tactus shows its tray icon: a stream is live.</summary>
    public static bool Shows(PcPauseControl pause, PcLanes lanes) =>
        lanes.Active.Count > 0 || pause.InTurn || pause.PausedApps.Count > 0 || pause.Latched;
}

/// <summary>The tray icon itself (made on the UI thread; it follows the pause control and the lanes).</summary>
public sealed class TactusTray : IDisposable
{
    private readonly PcPauseControl pause;
    private readonly PcStopControl stop;
    private readonly PcLanes lanes;
    private readonly Func<bool>? asksWaiting;
    private readonly Action? answer;
    private TrayIcon? icon;
    private TactusSmallState? drawn;

    public TactusTray(PcPauseControl pause, PcStopControl stop, PcLanes lanes, Func<bool>? asksWaiting = null, Action? answer = null)
    {
        this.pause = pause;
        this.stop = stop;
        this.lanes = lanes;
        this.asksWaiting = asksWaiting;
        this.answer = answer;
        pause.Changed += Schedule;
        lanes.ActiveChanged += OnActive;
        Update();
    }

    public void Dispose()
    {
        pause.Changed -= Schedule;
        lanes.ActiveChanged -= OnActive;
        icon?.Dispose();
        icon = null;
    }

    private void OnActive(IReadOnlyList<string> active) => Schedule();

    private void Schedule() => Dispatcher.UIThread.Post(Update, DispatcherPriority.Background);

    private void Update()
    {
        if (!TactusTrayMenu.Shows(pause, lanes))
        {
            icon?.Dispose();
            icon = null;
            drawn = null;
            return;
        }
        icon ??= new TrayIcon { ToolTipText = "Tactus" };
        var state = TactusTrayMenu.State(pause, lanes);
        if (drawn != state)
        {
            using var stream = new MemoryStream(TactusMarkImage.Ico(state, LightTaskbar() ? new SKColor(0x1C, 0x1C, 0x1E) : new SKColor(0xEC, 0xEC, 0xEE)));
            icon.Icon = new WindowIcon(stream);
            drawn = state;
        }
        var menu = new NativeMenu();
        foreach (var item in TactusTrayMenu.Items(pause, stop, asksWaiting, answer))
        {
            var entry = new NativeMenuItem(item.Label);
            entry.Click += (_, _) => item.Run();
            menu.Items.Add(entry);
        }
        icon.Menu = menu;
        icon.IsVisible = true;
    }

    // The taskbar's own theme (read only): a light taskbar gets the dark mark.
    private static bool LightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
