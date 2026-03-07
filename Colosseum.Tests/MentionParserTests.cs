using Colosseum.Core.Models;
using Colosseum.Core.Services;

namespace Colosseum.Tests;

public class MentionParserTests
{
    private static readonly List<Gladiator> Squad =
        [Gladiator.Maximus, Gladiator.Brutus, Gladiator.Cassius, Gladiator.Valeria, Gladiator.Arbiter];

    [Fact]
    public void Extract_ValidMention_ReturnsName()
    {
        var result = MentionParser.Extract("@Maximus your N+1 is showing.", Squad, "Brutus");
        Assert.Contains("Maximus", result);
    }

    [Fact]
    public void Extract_SelfMention_IsStripped()
    {
        var result = MentionParser.Extract("I, @Brutus, agree with myself.", Squad, "Brutus");
        Assert.DoesNotContain("Brutus", result);
    }

    [Fact]
    public void Extract_UnknownMention_IsStripped()
    {
        var result = MentionParser.Extract("@Caesar is not a valid gladiator.", Squad, "Brutus");
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_CaseInsensitive_ReturnsCanonicalName()
    {
        var result = MentionParser.Extract("@maximus your allocations.", Squad, "Brutus");
        Assert.Contains("Maximus", result);
    }

    [Fact]
    public void Extract_MultipleMentions_ReturnsBoth()
    {
        var result = MentionParser.Extract("@Maximus and @Valeria both miss the point.", Squad, "Cassius");
        Assert.Contains("Maximus", result);
        Assert.Contains("Valeria", result);
    }

    [Fact]
    public void Extract_ArbiterMention_IsValid()
    {
        var result = MentionParser.Extract("@Arbiter please resolve this.", Squad, "Maximus");
        Assert.Contains("Arbiter", result);
    }

    [Fact]
    public void Extract_EmbeddedInPunctuation_Extracted()
    {
        var result = MentionParser.Extract("@Brutus, your YAGNI argument fails here.", Squad, "Cassius");
        Assert.Contains("Brutus", result);
    }

    [Fact]
    public void Extract_Deduplicated_SingleEntry()
    {
        var result = MentionParser.Extract("@Maximus @Maximus double mention.", Squad, "Cassius");
        Assert.Single(result);
        Assert.Equal("Maximus", result[0]);
    }
}
