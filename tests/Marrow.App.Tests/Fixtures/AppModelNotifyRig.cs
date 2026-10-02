using System.Reflection;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for the turn-finished, dock-badge and notification-permission tests of App/AppModel.swift:1173-1273.
// Helper types live here, never inside a test class (R-TEST-3).

/// <summary>
/// One AppModel over one project, with everything its attention path reaches bound to a recorder (R-TEST-10): the
/// notification centre is a <see cref="RecordingAppToastCenter"/> (no Windows toast is ever posted), the finish chime's
/// sound output is a <see cref="SettingsViewSoundRecorder"/> (no audio device is opened; a start is counted), and
/// "the app is frontmost" is a flag the test sets. The model runs on the harness's manual main loop and clock.
/// </summary>
public sealed class AppModelNotifyRig : IDisposable
{
    private static readonly FieldInfo DockUnreadCount =
        typeof(AppModel).GetField("dockUnreadCount", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(AppModel), "dockUnreadCount");

    private readonly List<IDisposable> scopes = [];

    public AppModelNotifyRig(Func<Project, IReadOnlyList<ChatThread>> threads, bool active = false)
    {
        Harness = new AppModelHarness();
        Project = AppModelSamples.Project("Project", Harness.NewFolder("notify"));
        Threads = threads(Project);
        Harness.WriteLibrary([Project], Threads);
        Center = new RecordingAppToastCenter();
        Sound = new SettingsViewSoundRecorder();
        Active = active;
        scopes.Add(ToastNotifications.Center.Override(Center));
        scopes.Add(SoundOutput.Current.Override(Sound));
        scopes.Add(NSApplication.IsActive.Override(() => Active));
        Model = Harness.MakeModel();
    }

    public AppModelHarness Harness { get; }

    public AppModel Model { get; }

    public Project Project { get; }

    public IReadOnlyList<ChatThread> Threads { get; }

    /// <summary>Every toast the model asked the notification centre for, and every permission request it made.</summary>
    public RecordingAppToastCenter Center { get; }

    /// <summary>The finish chime's output: each play that reached it counts one start.</summary>
    public SettingsViewSoundRecorder Sound { get; }

    /// <summary>NSApp.isActive: whether the app is the frontmost application.</summary>
    public bool Active { get; set; }

    /// <summary>The count updateDockBadge last computed (the badge text, in a form the taskbar can keep).</summary>
    public long DockBadge => (long)DockUnreadCount.GetValue(Model)!;

    public void Configure(bool chime, bool notify) => Harness.Main.OnMain(() =>
    {
        Model.Settings.ChimeWhenFinished = chime;
        Model.Settings.NotifyWhenFinished = notify;
    });

    /// <summary>A turn ended; the notification tasks it spawned run to their end before this returns.</summary>
    public void Finish(Guid id, TurnStatus status, bool continues = false)
    {
        Harness.Main.OnMain(() => Model.TurnFinished(id, status, continues));
        Harness.Main.Loop.RunUntilIdle();
    }

    public ChatThread Thread(Guid id) => Harness.Main.OnMain(() => Model.Thread(id)!);

    public void Dispose()
    {
        for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
        Harness.Dispose();
    }
}
