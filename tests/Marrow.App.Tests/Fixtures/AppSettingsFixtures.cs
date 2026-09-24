using System.Text;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The AppSettings tests run alone: AppSettings.init assigns the process-wide ThemeManager.Current
/// (App/AppSettings.swift:728), which R-TEST-5 allows only inside one non-parallel collection.
/// </summary>
[CollectionDefinition("AppSettings", DisableParallelization = true)]
public sealed class AppSettingsCollection;

/// <summary>
/// A throwaway world for one AppSettings test (R-TEST-5, R-TEST-9): a TestRuntime (main loop, clock), the production
/// settings store kept in a file under MARROW_PORT_DATA_DIR and wrapped so every write is recorded, and an in-memory
/// Credential Manager behind SecretStore.Shared, so no test ever reaches the real credential store or user data.
/// <see cref="Relaunch"/> flushes the settings file and reopens it, as the next launch of the app would.
/// </summary>
public sealed class AppSettingsScope : IDisposable
{
    private readonly IDisposable defaultsScope;
    private readonly IDisposable secretsScope;
    private readonly AppTheme savedTheme;

    private AppSettingsScope(string folder)
    {
        Folder = folder;
        SettingsPath = Path.Combine(folder, "settings.json");
        savedTheme = ThemeManager.Current;
        Runtime = new TestRuntime();
        Defaults = new RecordingSettingsStore(new SettingsStore(SettingsPath));
        defaultsScope = UserDefaults.Standard.Override(Defaults);
        Credentials = new AppSettingsCredentialBackend(() => Runtime.Loop.CheckAccess());
        secretsScope = SecretStore.Shared.Override(new SecretStore(Credentials));
    }

    public static AppSettingsScope Create()
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory)) dataDirectory = Path.GetTempPath();
        var folder = Path.Combine(dataDirectory, "AppSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new AppSettingsScope(folder);
    }

    public TestRuntime Runtime { get; }

    public string Folder { get; }

    public string SettingsPath { get; }

    public RecordingSettingsStore Defaults { get; }

    public AppSettingsCredentialBackend Credentials { get; }

    /// <summary>Runs <paramref name="body"/> on the main loop (AppSettings is a main-actor model).</summary>
    public void OnMain(Action body) => Runtime.OnMain(body);

    /// <summary>Writes the settings file and reopens it, as a relaunch would read it.</summary>
    public void Relaunch() => Defaults.Reopen(SettingsPath);

    /// <summary>Whether any file under this test's folder holds <paramref name="text"/> as UTF-8 bytes.</summary>
    public bool AnyFileContains(string text)
    {
        Assert.True(Defaults.Inner.Synchronize(), "the settings file could not be written");
        var needle = Encoding.UTF8.GetBytes(text);
        foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.AsSpan().IndexOf(needle) >= 0) return true;
        }
        return false;
    }

    public void Dispose()
    {
        secretsScope.Dispose();
        defaultsScope.Dispose();
        Runtime.Dispose();
        ThemeManager.Current = savedTheme;
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The production <see cref="SettingsStore"/> behind a wrapper that records every write in order, as
/// ("set", key) or ("remove", key), so a test can prove what was and was not written (PR-SETTINGS-085 shape).
/// </summary>
public sealed class RecordingSettingsStore(SettingsStore inner) : ISettingsStore
{
    public SettingsStore Inner { get; private set; } = inner;

    public List<(string Op, string Key)> Writes { get; } = [];

    public object? Object(string key) => Inner.Object(key);

    public void Set(string key, object? value)
    {
        Writes.Add(("set", key));
        Inner.Set(key, value);
    }

    public void RemoveObject(string key)
    {
        Writes.Add(("remove", key));
        Inner.RemoveObject(key);
    }

    /// <summary>Writes the file, then reads it back into a fresh store; the write log is cleared.</summary>
    public void Reopen(string path)
    {
        Assert.True(Inner.Synchronize(), "the settings file could not be written");
        Inner = new SettingsStore(path);
        Writes.Clear();
    }

    /// <summary>Seeds a value as an earlier launch would have left it, without recording it as a write.</summary>
    public void Seed(string key, object value) => Inner.Set(key, value);
}

/// <summary>
/// An in-memory Credential Manager for the secret store's backend seam (ED-093): CRED_TYPE_GENERIC credentials by
/// target name, compared without case, each stored and returned as a copy. Every call is logged with whether it ran
/// on the main loop. <see cref="RefuseWrites"/> makes every write fail with ERROR_ACCESS_DENIED (5), as a store that
/// refuses the key or is unavailable altogether would (PR-PROVREG-239 failure cases).
/// </summary>
public sealed class AppSettingsCredentialBackend(Func<bool> onMain) : ICredentialBackend
{
    public const int ErrorSuccess = 0;
    public const int ErrorAccessDenied = 5;
    public const int ErrorNotFound = 1168;

    private readonly Lock gate = new();
    private readonly Dictionary<string, GenericCredential> credentials = new(StringComparer.OrdinalIgnoreCase);

    public bool RefuseWrites { get; set; }

    /// <summary>Every call in order: Op is "write", "read" or "delete".</summary>
    public List<(string Op, string Target, bool OnMain)> Operations { get; } = [];

    public int Write(GenericCredential credential)
    {
        lock (gate)
        {
            Operations.Add(("write", credential.TargetName, onMain()));
            if (RefuseWrites) return ErrorAccessDenied;
            credentials.Remove(credential.TargetName);
            credentials[credential.TargetName] = Copy(credential);
            return ErrorSuccess;
        }
    }

    public int Read(string targetName, out GenericCredential? credential)
    {
        lock (gate)
        {
            Operations.Add(("read", targetName, onMain()));
            if (credentials.TryGetValue(targetName, out var held))
            {
                credential = Copy(held);
                return ErrorSuccess;
            }
            credential = null;
            return ErrorNotFound;
        }
    }

    public int Delete(string targetName)
    {
        lock (gate)
        {
            Operations.Add(("delete", targetName, onMain()));
            return credentials.Remove(targetName) ? ErrorSuccess : ErrorNotFound;
        }
    }

    /// <summary>Whether any stored credential blob holds <paramref name="text"/> as UTF-8 bytes.</summary>
    public bool AnyBlobContains(string text)
    {
        var needle = Encoding.UTF8.GetBytes(text);
        lock (gate) return credentials.Values.Any(c => c.Blob.AsSpan().IndexOf(needle) >= 0);
    }

    private static GenericCredential Copy(GenericCredential c) =>
        new(c.TargetName, c.UserName, c.Blob.ToArray(), c.Type, c.Persist,
            c.Attributes.Select(a => new CredentialAttribute(a.Keyword, a.Value.ToArray())).ToList());
}
