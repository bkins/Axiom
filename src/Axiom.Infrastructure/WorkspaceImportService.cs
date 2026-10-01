using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class WorkspaceImportService(WorkspaceFileOperations operations)
{
    public static bool IsMarkdown(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string path) => MarkdownImageReferenceService.FilePickerPatterns.Any(pattern =>
        Path.GetExtension(path).Equals(pattern[1..], StringComparison.OrdinalIgnoreCase));

    public async Task<byte[]> ReviewAsync(string source, bool image, CancellationToken cancellationToken = default)
    {
        if (image ? !IsImage(source) : !IsMarkdown(source))
            throw new ArgumentException(image ? "Choose a supported local image." : "Choose a Markdown document.");
        var limit = image ? 20 * 1024 * 1024 : 4 * 1024 * 1024;
        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > limit) throw new IOException($"The selected file exceeds the {limit / (1024 * 1024)} MiB import limit.");
        using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + read > limit) throw new IOException("The selected file grew beyond the import limit.");
            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return content.ToArray();
    }

    public Task<string?> CopyReviewedAsync(WorkspaceRecord workspace
                                         , string destinationRelative
                                         , byte[] content
                                         , bool image
                                         , CancellationToken cancellationToken = default)
    {
        if (image ? !IsImage(destinationRelative) : !IsMarkdown(destinationRelative))
            throw new ArgumentException("Retain a supported file extension for the imported file.");
        if (content.Length > (image ? 20 * 1024 * 1024 : 4 * 1024 * 1024))
            throw new IOException("The reviewed file exceeds the import limit.");
        return operations.ExecuteAsync(workspace, image ? DocumentMutationKind.ImportAsset : DocumentMutationKind.ImportDocument,
            null, destinationRelative, cancellationToken: cancellationToken, importContent: (byte[])content.Clone());
    }
}
