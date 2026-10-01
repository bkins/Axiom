using System.Diagnostics;
using Microsoft.Win32;

namespace Axiom.Infrastructure;

public sealed class WindowsInstallationService(AppDataPaths paths)
{
    public async Task<string> InstallAsync(string source, bool startMenuShortcut, bool markdownAssociation, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Per-user installation requires Windows.");
        var state = await new StateMaintenanceService(paths).InspectAsync();
        if (state.Integrity != "ok" || state.PendingOperations > 0) throw new IOException("Resolve state integrity or pending file operations before installation.");
        Directory.CreateDirectory(paths.BackupDirectory);
        await new StateMaintenanceService(paths).ExportBackupAsync(Path.Combine(paths.BackupDirectory, "pre-install-" + Guid.NewGuid().ToString("N") + ".db"), cancellationToken);
        var packages = new PortablePackageService(paths);
        var sourceManifest = await packages.ValidateAsync(source, cancellationToken);
        PortablePackageService.EnsureSchemaCompatible(sourceManifest.SchemaVersion, state.SchemaVersion);
        var destination = Path.Combine(paths.Root, "Versions", sourceManifest.Version + "-" + sourceManifest.CommitId);
        if (!Directory.Exists(destination)) destination = await packages.InstallAsync(source, state.SchemaVersion, cancellationToken);
        var manifest = await packages.ValidateAsync(destination, cancellationToken);
        if (manifest.InformationalVersion != sourceManifest.InformationalVersion ||
            !manifest.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).SequenceEqual(sourceManifest.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)))
            throw new IOException("The retained version contradicts this package's identity or payload.");
        cancellationToken.ThrowIfCancellationRequested();
        await RegisterAsync(destination, manifest, startMenuShortcut, markdownAssociation, cancellationToken);
        return destination;
    }

    public async Task RegisterAsync(string directory, PortablePackageManifest manifest, bool shortcut, bool association, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var destination = Path.GetFullPath(directory);
        var versions = Path.GetFullPath(Path.Combine(paths.Root, "Versions"));
        if (Path.GetDirectoryName(destination) != versions || Path.GetFileName(destination) != manifest.Version + "-" + manifest.CommitId)
            throw new IOException("Register only a verified version owned by this Axiom state root.");
        var verified = await new PortablePackageService(paths).ValidateAsync(destination, cancellationToken);
        if (verified.InformationalVersion != manifest.InformationalVersion) throw new IOException("Installation identity changed.");
        var registration = "Axiom-" + WorkspaceFileOperations.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(paths.Root)))[..16];
        var integration = Path.Combine(paths.Root, "Installation");
        WorkspaceFileOperations.ValidateAncestors(paths.Root, integration, requireParent: false);
        Directory.CreateDirectory(integration);
        var uninstall = Path.Combine(integration, "Uninstall-" + registration + ".ps1");
        var launcher = Path.Combine(integration, "Launch-" + registration + ".ps1");
        var executable = Path.Combine(destination, "Axiom.App.exe");
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), registration + ".lnk");
        var programId = registration + ".Markdown";
        var command = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{launcher}\"";
        await File.WriteAllTextAsync(launcher, $$"""
            param([string]$Document)
            $env:AXIOM_STATE_ROOT = '{{Quote(paths.Root)}}'
            $arguments = if ($Document) { @('"' + $Document.Replace('"', '') + '"') } else { @() }
            if ($arguments.Count) { Start-Process -FilePath '{{Quote(executable)}}' -WorkingDirectory '{{Quote(destination)}}' -ArgumentList $arguments }
            else { Start-Process -FilePath '{{Quote(executable)}}' -WorkingDirectory '{{Quote(destination)}}' }
            """, cancellationToken);
        await File.WriteAllTextAsync(uninstall, BuildUninstallScript(destination, paths.Root, registration, shortcut || File.Exists(link) ? link : null, programId), cancellationToken);
        // Register after payload and helper files are complete. Retained versions make recovery explicit.
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + registration);
        key.SetValue("DisplayName", "Axiom (current user)");
        key.SetValue("DisplayVersion", manifest.Version);
        key.SetValue("DisplayIcon", executable);
        key.SetValue("InstallLocation", destination);
        key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstall}\"");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
        if (association)
        {
            using var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + programId + @"\shell\open\command");
            app.SetValue("", command + " -Document \"%1\"");
            foreach (var extension in new[] { ".md", ".markdown" })
            {
                using var choices = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extension + @"\OpenWithProgids");
                choices.SetValue(programId, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        if (shortcut)
        {
            var script = Path.Combine(integration, "Shortcut-" + registration + ".ps1");
            await File.WriteAllTextAsync(script, $$"""
                $shell = New-Object -ComObject WScript.Shell
                $link = $shell.CreateShortcut('{{Quote(link)}}')
                $link.TargetPath = 'powershell.exe'
                $link.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "{{Quote(launcher)}}"'
                $link.IconLocation = '{{Quote(executable)}}'
                $link.Save()
                """, cancellationToken);
            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public static string BuildUninstallScript(string directory, string stateRoot, string registration, string? shortcut, string programId) => $$"""
        $ErrorActionPreference = 'Stop'
        $package = [IO.Path]::GetFullPath('{{Quote(directory)}}')
        $state = [IO.Path]::GetFullPath('{{Quote(stateRoot)}}')
        $versions = [IO.Path]::GetFullPath((Join-Path $state 'Versions'))
        if ([IO.Path]::GetDirectoryName($package) -ne $versions) { throw 'Installation is outside the owned Versions folder.' }
        if (Get-Process -Name Axiom.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $package 'Axiom.App.exe') }) { throw 'Close this installed Axiom version before uninstalling.' }
        $manifestPath = Join-Path $package 'payload-manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $directories = @($package)
        for ($index = 0; $index -lt $directories.Count; $index++) {
            foreach ($item in Get-ChildItem -LiteralPath $directories[$index] -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked installation entry retained.' }
                if ($item.PSIsContainer) { $directories += $item.FullName }
            }
        }
        $owned = @()
        foreach ($file in $manifest.Files) {
            $relative = [string]$file.RelativePath
            if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.?([\\/]|$)|:') { throw 'Unsafe manifest path.' }
            $target = [IO.Path]::GetFullPath((Join-Path $package $relative))
            if (-not $target.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Escaping manifest path.' }
            $ancestor = $target
            while ($ancestor -and $ancestor.StartsWith($state, [StringComparison]::OrdinalIgnoreCase)) {
                if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked installation path.' }
                $ancestor = [IO.Path]::GetDirectoryName($ancestor)
            }
            $algorithm = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($target)
            try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $algorithm.Dispose() }
            if ($hash -ne $file.Sha256) { throw 'Modified payload retained; uninstall stopped.' }
            $owned += $target
        }
        if ((Get-Item -LiteralPath $manifestPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked manifest.' }
        foreach ($target in $owned) { Remove-Item -LiteralPath $target }
        Remove-Item -LiteralPath $manifestPath
        Get-ChildItem -LiteralPath $package -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { if (-not (Get-ChildItem -LiteralPath $_.FullName -Force)) { Remove-Item -LiteralPath $_.FullName } }
        if (-not (Get-ChildItem -LiteralPath $package -Force)) { Remove-Item -LiteralPath $package }
        $registration = '{{Quote(registration)}}'
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $registration
        if ((Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).InstallLocation -eq $package) {
            Remove-Item -LiteralPath $key -Recurse
            Remove-Item -LiteralPath 'HKCU:\Software\Classes\{{Quote(programId)}}' -Recurse -ErrorAction SilentlyContinue
            foreach ($extension in @('.md', '.markdown')) { Remove-ItemProperty -LiteralPath ('HKCU:\Software\Classes\' + $extension + '\OpenWithProgids') -Name '{{Quote(programId)}}' -ErrorAction SilentlyContinue }
            $shortcut = '{{Quote(shortcut ?? "")}}'
            if ($shortcut -and (Test-Path -LiteralPath $shortcut)) { Remove-Item -LiteralPath $shortcut }
        }
        Write-Output 'Axiom integration and this installed payload removed. User state, documents, backups and other retained versions remain.'
        """;

    private static async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Installation helper could not start.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new IOException(await errors);
    }
    private static string Quote(string text) => text.Replace("'", "''", StringComparison.Ordinal);
}
