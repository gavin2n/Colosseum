using System.Text.Json;
using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Colosseum.Core.Services;

public class ModeratorService(
    IClaudeProvider claude,
    GladiatorService gladiatorService,
    IOptions<ColosseumOptions> options,
    ILogger<ModeratorService> logger)
{
    private readonly ColosseumOptions _opts = options.Value;

    public async Task<Verdict> RenderVerdictAsync(ReviewSession session, CancellationToken ct = default)
    {
        var prompt = gladiatorService.BuildArbiterPrompt(session);

        string rawText;
        try
        {
            rawText = await claude.CompleteAsync(prompt, _opts.SummaryModel, _opts.MaxVerdictTokens, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Arbiter verdict call failed");
            return new Verdict { ParseFailed = true, RawSummary = "Arbiter call failed." };
        }

        return ParseVerdict(rawText, session);
    }

    private Verdict ParseVerdict(string rawText, ReviewSession session)
    {
        try
        {
            var start = rawText.IndexOf('{');
            var end = rawText.LastIndexOf('}');
            if (start < 0 || end < 0)
                throw new InvalidOperationException("No JSON object found in response");

            var json = rawText[start..(end + 1)];
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var mergeResolutions = new List<MergeResolution>();
            if (root.TryGetProperty("mergeResolutions", out var merges))
            {
                foreach (var m in merges.EnumerateArray())
                {
                    var decisionStr = m.GetProperty("decision").GetString() ?? "Distinguish";
                    var decision = decisionStr switch
                    {
                        "Merge" => MergeDecision.Merge,
                        "DismissOne" => MergeDecision.DismissOne,
                        _ => MergeDecision.Distinguish
                    };

                    var issueAId = Guid.Parse(m.GetProperty("issueAId").GetString()!);
                    var issueBId = Guid.Parse(m.GetProperty("issueBId").GetString()!);

                    var issueA = session.Issues.FirstOrDefault(i => i.Id == issueAId);
                    var issueB = session.Issues.FirstOrDefault(i => i.Id == issueBId);

                    mergeResolutions.Add(new MergeResolution(
                        issueAId, issueBId, decision,
                        m.GetProperty("reason").GetString() ?? string.Empty,
                        issueA?.RaisedByGladiatorName,
                        issueB?.RaisedByGladiatorName));
                }
            }

            var rankedIssues = new List<RankedIssue>();
            if (root.TryGetProperty("rankedIssues", out var ranked))
            {
                foreach (var r in ranked.EnumerateArray())
                {
                    rankedIssues.Add(new RankedIssue(
                        Guid.Parse(r.GetProperty("issueId").GetString()!),
                        r.GetProperty("rank").GetInt32(),
                        r.GetProperty("rationale").GetString() ?? string.Empty));
                }
            }

            var dismissedIssues = new List<DismissedIssue>();
            if (root.TryGetProperty("dismissedIssues", out var dismissed))
            {
                foreach (var d in dismissed.EnumerateArray())
                {
                    dismissedIssues.Add(new DismissedIssue(
                        Guid.Parse(d.GetProperty("issueId").GetString()!),
                        d.GetProperty("reason").GetString() ?? string.Empty));
                }
            }

            var summary = root.TryGetProperty("summary", out var s)
                ? s.GetString() ?? string.Empty
                : string.Empty;

            return new Verdict
            {
                MergedIssues = mergeResolutions,
                RankedIssues = rankedIssues,
                DismissedIssues = dismissedIssues,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Arbiter JSON verdict — storing raw text");
            return new Verdict { ParseFailed = true, RawSummary = rawText };
        }
    }
}
