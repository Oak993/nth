using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Tests.Port.Skills.Pipelines;
// Test-only controlled worker: Exit bypasses all catch/finally/dispose paths, preserving the raw crash residue.
var rig = new PipelineRig();
var installStep = args[0].StartsWith("install:", StringComparison.Ordinal) ? Enum.Parse<InstallStep>(args[0][8..]) : (InstallStep?)null;
var pipeline = await rig.Create(step => { if (args[0] == step.ToString()) Environment.Exit(71); }, hardInstallCrash: installStep);
Console.WriteLine(rig.Paths.Root);
Console.Out.Flush();
await pipeline.UpdateAsync("fixture", rig.Progress);
Environment.Exit(72); // The committed state, before any lease release or GC.
