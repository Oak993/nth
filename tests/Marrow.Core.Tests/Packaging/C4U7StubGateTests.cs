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
                if ($Path -eq $badStub) {
                    $script:stubAppIdentity = [object]::ReferenceEquals($Expected, $identity.App)
                    $script:stubEnglish = [bool]$CheckLanguage
                    throw 'Execution stub carries foreign resources.'
                }
            }
            function Get-NthVersionInfo { param($Path) [pscustomobject]@{ FileDescription = 'nth'; ProductName = 'nth'; CompanyName = 'oak993' } }
            """);
        Assert.Equal("Execution stub carries foreign resources.", result.GetProperty("message").GetString());
        Assert.True(result.GetProperty("stubAppIdentity").GetBoolean());
        Assert.True(result.GetProperty("stubEnglish").GetBoolean());
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

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_None_does_not_activate_any_signature_reads() => CheckSigningActivation("None", 0);

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_SignTool_checks_all_exes_including_the_stub() => CheckSigningActivation("SignTool", 6);

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_Azure_checks_all_exes_including_the_stub() => CheckSigningActivation("Azure", 6);

    private static void CheckSigningActivation(string sign, int expectedCount)
    {
        var result = C4PackagingCheck.Invoke("every exe is signed", """
            function Get-NthSignatureState { param($Path)
                $script:signaturePaths += $Path
                [pscustomobject]@{ Signed = $true; Timestamped = $true; Status = 'Valid' }
            }
            """, includeSigningCondition: true, sign: sign);
        Assert.Equal("", result.GetProperty("message").GetString());
        var paths = result.GetProperty("signaturePaths").EnumerateArray().Select(p => p.GetString()!).ToArray();
        Assert.Equal(expectedCount, paths.Length);
        Assert.Equal(sign == "None" ? 0 : 1, paths.Count(p => p.EndsWith("nth_ExecutionStub.exe", StringComparison.Ordinal)));
    }
}
