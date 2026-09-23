namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Foundation's <c>CocoaError</c> for the codes the source throws (ED-100): <c>.fileReadUnknown</c>
/// (WorkingTreeWatch.swift:333) and <c>.fileReadCorruptFile</c> (RevertPreview.swift:33, :36). Its description is
/// Foundation's English text for an error with no file name, which is Apple's text and not the source's, so it is
/// UNVERIFIED as oracle item O-25 (R-ERR-2).
/// </summary>
public sealed class CocoaError(CocoaError.ErrorCode code) : SwiftError, ISwiftErrorIdentity
{
    /// <summary>The <c>CocoaError.Code</c> values in use, with Foundation's numbers.</summary>
    public enum ErrorCode
    {
        /// <summary><c>NSFileReadUnknownError</c>.</summary>
        FileReadUnknown = 256,

        /// <summary><c>NSFileReadCorruptFileError</c>.</summary>
        FileReadCorruptFile = 259,
    }

    public ErrorCode ErrorCodeValue { get; } = code; // Swift: code

    public string Domain => "NSCocoaErrorDomain";

    public long Code => (long)ErrorCodeValue;

    // U+2019 RIGHT SINGLE QUOTATION MARK in both texts, as Foundation writes them.
    public override string? ErrorDescription => ErrorCodeValue switch
    {
        ErrorCode.FileReadUnknown => "The file couldn’t be opened.",
        ErrorCode.FileReadCorruptFile => "The file couldn’t be opened because it isn’t in the correct format.",
    };
}
