using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Artifacts;

namespace Marrow.App.Tests.Views.Artifacts;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-10")]
public sealed class WebReviewTabulaPersistenceTests(HeadlessSession headless)
{
    [Fact]
    public void A_future_Tabula_file_is_left_byte_exact_when_the_choice_changes() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-future-tabula"), TabulaState.FileName);
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"format\":99,\"artifact\":\"old\",\"future\":{\"mode\":\"other\"}}\n");
        File.WriteAllBytes(file, bytes);
        var state = TabulaState.Load(Guid.NewGuid(), file);
        state.Select(new TabulaRef(state.Thread, "new"));
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        Assert.Equal("new", state.Selected!.Id);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    });

    [Fact]
    public void Unknown_fields_of_the_current_Tabula_format_survive_a_choice_change() => headless.OnUiThread(() =>
    {
        using var store = new AppModelHarness();
        var file = Path.Combine(store.NewFolder("review-unknown-tabula"), TabulaState.FileName);
        File.WriteAllText(file, "{\"format\":1,\"artifact\":\"old\",\"future\":{\"mode\":\"other\"}}");
        var state = TabulaState.Load(Guid.NewGuid(), file);
        state.Select(new TabulaRef(state.Thread, "new"));
        HeadlessSession.RunDispatcherUntil(state.Persistence, TimeSpan.FromSeconds(10));
        var saved = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal("other", saved["future"]?["mode"]?.GetValue<string>());
        Assert.Equal("new", saved["artifact"]!.GetValue<string>());
    });
}
