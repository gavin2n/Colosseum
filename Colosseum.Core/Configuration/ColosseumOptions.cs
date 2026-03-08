using System.ComponentModel.DataAnnotations;

namespace Colosseum.Core.Configuration;

public class ColosseumOptions
{
    public const string Section = "Colosseum";

    /// <summary>
    /// Which Claude backend to use. "Cli" (default) uses the local claude CLI;
    /// "Sdk" uses the Anthropic API directly and requires AnthropicApiKey.
    /// </summary>
    public string Provider { get; set; } = "Cli";

    /// <summary>
    /// Path to the claude CLI binary. Defaults to "claude" (assumes it's on PATH).
    /// Only used when Provider = "Cli".
    /// </summary>
    public string CliBinaryPath { get; set; } = "claude";

    /// <summary>
    /// Anthropic API key. Only required when Provider = "Sdk".
    /// </summary>
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

    /// <summary>Per-call timeout in seconds for Claude providers. Prevents hung processes blocking sessions.</summary>
    public int ClaudeCallTimeoutSeconds { get; set; } = 120;

    /// <summary>Max token budget for the Arbiter verdict response.</summary>
    public int MaxVerdictTokens { get; set; } = 2000;

    /// <summary>How many prior turns to include in each gladiator prompt. Older turns are omitted to cap token growth.</summary>
    public int MaxContextTurns { get; set; } = 10;

    public string DebateModel { get; set; } = "claude-sonnet-4-6";

    public string SummaryModel { get; set; } = "claude-haiku-4-5-20251001";
}
