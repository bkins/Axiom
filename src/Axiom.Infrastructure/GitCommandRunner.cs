using System.Diagnostics;
using System.Text;

namespace Axiom.Infrastructure;

internal sealed class GitCommandRunner(string executable = "git")
{
    internal async Task<GitCommandResult> RunAsync(string directory
                                                , IReadOnlyList<string> arguments
                                                , CancellationToken cancellationToken = default
                                                , byte[]? input = null
                                                , string? indexPath = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        timeout.Token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory       = directory
          , UseShellExecute        = false
          , CreateNoWindow         = true
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
          , RedirectStandardInput  = input is not null
        };
        // Inherited repository overrides must not redirect an application document to another repository.
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_LITERAL_PATHSPECS"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        if (indexPath is not null) start.Environment["GIT_INDEX_FILE"] = indexPath;
        foreach (var argument in new[] { "--no-pager", "-c", "core.quotePath=false", "-c", "diff.external=", "-c", "core.fsmonitor=false" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            var outputTask = ReadBoundedAsync(process.StandardOutput.BaseStream, 8 * 1024 * 1024, process, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError.BaseStream, 64 * 1024, process, timeout.Token);
            var inputTask = input is null ? Task.CompletedTask : WriteInputAsync(process, input, timeout.Token);
            await Task.WhenAll(outputTask, errorTask, inputTask, process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode, await outputTask, Encoding.UTF8.GetString(await errorTask));
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new IOException("Git is unavailable. Install Git to use document history; editing and recovery remain available.", exception);
        }
        catch
        {
            if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task WriteInputAsync(Process process, byte[] input, CancellationToken cancellationToken)
    {
        await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
        process.StandardInput.Close();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, Process process, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > limit)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new IOException("Git history output exceeds the supported size limit.");
            }
            output.Write(buffer, 0, count);
        }
    }
}
