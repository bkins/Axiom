using Axiom.Core;

namespace Axiom.Infrastructure;

internal static class ExportImageReader
{
    public static ExportImage? Read(string destination, ExportSnapshot snapshot, List<string> warnings)
    {
        try
        {
            if (snapshot.SourcePath is null || Uri.TryCreate(destination, UriKind.Absolute, out _) || destination.StartsWith("//", StringComparison.Ordinal))
                throw new IOException("Only local images relative to a saved document can be embedded.");
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(snapshot.SourcePath)!, Uri.UnescapeDataString(destination.Split(['?', '#'], 2)[0])));
            var mediaType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => null
            };
            if (mediaType is null) throw new IOException("Supported export images: PNG, JPEG, GIF and BMP.");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 20 * 1024 * 1024) throw new IOException("Image is missing or exceeds 20 MiB.");
            var bytes = File.ReadAllBytes(path);
            ExportImageDimensions.Read(bytes, mediaType);
            return new(bytes, mediaType);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            warnings.Add($"Image '{destination}': {exception.Message} Alt text retained.");
            return null;
        }
    }
}
