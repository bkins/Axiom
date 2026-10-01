using System.Diagnostics;
using System.Text;

namespace Axiom.Infrastructure;

public sealed class LinuxRecycleBin : IRecycleBin
{
    public void Recycle(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The Linux Trash adapter requires Linux.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) throw new ArgumentException("Choose an absolute document path.", nameof(path));
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The document is unavailable or linked; it was retained.");
        if (!File.Exists("/usr/bin/gio"))
            throw new IOException("The Linux desktop Trash service (/usr/bin/gio) is unavailable. The document was retained.");
        RunTrashAsync(path).GetAwaiter().GetResult();
    }

    private static async Task RunTrashAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo("/usr/bin/gio")
        {
            WorkingDirectory       = "/"
          , UseShellExecute        = false
          , CreateNoWindow         = true
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
        };
        // An absolute local path follows the option terminator. No shell, force,
        // empty-trash, remove or permanent-delete command is available here.
        start.ArgumentList.Add("trash");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(path);
        start.Environment["GIO_USE_VFS"] = "local";
        using var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw new IOException("The Linux Trash service could not start.");
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException("The Linux Trash service failed: " + Encoding.UTF8.GetString(await error.ConfigureAwait(false)));
            if (File.Exists(path)) throw new IOException("The Linux Trash service did not move the document; refresh before retrying.");
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new IOException("The Linux Trash service timed out. Refresh or restart to reconcile any protected operation receipt; no permanent-delete fallback was attempted.", exception);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) return output.ToArray();
            if (output.Length + count > 16 * 1024) throw new IOException("The Linux Trash service returned excessive output.");
            output.Write(buffer, 0, count);
        }
    }
}
