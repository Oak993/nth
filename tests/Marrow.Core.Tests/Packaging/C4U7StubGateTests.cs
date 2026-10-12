using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7StubGateTests
{
    [Fact]
    [Trait("Decision", "ED-235")]
    public void Metadata_gate_rejects_an_execution_stub_with_foreign_resources()
    {
        var result = C4PackagingCheck.Invoke("version resources match", """
            function Assert-NthVersionResource { param($Path, $Expected, [switch]$CheckLanguage)
                if ($Path -eq $badStub) { throw 'Execution stub carries foreign resources.' }
            }
            function Get-NthVersionInfo { param($Path) [pscustomobject]@{ FileDescription = 'nth'; ProductName = 'nth'; CompanyName = 'oak993' } }
            """);
        Assert.Equal("Execution stub carries foreign resources.", result.GetProperty("message").GetString());
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Optional_signing_gate_rejects_an_execution_stub_without_a_valid_timestamped_signature()
    {
        var result = C4PackagingCheck.Invoke("every exe is signed", """
            function Get-NthSignatureState { param($Path)
                [pscustomobject]@{ Signed = ($Path -ne $badStub); Timestamped = ($Path -ne $badStub); Status = if ($Path -eq $badStub) { 'NotSigned' } else { 'Valid' } }
            }
            """);
        Assert.Contains("nth_ExecutionStub.exe", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("NotSigned", result.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
