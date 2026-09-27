using System.Text.RegularExpressions;

namespace Axiom.Core;

public sealed record MarkdownImageReferenceResult(MarkdownEditResult? Edit, string? Error)
{
    public bool Success => Edit is not null;
}

public sealed partial class MarkdownImageReferenceService
{
    private static readonly HashSet<string> SupportedExtensions =
        new([".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"], StringComparer.OrdinalIgnoreCase);

    public MarkdownImageReferenceResult CreateInsertion(
        string text,
        int selectionStart,
        int selectionLength,
        string workspaceRoot,
        string documentPath,
        string imagePath)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateSelection(text, selectionStart, selectionLength);

        if (TouchesCanonicalIdentity(text, selectionStart, selectionLength))
            return Failure("Move the caret below the Axiom document identity line before inserting an image.");

        var root = WorkspaceService.NormalizeRootPath(workspaceRoot);
        var document = Path.GetFullPath(documentPath);
        var image = Path.GetFullPath(imagePath);
        if (!IsInside(root, document))
            return Failure("Save the Markdown document inside the active workspace before inserting an image.");
        if (!File.Exists(image))
            return Failure("The selected image no longer exists.");
        if (!IsInside(root, image))
            return Failure("Choose an image inside the active workspace.");
        if (!SupportedExtensions.Contains(Path.GetExtension(image)))
            return Failure("Choose a supported image file: PNG, JPEG, GIF, WebP, BMP, or SVG.");

        var selected = text.Substring(selectionStart, selectionLength);
        var usesSelection = selectionLength > 0 && !string.IsNullOrWhiteSpace(selected) &&
            !selected.Contains('\r') && !selected.Contains('\n');
        var alt = usesSelection ? selected.Trim() : CreateAltPlaceholder(image);
        alt = EscapeAltText(alt);
        var relativePath = Path.GetRelativePath(Path.GetDirectoryName(document)!, image);
        var destination = EncodeMarkdownPath(relativePath);
        var insertion = $"![{alt}]({destination})";
        var updated = text[..selectionStart] + insertion + text[(selectionStart + selectionLength)..];
        var resultStart = usesSelection ? selectionStart + insertion.Length : selectionStart + 2;
        var resultLength = usesSelection ? 0 : alt.Length;
        return new MarkdownImageReferenceResult(
            new MarkdownEditResult(updated, resultStart, resultLength, true),
            null);
    }

    public static IReadOnlyCollection<string> FilePickerPatterns { get; } =
        ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.svg"];

    private static string CreateAltPlaceholder(string imagePath)
    {
        var value = Path.GetFileNameWithoutExtension(imagePath).Replace('-', ' ').Replace('_', ' ');
        value = WhitespaceRegex().Replace(value, " ").Trim();
        return string.IsNullOrEmpty(value) ? "image" : value;
    }

    private static string EscapeAltText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string EncodeMarkdownPath(string relativePath) => string.Join(
        '/',
        relativePath.Replace('\\', '/').Split('/').Select(segment =>
            segment is "." or ".." ? segment : Uri.EscapeDataString(segment)));

    private static bool IsInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (candidate.Equals(root, comparison)) return true;
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    private static bool TouchesCanonicalIdentity(string text, int start, int length)
    {
        if (DocumentIdCodec.Find(text) is null) return false;
        var newlineIndex = text.IndexOf('\n');
        var protectedLength = newlineIndex < 0 ? text.Length : newlineIndex + 1;
        return length == 0 ? start < protectedLength : start < protectedLength && start + length > 0;
    }

    private static MarkdownImageReferenceResult Failure(string message) => new(null, message);

    private static void ValidateSelection(string text, int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > text.Length || length > text.Length - start)
            throw new ArgumentOutOfRangeException(nameof(length), "The selection must be inside the source text.");
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
