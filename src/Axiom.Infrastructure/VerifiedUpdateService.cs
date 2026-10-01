using System.IO.Compression;
using System.Text.Json;

namespace Axiom.Infrastructure;

public sealed class VerifiedUpdateService(HttpClient client)
{
    private const long MaximumDownload = 512L * 1024 * 1024;
    private const long MaximumExpanded = 1024L * 1024 * 1024;

    public async Task<VerifiedUpdateOffer> CheckAsync(Uri feed, string expectedFeedHash, string currentVersion, int schema, CancellationToken cancellationToken = default)
    {
        RequireHttps(feed);
        RequireHash(expectedFeedHash);
        var bytes = await DownloadAsync(feed, 256 * 1024, cancellationToken);
        if (!WorkspaceFileOperations.Hash(bytes).Equals(expectedFeedHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The update offer does not match the independently trusted SHA-256. Nothing was installed.");
        var offer = JsonSerializer.Deserialize<VerifiedUpdateOffer>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new IOException("The update offer is empty.");
        if (offer.FormatVersion != 1 || !Version.TryParse(offer.Version, out var version) || version <= Version.Parse(currentVersion) ||
            string.IsNullOrEmpty(offer.CommitId) || offer.CommitId.Length is < 7 or > 40 || !offer.CommitId.All(char.IsAsciiHexDigit) ||
            string.IsNullOrWhiteSpace(offer.Channel) || offer.Channel.Length > 100 || offer.ReleaseNotes is null || offer.ReleaseNotes.Length > 16000 || string.IsNullOrWhiteSpace(offer.PackageUrl))
            throw new IOException("The update identity is invalid or is not newer than this app.");
        PortablePackageService.EnsureSchemaCompatible(offer.SchemaVersion, schema);
        RequireHttps(new Uri(offer.PackageUrl));
        RequireHash(offer.PackageSha256);
        return offer;
    }

    public async Task<string> DownloadPackageAsync(VerifiedUpdateOffer offer, string downloadRoot, CancellationToken cancellationToken = default)
    {
        RequireHttps(new Uri(offer.PackageUrl));
        RequireHash(offer.PackageSha256);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(downloadRoot);
        var existing = root;
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing) ?? throw new IOException("The download parent is unavailable.");
        WorkspaceFileOperations.ValidateAncestors(existing, Path.Combine(root, "probe"), requireParent: false);
        Directory.CreateDirectory(root);
        WorkspaceFileOperations.ValidateAncestors(root, Path.Combine(root, "probe"), requireParent: false);
        var stage = Path.Combine(root, "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        // Failed/cancelled staging remains isolated for diagnosis. It is never installed.
        var archivePath = Path.Combine(stage, "download.zip");
        var bytes = await DownloadAsync(new Uri(offer.PackageUrl), MaximumDownload, cancellationToken);
        if (!WorkspaceFileOperations.Hash(bytes).Equals(offer.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The downloaded package failed its trusted hash check.");
        await File.WriteAllBytesAsync(archivePath, bytes, cancellationToken);
        var package = Path.Combine(stage, "package");
        Directory.CreateDirectory(package);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > 10000) throw new IOException("The update archive entry count is unsupported.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Split('/').Any(part => part is ".." or "." || part.Contains(':')) ||
                !seen.Add(name.TrimEnd('/')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IOException("The update archive contains an unsafe, linked or duplicate entry.");
            var target = Path.GetFullPath(Path.Combine(package, name));
            if (!target.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The archive escapes its staging folder.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpanded || entry.Length > MaximumDownload) throw new IOException("The expanded update exceeds its size limit.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await CopyBoundedAsync(source, destination, entry.Length, cancellationToken);
        }
        var manifest = await new PortablePackageService(new AppDataPaths()).ValidateAsync(package, cancellationToken);
        if (manifest.Version != offer.Version || manifest.CommitId != offer.CommitId || manifest.SchemaVersion != offer.SchemaVersion ||
            manifest.Channel is not null && manifest.Channel != offer.Channel)
            throw new IOException("The verified payload contradicts the trusted update offer.");
        return package;
    }

    private async Task<byte[]> DownloadAsync(Uri uri, long maximum, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri != uri) throw new IOException("Update redirects require a new explicit trusted URL.");
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("The download exceeds its size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        await CopyBoundedAsync(source, destination, maximum, cancellationToken);
        return destination.ToArray();
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += count;
            if (total > maximum) throw new IOException("The downloaded or expanded content exceeds its size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static void RequireHash(string hash)
    {
        if (hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)) throw new ArgumentException("Provide a 64-character SHA-256 from an independently trusted source.");
    }
    private static void RequireHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Updates require an explicit HTTPS URL without embedded credentials or fragments.");
    }
}
