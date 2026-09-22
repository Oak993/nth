using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>Core/Support/JSONRPC.swift:31-37 RPCError (a LocalizedError), trimmed: errorDescription is the message.</summary>
public sealed class RpcErrorSample(long code, string message) : SwiftError
{
    public long Code { get; } = code;

    public override string? ErrorDescription { get; } = message;
}

/// <summary>A Swift error enum with no description: Foundation prints its domain and case index.</summary>
public sealed class SampleError : SwiftError, ISwiftErrorIdentity
{
    public override string? ErrorDescription => null;

    public string Domain => "DroppyCode.SampleError";

    public long Code => 2;
}
