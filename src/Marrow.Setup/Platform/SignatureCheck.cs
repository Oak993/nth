namespace Marrow.Setup.Platform;

/// <summary>What the setup's own signature says (nth installer PLAN.md 3, step 3).</summary>
public enum SignatureState
{
    /// <summary>Not checked yet: Welcome's signature line waits, and Install waits for it.</summary>
    Unknown,

    /// <summary>A valid Authenticode signature: Welcome shows "Signed by ‹Publisher›".</summary>
    Signed,

    /// <summary>No signature: Welcome shows "Unsigned test build, not for sharing" in the warning colour.</summary>
    Unsigned,

    /// <summary>A signature that does not verify (changed file, untrusted or revoked certificate): the Problem screen, nothing installed.</summary>
    Invalid,
}

/// <summary>A signature check's answer: the state, the signer's name when signed, and WinVerifyTrust's result.</summary>
public sealed record SignatureResult(SignatureState State, string? Publisher, int Status)
{
    public static SignatureResult Pending { get; } = new(SignatureState.Unknown, null, 0);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, step 3; unit U1, ED-NEW-inst-u1-1). WinVerifyTrust on a file, with no UI, no
/// revocation check and cached URLs only, so the check never goes to the network ("This setup works offline and sends
/// nothing"). The publisher is the signer certificate's simple display name (its subject's common name). A file with no
/// signature at all is Unsigned; any other failure means a signature is there and does not hold, which is Invalid.
/// </summary>
public static class SignatureCheck
{
    public static unsafe SignatureResult Verify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows()) return new SignatureResult(SignatureState.Unsigned, null, Native.TRUST_E_NOSIGNATURE);
        fixed (char* pathText = Path.GetFullPath(path))
        {
            var fileInfo = new Native.WinTrustFileInfo { StructSize = (uint)sizeof(Native.WinTrustFileInfo), FilePath = (nint)pathText };
            var data = new Native.WinTrustData
            {
                StructSize = (uint)sizeof(Native.WinTrustData),
                UiChoice = Native.WTD_UI_NONE,
                RevocationChecks = Native.WTD_REVOKE_NONE,
                UnionChoice = Native.WTD_CHOICE_FILE,
                FileInfo = (nint)(&fileInfo),
                StateAction = Native.WTD_STATEACTION_VERIFY,
                ProviderFlags = Native.WTD_REVOCATION_CHECK_NONE | Native.WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var action = Native.WintrustActionGenericVerifyV2;
            var status = Native.WinVerifyTrust(-1, ref action, ref data); // INVALID_HANDLE_VALUE: no UI, no owner window
            try
            {
                return Classify(status, status == 0 ? SignerName(data.StateData) : null);
            }
            finally
            {
                data.StateAction = Native.WTD_STATEACTION_CLOSE;
                _ = Native.WinVerifyTrust(-1, ref action, ref data);
            }
        }
    }

    /// <summary>WinVerifyTrust's result as the window reads it.</summary>
    public static SignatureResult Classify(int status, string? publisher) => status switch
    {
        0 => new SignatureResult(SignatureState.Signed, string.IsNullOrWhiteSpace(publisher) ? null : publisher, status),
        Native.TRUST_E_NOSIGNATURE or Native.TRUST_E_SUBJECT_FORM_UNKNOWN or Native.TRUST_E_PROVIDER_UNKNOWN => new SignatureResult(SignatureState.Unsigned, null, status),
        _ => new SignatureResult(SignatureState.Invalid, null, status),
    };

    /// <summary>Checks the running setup's own file on a pool thread (it hashes the whole file, payload included).</summary>
    public static Task<SignatureResult> VerifySelfAsync() =>
        Environment.ProcessPath is { } self ? Task.Run(() => Verify(self)) : Task.FromResult(new SignatureResult(SignatureState.Unsigned, null, Native.TRUST_E_NOSIGNATURE));

    // CRYPT_PROVIDER_SGNR: cbStruct (4), sftVerifyAsOf (8), csCertChain (4), then pasCertChain (a pointer, offset 16 on x64 and
    // ARM64); CRYPT_PROVIDER_CERT: cbStruct (4), then pCert (a pointer, offset 8). Signer 0's first certificate is the signer's own.
    private static unsafe string? SignerName(nint stateData)
    {
        if (stateData == 0) return null;
        var provider = Native.WTHelperProvDataFromStateData(stateData);
        if (provider == 0) return null;
        var signer = Native.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == 0) return null;
        var chainCount = *(uint*)(signer + 12);
        var chain = *(nint*)(signer + 16);
        if (chainCount == 0 || chain == 0) return null;
        var certificate = *(nint*)(chain + 8);
        if (certificate == 0) return null;
        var length = Native.CertGetNameString(certificate, Native.CERT_NAME_SIMPLE_DISPLAY_TYPE, 0, 0, null, 0);
        if (length <= 1) return null;
        var buffer = new char[length];
        fixed (char* text = buffer)
        {
            var written = Native.CertGetNameString(certificate, Native.CERT_NAME_SIMPLE_DISPLAY_TYPE, 0, 0, text, length);
            return written <= 1 ? null : new string(text, 0, (int)written - 1);
        }
    }
}
