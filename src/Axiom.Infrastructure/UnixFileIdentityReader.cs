using System.Diagnostics;
using System.Globalization;

namespace Axiom.Infrastructure;

/// <summary>Read-only device/inode comparison prevents separate dirty tabs for Unix spelling aliases/hard links.</summary>
public sealed class UnixFileIdentityReader
{
    public async Task<IReadOnlyList<string>> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var normalized = paths.Select(Path.GetFullPath).ToArray();
        if (normalized.Length is < 1 or > 128 || normalized.Sum(path => (long)path.Length) > 65_536)
            throw new InvalidOperationException("Choose a smaller open-document set for native file-identity verification.");
        var start = new ProcessStartInfo("/usr/bin/stat")
        {
            UseShellExecute = false
          , CreateNoWindow = true
          , RedirectStandardOutput = true
          , RedirectStandardError = true
        };
        start.ArgumentList.Add("-L");
        start.ArgumentList.Add(OperatingSystem.IsMacOS() ? "-f" : "-c");
        start.ArgumentList.Add(OperatingSystem.IsMacOS() ? "%Ud:%Ui" : "%d:%i");
        foreach (var path in normalized) start.ArgumentList.Add(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Native file identity is unavailable.");
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0) throw new InvalidOperationException("Native file identity is unavailable; source was not opened or edited.");
            var lines = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != normalized.Length) throw new InvalidOperationException("Native file identity returned an incomplete result.");
            return lines.Select(ParseIdentity).ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Native file identity timed out; source was not opened or edited."); }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("Native file identity requires the system /usr/bin/stat tool."); }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    public static string ParseIdentity(string output)
    {
        var values = output.Trim().Split(':');
        if (values.Length != 2 || !ulong.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var device)
                               || !ulong.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var inode) || inode == 0)
            throw new InvalidOperationException("Native file identity returned an invalid device/inode.");
        return device.ToString(CultureInfo.InvariantCulture) + ":" + inode.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (result.Length + count > 16_384) throw new InvalidOperationException("Native file identity output exceeded its bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
