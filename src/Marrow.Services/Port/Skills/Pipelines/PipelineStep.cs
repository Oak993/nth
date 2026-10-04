// PORT-ONLY: SK-8 / DL-8, ONDEMAND 3.7. BeforeCommit is the last fallible checkpoint.
namespace Marrow.Services.Port.Skills.Pipelines;
// integ-sk8-sk9: SetAside is Repair's step after the damaged folder is renamed to a tmp- name and before the same pin is reinstalled at its path.
public enum PipelineStep { Downloaded, Installed, RoutesPrepared, BeforeCommit, SetAside }
