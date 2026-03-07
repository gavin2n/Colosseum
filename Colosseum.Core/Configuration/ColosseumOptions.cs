using System.ComponentModel.DataAnnotations;

namespace Colosseum.Core.Configuration;

public class ColosseumOptions
{
    public const string Section = "Colosseum";

    [Required]
    public string AnthropicApiKey { get; set; } = string.Empty;

    public string? GitHubToken { get; set; }

    [Range(1, 5)]
    public int MaxRounds { get; set; } = 2;

    [Range(50, 500)]
    public int MaxTurnTokens { get; set; } = 150;

    [Range(0.0, 1.0)]
    public double SimilarityThreshold { get; set; } = 0.75;

    [Range(0.0, 1.0)]
    public double TrigramPrefilterThreshold { get; set; } = 0.4;

    public int MaxDiffTokens { get; set; } = 8000;

    public string DebateModel { get; set; } = "claude-sonnet-4-6";

    public string SummaryModel { get; set; } = "claude-haiku-4-5-20251001";
}
