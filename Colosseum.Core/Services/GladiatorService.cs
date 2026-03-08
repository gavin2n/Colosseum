using Colosseum.Core.Configuration;
using Colosseum.Core.Models;
using Microsoft.Extensions.Options;

namespace Colosseum.Core.Services;

public class GladiatorService(IOptions<ColosseumOptions> options)
{
    private readonly ColosseumOptions _opts = options.Value;

    public string BuildPrompt(
        Gladiator gladiator,
        string diff,
        IReadOnlyList<DebateTurn> priorTurns,
        IReadOnlyList<Issue> openIssues,
        IReadOnlyList<Gladiator> activeGladiators,
        int round)
    {
        var sb = new System.Text.StringBuilder();

        // 1. Persona
        sb.AppendLine($"""
            You are {gladiator.Name}, a code review gladiator focused on {gladiator.Domain}.
            Personality: {gladiator.Personality}

            You are in a structured code review debate. Your job is to find real problems in the code diff provided.
            """);

        // 2. @mention instructions
        var otherNames = activeGladiators
            .Where(g => g.Id != gladiator.Id && !g.IsArbiter)
            .Select(g => g.Name);
        sb.AppendLine($"""
            DEBATE RULES:
            - Keep your turn to 2-3 sentences maximum. Be direct and specific.
            - Address others directly using @Name (e.g. {string.Join(", ", otherNames.Select(n => "@" + n))}).
            - When responding to another gladiator's point, start with their @mention.
            - You may support or challenge other positions.
            """);

        // 3. Issue flag format
        sb.AppendLine("""
            ISSUE FORMAT — raise new issues exactly like this:
            [ISSUE: brief title | severity: high|medium|low]

            OVERLAP FORMAT — if your concern overlaps an existing issue:
            [OVERLAP: exact-issue-title]
            Then explain whether you agree, disagree, or see a distinction.

            Raise at most 1-2 new issues per turn. Quality over quantity.
            """);

        // 4. Code diff
        sb.AppendLine("CODE DIFF:");
        sb.AppendLine("```diff");
        sb.AppendLine(diff);
        sb.AppendLine("```");

        // 5. Prior turns — windowed to avoid unbounded prompt growth.
        if (priorTurns.Count > 0)
        {
            var window = priorTurns.Count > _opts.MaxContextTurns
                ? priorTurns.Skip(priorTurns.Count - _opts.MaxContextTurns).ToList()
                : priorTurns;

            if (priorTurns.Count > _opts.MaxContextTurns)
                sb.AppendLine($"\nDEBATE SO FAR (last {_opts.MaxContextTurns} turns shown; {priorTurns.Count - _opts.MaxContextTurns} earlier turns omitted):");
            else
                sb.AppendLine("\nDEBATE SO FAR:");

            foreach (var t in window)
            {
                var mentions = t.Mentions.Count > 0
                    ? $" [mentioned: {string.Join(", ", t.Mentions.Select(m => "@" + m))}]"
                    : string.Empty;
                sb.AppendLine($"[{t.GladiatorName} — Round {t.Round}]{mentions}");
                sb.AppendLine(t.Text);
                sb.AppendLine();
            }
        }

        // 6. Open issues list (omit on round 1 turn 1 — nothing raised yet)
        if (openIssues.Count > 0)
        {
            sb.AppendLine("\nOPEN ISSUES RAISED SO FAR:");
            foreach (var issue in openIssues)
            {
                var candidate = issue.MergeCandidate.HasValue ? " [⚭ merge candidate]" : string.Empty;
                sb.AppendLine($"- [{issue.Severity.ToString().ToUpper()}] \"{issue.Title}\" (raised by {issue.RaisedByGladiatorName}){candidate}");
            }
            sb.AppendLine("Reference these by title when relevant rather than re-raising them.");
        }

        sb.AppendLine($"\nThis is Round {round}. Speak now, {gladiator.Name}:");

        return sb.ToString();
    }

    public string BuildArbiterPrompt(ReviewSession session)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("""
            You are the Arbiter — a calm, impartial moderator of the code review debate.
            You did not participate in the debate. Your role is to render a final structured verdict.

            You must:
            1. Rule on every flagged merge candidate pair (Merge / Distinguish / DismissOne)
            2. Rank all confirmed issues by importance
            3. Dismiss issues that were rejected by majority of gladiators
            4. Write a 2-paragraph summary of the review

            Respond ONLY with valid JSON in this exact format:
            {
              "mergeResolutions": [
                { "issueAId": "guid", "issueBId": "guid", "decision": "Merge|Distinguish|DismissOne", "reason": "string" }
              ],
              "rankedIssues": [
                { "issueId": "guid", "rank": 1, "rationale": "string" }
              ],
              "dismissedIssues": [
                { "issueId": "guid", "reason": "string" }
              ],
              "summary": "paragraph1\n\nparagraph2"
            }
            """);

        sb.AppendLine("\nFULL DEBATE TRANSCRIPT:");
        foreach (var t in session.Turns)
        {
            sb.AppendLine($"[{t.GladiatorName} — Round {t.Round}]");
            sb.AppendLine(t.Text);
            sb.AppendLine();
        }

        sb.AppendLine("\nALL ISSUES:");
        foreach (var issue in session.Issues)
        {
            var candidate = issue.MergeCandidate.HasValue
                ? $" | merge-candidate-with: {issue.MergeCandidate}"
                : string.Empty;
            sb.AppendLine($"""
                ID: {issue.Id}
                Title: {issue.Title}
                Severity: {issue.Severity}
                Raised by: {issue.RaisedByGladiatorName}
                Votes for: {issue.VotesFor} | Votes against: {issue.VotesAgainst}
                Status: {issue.Status}{candidate}
                """);
        }

        return sb.ToString();
    }
}
