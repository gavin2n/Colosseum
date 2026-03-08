using Colosseum.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Colosseum.Core.Services;

public record PrInfo(string Owner, string Repo, int Number, string? Title, string Diff);

public class GitHubService(IOptions<ColosseumOptions> options, ILogger<GitHubService> logger)
{
    private readonly ColosseumOptions _opts = options.Value;

    private static readonly System.Text.RegularExpressions.Regex PrUrlRegex =
        new(@"github\.com/([^/]+)/([^/]+)/pull/(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Reuse a single client instance — constructing one per request risks socket exhaustion.
    private readonly GitHubClient _client = CreateClient(options.Value);

    private static GitHubClient CreateClient(ColosseumOptions opts)
    {
        var client = new GitHubClient(new ProductHeaderValue("Colosseum"));
        if (!string.IsNullOrWhiteSpace(opts.GitHubToken))
            client.Credentials = new Credentials(opts.GitHubToken);
        return client;
    }

    public (string Owner, string Repo, int Number) ParsePrUrl(string url)
    {
        var m = PrUrlRegex.Match(url);
        if (!m.Success)
            throw new ArgumentException($"Invalid GitHub PR URL: {url}");
        return (m.Groups[1].Value, m.Groups[2].Value, int.Parse(m.Groups[3].Value));
    }

    public async Task<PrInfo> FetchPrAsync(string prUrl, CancellationToken ct = default)
    {
        var (owner, repo, number) = ParsePrUrl(prUrl);

        PullRequest? pr = null;
        try
        {
            pr = await _client.PullRequest.Get(owner, repo, number);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException($"PR #{number} not found in {owner}/{repo}. Check the URL and GitHub token.");
        }
        catch (RateLimitExceededException ex)
        {
            throw new InvalidOperationException(
                $"GitHub API rate limit exceeded. Resets at {ex.Reset:HH:mm} UTC. Use a GitHub token to raise the limit.", ex);
        }
        catch (AuthorizationException)
        {
            throw new InvalidOperationException("GitHub token is invalid or lacks read access to this repository.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to reach GitHub API: {ex.Message}", ex);
        }

        var diff = await FetchDiffAsync(owner, repo, number, ct);

        return new PrInfo(owner, repo, number, pr.Title, diff);
    }

    private async Task<string> FetchDiffAsync(string owner, string repo, int number, CancellationToken ct)
    {
        // Octokit can fetch raw diff via Accept header
        var response = await _client.Connection.Get<string>(
            new Uri($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}"),
            new Dictionary<string, string> { ["Accept"] = "application/vnd.github.v3.diff" });

        var diff = response.Body ?? string.Empty;

        // Rough token estimate: ~4 chars per token
        var estimatedTokens = diff.Length / 4;
        if (estimatedTokens > _opts.MaxDiffTokens)
        {
            logger.LogWarning("Diff is ~{tokens} tokens — truncating to {max}", estimatedTokens, _opts.MaxDiffTokens);
            var maxChars = _opts.MaxDiffTokens * 4;
            diff = diff[..maxChars] + "\n\n[DIFF TRUNCATED — remaining hunks omitted to stay within token budget]";
        }

        return diff;
    }
}
