using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Window.Native;

// The library the gallery's scenes run on: three projects (the first a real git checkout), threads of every kind the sidebar
// draws (pinned, unread, settled, with Hydra heads under one, on three providers), and the long conversation of GalleryTranscript.
// It is written to the child's own data folder (the harness's ambient Storage root) before the app model is made, so the real
// AppModel loads it as it would load the owner's, and the real views read the thread documents from disk. No provider CLI is
// ever started: every CLI provider is given a binary path that does not exist before anything can ask for it.
internal static class GalleryContent
{
    /// <summary>A real folder under the child's temp folder, optionally a git checkout with one commit.</summary>
    private static string Folder(string name, bool git)
    {
        var path = Path.Combine(Path.GetTempPath(), "gallery-projects", name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "README.md"), "# " + name + Environment.NewLine);
        if (!git) return path;
        foreach (var arguments in new[] { "init -q -b main", "config user.name gallery", "config user.email gallery@example.com", "config commit.gpgsign false", "add -A", "commit -q -m initial" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = path, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                process?.WaitForExit(15000);
            }
            catch (System.ComponentModel.Win32Exception) { return path; } // no git on this machine: the folder is simply not a checkout
        }
        return path;
    }

    private static ChatThread Thread(Project project, ProviderKind provider, string model, string? effort, string title, DateTimeOffset updated, bool fast = false) =>
        new ChatThread(project.Id, provider, model, effort, RuntimeMode.Supervised, fast) with { Title = title, HasCustomTitle = true, UpdatedAt = updated, CreatedAt = updated.AddHours(-2) };

    /// <summary>Writes the library and every thread document to the ambient store, before the AppModel is made.</summary>
    internal static GalleryData Seed()
    {
        var now = SwiftDate.Now;
        var web = new Project("droppy-web", Folder("droppy-web", git: true));
        var api = new Project("marrow-api", Folder("marrow-api", git: false));
        var notes = new Project("notes", Folder("notes", git: false)) with { IsExpanded = true };
        var pair = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "opus", OrchestratorEffort = "high", WorkerProvider = ProviderKind.Codex, WorkerModel = "gpt-5.5", WorkerEffort = "medium", Name = "Claude and Codex",
        };

        var main = Thread(web, ProviderKind.Claude, "opus", "high", "Add a dark mode toggle to settings", now.AddMinutes(-5)) with { LastStatus = TurnStatus.Completed, Branch = "main" };
        var codex = Thread(web, ProviderKind.Codex, "gpt-5.5", "medium", "Fix the flaky checkout test", now.AddHours(-3)) with { LastStatus = TurnStatus.Completed };
        var unread = Thread(web, ProviderKind.Claude, "sonnet", "medium", "Migrate to pnpm workspaces", now.AddHours(-1)) with { HasUnread = true, LastStatus = TurnStatus.Completed };
        var pinned = Thread(web, ProviderKind.Claude, "sonnet", "low", "Release checklist for 2.4", now.AddDays(-2)) with { IsPinned = true };
        var rate = Thread(api, ProviderKind.Codex, "gpt-5.5", "high", "Rate limiter for /v1/chat", now.AddHours(-9));
        var retries = Thread(api, ProviderKind.Claude, "opus", "medium", "Document the webhook retries", now.AddDays(-1)) with { IsSettled = true, SettledAt = now.AddDays(-1) };
        var roadmap = Thread(notes, ProviderKind.Claude, "opus", "xhigh", "Plan the Q4 roadmap", now.AddDays(-4));
        var archivedOne = Thread(api, ProviderKind.Codex, "gpt-5.5-mini", "low", "Prototype the sidebar animation", now.AddDays(-9)) with { IsArchived = true, LastStatus = TurnStatus.Completed };
        var archivedTwo = Thread(notes, ProviderKind.Claude, "sonnet", "medium", "Spike: SQLite as the local store", now.AddDays(-15)) with { IsArchived = true, LastStatus = TurnStatus.Completed };
        var hank = HeadThread(web, main, 0, "Audit the contrast ratios", now);
        var walter = HeadThread(web, main, 1, "Add Storybook stories", now);

        var library = new Library() with
        {
            Projects = new ValueList<Project>([web, api, notes]),
            Threads = new ValueList<ChatThread>([main, hank, walter, codex, unread, pinned, rate, retries, roadmap, archivedOne, archivedTwo]),
        };
        DiskWriter.Shared.WriteSynchronously(library, Storage.LibraryURL);
        DiskWriter.Shared.WriteSynchronously(GalleryTranscript.Document(main.Id, now), Storage.ThreadURL(main.Id));
        DiskWriter.Shared.WriteSynchronously(GalleryTranscript.Short(codex.Id, now, "The checkout test fails about one run in ten. Find out why.",
            "It races the mocked payment clock: the test advances time after the request is sent. Moving the advance before the send makes it deterministic."), Storage.ThreadURL(codex.Id));
        DiskWriter.Shared.WriteSynchronously(GalleryTranscript.Short(unread.Id, now, "Move the repo to pnpm workspaces.", "The workspace file and the lockfile are in place; CI still needs the new install step."), Storage.ThreadURL(unread.Id));
        DiskWriter.Shared.WriteSynchronously(GalleryTranscript.Short(hank.Id, now, "Check the dark and light token pairs against WCAG AA and list any that fail.",
            "Every text pair passes AA. One border token, `--line-faint`, is 2.1:1 on the light surface; it is decorative, so I left it and noted it in the report."), Storage.ThreadURL(hank.Id));
        DiskWriter.Shared.WriteSynchronously(GalleryTranscript.Short(walter.Id, now, "Add Dark and Light stories for ThemeToggle.",
            "Added `ThemeToggle.stories.tsx` with a Dark and a Light story that set the `theme` key before render. Storybook builds clean."), Storage.ThreadURL(walter.Id));
        return new GalleryData { Web = web, Api = api, Notes = notes, Main = main, Codex = codex, Unread = unread, Pinned = pinned, Pair = pair, Hank = hank, Walter = walter };
    }

    private static ChatThread HeadThread(Project project, ChatThread parent, long index, string task, DateTimeOffset now) =>
        new ChatThread(project.Id, ProviderKind.Codex, "gpt-5.5", "medium", RuntimeMode.Supervised) with
        {
            Title = task, ParentThreadID = parent.Id, UpdatedAt = now.AddMinutes(-4), CreatedAt = now.AddMinutes(-40),
            Hydra = new HydraHeadInfo(index, task, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated)
            {
                HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, Summary = "Done.", ToolCalls = 14 + index * 6, Tokens = 38_000 + index * 21_000,
                StartedAt = now.AddMinutes(-40), FinishedAt = now.AddMinutes(-38 + index * 2),
            },
        };

    /// <summary>Provider statuses, model catalogues, binary paths and the Hydra pair, on the made model (on the UI thread).</summary>
    internal static void Equip(AppModel model, GalleryData? data)
    {
        var missing = @"C:\gallery\no-cli\missing.exe"; // a path that does not exist: no CLI can be found or started
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
            if (!kind.IsAPIKeyBased) model.Settings.SetBinaryPath(missing, kind);
        var statuses = new Dictionary<ProviderKind, ProviderStatus>
        {
            [ProviderKind.Claude] = Installed("2.1.4", new ProviderStatus.Auth.SignedIn("ada@example.com")),
            [ProviderKind.Codex] = Installed("0.47.0", new ProviderStatus.Auth.SignedIn("ada@example.com")),
            [ProviderKind.Cursor] = Installed("2025.09.1", new ProviderStatus.Auth.SignedOut()),
            [ProviderKind.Copilot] = Installed("1.0.3", new ProviderStatus.Auth.SignedIn("ada-dev")),
        };
        typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map =>
        {
            foreach (var (kind, status) in statuses) map[kind] = status;
        }));
        var efforts = new ValueList<string>(["low", "medium", "high", "xhigh"]);
        model.Providers.UpdateCatalog(new ValueList<ModelOption>([
            new ModelOption("gpt-5.5", "GPT-5.5", detail: "Frontier model for coding", efforts: efforts, defaultEffort: "medium", isDefault: true, fastTier: "priority"),
            new ModelOption("gpt-5.5-mini", "GPT-5.5 Mini", detail: "Faster, for everyday edits", efforts: new ValueList<string>(["low", "medium", "high"]), defaultEffort: "medium"),
        ]), ProviderKind.Codex);
        if (data is not null) model.Settings.AddHydraPair(data.Pair);
    }

    private static ProviderStatus Installed(string version, ProviderStatus.Auth auth) => new()
    {
        Executable = SwiftURL.FileURLWithPath("/usr/local/bin/cli"), Version = version, ProviderStatusAuth = auth,
    };
}
