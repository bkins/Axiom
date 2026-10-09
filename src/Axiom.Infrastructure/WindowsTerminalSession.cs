using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Axiom.Infrastructure;

/// <summary>Owns a Windows pseudoconsole and its shell. Pipe reads and writes run independently.</summary>
public sealed class WindowsTerminalSession : IAsyncDisposable
{
    private nint _console;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Task _reader;
    private readonly nint _process;
    private bool _disposed;
    private Task? _disposeTask;
    public int ProcessId { get; }
    public bool IsRunning => !_disposed && WaitForSingleObject(_process, 0) == 258;

    public WindowsTerminalSession(string repositoryRoot, string promptToken, Action<string> output)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Embedded terminal requires Windows.");
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (promptToken.Length != 32 || promptToken.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("Invalid prompt token.");
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        nint attributes = 0, environment = 0;
        try
        {
            Check(CreatePipe(out inputRead, out inputWrite, 0, 0));
            Check(CreatePipe(out outputRead, out outputWrite, 0, 0));
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(new(100, 30), inputRead, outputWrite, 0, out _console));
            nuint size = 0;
            InitializeProcThreadAttributeList(0, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            Check(UpdateProcThreadAttribute(attributes, 0, (nint)0x20016, _console, (nuint)nint.Size, 0, 0));
            var start = new StartupInfoEx { StartupInfo = new() { Size = Marshal.SizeOf<StartupInfoEx>() }, AttributeList = attributes };
            var executable = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var escapedRoot = root.Replace("'", "''");
            var setup = "$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new(); Import-Module PSReadLine -ErrorAction SilentlyContinue; function global:prompt { $where = if ([IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\\') -ieq '" + escapedRoot.TrimEnd('\\') + "') { 'root' } else { 'other' }; [Console]::Write([char]27 + ']777;" + promptToken + ";' + $where + [char]7); 'PS ' + (Get-Location).Path + '> ' }";
            var command = new StringBuilder('"' + executable + "\" -NoLogo -NoProfile -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(setup)));
            var variables = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Where(pair => !((string)pair.Key).StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => (string)pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value);
            environment = Marshal.StringToHGlobalUni(string.Join('\0', variables) + "\0\0");
            Check(CreateProcess(executable, command, 0, 0, false, 0x80400, environment, root, ref start, out var process));
            _process = process.Process; ProcessId = process.ProcessId;
            CloseHandle(process.Thread);
            inputRead.Dispose(); outputWrite.Dispose();
            _input = new(inputWrite, FileAccess.Write, 4096, false); inputWrite = null;
            _output = new(outputRead, FileAccess.Read, 4096, false); outputRead = null;
            _reader = Task.Run(() =>
            {
                var decoder = Encoding.UTF8.GetDecoder(); var buffer = new byte[8192]; var characters = new char[8192];
                try
                {
                    int count;
                    while ((count = _output.Read(buffer)) > 0)
                    {
                        var length = decoder.GetChars(buffer, 0, count, characters, 0);
                        output(new string(characters, 0, length));
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });
        }
        catch
        {
            if (_console != 0) { ClosePseudoConsole(_console); _console = 0; }
            if (_process != 0) CloseHandle(_process);
            throw;
        }
        finally
        {
            inputRead?.Dispose(); inputWrite?.Dispose(); outputRead?.Dispose(); outputWrite?.Dispose();
            if (attributes != 0) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            if (environment != 0) Marshal.FreeHGlobal(environment);
        }
    }
    public async Task WriteAsync(string input)
    {
        if (input.Length > 16384) throw new ArgumentException("Terminal input exceeds the per-message limit.");
        await _writeGate.WaitAsync();
        try { if (_disposed) return; var bytes = Encoding.UTF8.GetBytes(input); await Task.Run(() => { _input.Write(bytes); _input.Flush(); }); }
        finally { _writeGate.Release(); }
    }
    public void Resize(int columns, int rows)
    {
        if (!_disposed && _console != 0) Marshal.ThrowExceptionForHR(ResizePseudoConsole(_console, new((short)Math.Clamp(columns, 20, 400), (short)Math.Clamp(rows, 5, 200))));
    }
    public ValueTask DisposeAsync() => new(_disposeTask ??= CloseAsync());
    private async Task CloseAsync()
    {
        _disposed = true;
        // Closing the console first releases a pipe writer blocked by a foreground program.
        var console = _console; _console = 0;
        await Task.Run(() => ClosePseudoConsole(console));
        await _writeGate.WaitAsync();
        try
        {
            _input.Dispose();
            await _reader;
            _output.Dispose(); CloseHandle(_process);
        }
        finally { _writeGate.Release(); }
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Coord(short X, short Y);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public nint Reserved; public nint Desktop; public nint Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize; public nint ReservedBytes, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public nint AttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(nint console, Coord size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(nint console);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nuint size, nint previous, nint returnSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
