using Marrow.Services.Port.Skills.PluginImport;

namespace Marrow.Services.Tests.Port.Skills.PluginImport;

// ED-NEW-mcpimport-2: the plugin import controller's target, recording what it was asked to add or replace.
internal sealed class FakePluginImportTarget : IPluginImportTarget
{
    public List<(string Id, string Name)> Own { get; } = [];
    public List<(string Folder, string Name, string? Replacing)> Calls { get; } = [];

    public Task<IReadOnlyList<(string Id, string Name)>> ExistingAsync() => Task.FromResult<IReadOnlyList<(string Id, string Name)>>([.. Own]);

    public Task<string?> AddAsync(string folder, string name, string? replacingId, CancellationToken cancel)
    {
        Calls.Add((folder, name, replacingId));
        if (replacingId is not null) Own.RemoveAll(o => o.Id == replacingId);
        Own.Add(("id-" + name + Calls.Count, name));
        return Task.FromResult<string?>(null);
    }
}
