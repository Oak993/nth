using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class OpenCodeUnconstrainedSchemaReviewTests
{
    [Fact]
    public void An_unconstrained_top_level_noReply_schema_accepts_the_boolean_sent_by_injection()
    {
        const string document = "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"noReply\":{}}}}}}}}}}";

        Assert.True(OpenCodeHistoryInjection.OpenApiSupportsNoReply(document));
    }
}
