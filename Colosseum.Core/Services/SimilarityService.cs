using System.Text.Json;
using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Colosseum.Core.Services;

public class SimilarityService(
    IClaudeProvider claude,
    IOptions<ColosseumOptions> options,
    ILogger<SimilarityService> logger)
{
    private readonly ColosseumOptions _opts = options.Value;
    // Cache: (issueAId, issueBId) -> similarity result to avoid duplicate API calls.
    // ConcurrentDictionary is required because the singleton is shared across concurrent sessions.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, Guid), bool> _cache = new();

    public async Task<List<(Issue Issue, float Score)>> FindCandidatesAsync(
        Issue newIssue,
        IReadOnlyList<Issue> existingIssues,
        CancellationToken ct = default)
    {
        var candidates = new List<(Issue, float)>();

        foreach (var existing in existingIssues)
        {
            if (existing.Id == newIssue.Id) continue;

            var trigramScore = TrigramOverlap(newIssue.Title, existing.Title);

            if (trigramScore < (float)_opts.TrigramPrefilterThreshold)
                continue;

            if (trigramScore >= 1.0f)
            {
                candidates.Add((existing, 1.0f));
                continue;
            }

            var cacheKey = newIssue.Id.CompareTo(existing.Id) < 0
                ? (newIssue.Id, existing.Id)
                : (existing.Id, newIssue.Id);

            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (cached) candidates.Add((existing, (float)_opts.SimilarityThreshold));
                continue;
            }

            var similar = await SemanticCheckAsync(newIssue, existing, ct);
            _cache[cacheKey] = similar;

            if (similar)
                candidates.Add((existing, (float)_opts.SimilarityThreshold));
        }

        return candidates;
    }

    private async Task<bool> SemanticCheckAsync(Issue a, Issue b, CancellationToken ct)
    {
        try
        {
            var prompt = $$"""
                Are these two code review issues describing the same problem?

                Issue A: {{a.Title}}
                {{a.Description}}

                Issue B: {{b.Title}}
                {{b.Description}}

                Reply ONLY with JSON: { "similar": true|false, "confidence": 0.0-1.0, "reason": "string" }
                """;

            var text = await claude.CompleteAsync(prompt, _opts.SummaryModel, 150, ct);

            // Extract JSON from response
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < 0) return false;

            var json = text[start..(end + 1)];
            var doc = JsonDocument.Parse(json);

            var similar = doc.RootElement.GetProperty("similar").GetBoolean();
            var confidence = doc.RootElement.GetProperty("confidence").GetDouble();

            return similar && confidence >= _opts.SimilarityThreshold;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Semantic similarity check failed — defaulting to not similar (fail-safe)");
            return false;
        }
    }

    public static float TrigramOverlap(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 1.0f;

        var triA = GetTrigrams(a.ToLowerInvariant());
        var triB = GetTrigrams(b.ToLowerInvariant());

        if (triA.Count == 0 && triB.Count == 0) return 1.0f;
        if (triA.Count == 0 || triB.Count == 0) return 0.0f;

        var intersection = triA.Intersect(triB).Count();
        var union = triA.Union(triB).Count();

        return union == 0 ? 0f : (float)intersection / union;
    }

    private static HashSet<string> GetTrigrams(string s)
    {
        var trigrams = new HashSet<string>();
        var padded = " " + s + " ";
        for (var i = 0; i < padded.Length - 2; i++)
            trigrams.Add(padded.Substring(i, 3));
        return trigrams;
    }
}
