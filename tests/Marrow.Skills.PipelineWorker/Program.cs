using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Tests.Port.Skills.Pipelines;
// Test-only controlled worker: Exit bypasses all catch/finally/dispose paths, preserving the raw crash residue.
var repair = args[0].StartsWith("repair:", StringComparison.Ordinal); // integ-sk8-sk9: Repair of a damaged pin, on real NTFS ACLs.
var step = repair ? args[0][7..] : args[0];
var rig = new PipelineRig(windows: repair);
var installStep = step.StartsWith("install:", StringComparison.Ordinal) ? Enum.Parse<InstallStep>(step[8..]) : (InstallStep?)null;
var pipeline = await rig.Create(at => { if (step == at.ToString()) Environment.Exit(71); }, hardInstallCrash: installStep);
Console.WriteLine(rig.Paths.Root);
Console.Out.Flush();
if (repair)
{
    rig.Damage(pipeline.Snapshot().Single().Folder);
    if (pipeline.Acquire("fixture") is not null) Environment.Exit(73); // The seal check must mark it Damaged before Repair.
    if (!await pipeline.RepairAsync("fixture", rig.Progress)) Environment.Exit(74);
}
else await pipeline.UpdateAsync("fixture", rig.Progress);
Environment.Exit(72); // The committed state, before any lease release or GC.
