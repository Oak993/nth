using Marrow.Setup.Platform;

namespace Marrow.Setup.Tests.Platform;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, step 3; unit U1 (ED-NEW-inst-u1-1). WinVerifyTrust on a file, offline
// (no revocation check, cached URLs only): a valid signature is Signed with the signer's name, no signature is Unsigned,
// and a signature that is there but does not hold (a changed file) is Invalid, which sends the window to Problem. The
// files checked are the .NET runtime's own signed library, a changed copy of it in a temp folder, this test assembly and
// a file of random bytes; nothing is run.
public sealed class SignatureCheckTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("nth-setup-signature-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void WinVerifyTrusts_answers_are_read_as_signed_unsigned_or_invalid()
    {
        Assert.Equal(new SignatureResult(SignatureState.Signed, "nth Publisher", 0), SignatureCheck.Classify(0, "nth Publisher"));
        Assert.Null(SignatureCheck.Classify(0, " ").Publisher);
        Assert.Equal(SignatureState.Unsigned, SignatureCheck.Classify(unchecked((int)0x800B0100), null).State); // TRUST_E_NOSIGNATURE
        Assert.Equal(SignatureState.Unsigned, SignatureCheck.Classify(unchecked((int)0x800B0003), null).State); // TRUST_E_SUBJECT_FORM_UNKNOWN
        Assert.Equal(SignatureState.Unsigned, SignatureCheck.Classify(unchecked((int)0x800B0001), null).State); // TRUST_E_PROVIDER_UNKNOWN
        Assert.Equal(SignatureState.Invalid, SignatureCheck.Classify(unchecked((int)0x80096010), "x").State); // TRUST_E_BAD_DIGEST
        Assert.Equal(SignatureState.Invalid, SignatureCheck.Classify(unchecked((int)0x800B0109), "x").State); // CERT_E_UNTRUSTEDROOT
        Assert.Equal(SignatureState.Invalid, SignatureCheck.Classify(unchecked((int)0x800B010C), "x").State); // CERT_E_REVOKED
        Assert.Null(SignatureCheck.Classify(unchecked((int)0x80096010), "x").Publisher);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_signed_file_names_its_signer()
    {
        var result = SignatureCheck.Verify(SignedRuntimeFile());

        Assert.Equal(SignatureState.Signed, result.State);
        Assert.Contains(result.Publisher, new[] { ".NET", "Microsoft Corporation" }); // the runtime's signer
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_changed_copy_of_a_signed_file_is_invalid()
    {
        var copy = Path.Combine(folder, "changed.dll");
        File.Copy(SignedRuntimeFile(), copy);
        using (var file = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = file.Length / 2;
            var value = file.ReadByte();
            file.Position = file.Length / 2;
            file.WriteByte((byte)(value ^ 0x5A));
        }

        var result = SignatureCheck.Verify(copy);

        Assert.Equal(SignatureState.Invalid, result.State);
        Assert.Null(result.Publisher);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void An_unsigned_program_and_a_file_that_is_no_program_are_unsigned()
    {
        Assert.Equal(SignatureState.Unsigned, SignatureCheck.Verify(typeof(SignatureCheckTests).Assembly.Location).State);
        var noise = Path.Combine(folder, "noise.bin");
        File.WriteAllBytes(noise, Enumerable.Range(0, 4096).Select(i => (byte)((i * 31) & 0xFF)).ToArray());
        Assert.Equal(SignatureState.Unsigned, SignatureCheck.Verify(noise).State);
    }

    // The shared framework's coreclr.dll carries Microsoft's embedded Authenticode signature.
    private static string SignedRuntimeFile() => Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "coreclr.dll");
}
