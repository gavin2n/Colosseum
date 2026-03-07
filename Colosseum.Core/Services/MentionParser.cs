using System.Text.RegularExpressions;
using Colosseum.Core.Models;

namespace Colosseum.Core.Services;

public static class MentionParser
{
    private static readonly Regex MentionRegex = new(@"@([A-Za-z]+)", RegexOptions.Compiled);

    public static List<string> Extract(string turnText, IEnumerable<Gladiator> activeGladiators, string selfName)
    {
        var validNames = activeGladiators
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in MentionRegex.Matches(turnText))
        {
            var name = m.Groups[1].Value;
            if (string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!validNames.Contains(name))
                continue;
            // Normalise to the canonical casing from the squad
            var canonical = validNames.First(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            found.Add(canonical);
        }

        return [.. found];
    }
}
