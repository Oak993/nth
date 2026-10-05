namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-13 review fix. An installed tree the app could not rebuild trust for: its record was dropped from state.json, but its folders are retained (never collected)
// until an explicit removal of the item or a Reset. An empty folder list means the folders are unknown, so every tool and store tree is retained.
public sealed record PipelineOrphan(string Id, string[] Folders);
