using Marrow.App.Port.Skills;

namespace Marrow.App.Tests.Port.Skills;

public sealed class OfferTransferWordsTests
{
    [Theory]
    [Trait("Decision", "ED-198")] // English ByteCountFormatStyle rules already transcribed in SwiftFormatters.cs:479-560 (O-66).
    [InlineData(0L, "Zero kB")]
    [InlineData(1L, "1 byte")]
    [InlineData(998L, "998 bytes")]
    [InlineData(999L, "1 kB")]
    [InlineData(2500L, "2 kB")]
    [InlineData(999499L, "1 MB")]
    [InlineData(1000000L, "1 MB")]
    [InlineData(1050000L, "1 MB")]
    [InlineData(1250000000L, "1.25 GB")]
    public void Transfer_sizes_use_the_existing_English_file_byte_rules_without_a_native_locale_dependency(long bytes, string words)
    {
        Assert.Equal(words, OfferTransferWords.Bytes(bytes));
    }
}
