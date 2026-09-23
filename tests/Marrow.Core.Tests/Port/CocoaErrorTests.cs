using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-100: Foundation's CocoaError for the two codes the source throws. The description texts are Apple's (O-25).
public sealed class CocoaErrorTests
{
    [Fact]
    [Trait("Decision", "ED-100")]
    public void The_codes_and_domain_are_Foundations()
    {
        var corrupt = new CocoaError(CocoaError.ErrorCode.FileReadCorruptFile);
        var unknown = new CocoaError(CocoaError.ErrorCode.FileReadUnknown);
        Assert.Equal("NSCocoaErrorDomain", corrupt.Domain);
        Assert.Equal(259, corrupt.Code);
        Assert.Equal("NSCocoaErrorDomain", unknown.Domain);
        Assert.Equal(256, unknown.Code);
    }

    [Fact]
    [Trait("Decision", "ED-100")]
    [Trait("Rule", "R-ERR-2")]
    [Trait("Oracle", "O-25")]
    public void LocalizedDescription_gives_Foundations_English_text_with_right_single_quotes()
    {
        Assert.Equal(
            "The file couldn’t be opened because it isn’t in the correct format.",
            SwiftErrors.LocalizedDescription(new CocoaError(CocoaError.ErrorCode.FileReadCorruptFile)));
        Assert.Equal(
            "The file couldn’t be opened.",
            SwiftErrors.LocalizedDescription(new CocoaError(CocoaError.ErrorCode.FileReadUnknown)));
    }

    [Fact]
    [Trait("Decision", "ED-100")]
    public void The_exception_message_is_the_description()
    {
        Exception error = new CocoaError(CocoaError.ErrorCode.FileReadCorruptFile);
        Assert.Equal("The file couldn’t be opened because it isn’t in the correct format.", error.Message);
    }
}
