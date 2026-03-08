using System.Text.RegularExpressions;
using Colosseum.Core.Models;
using Microsoft.Extensions.Logging;

namespace Colosseum.Core.Services;

public record IssueUpdate(Issue Issue, string ChangeType);

public class IssueTracker(ILogger<IssueTracker> logger)
{
    private static readonly Regex IssueTagRegex =
        new(@"\[ISSUE:\s*([^|]+)\|\s*severity:\s*(high|medium|low)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OverlapTagRegex =
        new(@"\[OVERLAP:\s*([^\]]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SupportRegex =
        new(@"@(\w+)\s+is\s+(?:correct|right|onto something)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DisputeRegex =
        new(@"(?:I\s+dispute|disagree with)\s+@(\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public (List<Issue> NewIssues, List<string> OverlapTitles) ParseAndApply(
        string turnText,
        Gladiator gladiator,
        IReadOnlyList<Issue> sessionIssues)
    {
        var newIssues = new List<Issue>();
        var overlapTitles = new List<string>();

        // Parse [ISSUE: ...] tags
        foreach (Match m in IssueTagRegex.Matches(turnText))
        {
            var title = m.Groups[1].Value.Trim();
            var severityStr = m.Groups[2].Value.Trim().ToLower();
            var severity = severityStr switch
            {
                "high" => IssueSeverity.High,
                "low" => IssueSeverity.Low,
                _ => IssueSeverity.Medium
            };

            var issue = new Issue
            {
                Title = title,
                Description = ExtractContext(turnText, m.Index),
                Severity = severity,
                RaisedByGladiatorId = gladiator.Id,
                RaisedByGladiatorName = gladiator.Name
            };
            issue.DebateTrail.Add(new IssueDebateEntry(
                gladiator.Id, gladiator.Name, Stance.Raised,
                issue.Description, DateTimeOffset.UtcNow));

            newIssues.Add(issue);
        }

        // Parse [OVERLAP: ...] tags
        foreach (Match m in OverlapTagRegex.Matches(turnText))
        {
            var title = m.Groups[1].Value.Trim();
            var matched = FuzzyFindIssue(title, sessionIssues);
            if (matched is null)
            {
                logger.LogDebug("OVERLAP tag '{title}' found no matching issue — ignoring", title);
                continue;
            }
            overlapTitles.Add(matched.Title);
            matched.DebateTrail.Add(new IssueDebateEntry(
                gladiator.Id, gladiator.Name, Stance.Overlap,
                $"[Overlap signal] {gladiator.Name} flagged overlap with this issue.",
                DateTimeOffset.UtcNow));
        }

        // Parse stance signals from natural language
        ParseStanceSignals(turnText, gladiator, sessionIssues);

        return (newIssues, overlapTitles);
    }

    public void FlagMergeCandidate(Issue a, Issue b)
    {
        a.MergeCandidate = b.Id;
        b.MergeCandidate = a.Id;
        a.Status = IssueStatus.MergeCandidate;
        b.Status = IssueStatus.MergeCandidate;

        var notice = $"[System] Flagged as potential duplicate of: {b.Title}";
        a.DebateTrail.Add(new IssueDebateEntry(Guid.Empty, "System", Stance.Raised, notice, DateTimeOffset.UtcNow));

        var noticeB = $"[System] Flagged as potential duplicate of: {a.Title}";
        b.DebateTrail.Add(new IssueDebateEntry(Guid.Empty, "System", Stance.Raised, noticeB, DateTimeOffset.UtcNow));
    }

    private void ParseStanceSignals(string turnText, Gladiator gladiator, IReadOnlyList<Issue> sessionIssues)
    {
        foreach (Match m in SupportRegex.Matches(turnText))
        {
            // Find the most recent issue raised by the mentioned gladiator
            var mentionedName = m.Groups[1].Value;
            var recentIssue = sessionIssues.LastOrDefault(i =>
                i.RaisedByGladiatorName.Equals(mentionedName, StringComparison.OrdinalIgnoreCase));
            if (recentIssue is null) continue;

            recentIssue.VotesFor++;
            recentIssue.DebateTrail.Add(new IssueDebateEntry(
                gladiator.Id, gladiator.Name, Stance.Seconds,
                $"{gladiator.Name} seconded this issue.", DateTimeOffset.UtcNow));
        }

        foreach (Match m in DisputeRegex.Matches(turnText))
        {
            var mentionedName = m.Groups[1].Value;
            var recentIssue = sessionIssues.LastOrDefault(i =>
                i.RaisedByGladiatorName.Equals(mentionedName, StringComparison.OrdinalIgnoreCase));
            if (recentIssue is null) continue;

            recentIssue.VotesAgainst++;
            recentIssue.DebateTrail.Add(new IssueDebateEntry(
                gladiator.Id, gladiator.Name, Stance.Disputes,
                $"{gladiator.Name} disputes this issue.", DateTimeOffset.UtcNow));
        }
    }

    private static Issue? FuzzyFindIssue(string title, IReadOnlyList<Issue> issues)
    {
        // Exact match first
        var exact = issues.FirstOrDefault(i =>
            i.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Trigram fuzzy match
        return issues
            .Select(i => (Issue: i, Score: SimilarityService.TrigramOverlap(title, i.Title)))
            .Where(x => x.Score >= 0.4f)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault().Issue;
    }

    private static string ExtractContext(string turnText, int issueTagIndex)
    {
        // Take the sentence(s) before the [ISSUE: ...] tag as description
        var before = turnText[..issueTagIndex].Trim();
        var sentences = before.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        var last = sentences.LastOrDefault()?.Trim();
        if (last is not null) return last;
        return before.Length > 200 ? before[^200..] : before;
    }
}
