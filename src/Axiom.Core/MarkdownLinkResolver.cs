using System.Text;

namespace Axiom.Core;

public sealed class MarkdownLinkResolver(IMarkdownLinkTargetReader targets)
{
    private readonly MarkdownStructureService _structure = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<MarkdownLinkResolution> ResolveAsync(string? workspaceRoot
                                                        , string? sourcePath
                                                        , string destination
                                                        , Func<string, DocumentStructure?>? openDocument = null
                                                        , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = ResolveLocation(workspaceRoot, sourcePath, destination);
        if (location.Status != MarkdownLinkResolutionStatus.Resolved) return location;
        var target = await targets.ReadAsync(WorkspaceService.NormalizeRootPath(workspaceRoot!), location.TargetPath!, cancellationToken);
        if (target.Status != MarkdownLinkResolutionStatus.Resolved)
            return location with { Status = target.Status, Message = target.Message ?? "The linked document is unavailable." };
        if (string.IsNullOrEmpty(location.Fragment)) return location;
        var document = openDocument?.Invoke(location.TargetPath!) ?? _structure.Parse(target.Text ?? string.Empty);
        var heading = document.FindHeading(location.Fragment);
        return heading is null
            ? location with { Status = MarkdownLinkResolutionStatus.MissingHeading, Message = $"The document exists, but heading '#{location.Fragment}' was not found." }
            : location with { Source = heading.Source };
    }

    public static MarkdownLinkResolution ResolveLocation(string? workspaceRoot, string? sourcePath, string destination)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return new(MarkdownLinkResolutionStatus.NoWorkspace, "Open a workspace before following document links.");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new(MarkdownLinkResolutionStatus.SourceNotSaved, "Save this document inside the workspace before following relative links.");
        try
        {
            var root = WorkspaceService.NormalizeRootPath(workspaceRoot);
            var source = Path.GetFullPath(sourcePath);
            if (!IsWithin(root, source))
                return new(MarkdownLinkResolutionStatus.OutsideWorkspace, "The source document is outside the active workspace.");
            var separator = destination.IndexOf('#');
            var pathPart = separator < 0 ? destination : destination[..separator];
            var fragmentPart = separator < 0 ? string.Empty : destination[(separator + 1)..];
            if (!TryDecode(pathPart, out var decodedPath) || !TryDecode(fragmentPart, out var fragment))
                return new(MarkdownLinkResolutionStatus.InvalidPath, "The link contains malformed URI escapes.");
            if (decodedPath.StartsWith('/') || decodedPath.StartsWith('\\') || Path.IsPathRooted(decodedPath))
                return new(MarkdownLinkResolutionStatus.AbsolutePath, "Use a relative document link inside the active workspace.");
            if (decodedPath.Contains(':'))
                return new(MarkdownLinkResolutionStatus.UnsupportedScheme, "This action follows local relative Markdown links; URI schemes are unsupported.");
            if (decodedPath.Contains('?'))
                return new(MarkdownLinkResolutionStatus.UnsupportedQuery, "Query strings are unsupported for document navigation.");
            if (decodedPath.Any(character => character < 32 || "<>\"|*".Contains(character)) || fragment.Any(char.IsControl))
                return new(MarkdownLinkResolutionStatus.InvalidPath, "The link contains unsupported path or fragment characters.");
            var relativePath = decodedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var targetPath = string.IsNullOrEmpty(relativePath)
                ? source
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, relativePath));
            if (!IsWithin(root, targetPath))
                return new(MarkdownLinkResolutionStatus.OutsideWorkspace, "The link leaves the active workspace. Open another workspace deliberately to use that document.");
            if (!targetPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !targetPath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                return new(MarkdownLinkResolutionStatus.UnsupportedDocument, "Only .md and .markdown document links are supported.");
            return new(MarkdownLinkResolutionStatus.Resolved, "Document link resolved.", targetPath, fragment);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(MarkdownLinkResolutionStatus.InvalidPath, "The document link cannot be interpreted as a valid local path.");
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '%') continue;
                var bytes = new List<byte>();
                while (index < value.Length && value[index] == '%')
                {
                    if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])) return false;
                    if (!byte.TryParse(value.AsSpan(index + 1, 2)
                                     , System.Globalization.NumberStyles.HexNumber
                                     , System.Globalization.CultureInfo.InvariantCulture
                                     , out var parsed)) return false;
                    bytes.Add(parsed);
                    index += 3;
                }
                StrictUtf8.GetCharCount(bytes.ToArray());
                index--;
            }
            decoded = Uri.UnescapeDataString(value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
