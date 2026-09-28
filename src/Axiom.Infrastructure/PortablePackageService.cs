using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class PortablePackageService(AppDataPaths paths, Func<string, string?>? readAssemblyIdentity = null)
{
    public const string ManifestName = "payload-manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<PortablePackageManifest> ValidateAsync(string directory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, ManifestName);
        WorkspaceFileOperations.ValidateAncestors(root, manifestPath);
        await using var stream = File.OpenRead(manifestPath);
        if (stream.Length > 2_000_000) throw new IOException("The payload manifest is too large.");
        var manifest = await JsonSerializer.DeserializeAsync<PortablePackageManifest>(stream, JsonOptions, cancellationToken) ?? throw new IOException("The payload manifest is empty.");
        if (manifest.FormatVersion != 1 || !Version.TryParse(manifest.Version, out _) || manifest.SchemaVersion < 1 || manifest.Files is null || manifest.Files.Count == 0 || string.IsNullOrWhiteSpace(manifest.CommitId) ||
            manifest.InformationalVersion != manifest.Version + "+" + manifest.CommitId ||
            (manifest.CommitId != "local" && (manifest.CommitId.Length < 7 || manifest.CommitId.Length > 40 || !manifest.CommitId.All(Uri.IsHexDigit))))
            throw new IOException("The package identity is invalid or contradictory.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = ResolvePayloadPath(root, file.RelativePath);
            WorkspaceFileOperations.ValidateAncestors(root, fullPath);
            if (!seen.Add(file.RelativePath.Replace('\\', '/')) || file.RelativePath.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The manifest contains duplicate or self-referencing entries.");
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length != file.Length || !string.Equals(WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(fullPath, cancellationToken)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Payload integrity failed: " + file.RelativePath);
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked payload entries are not allowed.");
            if (Directory.Exists(entry)) continue;
            var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
            if (relative != ManifestName && !seen.Contains(relative)) throw new IOException("Unexpected unverified payload file: " + relative);
        }
        if (!seen.Contains("Axiom.App.exe") || !seen.Contains("Axiom.App.dll") || !seen.Contains("Axiom.Infrastructure.dll")) throw new IOException("The package must contain the Axiom executable and assemblies.");
        var assemblyPath = Path.Combine(root, "Axiom.App.dll");
        var assemblyIdentity = readAssemblyIdentity is null
            ? System.Diagnostics.FileVersionInfo.GetVersionInfo(assemblyPath).ProductVersion
            : readAssemblyIdentity(assemblyPath);
        if (assemblyIdentity != manifest.InformationalVersion) throw new IOException("The package version contradicts its compiled assembly.");
        var executablePath = Path.Combine(root, "Axiom.App.exe");
        var executableIdentity = readAssemblyIdentity is null
            ? System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).ProductVersion
            : readAssemblyIdentity(executablePath);
        if (executableIdentity != manifest.InformationalVersion) throw new IOException("The package version contradicts its compiled executable.");
        if (PortableAssemblyMetadata.ReadSchemaVersion(Path.Combine(root, "Axiom.Infrastructure.dll")) != manifest.SchemaVersion)
            throw new IOException("The package schema contradicts its compiled Infrastructure assembly.");
        return manifest;
    }

    public async Task<string> InstallAsync(string sourceDirectory, int databaseSchema, CancellationToken cancellationToken = default)
    {
        var manifest = await ValidateAsync(sourceDirectory, cancellationToken);
        EnsureSchemaCompatible(manifest.SchemaVersion, databaseSchema);
        var versions = Path.Combine(paths.Root, "Versions");
        WorkspaceFileOperations.ValidateAncestors(paths.Root, versions);
        Directory.CreateDirectory(versions);
        var destination = Path.Combine(versions, manifest.Version + "-" + manifest.CommitId);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("This version already exists. Use Switch Installed Version to open it.");
        var stage = Path.Combine(versions, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        // Keep failed staging for diagnosis; never delete or overwrite an installed
        // version automatically. The destination rename is no-overwrite.
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolvePayloadPath(Path.GetFullPath(sourceDirectory), file.RelativePath);
            WorkspaceFileOperations.ValidateAncestors(Path.GetFullPath(sourceDirectory), source);
            var target = ResolvePayloadPath(stage, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, false);
        }
        File.Copy(Path.Combine(sourceDirectory, ManifestName), Path.Combine(stage, ManifestName), false);
        await ValidateAsync(stage, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(stage, destination);
        return destination;
    }

    public static void EnsureSchemaCompatible(int supportedSchema, int databaseSchema)
    {
        if (supportedSchema < databaseSchema) throw new IOException($"This package supports schema {supportedSchema}; the current state uses schema {databaseSchema}. Choose a compatible version.");
    }

    private static string ResolvePayloadPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(part => part is ".." or "." or "" || part.Contains(':')))
            throw new IOException("The payload contains an escaping or invalid path.");
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The payload path escapes its package.");
        return fullPath;
    }
}
