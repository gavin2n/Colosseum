using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace Colosseum.Core.Services;

/// <summary>
/// Claude provider that calls the Anthropic API directly via the Anthropic.SDK NuGet package.
/// Requires <c>Colosseum:AnthropicApiKey</c> to be set.
/// </summary>
public class AnthropicSdkProvider(AnthropicClient client) : IClaudeProvider
{
    public async Task<string> CompleteAsync(
        string prompt, string model, int maxTokens, CancellationToken ct = default)
    {
        var request = new MessageParameters
        {
            Model = model,
            MaxTokens = maxTokens,
            Stream = false,
            Messages = [new Message(RoleType.User, prompt)]
        };

        var response = await client.Messages.GetClaudeMessageAsync(request, ct);
        return response.Message.ToString() ?? string.Empty;
    }
}
