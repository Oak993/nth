// PORT-ONLY test fixture (SK-11f, R-TEST-9). A real SK-5 store and SK-9 generation for the "native" provider on a throwaway root, with the
// fixture skills SK-9's own tests use (SKILL.md body "BODY SENTINEL", a reference "REFERENCE SENTINEL").
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class NativeSkillsKit : IDisposable
{
    private readonly GenerationFixture fixture = new();
    private readonly List<(GenerationItem Item, bool On)> items = [];
    private SkillGenerations? service;

    public GenerationBudget Budget { get; set; } = new(8000);
    public GenerationFixture Fixture => fixture;

    public GenerationItem Add(string id, string name, bool installed = true, bool on = true)
    {
        var item = fixture.Add(id, name, installed);
        items.Add((item, on));
        return item;
    }

    public Generation Start(string chat = "chat-1", IEnumerable<ExistingSkill>? existing = null)
    {
        service ??= fixture.Open();
        return service.Start("native", chat, items.Select(i => i.Item), fixture.Switches(items.Select(i => (i.Item, i.On)).ToArray()), Budget, existing);
    }

    public void Dispose() => fixture.Dispose();
}
