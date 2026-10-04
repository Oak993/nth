using Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: SK-6 NativeAOT entrypoint, copied under each provided executable name.
var executable = Environment.ProcessPath ?? throw new InvalidOperationException();
var definition = ShimDefinition.Read(Path.ChangeExtension(executable, ".shim"));
return await ShimRunner.RunAsync(definition, args, new WindowsShimLifetime());
