// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2). Settings > Plugins > "Import": the observable model of the import (the pattern of ImportModel, SK-18), the target that adds a plugin the
// way "+ Add > From a folder" does (CustomAddSeams' backend: check the folder, add the checked plugin; the own-plugins list refreshes), the scan's seam, and the opening of the sheet in the window.
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Support;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.PluginImport;

namespace Marrow.App.Port.Skills.PluginImport;

internal sealed class PluginImportModel : MainActorModel
{
    private int revision;
    private bool posted;
    private int pulse;

    public PluginImportModel(PluginImportController controller)
    {
        Controller = controller;
        controller.Changed += OnChanged;
        revision = controller.Revision;
    }

    public PluginImportController Controller { get; }

    public bool SheetShown { get; set; }

    /// <summary>The folder chooser of the view on screen; null when none is bound.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
            return;
        }
        lock (this)
        {
            if (posted) return;
            posted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (this) posted = false;
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
        });
    }
}

/// <summary>"+ Add > From a folder" as a target: the same backend and the same own-plugins list.</summary>
internal sealed class CustomPluginImportTarget(ICustomAddBackend backend, CustomOwnModel? own) : IPluginImportTarget
{
    public async Task<IReadOnlyList<(string Id, string Name)>> ExistingAsync()
    {
        if (own is null) return [];
        await own.Refresh();
        return await Dispatcher.UIThread.InvokeAsync(() => (IReadOnlyList<(string, string)>)own.Items(CustomAddKind.Plugins).Select(i => (i.Id, i.Name)).ToArray());
    }

    public async Task<string?> AddAsync(string folder, string name, string? replacingId, CancellationToken cancel)
    {
        var candidates = await backend.Check(new CustomAddRequest(CustomAddKind.Plugins, CustomAddSource.Folder, folder, "", ""), cancel);
        var pick = candidates.FirstOrDefault(c => c.Problem is null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? (candidates.Count(c => c.Problem is null) == 1 ? candidates.First(c => c.Problem is null) : null);
        try
        {
            if (pick is null) return candidates.FirstOrDefault(c => c.Problem is not null)?.Problem ?? PluginImportWords.NotFound;
            // Replace: the new copy goes in first and the old one only after that worked, so a failure here leaves the plugin nth had (ED-NEW-mcpimport-6)
            if (replacingId is not null && own is not null) await backend.Replace(replacingId, pick, new Progress<double>(), cancel);
            else await backend.Add(pick, new Progress<double>(), cancel);
            return null;
        }
        finally
        {
            if (backend is ICustomAddLifetime lifetime) await lifetime.Discard([.. candidates.Where(c => !ReferenceEquals(c, pick))]);
        }
    }
}

internal static class PluginImportSeams
{
    /// <summary>The read-only scan of the plugins other apps installed (plus the folders chosen). A test overrides it, so no test reads the real profile.</summary>
    public static Ambient<Func<IReadOnlyList<string>, ForeignPluginScan>> Scan { get; } = new(() => (Func<IReadOnlyList<string>, ForeignPluginScan>)DefaultScan);

    private static ForeignPluginScan DefaultScan(IReadOnlyList<string> folders)
    {
        var home = LoginEnvironment.HomeDirectory;
        if (!Path.IsPathFullyQualified(home)) return new ForeignPluginScan([], []);
        // CLAUDE_CONFIG_DIR and CODEX_HOME: where Claude Code and Codex keep their plugins when the variable is set
        return new ForeignPluginScanner(home, folders, McpPlaceholders.EnvironmentValue("CLAUDE_CONFIG_DIR"), McpPlaceholders.EnvironmentValue("CODEX_HOME")).Scan();
    }
}

internal static class PluginImportLauncher
{
    private static readonly object Gate = new();
    private static PluginImportModel? shared;
    private static object? sharedFor;

    /// <summary>The import model over the app's custom-add backend, kept for the life of the app (one per backend); null when plugins cannot be added (no skills host).</summary>
    public static PluginImportModel? Model()
    {
        var services = CustomAddSeams.Backends.Current;
        if (services.For(CustomAddKind.Plugins) is not { } backend) return null;
        lock (Gate)
        {
            if (shared is not null && ReferenceEquals(sharedFor, services)) return shared;
            PluginImportModel? model = null;
            var controller = new PluginImportController(folders => PluginImportSeams.Scan.Current(folders), new CustomPluginImportTarget(backend, services.Own), () => model?.PickFolder?.Invoke() ?? Task.FromResult<string?>(null));
            model = new PluginImportModel(controller);
            shared = model;
            sharedFor = services;
            return model;
        }
    }

    public static void Open(Control anchor)
    {
        if (Model() is not { } model || model.SheetShown) return;
        model.SheetShown = true;
        model.PickFolder = async () =>
        {
            if (TopLevel.GetTopLevel(anchor) is not { } top) return null;
            var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = ImportWords.ChooseFolder });
            return picked.FirstOrDefault()?.TryGetLocalPath();
        };
        _ = model.Controller.OpenAsync();
        ChatSheetPresentation.Show(ImportSheetAnchor.For(anchor), dismiss => new ImportWashLayer(new PluginImportSheetView(model, dismiss)), () =>
        {
            model.SheetShown = false;
            model.PickFolder = null;
            model.Controller.Close();
        }, inWindow: true, still: LibraryUi.Still(anchor));
    }
}
