using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W10, what the Visus and Tabula pages do in the app (ED-NEW-web-w10-4). The pages are self-contained sections that take
// their parts; these are the app's: the kept site answers of Visus, the system folder picker, "Clear browsing data" and "Forget agent data"
// (VisusPrivacy), the artifact store's storage per thread with its Clear, and the confirmation that asks before something that cannot be undone.

namespace Marrow.App.Port.Web;

public static class WebSettingsPages
{
    /// <summary>Asks before an irreversible clear; tests answer it. The default is a modal alert over the window, Cancel by default.</summary>
    public static Func<string, string, string, Task<bool>> Confirm { get; set; } = ConfirmWithAlertAsync;

    /// <summary>Picks a folder with the system dialog; tests answer it. Null: cancelled.</summary>
    public static Func<Task<string?>> PickFolder { get; set; } = PickFolderAsync;

    public static VisusSettingsParts Visus() => new()
    {
        Settings = static () => WebSettingsHost.Current,
        Permissions = static () => VisusHost.Permissions.All,
        RemovePermission = static entry => VisusHost.Permissions.Remove(entry.Profile, entry.Origin, entry.Kind),
        ObservePermissions = static notify =>
        {
            var store = VisusHost.Permissions;
            store.Changed += notify;
            return new Unsubscribe(() => store.Changed -= notify);
        },
        DefaultDownloadsFolder = static () => Marrow.App.Views.Composer.RecentDownloads.DirectoryProvider.Current(),
        PickFolder = static () => PickFolder(),
        ClearBrowsingData = static profile => VisusPrivacy.ClearBrowsingDataAsync(profile),
        ForgetAgentData = static () => VisusPrivacy.ForgetAgentDataAsync(),
        Confirm = static (question, detail, verb) => Confirm(question, detail, verb),
    };

    public static TabulaSettingsParts Tabula() => new()
    {
        Settings = static () => WebSettingsHost.Current,
        Storage = ReadStorageAsync,
        ClearArtifacts = static thread => ClearArtifactsAsync(thread),
        Confirm = static (question, detail, verb) => Confirm(question, detail, verb),
    };

    // The bytes each thread's artifacts take, counted off the UI thread; the titles are read on it after.
    private static async Task<IReadOnlyList<ArtifactStorageRow>> ReadStorageAsync(CancellationToken cancellationToken)
    {
        if (ArtifactHost.Shared.Current.Store is not { } store) return [];
        var counted = await Task.Run(() => store.Threads().Select(thread => (Thread: thread, Bytes: store.StorageUsed(thread))).Where(pair => pair.Bytes > 0).ToList(), cancellationToken);
        var model = Marrow.App.Window.WindowManager.Shared.Model;
        return [.. counted.Select(pair => new ArtifactStorageRow(pair.Thread, TitleOf(model, pair.Thread), pair.Bytes)).OrderByDescending(row => row.Bytes).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string TitleOf(AppModel model, Guid thread) =>
        model.Thread(thread)?.Title is { Length: > 0 } title && title != ChatThread.Untitled ? title : "Thread " + SwiftUUID.UuidString(thread)[..8].ToLowerInvariant();

    private static Task ClearArtifactsAsync(Guid thread)
    {
        if (ArtifactHost.Shared.Current.Store is not { } store) return Task.CompletedTask;
        return Task.Run(() => store.Clear(thread));
    }

    private static async Task<string?> PickFolderAsync()
    {
        var top = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(window => window.IsActive)
            ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top?.StorageProvider is not { CanPickFolder: true } storage) return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the downloads folder", AllowMultiple = false });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    private static Task<bool> ConfirmWithAlertAsync(string question, string detail, string verb)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new AlertRequest(question, detail,
        [
            new AlertButton(verb, () => answer.TrySetResult(true)) { Role = AlertButtonRole.Destructive },
            new AlertButton("Cancel", () => answer.TrySetResult(false)) { Role = AlertButtonRole.Cancel },
        ], AlertKind.ConfirmationDialog);
        _ = ShowAsync(request, answer);
        return answer.Task;
    }

    private static async Task ShowAsync(AlertRequest request, TaskCompletionSource<bool> answer)
    {
        try
        {
            await AlertPresentation.Show(request);
        }
        finally
        {
            answer.TrySetResult(false); // closed any other way (the window going away): nothing is cleared
        }
    }

    private sealed class Unsubscribe(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
