using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class EnglishSpellingTests
{
    [Fact]
    public void OfflineDictionaryChecksInflectionsAndSuggestsCorrections()
    {
        var service = new EnglishSpellingService();
        Assert.True(service.Check("running"));
        Assert.False(service.Check("speling"));
        Assert.Contains("spelling", service.Suggest("speling"));
    }

    [Fact]
    public void MarkdownChecksProseButSkipsCodeDestinationsAndPersonalWords()
    {
        const string source = "speling Axiom https://urltypooo.example emailtypooo@example.com\n\n`codetypooo` [link](https://destinationtypooo.example)\n\n```cs\nfencedtypooo\n```";
        var issues = new EnglishSpellingService().FindIssues(source, ["Axiom"]);
        var issue = Assert.Single(issues);
        Assert.Equal("speling", issue.Word);
        Assert.Equal(0, issue.Offset);
        Assert.Empty(new EnglishSpellingService().FindIssues(source, ["Axiom", "speling"]));
    }
}
