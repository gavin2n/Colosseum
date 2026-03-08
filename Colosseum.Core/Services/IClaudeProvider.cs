namespace Colosseum.Core.Services;

/// <summary>
/// Abstraction over Claude completion backends.
/// Implement this to swap between the Anthropic SDK (direct API key) and
/// the Claude CLI (claude -p) without changing any calling code.
/// </summary>
public interface IClaudeProvider
{
    /// <summary>
    /// Sends a single prompt and returns the raw text response.
    /// </summary>
    /// <param name="prompt">The full prompt text.</param>
    /// <param name="model">Model identifier (e.g. "claude-sonnet-4-6").</param>
    /// <param name="maxTokens">Desired output token budget. CLI provider may not honour this.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> CompleteAsync(string prompt, string model, int maxTokens, CancellationToken ct = default);
}
