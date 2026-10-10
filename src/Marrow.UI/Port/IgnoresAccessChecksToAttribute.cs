namespace System.Runtime.CompilerServices;

// ED-NEW-T45: only the runtime-generated WinUI effect subclasses use this;
// the pinned Avalonia Win32 effect base is internal, like its compositor API.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
