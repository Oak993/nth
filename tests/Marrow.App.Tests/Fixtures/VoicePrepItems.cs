using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-15: the progress items of a voice preparation, recorded in order as they are reported (synchronously, so a test sees the order the work made them in).</summary>
public sealed class VoicePrepItems : IProgress<VoicePrepItem>
{
    public List<VoicePrepItem> All { get; } = [];

    public void Report(VoicePrepItem value) => All.Add(value);

    public IReadOnlyList<VoicePrepItem> Of(string id) => All.Where(i => i.Id == id).ToList();
}
