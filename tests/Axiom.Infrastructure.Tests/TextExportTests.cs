using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class TextExportTests
{
    [Fact]
    public async Task RendersBufferStructureUnicodeAndLinksWithoutSavingSource()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source.md");
        await File.WriteAllTextAsync(source, "saved");
        var markdown = DocumentIdCodec.Enroll("# Café 一\n\n**Bold** [**site**](https://example.com)\n\n3. Three\n4. Four\n   - Nested\n\n> Quote\n\n```\n  code\n```\n\n![Alt](missing.png)\n\n| A | B |\n|---|---|\n| One | Two |", Guid.NewGuid());
        var destination = Path.Combine(fixture.Root, "out.txt");
        var result = await new DocumentExportService().WriteAsync(new(markdown, source), destination, false, [source], fixture.State.Paths.Root);
        var text = await File.ReadAllTextAsync(destination);
        Assert.Contains("Café 一", text);
        Assert.Contains("Bold site (https://example.com)", text);
        Assert.Contains("3. Three", text);
        Assert.Contains("4. Four", text);
        Assert.Contains("- Nested", text);
        Assert.Contains("> Quote", text);
        Assert.Contains("      code", text);
        Assert.Contains("Alt", text);
        Assert.Contains("One | Two", text);
        Assert.DoesNotContain("documentId", text);
        Assert.DoesNotContain("**", text);
        Assert.Equal("saved", await File.ReadAllTextAsync(source));
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task DecodesEntitiesAndRetainsTaskMarkersWhileOmittingUnsafeDestinations()
    {
        using var fixture = new MutationTestWorkspace();
        var destination = Path.Combine(fixture.Root, "out.txt");
        var result = await new DocumentExportService().WriteAsync(new("A &amp; B &#x4E00;\n\n- [x] Done\n- [ ] Todo\n\n[bad](javascript:alert)", null), destination, false, [], fixture.State.Paths.Root);
        var text = await File.ReadAllTextAsync(destination);
        Assert.Contains("A & B 一", text);
        Assert.Contains("☑ Done", text);
        Assert.Contains("☐ Todo", text);
        Assert.Contains("bad", text);
        Assert.DoesNotContain("javascript:", text);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task CancelledReplacementPreservesDestinationAndLeavesNoTemporaryFile()
    {
        using var fixture = new MutationTestWorkspace();
        var destination = Path.Combine(fixture.Root, "out.txt");
        await File.WriteAllTextAsync(destination, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocumentExportService().WriteAsync(new("new", null), destination, true, [], fixture.State.Paths.Root, cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
    }
}
