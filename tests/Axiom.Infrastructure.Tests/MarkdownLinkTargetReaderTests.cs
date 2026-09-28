using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class MarkdownLinkTargetReaderTests
{
    [Fact]
    public async Task LinkedDirectoryAndItsMissingChildrenAreExcludedBeforeReading()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), $"AxiomLinkedTarget-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"AxiomLinkedOutside-{Guid.NewGuid():N}");
        var junction = Path.Combine(root, "linked");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside, "a.md"), "# Outside");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{junction}\" \"{outside}\""
              ,
                CreateNoWindow = true
              ,
                UseShellExecute = false
              ,
                RedirectStandardOutput = true
              ,
                RedirectStandardError = true
            })!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            var reader = new MarkdownLinkTargetReader(new FileDocumentStore());

            var existing = await reader.ReadAsync(root, Path.Combine(junction, "a.md"));
            var missing = await reader.ReadAsync(root, Path.Combine(junction, "missing.md"));
            var resolver = new MarkdownLinkResolver(reader);
            var escaped = await resolver.ResolveAsync(root, Path.Combine(root, "source.md"), "linked/a.md");

            Assert.Equal(MarkdownLinkResolutionStatus.ExcludedPath, existing.Status);
            Assert.Equal(MarkdownLinkResolutionStatus.ExcludedPath, missing.Status);
            Assert.Equal(MarkdownLinkResolutionStatus.ExcludedPath, escaped.Status);
            Assert.Null(existing.Text);
            Assert.Null(missing.Text);
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task ReadsOrdinaryMarkdownWithoutEnrollingAndDistinguishesMissingAndOutsideTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomLinkReader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "ordinary.md");
            const string text = "# Ordinary\n\nNo identity.";
            await File.WriteAllTextAsync(target, text);
            var bytes = await File.ReadAllBytesAsync(target);
            var reader = new MarkdownLinkTargetReader(new FileDocumentStore());

            var read = await reader.ReadAsync(root, target);
            var missing = await reader.ReadAsync(root, Path.Combine(root, "missing", "a.md"));
            var outside = await reader.ReadAsync(root, Path.Combine(Path.GetTempPath(), "outside.md"));

            Assert.Equal(MarkdownLinkResolutionStatus.Resolved, read.Status);
            Assert.Equal(text, read.Text);
            Assert.Null(DocumentIdCodec.Find(read.Text!));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
            Assert.Equal(MarkdownLinkResolutionStatus.MissingDocument, missing.Status);
            Assert.Equal(MarkdownLinkResolutionStatus.Unavailable, outside.Status);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
