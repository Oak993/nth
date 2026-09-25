using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B2 1fbf81e), :1009-1068.
public sealed class AppModelSubagentTests
{
    [Fact]
    [Trait("Req", "PR-STORE-097")] // Core/Models/Library.swift:156-166, :188-192; App/AppModel.swift:1055-1068
    public void Closing_an_idle_panel_helper_keeps_it_under_its_parent_in_the_sidebar()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("close subagent"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = true };
        h.WriteLibrary([project], [parent, helper]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Equal(helper.Id, model.Subagent(parent.Id)?.Id);
            Assert.Null(model.ExistingRuntime(helper.Id));

            model.CloseSubagent(helper.Id);

            var closed = Assert.IsType<ChatThread>(model.Thread(helper.Id));
            Assert.Equal(parent.Id, closed.ParentThreadID);
            Assert.False(closed.IsInPanel);
            Assert.Null(model.Subagent(parent.Id));
            Assert.Contains(model.Helpers(parent.Id), thread => thread.Id == helper.Id);
            Assert.Null(model.ExistingRuntime(helper.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-097")] // Core/Models/Library.swift:156-166, :188-192; App/AppModel.swift:1020-1051
    public void Spawning_a_helper_copies_the_parent_choices_and_starts_it_in_the_parent_panel()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("spawn subagent"));
        var parent = AppModelSamples.Thread(project) with
        {
            Model = "fixture-model",
            Effort = "high",
            RuntimeMode = RuntimeMode.AutoAcceptEdits,
            FastMode = true,
        };
        h.WriteLibrary([project], [parent]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model); // A missing test CLI path prevents this send from invoking a real provider.

        h.Main.OnMain(() =>
        {
            var helper = Assert.IsType<ChatThread>(model.SpawnSubagent(parent.Id, "Review", "Inspect the change."));

            Assert.Equal(parent.ProjectID, helper.ProjectID);
            Assert.Equal(parent.Provider, helper.Provider);
            Assert.Equal(parent.Model, helper.Model);
            Assert.Equal(parent.Effort, helper.Effort);
            Assert.Equal(parent.RuntimeMode, helper.RuntimeMode);
            Assert.Equal(parent.FastMode, helper.FastMode);
            Assert.Equal(parent.Id, helper.ParentThreadID);
            Assert.True(helper.IsHelper);
            Assert.True(helper.IsInPanel);
            Assert.Equal("Review", helper.Title);
            Assert.True(helper.HasCustomTitle);
            Assert.Equal(helper.Id, model.Subagent(parent.Id)?.Id);
            Assert.NotNull(model.ExistingRuntime(helper.Id));
        });
    }
}
