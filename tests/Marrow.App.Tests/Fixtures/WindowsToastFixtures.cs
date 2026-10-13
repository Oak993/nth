using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

// No Windows API, registry, toast, window, pipe or URI launcher is called by these doubles.
public sealed class RecordingToastPlatform : IWindowsToastPlatform
{
    public Dictionary<(string Key, string Name), string> Registry { get; } = [];
    public List<(string Key, string Name, string Value)> Writes { get; } = [];
    public List<string> AppIds { get; } = [];
    public List<(string AppId, string Tag, string Xml)> Posts { get; } = [];
    public Dictionary<(string AppId, string Tag), string> Delivered { get; } = [];
    public Action? BeforeShow { get; set; }
    public bool FailShow { get; set; }
    public List<(string AppId, string Tag)> Removed { get; } = [];
    public bool FailRemove { get; set; }
    public void SetProcessAppId(string appId) => AppIds.Add(appId);
    public string? ReadRegistry(string key, string name) => Registry.GetValueOrDefault((key, name));
    public void WriteRegistry(string key, string name, string value)
    {
        Writes.Add((key, name, value));
        Registry[(key, name)] = value;
    }
    public List<string> DeletedTrees { get; } = [];
    public void DeleteRegistryTree(string key)
    {
        DeletedTrees.Add(key);
        foreach (var entry in Registry.Keys.Where(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                     || entry.Key.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            Registry.Remove(entry);
    }
    public void Remove(string appId, string tag)
    {
        if (FailRemove) throw new InvalidOperationException("fake removal denial");
        Removed.Add((appId, tag));
        Delivered.Remove((appId, tag));
    }
    public void Show(string appId, string tag, string xml)
    {
        BeforeShow?.Invoke();
        Posts.Add((appId, tag, xml));
        if (FailShow) throw new InvalidOperationException("fake toast denial");
        Delivered[(appId, tag)] = xml;
    }
}

public sealed class RecordingAppToastCenter : IToastCenter
{
    public List<ToastRequest> Requests { get; } = [];
    public List<string> Removed { get; } = [];
    public int Permissions { get; private set; }
    public bool FailPermission { get; set; }
    public Action<AppDelegate>? Installed { get; set; }
    public void InstallDelegate(AppDelegate appDelegate) => Installed?.Invoke(appDelegate);
    public void RequestPermission()
    {
        Permissions++;
        if (FailPermission) throw new InvalidOperationException("fake permission denial");
    }
    public Task Add(ToastRequest request)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
    public Task Remove(string identifier)
    {
        Removed.Add(identifier);
        return Task.CompletedTask;
    }
}

public sealed class RecordingToastWindows(Action<string>? onEvent = null) : IToastActivationWindows
{
    public List<string> Events { get; } = [];
    private void Record(string value) { Events.Add(value); onEvent?.Invoke(value); }
    public void ShowMain() => Record("main");
    public void Activate() => Record("activate");
    public void ShowAbout() => Record("about");
}

public sealed class RecordingToastTransport : IToastLaunchTransport, IDisposable
{
    public List<(string Name, string Payload)> Sent { get; } = [];
    public Action<string>? Received { get; private set; }
    public bool Send(string name, string payload)
    {
        Sent.Add((name, payload));
        Received?.Invoke(payload);
        return true;
    }
    public IDisposable Listen(string name, Action<string> received) { Received = received; return this; }
    public void Dispose() => Received = null;
}

public sealed class RecordingToastIconFiles : IToastIconFiles
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public int Writes { get; private set; }
    public byte[]? Read(string path) => Files.GetValueOrDefault(path);
    public void Write(string path, byte[] bytes) { Writes++; Files[path] = bytes.ToArray(); }
}
