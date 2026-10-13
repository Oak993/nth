// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 5; unit AS-19; ED-NEW-dextra-as19-7). No Swift twin.
// Saves an approved draft as an own skill. It writes the SKILL.md to a staging folder and hands that folder to the own-skill service
// (Marrow.Services/Port/Skills/Custom), exactly as a folder the owner adds himself: the service checks it (the name, the description, the
// files), copies it into its own store and pins it. Nothing here writes into the skills store, and nothing runs. The staging folder is
// removed afterwards, whatever happened.
using System.Text;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Assistant;

/// <summary>How a save ended: the id of the own item it made, or why it did not.</summary>
public sealed record TeachSaved(string? ItemId, string? Problem)
{
    public bool Ok => ItemId is not null;

    /// <summary>The name is already an own skill's: the owner picks another.</summary>
    public bool NameTaken { get; init; }
}

public sealed class TeachSkillWriter(ICustomAddBackend backend, TimeProvider time)
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Adds the draft as an own skill, staging it in <paramref name="staging"/> (an empty folder the caller made and removes).</summary>
    public async Task<TeachSaved> Save(TeachDraft draft, string staging, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Problem() is { } problem) return new TeachSaved(null, problem);
        await File.WriteAllBytesAsync(Path.Combine(staging, "SKILL.md"), Utf8.GetBytes(TeachSkillText.Skill(draft, time.GetUtcNow())), cancel).ConfigureAwait(false);
        IReadOnlyList<CustomAddCandidate> found;
        try
        {
            found = await backend.Check(new CustomAddRequest(CustomAddKind.Skills, CustomAddSource.Folder, staging, draft.Name, ""), cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            return new TeachSaved(null, "nth could not read the skill it wrote: " + error.Message);
        }
        var candidate = found.FirstOrDefault(c => string.Equals(c.Name, draft.Name, StringComparison.Ordinal));
        if (candidate is null) return Failed(found, "nth could not find the skill it wrote.");
        if (candidate.Problem is { } bad) return Failed(found, bad);
        try
        {
            var item = await backend.Add(candidate, new NoProgress(), cancel).ConfigureAwait(false);
            return new TeachSaved(item.Id, null);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("already uses that name", StringComparison.Ordinal))
        {
            return Failed(found, "A skill named “" + draft.Name + "” already exists. Pick another name.") with { NameTaken = true };
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failed(found, "nth could not add the skill: " + error.Message);
        }
    }

    // A check that is not followed by an add leaves its snapshot waiting in the service; let it go.
    private TeachSaved Failed(IReadOnlyList<CustomAddCandidate> found, string problem)
    {
        if (backend is CustomSources sources) sources.Discard(found);
        return new TeachSaved(null, problem);
    }

    // The own-skill service reports progress; a skill of one small file has nothing to show.
    private sealed class NoProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }
}
