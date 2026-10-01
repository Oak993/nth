using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Builds ThreadRuntime's private launch signature (ThreadRuntime.swift:510-526) with the same function
/// EnsureSession calls, and reads its fields (ED-119: Marrow.App has no InternalsVisibleTo).</summary>
public static class ThreadRuntimeSignatureProbe
{
    public static object Make(ChatThread thread, string directory = "C:/work", bool antiSlop = false, HydraLaunch? hydra = null) =>
        typeof(ThreadRuntime).GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [thread, directory, antiSlop, hydra])!;

    public static object? Field(object signature, string name) =>
        signature.GetType().GetProperty(name)!.GetValue(signature);
}
