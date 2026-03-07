using Colosseum.Core.Services;

namespace Colosseum.Tests;

public class SimilarityServiceTests
{
    [Fact]
    public void TrigramOverlap_IdenticalStrings_ReturnsOne()
    {
        var score = SimilarityService.TrigramOverlap("N+1 query in loop", "N+1 query in loop");
        Assert.Equal(1.0f, score);
    }

    [Fact]
    public void TrigramOverlap_CompletelyDifferent_ReturnsLow()
    {
        var score = SimilarityService.TrigramOverlap("N+1 query problem", "Missing null check");
        Assert.True(score < 0.4f);
    }

    [Fact]
    public void TrigramOverlap_EmptyStrings_ReturnsOne()
    {
        var score = SimilarityService.TrigramOverlap("", "");
        Assert.Equal(1.0f, score);
    }

    [Fact]
    public void TrigramOverlap_SimilarTitles_ReturnsMidRange()
    {
        // "Duplicate idempotency logic" vs "Idempotency belongs in domain service" — some overlap
        var score = SimilarityService.TrigramOverlap(
            "Duplicate idempotency logic in handlers",
            "Idempotency logic duplicated across handlers");
        Assert.True(score >= 0.2f, $"Expected moderate overlap, got {score}");
    }
}
