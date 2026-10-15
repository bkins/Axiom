using Axiom.Core;
using Xunit;
namespace Axiom.Core.Tests;
public sealed class MarkdownEditProposalTests
{
    [Fact] public void Complete_Proposal_Preserves_Embedded_Code_Fences()
        => Assert.Equal("# Revised\n```cs\ncode\n```", MarkdownEditProposal.Parse("# Old", "````markdown\n# Revised\n```cs\ncode\n```\n````"));
    [Theory]
    [InlineData("Replace the document yourself.")]
    [InlineData("```markdown\n\n```")]
    [InlineData("Commentary\n```markdown\n# Changed\n``` to apply")]
    public void Incomplete_Or_Commentary_Response_Is_Not_An_Applyable_Proposal(string response)
        => Assert.Throws<InvalidOperationException>(() => MarkdownEditProposal.Parse("# Source", response));
    [Fact] public void Document_Identity_Cannot_Be_Removed()
        => Assert.Throws<InvalidOperationException>(() => MarkdownEditProposal.Parse("<!-- axiom:documentId=60822ad3-c5ce-4781-81ac-dc0ed5986365 -->\n# Source", "```markdown\n# Revised\n```"));
}
