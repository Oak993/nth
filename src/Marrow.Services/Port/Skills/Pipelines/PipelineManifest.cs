namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-16 review fix (ED-NEW-cloud-sk-16). The catalogue entry an install was verified against (a one-item catalogue, as the catalogue file writes it), kept so a restart after a catalogue change can still trust that exact tree.
public sealed record PipelineManifest(string Id, string Pin, string Item);
